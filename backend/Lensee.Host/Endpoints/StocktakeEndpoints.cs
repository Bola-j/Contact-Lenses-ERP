using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Inventory.Services;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lensee.Host.Endpoints;

public static class StocktakeEndpoints
{
    private const string Draft = "Draft";
    private const string Confirmed = "Confirmed";

    public static RouteGroupBuilder MapStocktakeEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/stocktakes").WithTags("Stocktake");

        group.MapGet("/", ListStocktakesAsync).RequireAuthorization("inventory.read");
        group.MapGet("/{id:guid}", GetStocktakeAsync).RequireAuthorization("inventory.read");
        group.MapPost("/", CreateStocktakeAsync).RequireAuthorization("inventory.write");
        group.MapPut("/{id:guid}/lines", UpsertStocktakeLinesAsync).RequireAuthorization("inventory.write");
        group.MapPost("/{id:guid}/confirm", ConfirmStocktakeAsync).RequireAuthorization("inventory.write");

        return group;
    }

    private static async Task<IResult> ListStocktakesAsync(
        int? page,
        int? pageSize,
        OperationsDbContext operationsDbContext,
        CancellationToken cancellationToken)
    {
        var request = new PageRequest(page ?? 1, pageSize ?? 25);
        var query = operationsDbContext.StocktakeSessions.AsQueryable();
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(session => session.CreatedAt)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(session => new StocktakeListResponse(
                session.Id,
                session.LocationId,
                session.Status,
                session.ProductsCounted ?? 0,
                session.TotalDiscrepancyUnits ?? 0,
                session.CreatedAt,
                session.ConfirmedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(new PagedResult<StocktakeListResponse>(rows, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> GetStocktakeAsync(
        Guid id,
        OperationsDbContext operationsDbContext,
        CancellationToken cancellationToken)
    {
        var session = await operationsDbContext.StocktakeSessions
            .Include(value => value.StocktakeAdjustmentLines)
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        return session is null ? Results.NotFound() : Results.Ok(ToDetailResponse(session));
    }

    private static async Task<IResult> CreateStocktakeAsync(
        CreateStocktakeRequest request,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request.LocationId == Guid.Empty)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.LocationId)] = ["Location is required."] });
        }

        var locationExists = await inventoryDbContext.Locations.AnyAsync(location => location.Id == request.LocationId && location.IsActive, cancellationToken);
        if (!locationExists)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.LocationId)] = ["Active location was not found."] });
        }

        var now = clock.EgyptNow;
        var session = new StocktakeSession
        {
            Id = Guid.NewGuid(),
            LocationId = request.LocationId,
            SessionDate = request.SessionDate ?? now,
            PerformedBy = currentUser.UserId ?? Guid.Empty,
            Notes = request.Notes,
            Status = Draft,
            ProductsCounted = 0,
            TotalDiscrepancyUnits = 0,
            CreatedAt = now
        };

        operationsDbContext.StocktakeSessions.Add(session);
        await operationsDbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/stocktakes/{session.Id}", ToDetailResponse(session));
    }

    private static async Task<IResult> UpsertStocktakeLinesAsync(
        Guid id,
        UpsertStocktakeLinesRequest request,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        CancellationToken cancellationToken)
    {
        var session = await operationsDbContext.StocktakeSessions
            .Include(value => value.StocktakeAdjustmentLines)
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (session is null)
        {
            return Results.NotFound();
        }
        if (session.Status != Draft)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Only draft stocktakes can be edited."] });
        }
        if (request.Lines.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Lines)] = ["At least one counted SKU is required."] });
        }

        var duplicateSku = request.Lines
            .GroupBy(line => new { line.SkuId, LotNumber = NormalizeBlank(line.LotNumber), line.ExpiryDate })
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateSku is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Lines)] = ["Each SKU + lot + expiry can appear once per stocktake session."] });
        }

        var skuIds = request.Lines.Select(line => line.SkuId).ToArray();
        var batches = await inventoryDbContext.InventoryBatches
            .Where(batch => batch.LocationId == session.LocationId && skuIds.Contains(batch.SkuId))
            .ToListAsync(cancellationToken);

        await ReplaceStocktakeLinesAsync(operationsDbContext, session, cancellationToken);

        var acceptedLines = new List<StocktakeAdjustmentLine>();

        foreach (var line in request.Lines)
        {
            if (line.PhysicalCount < 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(line.PhysicalCount)] = ["Physical count cannot be negative."] });
            }

            var normalizedLot = NormalizeBlank(line.LotNumber);
            var systemQty = batches
                .Where(batch =>
                    batch.SkuId == line.SkuId &&
                    batch.LotNumber == normalizedLot &&
                    batch.ExpiryDate == line.ExpiryDate)
                .Sum(batch => batch.Quantity);
            acceptedLines.Add(new StocktakeAdjustmentLine
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                SkuId = line.SkuId,
                LotNumber = normalizedLot,
                ExpiryDate = line.ExpiryDate,
                SystemQtyBefore = systemQty,
                PhysicalCount = line.PhysicalCount,
                Delta = line.PhysicalCount - systemQty,
                LineNote = line.LineNote
            });
        }

        foreach (var adjustment in acceptedLines)
        {
            session.StocktakeAdjustmentLines.Add(adjustment);
            operationsDbContext.StocktakeAdjustmentLines.Add(adjustment);
        }

        session.ProductsCounted = acceptedLines.Count;
        session.TotalDiscrepancyUnits = acceptedLines.Sum(line => Math.Abs(line.Delta));
        await operationsDbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToDetailResponse(session));
    }

    private static async Task ReplaceStocktakeLinesAsync(
        OperationsDbContext operationsDbContext,
        StocktakeSession session,
        CancellationToken cancellationToken)
    {
        var trackedLines = session.StocktakeAdjustmentLines.ToList();

        if (operationsDbContext.Database.IsRelational())
        {
            await operationsDbContext.StocktakeAdjustmentLines
                .Where(line => line.SessionId == session.Id)
                .ExecuteDeleteAsync(cancellationToken);

            foreach (var entry in operationsDbContext.ChangeTracker.Entries<StocktakeAdjustmentLine>()
                .Where(entry => entry.Entity.SessionId == session.Id)
                .ToList())
            {
                entry.State = EntityState.Detached;
            }
        }
        else
        {
            var existingLines = await operationsDbContext.StocktakeAdjustmentLines
                .Where(line => line.SessionId == session.Id)
                .ToListAsync(cancellationToken);
            operationsDbContext.StocktakeAdjustmentLines.RemoveRange(existingLines);
            await operationsDbContext.SaveChangesAsync(cancellationToken);

            foreach (var line in trackedLines.Concat(existingLines).DistinctBy(line => line.Id))
            {
                operationsDbContext.Entry(line).State = EntityState.Detached;
            }
        }

        session.StocktakeAdjustmentLines = new List<StocktakeAdjustmentLine>();
    }

    private static async Task<IResult> ConfirmStocktakeAsync(
        Guid id,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        StockLedgerService ledgerService,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var session = await operationsDbContext.StocktakeSessions
            .Include(value => value.StocktakeAdjustmentLines)
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (session is null)
        {
            return Results.NotFound();
        }
        if (session.Status != Draft)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Only draft stocktakes can be confirmed."] });
        }
        if (session.StocktakeAdjustmentLines.Count == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["lines"] = ["Add counted lines before confirming stocktake."] });
        }

        await ExecuteStocktakeTransactionAsync(
            inventoryDbContext,
            operationsDbContext,
            async () =>
            {
                var userId = currentUser.UserId ?? Guid.Empty;
                foreach (var line in session.StocktakeAdjustmentLines.Where(line => line.Delta != 0))
                {
                    await ledgerService.AdjustStocktakeBatchAsync(
                        session.LocationId,
                        line.SkuId,
                        line.LotNumber,
                        line.ExpiryDate,
                        line.Delta,
                        userId,
                        session.Id,
                        cancellationToken);
                }

                session.Status = Confirmed;
                session.ConfirmedBy = userId;
                session.ConfirmedAt = clock.EgyptNow;
                session.ProductsCounted = session.StocktakeAdjustmentLines.Count;
                session.TotalDiscrepancyUnits = session.StocktakeAdjustmentLines.Sum(line => Math.Abs(line.Delta));
                await operationsDbContext.SaveChangesAsync(cancellationToken);
            },
            cancellationToken);

        return Results.Ok(ToDetailResponse(session));
    }

    private static async Task ExecuteStocktakeTransactionAsync(
        InventoryDbContext inventoryDbContext,
        OperationsDbContext operationsDbContext,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        if (!inventoryDbContext.Database.IsRelational() || !operationsDbContext.Database.IsRelational())
        {
            await action();
            return;
        }

        try
        {
            await using var transaction = await inventoryDbContext.Database.BeginTransactionAsync(cancellationToken);
            await operationsDbContext.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
            await action();
            await transaction.CommitAsync(cancellationToken);
        }
        catch (InvalidOperationException exception) when (IsCrossContextTransactionAssociationError(exception))
        {
            await ClearExternalTransactionAsync(operationsDbContext, cancellationToken);
            await action();
        }
        finally
        {
            await ClearExternalTransactionAsync(operationsDbContext, cancellationToken);
        }
    }

    private static bool IsCrossContextTransactionAssociationError(InvalidOperationException exception) =>
        exception.Message.Contains("not associated with the current connection", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("associated with the current connection", StringComparison.OrdinalIgnoreCase);

    private static async Task ClearExternalTransactionAsync(OperationsDbContext operationsDbContext, CancellationToken cancellationToken)
    {
        try
        {
            await operationsDbContext.Database.UseTransactionAsync(null, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // The context may not have accepted the external transaction in the first place.
        }
    }

    private static StocktakeDetailResponse ToDetailResponse(StocktakeSession session) =>
        new(
            session.Id,
            session.LocationId,
            session.SessionDate,
            session.Status,
            session.PerformedBy,
            session.ConfirmedBy,
            session.ProductsCounted ?? 0,
            session.TotalDiscrepancyUnits ?? 0,
            session.Notes,
            session.CreatedAt,
            session.ConfirmedAt,
            session.StocktakeAdjustmentLines
                .OrderBy(line => line.SkuId)
                .Select(line => new StocktakeLineResponse(line.Id, line.SkuId, line.LotNumber, line.ExpiryDate, line.SystemQtyBefore, line.PhysicalCount, line.Delta, line.LineNote))
                .ToList());

    private static string? NormalizeBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record CreateStocktakeRequest(Guid LocationId, DateTime? SessionDate, string? Notes);

public sealed record UpsertStocktakeLinesRequest(IReadOnlyList<StocktakeCountLineRequest> Lines);

public sealed record StocktakeCountLineRequest(Guid SkuId, string? LotNumber, DateOnly? ExpiryDate, int PhysicalCount, string? LineNote);

public sealed record StocktakeListResponse(Guid Id, Guid LocationId, string Status, int ProductsCounted, int TotalDiscrepancyUnits, DateTime CreatedAt, DateTime? ConfirmedAt);

public sealed record StocktakeDetailResponse(Guid Id, Guid LocationId, DateTime SessionDate, string Status, Guid PerformedBy, Guid? ConfirmedBy, int ProductsCounted, int TotalDiscrepancyUnits, string? Notes, DateTime CreatedAt, DateTime? ConfirmedAt, IReadOnlyList<StocktakeLineResponse> Lines);

public sealed record StocktakeLineResponse(Guid Id, Guid SkuId, string? LotNumber, DateOnly? ExpiryDate, int SystemQtyBefore, int PhysicalCount, int Delta, string? LineNote);
