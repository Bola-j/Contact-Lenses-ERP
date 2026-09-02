using Lensee.Host.Infrastructure;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Inventory.Services;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Primitives;
using Lensee.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

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
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!TryResolveReadLocationScope(currentUser, out var locationId))
        {
            return Results.Forbid();
        }

        var request = new PageRequest(page ?? 1, pageSize ?? 25);
        var query = operationsDbContext.StocktakeSessions.AsQueryable();
        if (locationId.HasValue)
        {
            query = query.Where(session => session.LocationId == locationId.Value);
        }
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
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!TryResolveReadLocationScope(currentUser, out var locationId))
        {
            return Results.Forbid();
        }

        var query = operationsDbContext.StocktakeSessions
            .Include(value => value.StocktakeAdjustmentLines)
            .Where(value => value.Id == id);
        if (locationId.HasValue)
        {
            query = query.Where(value => value.LocationId == locationId.Value);
        }

        var session = await query.FirstOrDefaultAsync(cancellationToken);
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
        CatalogDbContext catalogDbContext,
        InventoryDbContext inventoryDbContext,
        StocktakeBalanceLockService balanceLockService,
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

        if (request.Lines.Any(line => line.SkuId == Guid.Empty))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Lines)] = ["Every stocktake line requires a SKU."] });
        }
        if (request.Lines.Any(line => line.PhysicalCount < 0))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(StocktakeCountLineRequest.PhysicalCount)] = ["Physical count cannot be negative."] });
        }

        var skuIds = request.Lines.Select(line => line.SkuId).Distinct().ToArray();
        var activeSkuIds = await catalogDbContext.Skus
            .Where(sku => skuIds.Contains(sku.Id))
            .Where(sku => sku.IsActive)
            .Where(sku => sku.DeletedAt == null)
            .Where(sku => sku.Product.IsActive)
            .Where(sku => sku.Product.DeletedAt == null)
            .Select(sku => sku.Id)
            .ToListAsync(cancellationToken);

        var missingSkuIds = skuIds.Except(activeSkuIds).ToArray();
        if (missingSkuIds.Length > 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Lines)] = ["Every stocktake line SKU must exist and be active."] });
        }

        var acceptedLines = new List<StocktakeAdjustmentLine>();
        try
        {
            await SharedDbTransaction.ExecuteAsync(inventoryDbContext, async () =>
            {
                await LockStocktakeSessionAsync(operationsDbContext, session.Id, cancellationToken);
                await operationsDbContext.Entry(session).ReloadAsync(cancellationToken);
                if (session.Status != Draft)
                {
                    throw new StocktakeConflictException("The stocktake was confirmed while it was being edited.");
                }

                var baselineVersions = await balanceLockService.LockAndEnsureVersionsAsync(session.LocationId, skuIds, cancellationToken);
                var batches = await inventoryDbContext.InventoryBatches
                    .Where(batch => batch.LocationId == session.LocationId && skuIds.Contains(batch.SkuId))
                    .ToListAsync(cancellationToken);

                await ReplaceStocktakeLinesAsync(operationsDbContext, session, cancellationToken);
                acceptedLines.Clear();
                foreach (var line in request.Lines)
                {
                    var normalizedLot = NormalizeBlank(line.LotNumber);
                    var systemQty = batches
                        .Where(batch => batch.SkuId == line.SkuId && batch.LotNumber == normalizedLot && batch.ExpiryDate == line.ExpiryDate)
                        .Sum(batch => batch.Quantity);
                    acceptedLines.Add(new StocktakeAdjustmentLine
                    {
                        Id = Guid.NewGuid(),
                        SessionId = session.Id,
                        SkuId = line.SkuId,
                        LotNumber = normalizedLot,
                        ExpiryDate = line.ExpiryDate,
                        SystemQtyBefore = systemQty,
                        BaselineStockRowVersion = baselineVersions[line.SkuId],
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
            }, cancellationToken, operationsDbContext);
        }
        catch (StocktakeConflictException conflict)
        {
            return Results.Conflict(new { code = "transition-conflict", detail = conflict.Message });
        }
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
        StocktakeBalanceLockService balanceLockService,
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

        try
        {
            await ExecuteStocktakeTransactionAsync(
                inventoryDbContext,
                operationsDbContext,
                async () =>
                {
                    await LockStocktakeSessionAsync(operationsDbContext, id, cancellationToken);
                    operationsDbContext.Entry(session).State = EntityState.Detached;
                    session = await operationsDbContext.StocktakeSessions
                        .Include(value => value.StocktakeAdjustmentLines)
                        .SingleAsync(value => value.Id == id, cancellationToken);
                    if (session.Status != Draft)
                    {
                        throw new StocktakeConflictException("The stocktake was already confirmed.");
                    }

                    var skuIds = session.StocktakeAdjustmentLines.Select(line => line.SkuId).Distinct().ToArray();
                    var currentVersions = await balanceLockService.LockAndEnsureVersionsAsync(session.LocationId, skuIds, cancellationToken);
                    if (session.StocktakeAdjustmentLines.Any(line => line.BaselineStockRowVersion != currentVersions[line.SkuId]))
                    {
                        throw new StocktakeConflictException("Stock changed after this count was captured. Refresh and recount before confirming.");
                    }

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
        }
        catch (StocktakeConflictException conflict)
        {
            return Results.Conflict(new { code = "stale-stocktake-baseline", detail = conflict.Message });
        }

        return Results.Ok(ToDetailResponse(session));
    }

    private static async Task ExecuteStocktakeTransactionAsync(
        InventoryDbContext inventoryDbContext,
        OperationsDbContext operationsDbContext,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        await SharedDbTransaction.ExecuteAsync(inventoryDbContext, action, cancellationToken, operationsDbContext);
    }

    private static async Task LockStocktakeSessionAsync(
        OperationsDbContext operationsDbContext,
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        if (!operationsDbContext.Database.IsRelational())
        {
            return;
        }

        await operationsDbContext.Database.ExecuteSqlInterpolatedAsync(
            $"select 1 from operations.stocktake_sessions where id = {sessionId} for update",
            cancellationToken);
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
                .Select(line => new StocktakeLineResponse(line.Id, line.SkuId, line.LotNumber, line.ExpiryDate, line.SystemQtyBefore, line.PhysicalCount, line.Delta, line.BaselineStockRowVersion, line.LineNote))
                .ToList());

    private static string? NormalizeBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TryResolveReadLocationScope(ICurrentUser currentUser, out Guid? locationId)
    {
        locationId = null;
        if (!string.Equals(LenseeRoles.Normalize(currentUser.Role), LenseeRoles.WarehouseClerk, StringComparison.Ordinal))
        {
            return true;
        }

        if (!currentUser.LocationId.HasValue)
        {
            return false;
        }

        locationId = currentUser.LocationId.Value;
        return true;
    }
}

public sealed record CreateStocktakeRequest(Guid LocationId, DateTime? SessionDate, string? Notes);

public sealed record UpsertStocktakeLinesRequest(IReadOnlyList<StocktakeCountLineRequest> Lines);

public sealed record StocktakeCountLineRequest(Guid SkuId, string? LotNumber, DateOnly? ExpiryDate, int PhysicalCount, string? LineNote);

public sealed record StocktakeListResponse(Guid Id, Guid LocationId, string Status, int ProductsCounted, int TotalDiscrepancyUnits, DateTime CreatedAt, DateTime? ConfirmedAt);

public sealed record StocktakeDetailResponse(Guid Id, Guid LocationId, DateTime SessionDate, string Status, Guid PerformedBy, Guid? ConfirmedBy, int ProductsCounted, int TotalDiscrepancyUnits, string? Notes, DateTime CreatedAt, DateTime? ConfirmedAt, IReadOnlyList<StocktakeLineResponse> Lines);

public sealed record StocktakeLineResponse(Guid Id, Guid SkuId, string? LotNumber, DateOnly? ExpiryDate, int SystemQtyBefore, int PhysicalCount, int Delta, int BaselineStockRowVersion, string? LineNote);

internal sealed class StocktakeConflictException(string message) : Exception(message);
