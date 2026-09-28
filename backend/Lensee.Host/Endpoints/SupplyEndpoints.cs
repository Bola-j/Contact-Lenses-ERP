using System.Security.Cryptography;
using System.Text.Json;
using Lensee.Host.Infrastructure;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Finance.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Inventory.Services;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static class SupplyEndpoints
{
    private const string Draft = "Draft";
    private const string Arrived = "Arrived";
    private const string PartiallyReceived = "PartiallyReceived";
    private const string Received = "Received";
    private const string Cancelled = "Cancelled";
    private const string InventoryReceipt = "InventoryReceipt";
    // Legacy private helpers still support historical correction data but are no
    // longer reachable through Supply routes.
    private static readonly string[] AllowedPaymentCategories = ["SupplierPurchase", "Freight", "Customs", "Transport", "OtherShipmentCost"];
    private static readonly string[] AllowedMovementMethods = ["CashHandToHand", "CashTransaction", "BankTransfer", "Wallet"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapSupplyEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/supply/shipments")
            .WithTags("Supply")
            .RequireAuthorization();

        group.MapGet("/", ListShipmentsAsync).RequireAuthorization("supply.read").WithName("ListSupplyShipments");
        group.MapGet("/{id:guid}", GetShipmentAsync).RequireAuthorization("supply.read").WithName("GetSupplyShipment");
        group.MapGet("/{id:guid}/lines", GetLinesAsync).RequireAuthorization("supply.read").WithName("GetSupplyShipmentLines");
        group.MapGet("/{id:guid}/editor", GetEditorAsync).RequireAuthorization("supply.write").WithName("GetSupplyShipmentEditor");
        group.MapGet("/{id:guid}/history", GetHistoryAsync).RequireAuthorization("supply.read").WithName("GetSupplyShipmentHistory");
        group.MapPost("/", CreateShipmentAsync).RequireAuthorization("supply.write").WithName("CreateSupplyShipment");
        group.MapPut("/{id:guid}", UpdateShipmentAsync).RequireAuthorization("supply.write").WithName("UpdateSupplyShipment");
        group.MapPost("/{id:guid}/confirm", ConfirmShipmentAsync).RequireAuthorization("supply.write").WithName("ConfirmSupplyShipment");
        group.MapPost("/{id:guid}/receiving-sessions", CreateReceivingSessionAsync).RequireAuthorization("supply.receive").WithName("CreateSupplyReceivingSession");
        group.MapPut("/{id:guid}/receiving-sessions/{sessionId:guid}/lines", SaveReceivingLinesAsync).RequireAuthorization("supply.receive").WithName("SaveSupplyReceivingLines");
        group.MapPost("/{id:guid}/receiving-sessions/{sessionId:guid}/confirm", ConfirmReceivingSessionAsync).RequireAuthorization("supply.receive").WithName("ConfirmSupplyReceivingSession");
        group.MapPost("/{id:guid}/cancel", CancelShipmentAsync).RequireAuthorization("supply.write").WithName("CancelSupplyShipment");

        return group;
    }

    private static async Task<IResult> ListShipmentsAsync(
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        string? search,
        string? status,
        bool? paged,
        int? page,
        int? pageSize,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var query = operationsDbContext.SupplyShipments.AsNoTracking().AsQueryable();
        if (IsWarehouseClerk(currentUser))
        {
            if (currentUser.LocationId is not Guid assignedLocationId) return Results.Forbid();
            query = query.Where(value => value.DestinationLocationId == assignedLocationId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(value =>
                value.ShipmentNumber.ToLower().Contains(term) ||
                value.SupplierName.ToLower().Contains(term) ||
                (value.InvoiceNumber != null && value.InvoiceNumber.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(value => value.Status == NormalizeStatus(status));
        }

        var request = new PageRequest(page ?? 1, pageSize ?? 25);
        var total = paged == true ? await query.CountAsync(cancellationToken) : 0;
        var orderedQuery = query
            .OrderByDescending(value => value.CreatedAt)
            .ThenByDescending(value => value.Id);
        var boundedQuery = paged == true
            ? orderedQuery.Skip(request.Skip).Take(request.PageSize)
            : orderedQuery.Take(100);
        var shipments = await boundedQuery
            .Select(value => new SupplyShipmentListResponse(
                value.Id,
                value.ShipmentNumber,
                value.SupplierName,
                value.InvoiceNumber,
                value.ShipmentDate,
                value.Status,
                value.ConcurrencyVersion,
                value.DestinationLocationId,
                null,
                value.Lines.Sum(line => line.Quantity),
                value.InventoryReceiptOperationId,
                value.InventoryReceiptOperation != null ? value.InventoryReceiptOperation.OperationNumber : null,
                value.CreatedAt))
            .ToListAsync(cancellationToken);

        var locationLookup = await LoadLocationLookupAsync(inventoryDbContext, shipments.Select(value => value.DestinationLocationId), cancellationToken);

        var rows = shipments.Select(value => value with
        {
            DestinationLocationName = locationLookup.GetValueOrDefault(value.DestinationLocationId)?.Name
        }).ToList();

        return paged == true
            ? Results.Ok(new PagedResult<SupplyShipmentListResponse>(rows, request.Page, request.PageSize, total))
            : Results.Ok(rows);
    }

    private static async Task<IResult> GetShipmentAsync(Guid id, bool? includeCollections, OperationsDbContext operationsDbContext, InventoryDbContext inventoryDbContext, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var shipment = includeCollections == false
            ? await LoadShipmentSummaryAsync(operationsDbContext, id, cancellationToken)
            : await LoadShipmentAsync(operationsDbContext, id, cancellationToken);
        if (shipment is null)
        {
            return Results.NotFound();
        }
        if (!CanAccessShipmentLocation(currentUser, shipment.DestinationLocationId)) return Results.Forbid();

        var locationLookup = await LoadLocationLookupAsync(inventoryDbContext, [shipment.DestinationLocationId], cancellationToken);
        var response = ToDetailResponse(shipment, locationLookup, includeCollections != false);
        if (includeCollections == false)
        {
            response = response with
            {
                LineCount = await operationsDbContext.SupplyShipmentLines.AsNoTracking().CountAsync(value => value.ShipmentId == id, cancellationToken)
            };
        }
        return Results.Ok(response);
    }

    private static async Task<IResult> GetLinesAsync(Guid id, int? page, int? pageSize, OperationsDbContext dbContext, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var destination = await dbContext.SupplyShipments.AsNoTracking().Where(value => value.Id == id).Select(value => (Guid?)value.DestinationLocationId).SingleOrDefaultAsync(cancellationToken);
        if (destination is null) return Results.NotFound();
        if (!CanAccessShipmentLocation(currentUser, destination.Value)) return Results.Forbid();
        var request = new PageRequest(page ?? 1, pageSize ?? 50);
        var query = dbContext.SupplyShipmentLines.AsNoTracking().Where(value => value.ShipmentId == id);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(value => value.Id).Skip(request.Skip).Take(request.PageSize)
            .Select(line => new SupplyLineResponse(line.Id, line.SkuId, line.SkuCodeSnapshot, line.ProductNameSnapshot, line.Quantity, line.LotNumber, line.ExpiryDate, line.Notes))
            .ToListAsync(cancellationToken);
        return Results.Ok(new PagedResult<SupplyLineResponse>(rows, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> GetCostsAsync(Guid id, OperationsDbContext dbContext, CancellationToken cancellationToken)
    {
        if (!await dbContext.SupplyShipments.AsNoTracking().AnyAsync(value => value.Id == id, cancellationToken)) return Results.NotFound();
        var rows = await dbContext.SupplyShipmentCosts.AsNoTracking().Where(value => value.ShipmentId == id).OrderBy(value => value.Id)
            .Select(cost => new SupplyCostResponse(cost.Id, cost.CostType, cost.Description, cost.Amount)).ToListAsync(cancellationToken);
        return Results.Ok(rows);
    }

    private static async Task<IResult> GetEditorAsync(Guid id, OperationsDbContext dbContext, InventoryDbContext inventoryDbContext, CancellationToken cancellationToken)
    {
        var shipment = await dbContext.SupplyShipments.AsNoTracking()
            .Include(value => value.Lines).Include(value => value.InventoryReceiptOperation)
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (shipment is null) return Results.NotFound();
        return Results.Ok(new SupplyShipmentEditorResponse(
            shipment.Id, shipment.SupplierName, shipment.InvoiceNumber, shipment.ShipmentDate, shipment.Status,
            shipment.ConcurrencyVersion, shipment.DestinationLocationId, shipment.Notes,
            shipment.Lines.OrderBy(value => value.Id).Select(line => new SupplyEditorLineResponse(line.Id, line.SkuId, line.SkuCodeSnapshot, line.ProductNameSnapshot, line.Quantity, line.LotNumber, line.ExpiryDate, line.Notes)).ToList()));
    }

    private static async Task<IResult> GetHistoryAsync(Guid id, OperationsDbContext operationsDbContext, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var destination = await operationsDbContext.SupplyShipments.AsNoTracking().Where(value => value.Id == id).Select(value => (Guid?)value.DestinationLocationId).SingleOrDefaultAsync(cancellationToken);
        if (destination is null)
        {
            return Results.NotFound();
        }
        if (!CanAccessShipmentLocation(currentUser, destination.Value)) return Results.Forbid();

        var history = await operationsDbContext.SupplyShipmentHistoryLogs
            .Where(value => value.ShipmentId == id)
            .OrderByDescending(value => value.CreatedAt)
            .Select(value => new SupplyHistoryResponse(value.Id, value.Action, value.ActorUserId, value.CreatedAt, value.Summary))
            .ToListAsync(cancellationToken);

        return Results.Ok(history);
    }

    private static async Task<IResult> ListPaymentsAsync(Guid id, OperationsDbContext dbContext, CancellationToken cancellationToken)
    {
        if (!await dbContext.SupplyShipments.AsNoTracking().AnyAsync(value => value.Id == id, cancellationToken)) return Results.NotFound();
        var payments = await dbContext.SupplyPayments.AsNoTracking().Where(value => value.ShipmentId == id)
            .OrderByDescending(value => value.CreatedAt).ToListAsync(cancellationToken);
        var rows = payments.Select(ToPaymentResponse).ToList();
        var posted = rows.Where(value => value.Status == "Posted").Sum(value => value.Amount);
        var shipmentTotal = await dbContext.SupplyShipments.Where(value => value.Id == id).Select(value => value.LandedTotal).SingleAsync(cancellationToken);
        return Results.Ok(new SupplyPaymentSummaryResponse(rows, posted, shipmentTotal <= 0 || posted <= 0 ? "Unpaid" : posted < shipmentTotal ? "PartiallyPaid" : "Paid"));
    }

    private static async Task<IResult> CreatePaymentAsync(Guid id, SupplyPaymentRequest request, OperationsDbContext dbContext, FinanceDbContext financeDbContext, ICurrentUser currentUser, IClock clock, CancellationToken cancellationToken)
    {
        if (!await dbContext.SupplyShipments.AsNoTracking().AnyAsync(value => value.Id == id, cancellationToken)) return Results.NotFound();
        var errors = await ValidatePaymentRequestAsync(request, financeDbContext, cancellationToken);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var supplyFinanceLogId = await financeDbContext.SupplyFinanceLogs.AsNoTracking()
            .Where(value => value.SupplyShipmentId == id)
            .Select(value => (Guid?)value.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (supplyFinanceLogId is null)
            return Results.Conflict(new { code = "supply-finance-log-missing", detail = "The shipment has no Supply Finance Log." });
        var payment = new SupplyPayment
        {
            Id = Guid.NewGuid(),
            ShipmentId = id,
            SupplyFinanceLogId = supplyFinanceLogId,
            Category = request.Category!.Trim(),
            Amount = request.Amount,
            MovementMethod = request.MovementMethod!.Trim(),
            FinanceAccountId = request.FinanceAccountId,
            ExternalReference = FinanceLedgerService.NormalizeExternalReference(request.ExternalReference),
            Notes = TrimToNull(request.Notes),
            Status = Draft,
            CreatedBy = currentUser.UserId ?? Guid.Empty,
            CreatedAt = clock.EgyptNow,
            CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? Guid.NewGuid().ToString("N") : request.CorrelationId.Trim()
        };
        dbContext.SupplyPayments.Add(payment);
        AddHistory(dbContext, new SupplyShipment { Id = id }, "SupplyPaymentCreate", payment.CreatedBy, payment.CreatedAt, $"Supply payment {payment.Id} created as draft.");
        await PersistenceBoundary.CommitAsync(dbContext, cancellationToken);
        return Results.Created($"/api/v1/supply/shipments/{id}/payments/{payment.Id}", ToPaymentResponse(payment));
    }

    private static async Task<IResult> SubmitPaymentAsync(Guid id, Guid paymentId, OperationsDbContext dbContext, ICurrentUser currentUser, IClock clock, CancellationToken cancellationToken)
    {
        var payment = await dbContext.SupplyPayments.SingleOrDefaultAsync(value => value.Id == paymentId && value.ShipmentId == id, cancellationToken);
        if (payment is null) return Results.NotFound();
        if (payment.Status != Draft) return Results.Conflict(new { code = "invalid-transition", detail = "Only draft supply payments can be submitted." });
        payment.Status = "PendingReview"; payment.SubmittedBy = currentUser.UserId ?? Guid.Empty; payment.SubmittedAt = clock.EgyptNow;
        try
        {
            await PersistenceBoundary.CommitAsync(dbContext, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { code = "supply-payment-submit-conflict", detail = "The supply payment was changed by another request. Refresh and try again." });
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { code = "supply-payment-submit-save-conflict", detail = "The supply payment could not be submitted in its current state." });
        }
        return Results.Ok(ToPaymentResponse(payment));
    }

    private static async Task<IResult> ApprovePaymentAsync(Guid id, Guid paymentId, OperationsDbContext operationsDbContext, FinanceDbContext financeDbContext, FinanceLedgerService financeLedgerService, ICurrentUser currentUser, IClock clock, CancellationToken cancellationToken)
    {
        SupplyPayment? posted = null;
        try
        {
            await SharedDbTransaction.ExecuteAsync(operationsDbContext, async () =>
            {
                await LockShipmentAsync(operationsDbContext, id, cancellationToken);
                await LockSupplyPaymentAsync(operationsDbContext, paymentId, cancellationToken);
                var payment = await operationsDbContext.SupplyPayments.SingleOrDefaultAsync(value => value.Id == paymentId && value.ShipmentId == id, cancellationToken) ?? throw new KeyNotFoundException();
                if (payment.Status == "Posted") { posted = payment; return; }
                if (payment.Status != "PendingReview") throw new InvalidOperationException("Only submitted supply payments can be approved.");
                var actorId = currentUser.UserId ?? Guid.Empty;
                if (payment.CreatedBy == actorId || payment.SubmittedBy == actorId) throw new InvalidOperationException("The supply-payment creator cannot approve it.");
                if (payment.ReversesPaymentId is Guid originalPaymentId)
                {
                    await LockSupplyPaymentAsync(operationsDbContext, originalPaymentId, cancellationToken);
                    var original = await operationsDbContext.SupplyPayments.SingleAsync(value => value.Id == originalPaymentId, cancellationToken);
                    if (original.Status != "Posted" || original.PostedFinanceLedgerEntryId is null) throw new InvalidOperationException("The original supply payment is not posted.");
                    var originalEntry = await financeDbContext.FinanceLedgerEntries.SingleAsync(value => value.Id == original.PostedFinanceLedgerEntryId.Value, cancellationToken);
                    await financeLedgerService.ReverseMovementAsync(originalEntry, "SupplyPaymentReversal", payment.Id, actorId, payment.CorrelationId, cancellationToken, "Supply");
                    original.Status = "Corrected"; original.ReplacedByPaymentId = payment.Id;
                }
                var entry = await financeLedgerService.PostMovementAsync("SupplyPayment", payment.Id, "Supply", payment.MovementMethod, payment.Amount, payment.FinanceAccountId, payment.ExternalReference, payment.Category, FinanceLedgerService.Debit, actorId, null, payment.CorrelationId, cancellationToken);
                payment.Status = "Posted"; payment.ReviewedBy = actorId; payment.ReviewedAt = clock.EgyptNow; payment.PostedFinanceLedgerEntryId = entry.Id;
                posted = payment;
                await PersistenceBoundary.CommitAsync(operationsDbContext, cancellationToken);
            }, cancellationToken, financeDbContext);
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { code = "supply-payment-transition-conflict", detail = "The supply payment was changed by another request. Refresh and try again." }); }
        catch (DbUpdateException) { return Results.Conflict(new { code = "supply-payment-save-conflict", detail = "The supply payment could not be saved in its current state." }); }
        catch (InvalidOperationException) { return Results.Conflict(new { code = "supply-payment-posting-rejected", detail = "The supply payment could not be posted in its current state." }); }
        return Results.Ok(ToPaymentResponse(posted!));
    }

    private static async Task<IResult> RejectPaymentAsync(Guid id, Guid paymentId, SupplyPaymentRejectionRequest request, OperationsDbContext dbContext, ICurrentUser currentUser, IClock clock, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Reason)] = ["A rejection reason is required."] });
        await using var transaction = dbContext.Database.IsRelational()
            ? await PersistenceBoundary.OpenTransactionAsync(dbContext, cancellationToken)
            : null;
        await LockSupplyPaymentAsync(dbContext, paymentId, cancellationToken);
        var payment = await dbContext.SupplyPayments.SingleOrDefaultAsync(value => value.Id == paymentId && value.ShipmentId == id, cancellationToken);
        if (payment is null) return Results.NotFound();
        if (payment.Status != "PendingReview") return Results.Conflict(new { code = "invalid-transition", detail = "Only submitted supply payments can be rejected." });
        payment.Status = "Rejected"; payment.ReviewedBy = currentUser.UserId ?? Guid.Empty; payment.ReviewedAt = clock.EgyptNow; payment.RejectionReason = request.Reason.Trim();
        try
        {
            await PersistenceBoundary.CommitAsync(dbContext, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { code = "supply-payment-rejection-conflict", detail = "The supply payment was changed by another request. Refresh and try again." });
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { code = "supply-payment-rejection-save-conflict", detail = "The supply payment rejection could not be saved in its current state." });
        }
        return Results.Ok(ToPaymentResponse(payment));
    }

    private static async Task<IResult> CorrectPaymentAsync(Guid id, Guid paymentId, SupplyPaymentRequest request, OperationsDbContext dbContext, FinanceDbContext financeDbContext, ICurrentUser currentUser, IClock clock, CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await PersistenceBoundary.OpenTransactionAsync(dbContext, cancellationToken)
            : null;
        await LockSupplyPaymentAsync(dbContext, paymentId, cancellationToken);
        var original = await dbContext.SupplyPayments.SingleOrDefaultAsync(value => value.Id == paymentId && value.ShipmentId == id, cancellationToken);
        if (original is null) return Results.NotFound();
        if (original.Status != "Posted" || original.ReplacedByPaymentId is not null) return Results.Conflict(new { code = "supply-payment-not-correctable", detail = "Only an unreplaced posted supply payment can be corrected." });
        var errors = await ValidatePaymentRequestAsync(request, financeDbContext, cancellationToken); if (errors.Count > 0) return Results.ValidationProblem(errors);
        var replacement = new SupplyPayment { Id = Guid.NewGuid(), ShipmentId = id, SupplyFinanceLogId = original.SupplyFinanceLogId, Category = request.Category!.Trim(), Amount = request.Amount, MovementMethod = request.MovementMethod!.Trim(), FinanceAccountId = request.FinanceAccountId, ExternalReference = FinanceLedgerService.NormalizeExternalReference(request.ExternalReference), Notes = TrimToNull(request.Notes), Status = Draft, CreatedBy = currentUser.UserId ?? Guid.Empty, CreatedAt = clock.EgyptNow, ReversesPaymentId = original.Id, CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? Guid.NewGuid().ToString("N") : request.CorrelationId.Trim() };
        dbContext.SupplyPayments.Add(replacement);
        try
        {
            await PersistenceBoundary.CommitAsync(dbContext, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { code = "supply-payment-correction-conflict", detail = "The original supply payment was changed by another request. Refresh and try again." });
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { code = "supply-payment-correction-save-conflict", detail = "The supply payment correction could not be saved." });
        }
        return Results.Created($"/api/v1/supply/shipments/{id}/payments/{replacement.Id}", ToPaymentResponse(replacement));
    }

    private static async Task<IResult> CreateShipmentAsync(
        SupplyShipmentRequest request,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        InventoryDbContext inventoryDbContext,
        SupplyFinanceLogService supplyFinanceLogService,
        FinanceDbContext financeDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var built = await BuildShipmentPartsAsync(request, catalogDbContext, inventoryDbContext, cancellationToken);
        if (built.Errors.Count > 0)
        {
            return Results.ValidationProblem(built.Errors);
        }

        var now = clock.EgyptNow;
        var shipment = new SupplyShipment
        {
            Id = Guid.NewGuid(),
            ShipmentNumber = $"SUP-{now:yyyyMMddHHmmss}-{RandomNumberGenerator.GetInt32(100, 1000)}",
            SupplierName = request.SupplierName!.Trim(),
            InvoiceNumber = TrimToNull(request.InvoiceNumber),
            ShipmentDate = request.ShipmentDate ?? now,
            DestinationLocationId = request.DestinationLocationId,
            Status = Draft,
            Notes = TrimToNull(request.Notes),
            CreatedBy = currentUser.UserId ?? Guid.Empty,
            CreatedAt = now
        };

        ApplyParts(shipment, built);
        operationsDbContext.SupplyShipments.Add(shipment);
        AddHistory(operationsDbContext, shipment, "Create", currentUser.UserId ?? Guid.Empty, now, "Shipment ordered; financial details are managed in Finance.");
        await SharedDbTransaction.ExecuteAsync(operationsDbContext, async () =>
        {
            await PersistenceBoundary.CommitAsync(operationsDbContext, cancellationToken);
            await supplyFinanceLogService.EnsureAsync(shipment.Id, shipment.ShipmentNumber, shipment.SupplierName, shipment.Notes, currentUser.UserId ?? Guid.Empty, cancellationToken);
        }, cancellationToken, financeDbContext);

        return Results.Created($"/api/v1/supply/shipments/{shipment.Id}", ToDetailResponse(shipment, built.LocationLookup));
    }

    private static async Task<IResult> UpdateShipmentAsync(
        Guid id,
        SupplyShipmentRequest request,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        InventoryDbContext inventoryDbContext,
        SupplyFinanceLogService supplyFinanceLogService,
        FinanceDbContext financeDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var shipment = await operationsDbContext.SupplyShipments.FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (shipment is null)
        {
            return Results.NotFound();
        }

        if (shipment.Status != Draft)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(shipment.Status)] = ["Only draft supply shipments can be edited."] });
        }
        if (operationsDbContext.Database.IsRelational() && request.ExpectedVersion is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.ExpectedVersion)] = ["expectedVersion is required when editing a supply shipment."] });
        }

        var built = await BuildShipmentPartsAsync(request, catalogDbContext, inventoryDbContext, cancellationToken);
        if (built.Errors.Count > 0)
        {
            return Results.ValidationProblem(built.Errors);
        }

        await using var transaction = operationsDbContext.Database.IsRelational()
            ? await PersistenceBoundary.OpenTransactionAsync(operationsDbContext, cancellationToken)
            : null;

        await LockShipmentAsync(operationsDbContext, shipment.Id, cancellationToken);
        await operationsDbContext.Entry(shipment).ReloadAsync(cancellationToken);
        if (shipment.Status != Draft)
        {
            return Results.Conflict(new { code = "transition-conflict", detail = "The shipment is no longer a draft." });
        }
        if (request.ExpectedVersion.HasValue && shipment.ConcurrencyVersion != request.ExpectedVersion.Value)
        {
            return Results.Conflict(new { code = "stale-version", detail = "The shipment has been changed by another request." });
        }

        var oldLines = await operationsDbContext.SupplyShipmentLines.Where(value => value.ShipmentId == shipment.Id).ToListAsync(cancellationToken);
        operationsDbContext.SupplyShipmentLines.RemoveRange(oldLines);

        var now = clock.EgyptNow;
        shipment.SupplierName = request.SupplierName!.Trim();
        shipment.InvoiceNumber = TrimToNull(request.InvoiceNumber);
        shipment.ShipmentDate = request.ShipmentDate ?? now;
        shipment.DestinationLocationId = request.DestinationLocationId;
        shipment.Notes = TrimToNull(request.Notes);
        shipment.UpdatedBy = currentUser.UserId ?? Guid.Empty;
        shipment.UpdatedAt = now;
        shipment.ProductSubtotal = 0m;
        shipment.CostSubtotal = 0m;
        shipment.LandedTotal = 0m;
        foreach (var line in built.Lines)
        {
            line.ShipmentId = shipment.Id;
        }

        operationsDbContext.SupplyShipmentLines.AddRange(built.Lines);
        operationsDbContext.SupplyShipmentHistoryLogs.Add(new SupplyShipmentHistory
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipment.Id,
            Action = "Update",
            ActorUserId = currentUser.UserId ?? Guid.Empty,
            CreatedAt = now,
            Summary = "Draft shipment updated.",
            SnapshotData = JsonSerializer.Serialize(new
            {
                shipment.ShipmentNumber,
                shipment.SupplierName,
                shipment.InvoiceNumber,
                shipment.ShipmentDate,
                shipment.DestinationLocationId,
                shipment.Status,
                Lines = built.Lines.Select(line => new { line.SkuId, line.SkuCodeSnapshot, line.Quantity, line.LotNumber, line.ExpiryDate })
            }, JsonOptions)
        });
        await SharedDbTransaction.ExecuteAsync(operationsDbContext, async () =>
        {
            await PersistenceBoundary.CommitAsync(operationsDbContext, cancellationToken);
            await supplyFinanceLogService.EnsureAsync(shipment.Id, shipment.ShipmentNumber, shipment.SupplierName, shipment.Notes, currentUser.UserId ?? Guid.Empty, cancellationToken);
        }, cancellationToken, financeDbContext);
        return Results.NoContent();
    }

    private static async Task<IResult> ConfirmShipmentAsync(
        Guid id,
        OperationsDbContext operationsDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var shipment = await LoadShipmentAsync(operationsDbContext, id, cancellationToken);
        if (shipment is null)
        {
            return Results.NotFound();
        }

        if (shipment.Status != Draft)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(shipment.Status)] = ["Only draft supply shipments can be confirmed."] });
        }

        var now = clock.EgyptNow;
        var userId = currentUser.UserId ?? Guid.Empty;
        shipment.Status = Arrived;
        shipment.ConfirmedAt = now;
        shipment.ConfirmedBy = userId;
        AddHistory(operationsDbContext, shipment, "ArrivalConfirm", userId, now, "Shipment arrival confirmed; stock remains pending physical receiving.");
        try
        {
            await PersistenceBoundary.CommitAsync(operationsDbContext, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { code = "supply-arrival-transition-conflict", detail = "The shipment was changed by another request. Refresh and try again." });
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { code = "supply-arrival-save-conflict", detail = "The shipment arrival could not be saved because its current state changed." });
        }

        return Results.NoContent();
    }

    private static async Task<IResult> CreateReceivingSessionAsync(
        Guid id, OperationsDbContext operations, ICurrentUser user, IClock clock, CancellationToken ct)
    {
        var shipment = await LoadShipmentAsync(operations, id, ct);
        if (shipment is null) return Results.NotFound();
        if (!CanAccessShipmentLocation(user, shipment.DestinationLocationId)) return Results.Forbid();
        if (shipment.Status is not (Arrived or PartiallyReceived)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["shipment"] = ["Only arrived supply shipments with an outstanding quantity can be received."] });
        var existingSession = await operations.SupplyReceivingSessions
            .AsNoTracking()
            .Include(value => value.Lines)
            .SingleOrDefaultAsync(value => value.ShipmentId == id && value.Status == Draft, ct);
        if (existingSession is not null)
        {
            return Results.Ok(ToReceivingSessionResponse(existingSession));
        }
        var now = clock.EgyptNow;
        var session = new SupplyReceivingSession { Id = Guid.NewGuid(), ShipmentId = id, Status = Draft, Notes = $"Physical receipt for {shipment.ShipmentNumber}", CreatedBy = user.UserId ?? Guid.Empty, CreatedAt = now };
        foreach (var manifest in shipment.Lines)
            session.Lines.Add(new SupplyReceivingLine { Id = Guid.NewGuid(), ReceivingSessionId = session.Id, ShipmentLineId = manifest.Id, ReceivedQuantity = 0, Notes = manifest.Notes });
        operations.SupplyReceivingSessions.Add(session);
        AddHistory(operations, shipment, "ReceivingSessionCreate", user.UserId ?? Guid.Empty, now, $"Receiving count session {session.Id} created.");
        try
        {
            await PersistenceBoundary.CommitAsync(operations, ct);
        }
        catch (DbUpdateException)
        {
            var concurrentlyCreatedSession = await operations.SupplyReceivingSessions
                .AsNoTracking()
                .Include(value => value.Lines)
                .SingleOrDefaultAsync(value => value.ShipmentId == id && value.Status == Draft, ct);
            if (concurrentlyCreatedSession is not null)
            {
                return Results.Ok(ToReceivingSessionResponse(concurrentlyCreatedSession));
            }
            return Results.Conflict(new { code = "supply-receiving-session-already-open" });
        }
        return Results.Created($"/api/v1/supply/shipments/{id}/receiving-sessions/{session.Id}", ToReceivingSessionResponse(session));
    }

    private static async Task<IResult> SaveReceivingLinesAsync(Guid id, Guid sessionId, IReadOnlyList<SupplyReceivingLineRequest> request, OperationsDbContext operations, ICurrentUser user, CancellationToken ct)
    {
        await using var transaction = operations.Database.IsRelational()
            ? await PersistenceBoundary.OpenTransactionAsync(operations, ct)
            : null;
        await LockReceivingSessionAsync(operations, sessionId, ct);
        var session = await operations.SupplyReceivingSessions.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == sessionId && x.ShipmentId == id, ct);
        if (session is null) return Results.NotFound();
        var destination = await operations.SupplyShipments.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.DestinationLocationId).SingleOrDefaultAsync(ct);
        if (destination is null) return Results.NotFound();
        if (!CanAccessShipmentLocation(user, destination.Value)) return Results.Forbid();
        if (session.Status != Draft) return Results.ValidationProblem(new Dictionary<string,string[]> { ["session"] = ["Only draft receiving sessions can be changed."] });
        if (request.GroupBy(x => x.ShipmentLineId).Any(group => group.Count() > 1) || request.Any(x => x.ReceivedQuantity < 0 || x.LotNumber?.Length > 100 || x.Notes?.Length > 1000) || request.Select(x => x.ShipmentLineId).Except(session.Lines.Select(x => x.ShipmentLineId)).Any())
            return Results.ValidationProblem(new Dictionary<string,string[]> { ["lines"] = ["Receiving lines must be unique, belong to the shipment, and use valid non-negative values."] });
        foreach (var item in request)
        {
            var line = session.Lines.Single(x => x.ShipmentLineId == item.ShipmentLineId);
            line.ReceivedQuantity = item.ReceivedQuantity;
            line.LotNumber = TrimToNull(item.LotNumber);
            line.ExpiryDate = item.ExpiryDate;
            line.Notes = TrimToNull(item.Notes);
        }
        try
        {
            await PersistenceBoundary.CommitAsync(operations, ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { code = "supply-receiving-lines-transition-conflict", detail = "The receiving session changed while saving its lines. Refresh and try again." });
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { code = "supply-receiving-lines-save-conflict", detail = "The receiving lines could not be saved." });
        }
        return Results.NoContent();
    }

    private static async Task<IResult> ConfirmReceivingSessionAsync(Guid id, Guid sessionId, OperationsDbContext operations, InventoryDbContext inventory, CatalogDbContext catalog, StockLedgerService ledgerService, ICurrentUser user, IClock clock, CancellationToken ct)
    {
        var now = clock.EgyptNow; var actor = user.UserId ?? Guid.Empty;
        try { await SharedDbTransaction.ExecuteAsync(inventory, async () =>
        {
            await LockShipmentAsync(operations, id, ct);
            var shipment = await operations.SupplyShipments.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException();
            if (!CanAccessShipmentLocation(user, shipment.DestinationLocationId)) throw new UnauthorizedAccessException();
            var session = await operations.SupplyReceivingSessions.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == sessionId && x.ShipmentId == id, ct) ?? throw new KeyNotFoundException();
            if (shipment.Status is not (Arrived or PartiallyReceived) || session.Status != Draft) throw new InvalidOperationException("Receiving session is no longer confirmable.");
            var lines = session.Lines.Where(x => x.ReceivedQuantity > 0).ToArray();
            if (lines.Length == 0) throw new ArgumentException("Enter at least one received quantity.");
            var manifestById = shipment.Lines.ToDictionary(x => x.Id);
            if (lines.Any(x => !manifestById.ContainsKey(x.ShipmentLineId))) throw new ArgumentException("Receiving line is not in the shipment manifest.");
            var priorRows = await operations.SupplyReceivingLines
                .Where(x => x.ReceivingSession.ShipmentId == id && x.ReceivingSession.Status == "Confirmed")
                .GroupBy(x => x.ShipmentLineId)
                .Select(group => new { ShipmentLineId = group.Key, ReceivedQuantity = group.Sum(line => line.ReceivedQuantity) })
                .ToListAsync(ct);
            var prior = priorRows.ToDictionary(row => row.ShipmentLineId, row => row.ReceivedQuantity);
            if (lines.Any(x => x.ReceivedQuantity + prior.GetValueOrDefault(x.ShipmentLineId) > manifestById[x.ShipmentLineId].Quantity)) throw new ArgumentException("A receiving quantity exceeds the outstanding manifest quantity.");
            var skuIds = lines.Select(x => manifestById[x.ShipmentLineId].SkuId).Distinct().ToArray();
            if (await catalog.Skus.CountAsync(x => skuIds.Contains(x.Id) && x.IsActive && x.Product.IsActive && x.Product.DeletedAt == null, ct) != skuIds.Length) throw new ArgumentException("A received SKU or product is inactive or unavailable.");
            var operation = new OperationLog { Id = Guid.NewGuid(), OperationNumber = $"OP-{now:yyyyMMddHHmmss}-{RandomNumberGenerator.GetInt32(100, 1000)}", OperationType = InventoryReceipt, Status = Received, DestinationLocationId = shipment.DestinationLocationId, Notes = $"Supply receipt {shipment.ShipmentNumber}. {session.Notes}".Trim(), CreatedBy = actor, CreatedAt = now, ConfirmedBy = actor, ConfirmedAt = now };
            foreach (var count in lines)
            {
                var manifest = manifestById[count.ShipmentLineId];
                var lot = count.LotNumber ?? manifest.LotNumber;
                var expiry = count.ExpiryDate ?? manifest.ExpiryDate;
                if (string.IsNullOrWhiteSpace(lot) || expiry is null) throw new ArgumentException("Each received SKU requires a batch number and expiry date.");
                operation.OperationLines.Add(new OperationLine { Id = Guid.NewGuid(), OperationId = operation.Id, SkuId = manifest.SkuId, ProductNameSnapshot = manifest.ProductNameSnapshot, SkuCodeSnapshot = manifest.SkuCodeSnapshot, Section = "Standard", Quantity = count.ReceivedQuantity, EntryMode = "Pieces", BonusQuantity = 0, UnitPrice = 0m, UnitCost = null, LineTotal = 0m, LotNumber = lot, ExpiryDate = expiry, LineNotes = count.Notes ?? manifest.Notes });
            }
            await ledgerService.ReceiveSupplyBatchAsync(shipment.DestinationLocationId, lines.Select(x => { var m = manifestById[x.ShipmentLineId]; return new SupplyReceiptLine(m.SkuId, x.ReceivedQuantity, x.LotNumber ?? m.LotNumber!, x.ExpiryDate ?? m.ExpiryDate!.Value, x.Notes ?? m.Notes); }).ToArray(), actor, operation.Id, ct);
            operation.InventoryReceiptHeader = new InventoryReceiptHeader { Id = Guid.NewGuid(), OperationId = operation.Id, SupplierName = shipment.SupplierName, InvoiceNumber = shipment.InvoiceNumber, ReceiptDate = now, SupplyShipmentId = shipment.Id, SupplyReceivingSessionId = session.Id };
            var version = new OperationVersion { Id = Guid.NewGuid(), OperationId = operation.Id, VersionNumber = 1, SnapshotData = JsonSerializer.Serialize(new { operation.OperationType, operation.Status, operation.DestinationLocationId, Lines = operation.OperationLines.Select(ToOperationLineSnapshot) }, JsonOptions), Reason = "Supply physical receipt", EditedBy = actor, EditedAt = now };
            operation.OperationVersions.Add(version); operations.OperationLogs.Add(operation); await PersistenceBoundary.CommitAsync(operations, ct); operation.CurrentVersionId = version.Id;
            session.Status = "Confirmed"; session.ConfirmedAt = now; session.ConfirmedBy = actor; session.InventoryReceiptOperationId = operation.Id;
            foreach (var line in lines) prior[line.ShipmentLineId] = prior.GetValueOrDefault(line.ShipmentLineId) + line.ReceivedQuantity;
            shipment.Status = shipment.Lines.All(x => prior.GetValueOrDefault(x.Id) == x.Quantity) ? Received : PartiallyReceived; shipment.ConfirmedAt = now; shipment.ConfirmedBy = actor;
            AddHistory(operations, shipment, "ReceivingSessionConfirm", actor, now, $"Physical receipt {operation.OperationNumber} posted from receiving count {session.Id}.");
            await PersistenceBoundary.CommitAsync(operations, ct);
        }, ct, operations); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (UnauthorizedAccessException) { return Results.Forbid(); }
        catch (ArgumentException ex) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["lines"] = [ex.Message] }); }
        catch (InvalidOperationException ex) when (!string.Equals(ex.Message, "Receiving session is no longer confirmable.", StringComparison.Ordinal)) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["lines"] = [ex.Message] }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { code = "supply-receiving-not-confirmable", detail = ex.Message }); }
        catch (DbUpdateException) { return Results.Conflict(new { code = "supply-receiving-concurrency-conflict" }); }
        return Results.NoContent();
    }

    private static async Task<IResult> CancelShipmentAsync(
        Guid id,
        SupplyShipmentCancellationRequest request,
        OperationsDbContext operationsDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Reason)] = ["A cancellation reason is required."] });

        await using var transaction = operationsDbContext.Database.IsRelational()
            ? await PersistenceBoundary.OpenTransactionAsync(operationsDbContext, cancellationToken)
            : null;
        await LockShipmentAsync(operationsDbContext, id, cancellationToken);
        var shipment = await LoadShipmentAsync(operationsDbContext, id, cancellationToken);
        if (shipment is null)
        {
            return Results.NotFound();
        }

        if (shipment.Status != Draft)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(shipment.Status)] = ["Only draft supply shipments can be cancelled."] });
        }

        var now = clock.EgyptNow;
        shipment.Status = Cancelled;
        shipment.CancelledAt = now;
        shipment.CancelledBy = currentUser.UserId ?? Guid.Empty;
        AddHistory(operationsDbContext, shipment, "Cancel", currentUser.UserId ?? Guid.Empty, now, $"Draft shipment cancelled. Reason: {request.Reason.Trim()}");
        try
        {
            await PersistenceBoundary.CommitAsync(operationsDbContext, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { code = "supply-cancellation-transition-conflict", detail = "The shipment was changed by another request. Refresh and try again." });
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { code = "supply-cancellation-save-conflict", detail = "The shipment cancellation could not be saved because its current state changed." });
        }
        return Results.NoContent();
    }

    private static async Task LockShipmentAsync(OperationsDbContext dbContext, Guid shipmentId, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational()) return;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"select 1 from operations.supply_shipments where id = {shipmentId} for update",
            cancellationToken);
    }

    private static async Task LockSupplyPaymentAsync(OperationsDbContext dbContext, Guid paymentId, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational()) return;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"select 1 from operations.supply_payments where id = {paymentId} for update", cancellationToken);
    }

    private static async Task LockReceivingSessionAsync(OperationsDbContext dbContext, Guid sessionId, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational()) return;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"select 1 from operations.supply_receiving_sessions where \"Id\" = {sessionId} for update",
            cancellationToken);
    }

    private static async Task<Dictionary<string, string[]>> ValidatePaymentRequestAsync(SupplyPaymentRequest request, FinanceDbContext financeDbContext, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var category = AllowedPaymentCategories.FirstOrDefault(value => string.Equals(value, request.Category?.Trim(), StringComparison.OrdinalIgnoreCase));
        var method = AllowedMovementMethods.FirstOrDefault(value => string.Equals(value, request.MovementMethod?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (category is null) errors[nameof(request.Category)] = ["Supply payment category is invalid."];
        if (method is null) errors[nameof(request.MovementMethod)] = ["Supply payment movement method is invalid."];
        if (request.Amount <= 0) errors[nameof(request.Amount)] = ["Supply payment amount must be positive."];
        if (request.FinanceAccountId == Guid.Empty || !await financeDbContext.FinanceAccounts.AsNoTracking().AnyAsync(value => value.Id == request.FinanceAccountId && value.IsActive, cancellationToken))
            errors[nameof(request.FinanceAccountId)] = ["An active FinanceAccount is required."];
        if (method is "BankTransfer" or "Wallet" && string.IsNullOrWhiteSpace(request.ExternalReference))
            errors[nameof(request.ExternalReference)] = ["Bank and wallet supply payments require an external reference."];
        return errors;
    }

    private static SupplyPaymentResponse ToPaymentResponse(SupplyPayment value) => new(value.Id, value.ShipmentId, value.SupplyFinanceLogId, value.Category, value.Amount, value.MovementMethod, value.FinanceAccountId, value.ExternalReference, value.Status, value.Notes, value.CreatedBy, value.CreatedAt, value.SubmittedBy, value.SubmittedAt, value.ReviewedBy, value.ReviewedAt, value.RejectionReason, value.PostedFinanceLedgerEntryId, value.ReversesPaymentId, value.ReplacedByPaymentId, value.CorrelationId);

    private static async Task<SupplyBuildResult> BuildShipmentPartsAsync(SupplyShipmentRequest request, CatalogDbContext catalogDbContext, InventoryDbContext inventoryDbContext, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var lines = request.Lines ?? [];

        if (string.IsNullOrWhiteSpace(request.SupplierName))
        {
            errors[nameof(request.SupplierName)] = ["Supplier name is required."];
        }
        else if (request.SupplierName.Trim().Length > 255)
        {
            errors[nameof(request.SupplierName)] = ["Supplier name cannot exceed 255 characters."];
        }

        if (request.InvoiceNumber is { Length: > 100 })
        {
            errors[nameof(request.InvoiceNumber)] = ["Invoice number cannot exceed 100 characters."];
        }

        if (request.Notes is { Length: > 4000 })
        {
            errors[nameof(request.Notes)] = ["Notes cannot exceed 4000 characters."];
        }

        if (request.DestinationLocationId == Guid.Empty)
        {
            errors[nameof(request.DestinationLocationId)] = ["Active destination warehouse is required."];
        }

        if (lines.Count == 0)
        {
            errors[nameof(request.Lines)] = ["At least one SKU line is required."];
        }

        var location = await inventoryDbContext.Locations.AsNoTracking().FirstOrDefaultAsync(value => value.Id == request.DestinationLocationId && value.IsActive, cancellationToken);
        if (location is null)
        {
            errors[nameof(request.DestinationLocationId)] = ["Active destination warehouse is required."];
        }

        var skuIds = lines.Select(value => value.SkuId).Distinct().ToArray();
        var skus = await catalogDbContext.Skus
            .Include(value => value.Product)
            .AsNoTracking()
            .Where(value => skuIds.Contains(value.Id) && value.IsActive && value.Product.IsActive)
            .ToDictionaryAsync(value => value.Id, cancellationToken);

        var lineDrafts = new List<SupplyShipmentLine>();
        var duplicateLineKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!skus.TryGetValue(line.SkuId, out var sku))
            {
                errors[$"{nameof(request.Lines)}[{index}].{nameof(line.SkuId)}"] = ["Active SKU is required."];
                continue;
            }

            if (line.Quantity <= 0)
            {
                errors[$"{nameof(request.Lines)}[{index}].{nameof(line.Quantity)}"] = ["Quantity must be greater than zero."];
            }

            if (line.LotNumber is { Length: > 100 })
            {
                errors[$"{nameof(request.Lines)}[{index}].{nameof(line.LotNumber)}"] = ["Lot number cannot exceed 100 characters."];
            }

            if (line.Notes is { Length: > 1000 })
            {
                errors[$"{nameof(request.Lines)}[{index}].{nameof(line.Notes)}"] = ["Line notes cannot exceed 1000 characters."];
            }

            var duplicateKey = $"{line.SkuId:N}|{TrimToNull(line.LotNumber)?.ToUpperInvariant() ?? ""}|{line.ExpiryDate?.ToString("O") ?? ""}";
            if (!duplicateLineKeys.Add(duplicateKey))
            {
                errors[$"{nameof(request.Lines)}[{index}]"] = ["Duplicate SKU, lot, and expiry lines must be combined."];
            }

            var quantity = Math.Max(0, line.Quantity);
            lineDrafts.Add(new SupplyShipmentLine
            {
                Id = Guid.NewGuid(),
                SkuId = line.SkuId,
                ProductNameSnapshot = sku.Product.Name,
                SkuCodeSnapshot = sku.SkuCode,
                Quantity = quantity,
                UnitPrice = null,
                LineSubtotal = 0m,
                LotNumber = TrimToNull(line.LotNumber),
                ExpiryDate = line.ExpiryDate,
                Notes = TrimToNull(line.Notes)
            });
        }

        var locationLookup = location is null ? new Dictionary<Guid, Location>() : new Dictionary<Guid, Location> { [location.Id] = location };
        return new SupplyBuildResult(errors, lineDrafts, locationLookup);
    }

    private static async Task<Dictionary<string, string[]>> ValidateShipmentForConfirmationAsync(
        SupplyShipment shipment,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (shipment.Lines.Count == 0)
        {
            errors[nameof(shipment.Lines)] = ["At least one SKU line is required."];
        }

        if (shipment.Lines.Any(line => line.Quantity <= 0))
        {
            errors[nameof(shipment.Lines)] = ["Every SKU line quantity must be greater than zero."];
        }


        if (!await inventoryDbContext.Locations.AsNoTracking().AnyAsync(value => value.Id == shipment.DestinationLocationId && value.IsActive, cancellationToken))
        {
            errors[nameof(shipment.DestinationLocationId)] = ["Active destination warehouse is required."];
        }

        var skuIds = shipment.Lines.Select(value => value.SkuId).Distinct().ToArray();
        var activeSkuIds = await catalogDbContext.Skus
            .AsNoTracking()
            .Include(value => value.Product)
            .Where(value => skuIds.Contains(value.Id) && value.IsActive && value.Product.IsActive)
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);

        if (activeSkuIds.Length != skuIds.Length)
        {
            errors[nameof(shipment.Lines)] = ["Every SKU line must reference an active SKU before confirmation."];
        }

        return errors;
    }

    private static void ApplyParts(SupplyShipment shipment, SupplyBuildResult built)
    {
        shipment.Lines = built.Lines;
        shipment.ProductSubtotal = 0m;
        shipment.CostSubtotal = 0m;
        shipment.LandedTotal = 0m;
    }

    private static void AllocateCosts(List<SupplyShipmentLine> lines, decimal costTotal)
    {
        var productTotal = lines.Sum(value => value.LineSubtotal);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var allocated = productTotal == 0
                ? (lines.Count == 0 ? 0 : Math.Round(costTotal / lines.Count, 4))
                : Math.Round(costTotal * (line.LineSubtotal / productTotal), 4);

            if (index == lines.Count - 1)
            {
                allocated = costTotal - lines.Take(index).Sum(value => value.AllocatedCost);
            }

            line.AllocatedCost = allocated;
            line.LandedUnitCost = line.Quantity == 0 ? 0 : Math.Round((line.LineSubtotal + allocated) / line.Quantity, 4);
        }
    }

    private static async Task<SupplyShipment?> LoadShipmentAsync(OperationsDbContext dbContext, Guid id, CancellationToken cancellationToken) =>
        await dbContext.SupplyShipments
            .AsSplitQuery()
            .Include(value => value.Lines)
            .Include(value => value.Costs)
            .Include(value => value.HistoryLogs)
            .Include(value => value.InventoryReceiptOperation)
            .Include(value => value.ReceivingSessions)
                .ThenInclude(session => session.Lines)
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);

    private static async Task<SupplyShipment?> LoadShipmentSummaryAsync(OperationsDbContext dbContext, Guid id, CancellationToken cancellationToken) =>
        await dbContext.SupplyShipments.AsNoTracking()
            .Include(value => value.Lines)
            .Include(value => value.ReceivingSessions)
                .ThenInclude(session => session.Lines)
            .Include(value => value.InventoryReceiptOperation)
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);

    private static object ToReceivingSessionResponse(SupplyReceivingSession session) => new
    {
        session.Id,
        session.Status,
        session.CreatedAt,
        session.ConfirmedAt,
        session.InventoryReceiptOperationId,
        lines = session.Lines.Select(line => new
        {
            line.ShipmentLineId,
            line.ReceivedQuantity,
            line.LotNumber,
            line.ExpiryDate,
            line.Notes
        })
    };

    private static bool IsWarehouseClerk(ICurrentUser user) => string.Equals(user.Role, Lensee.SharedKernel.Security.LenseeRoles.WarehouseClerk, StringComparison.OrdinalIgnoreCase);

    private static bool CanAccessShipmentLocation(ICurrentUser user, Guid locationId) => !IsWarehouseClerk(user) || user.LocationId == locationId;

    private static async Task<IReadOnlyDictionary<Guid, Location>> LoadLocationLookupAsync(InventoryDbContext dbContext, IEnumerable<Guid> locationIds, CancellationToken cancellationToken)
    {
        var ids = locationIds.Distinct().ToArray();
        return await dbContext.Locations
            .AsNoTracking()
            .Where(value => ids.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
    }

    private static void AddHistory(OperationsDbContext dbContext, SupplyShipment shipment, string action, Guid actorUserId, DateTime now, string summary)
    {
        var history = new SupplyShipmentHistory
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipment.Id,
            Action = action,
            ActorUserId = actorUserId,
            CreatedAt = now,
            Summary = summary,
            SnapshotData = JsonSerializer.Serialize(ToSnapshot(shipment), JsonOptions)
        };
        shipment.HistoryLogs.Add(history);
        dbContext.SupplyShipmentHistoryLogs.Add(history);
    }

    private static SupplyShipmentListResponse ToListResponse(SupplyShipment shipment, IReadOnlyDictionary<Guid, Location> locationLookup) =>
        new(
            shipment.Id,
            shipment.ShipmentNumber,
            shipment.SupplierName,
            shipment.InvoiceNumber,
            shipment.ShipmentDate,
            shipment.Status,
            shipment.ConcurrencyVersion,
            shipment.DestinationLocationId,
            locationLookup.TryGetValue(shipment.DestinationLocationId, out var location) ? location.Name : null,
            shipment.Lines.Sum(value => value.Quantity),
            shipment.InventoryReceiptOperationId,
            shipment.InventoryReceiptOperation?.OperationNumber,
            shipment.CreatedAt);

    private static SupplyShipmentDetailResponse ToDetailResponse(SupplyShipment shipment, IReadOnlyDictionary<Guid, Location> locationLookup, bool includeCollections = true)
    {
        var receivedByLine = shipment.ReceivingSessions
            .Where(session => session.Status == "Confirmed")
            .SelectMany(session => session.Lines)
            .GroupBy(line => line.ShipmentLineId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.ReceivedQuantity));
        var receiving = new SupplyReceivingSummaryResponse(
            shipment.Lines.Select(line => new SupplyReceivingQuantityResponse(line.Id, receivedByLine.GetValueOrDefault(line.Id), line.Quantity - receivedByLine.GetValueOrDefault(line.Id))).ToList(),
            shipment.ReceivingSessions.Where(session => session.Status == Draft).Select(session => new SupplyReceivingSessionResponse(session.Id, session.Status, session.CreatedAt, session.ConfirmedAt, session.InventoryReceiptOperationId, session.Lines.Select(line => new SupplyReceivingLineResponse(line.ShipmentLineId, line.ReceivedQuantity, line.LotNumber, line.ExpiryDate, line.Notes)).ToList())).ToList(),
            shipment.ReceivingSessions.Where(session => session.Status == "Confirmed").OrderByDescending(session => session.ConfirmedAt).Select(session => new SupplyReceivingSessionResponse(session.Id, session.Status, session.CreatedAt, session.ConfirmedAt, session.InventoryReceiptOperationId, session.Lines.Where(line => line.ReceivedQuantity > 0).Select(line => new SupplyReceivingLineResponse(line.ShipmentLineId, line.ReceivedQuantity, line.LotNumber, line.ExpiryDate, line.Notes)).ToList())).ToList());
        return new(
            shipment.Id,
            shipment.ShipmentNumber,
            shipment.SupplierName,
            shipment.InvoiceNumber,
            shipment.ShipmentDate,
            shipment.Status,
            shipment.ConcurrencyVersion,
            shipment.DestinationLocationId,
            locationLookup.TryGetValue(shipment.DestinationLocationId, out var location) ? location.Name : null,
            shipment.Notes,
            shipment.InventoryReceiptOperationId,
            shipment.InventoryReceiptOperation?.OperationNumber,
            shipment.CreatedBy,
            shipment.CreatedAt,
            shipment.ConfirmedBy,
            shipment.ConfirmedAt,
            shipment.CancelledBy,
            shipment.CancelledAt,
            shipment.Lines.Count,
            includeCollections ? shipment.Lines.Select(line => new SupplyLineResponse(line.Id, line.SkuId, line.SkuCodeSnapshot, line.ProductNameSnapshot, line.Quantity, line.LotNumber, line.ExpiryDate, line.Notes)).ToList() : [],
            includeCollections ? shipment.HistoryLogs.OrderByDescending(value => value.CreatedAt).Select(value => new SupplyHistoryResponse(value.Id, value.Action, value.ActorUserId, value.CreatedAt, value.Summary)).ToList() : [],
            receiving);
    }

    private static object ToSnapshot(SupplyShipment shipment) => new
    {
        shipment.ShipmentNumber,
        shipment.SupplierName,
        shipment.InvoiceNumber,
        shipment.ShipmentDate,
        shipment.DestinationLocationId,
        shipment.Status,
        Lines = shipment.Lines.Select(line => new { line.SkuId, line.SkuCodeSnapshot, line.Quantity, line.LotNumber, line.ExpiryDate })
    };

    private static object ToOperationLineSnapshot(OperationLine line) => new
    {
        line.SkuId,
        line.SkuCodeSnapshot,
        line.ProductNameSnapshot,
        line.Quantity,
        line.UnitPrice,
        line.UnitCost,
        line.LineTotal,
        line.LotNumber,
        line.ExpiryDate,
        line.LineNotes
    };

    private static string NormalizeStatus(string value) =>
        string.Equals(value, Arrived, StringComparison.OrdinalIgnoreCase) ? Arrived :
        string.Equals(value, PartiallyReceived, StringComparison.OrdinalIgnoreCase) ? PartiallyReceived :
        string.Equals(value, Received, StringComparison.OrdinalIgnoreCase) ? Received :
        string.Equals(value, Cancelled, StringComparison.OrdinalIgnoreCase) ? Cancelled :
        Draft;

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record SupplyBuildResult(Dictionary<string, string[]> Errors, List<SupplyShipmentLine> Lines, IReadOnlyDictionary<Guid, Location> LocationLookup);
}

public sealed record SupplyShipmentCancellationRequest(string? Reason);

public sealed record SupplyShipmentRequest(
    string? SupplierName,
    string? InvoiceNumber,
    DateTime? ShipmentDate,
    Guid DestinationLocationId,
    string? Notes,
    IReadOnlyList<SupplyShipmentLineRequest>? Lines,
    uint? ExpectedVersion = null);

public sealed record SupplyShipmentLineRequest(Guid SkuId, int Quantity, string? LotNumber, DateOnly? ExpiryDate, string? Notes);

public sealed record SupplyShipmentCostRequest(string? CostType, string? Description, decimal Amount);

public sealed record SupplyReceivingLineRequest(Guid ShipmentLineId, int ReceivedQuantity, string? LotNumber, DateOnly? ExpiryDate, string? Notes);

public sealed record SupplyPaymentRequest(string? Category, decimal Amount, string? MovementMethod, Guid FinanceAccountId, string? ExternalReference, string? Notes, string? CorrelationId = null);

public sealed record SupplyPaymentRejectionRequest(string? Reason);

public sealed record SupplyPaymentResponse(Guid Id, Guid ShipmentId, Guid? SupplyFinanceLogId, string Category, decimal Amount, string MovementMethod, Guid FinanceAccountId, string? ExternalReference, string Status, string? Notes, Guid CreatedBy, DateTime CreatedAt, Guid? SubmittedBy, DateTime? SubmittedAt, Guid? ReviewedBy, DateTime? ReviewedAt, string? RejectionReason, Guid? PostedFinanceLedgerEntryId, Guid? ReversesPaymentId, Guid? ReplacedByPaymentId, string CorrelationId);

public sealed record SupplyPaymentSummaryResponse(IReadOnlyList<SupplyPaymentResponse> Payments, decimal PostedAmount, string SettlementStatus);

public sealed record SupplyShipmentListResponse(
    Guid Id,
    string ShipmentNumber,
    string SupplierName,
    string? InvoiceNumber,
    DateTime ShipmentDate,
    string Status,
    uint ConcurrencyVersion,
    Guid DestinationLocationId,
    string? DestinationLocationName,
    int Quantity,
    Guid? InventoryReceiptOperationId,
    string? InventoryReceiptOperationNumber,
    DateTime CreatedAt);

public sealed record SupplyShipmentDetailResponse(
    Guid Id,
    string ShipmentNumber,
    string SupplierName,
    string? InvoiceNumber,
    DateTime ShipmentDate,
    string Status,
    uint ConcurrencyVersion,
    Guid DestinationLocationId,
    string? DestinationLocationName,
    string? Notes,
    Guid? InventoryReceiptOperationId,
    string? InventoryReceiptOperationNumber,
    Guid CreatedBy,
    DateTime CreatedAt,
    Guid? ConfirmedBy,
    DateTime? ConfirmedAt,
    Guid? CancelledBy,
    DateTime? CancelledAt,
    int LineCount,
    IReadOnlyList<SupplyLineResponse> Lines,
    IReadOnlyList<SupplyHistoryResponse> History,
    SupplyReceivingSummaryResponse Receiving);

public sealed record SupplyLineResponse(Guid Id, Guid SkuId, string SkuCode, string ProductName, int Quantity, string? LotNumber, DateOnly? ExpiryDate, string? Notes);

public sealed record SupplyShipmentEditorResponse(Guid Id, string SupplierName, string? InvoiceNumber, DateTime ShipmentDate, string Status, uint ConcurrencyVersion, Guid DestinationLocationId, string? Notes, IReadOnlyList<SupplyEditorLineResponse> Lines);

public sealed record SupplyEditorLineResponse(Guid Id, Guid SkuId, string SkuCode, string ProductName, int Quantity, string? LotNumber, DateOnly? ExpiryDate, string? Notes);

public sealed record SupplyCostResponse(Guid Id, string CostType, string? Description, decimal Amount);

public sealed record SupplyHistoryResponse(Guid Id, string Action, Guid ActorUserId, DateTime CreatedAt, string? Summary);
public sealed record SupplyReceivingSummaryResponse(IReadOnlyList<SupplyReceivingQuantityResponse> Quantities, IReadOnlyList<SupplyReceivingSessionResponse> OpenSessions, IReadOnlyList<SupplyReceivingSessionResponse> History);
public sealed record SupplyReceivingQuantityResponse(Guid ShipmentLineId, int CumulativeReceived, int OutstandingQuantity);
public sealed record SupplyReceivingSessionResponse(Guid Id, string Status, DateTime CreatedAt, DateTime? ConfirmedAt, Guid? InventoryReceiptOperationId, IReadOnlyList<SupplyReceivingLineResponse> Lines);
public sealed record SupplyReceivingLineResponse(Guid ShipmentLineId, int ReceivedQuantity, string? LotNumber, DateOnly? ExpiryDate, string? Notes);
