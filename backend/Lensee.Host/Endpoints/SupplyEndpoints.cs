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
    private const string Received = "Received";
    private const string Cancelled = "Cancelled";
    private const string InventoryReceipt = "InventoryReceipt";
    private static readonly string[] AllowedCostTypes = ["Customs", "Freight", "Clearance", "Handling", "Insurance", "Other"];
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
        group.MapGet("/{id:guid}/costs", GetCostsAsync).RequireAuthorization("supply.read").WithName("GetSupplyShipmentCosts");
        group.MapGet("/{id:guid}/editor", GetEditorAsync).RequireAuthorization("supply.write").WithName("GetSupplyShipmentEditor");
        group.MapGet("/{id:guid}/history", GetHistoryAsync).RequireAuthorization("supply.read").WithName("GetSupplyShipmentHistory");
        group.MapGet("/{id:guid}/payments", ListPaymentsAsync).RequireAuthorization("supply.read").WithName("ListSupplyPayments");
        group.MapPost("/", CreateShipmentAsync).RequireAuthorization("supply.write").WithName("CreateSupplyShipment");
        group.MapPut("/{id:guid}", UpdateShipmentAsync).RequireAuthorization("supply.write").WithName("UpdateSupplyShipment");
        group.MapPost("/{id:guid}/confirm", ConfirmShipmentAsync).RequireAuthorization("supply.write").WithName("ConfirmSupplyShipment");
        group.MapPost("/{id:guid}/cancel", CancelShipmentAsync).RequireAuthorization("supply.write").WithName("CancelSupplyShipment");
        group.MapPost("/{id:guid}/payments", CreatePaymentAsync).RequireAuthorization("supply.write").WithName("CreateSupplyPayment");
        group.MapPost("/{id:guid}/payments/{paymentId:guid}/submit", SubmitPaymentAsync).RequireAuthorization("supply.write").WithName("SubmitSupplyPayment");
        group.MapPost("/{id:guid}/payments/{paymentId:guid}/approve", ApprovePaymentAsync).RequireAuthorization("supply.payments.approve").WithName("ApproveSupplyPayment");
        group.MapPost("/{id:guid}/payments/{paymentId:guid}/reject", RejectPaymentAsync).RequireAuthorization("supply.payments.approve").WithName("RejectSupplyPayment");
        group.MapPost("/{id:guid}/payments/{paymentId:guid}/correct", CorrectPaymentAsync).RequireAuthorization("supply.write").WithName("CorrectSupplyPayment");

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
        CancellationToken cancellationToken)
    {
        var query = operationsDbContext.SupplyShipments.AsNoTracking().AsQueryable();

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
                value.ProductSubtotal,
                value.CostSubtotal,
                value.LandedTotal,
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

    private static async Task<IResult> GetShipmentAsync(Guid id, bool? includeCollections, OperationsDbContext operationsDbContext, InventoryDbContext inventoryDbContext, CancellationToken cancellationToken)
    {
        var shipment = includeCollections == false
            ? await LoadShipmentSummaryAsync(operationsDbContext, id, cancellationToken)
            : await LoadShipmentAsync(operationsDbContext, id, cancellationToken);
        if (shipment is null)
        {
            return Results.NotFound();
        }

        var locationLookup = await LoadLocationLookupAsync(inventoryDbContext, [shipment.DestinationLocationId], cancellationToken);
        var response = ToDetailResponse(shipment, locationLookup, includeCollections != false);
        if (includeCollections == false)
        {
            response = response with
            {
                LineCount = await operationsDbContext.SupplyShipmentLines.AsNoTracking().CountAsync(value => value.ShipmentId == id, cancellationToken),
                IncompletePriceCount = await operationsDbContext.SupplyShipmentLines.AsNoTracking().CountAsync(value => value.ShipmentId == id && value.UnitPrice == null, cancellationToken),
                InvalidPriceCount = await operationsDbContext.SupplyShipmentLines.AsNoTracking().CountAsync(value => value.ShipmentId == id && value.UnitPrice <= 0, cancellationToken)
            };
        }
        return Results.Ok(response);
    }

    private static async Task<IResult> GetLinesAsync(Guid id, int? page, int? pageSize, OperationsDbContext dbContext, CancellationToken cancellationToken)
    {
        if (!await dbContext.SupplyShipments.AsNoTracking().AnyAsync(value => value.Id == id, cancellationToken)) return Results.NotFound();
        var request = new PageRequest(page ?? 1, pageSize ?? 50);
        var query = dbContext.SupplyShipmentLines.AsNoTracking().Where(value => value.ShipmentId == id);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderBy(value => value.Id).Skip(request.Skip).Take(request.PageSize)
            .Select(line => new SupplyLineResponse(line.Id, line.SkuId, line.SkuCodeSnapshot, line.ProductNameSnapshot, line.Quantity, line.UnitPrice, line.LineSubtotal, line.AllocatedCost, line.LandedUnitCost, line.LotNumber, line.ExpiryDate, line.Notes))
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
            .Include(value => value.Lines).Include(value => value.Costs).Include(value => value.InventoryReceiptOperation)
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (shipment is null) return Results.NotFound();
        return Results.Ok(new SupplyShipmentEditorResponse(
            shipment.Id, shipment.SupplierName, shipment.InvoiceNumber, shipment.ShipmentDate, shipment.Status,
            shipment.ConcurrencyVersion, shipment.DestinationLocationId, shipment.Notes,
            shipment.Lines.OrderBy(value => value.Id).Select(line => new SupplyEditorLineResponse(line.Id, line.SkuId, line.SkuCodeSnapshot, line.ProductNameSnapshot, line.Quantity, line.UnitPrice, line.LotNumber, line.ExpiryDate, line.Notes)).ToList(),
            shipment.Costs.OrderBy(value => value.Id).Select(cost => new SupplyCostResponse(cost.Id, cost.CostType, cost.Description, cost.Amount)).ToList()));
    }

    private static async Task<IResult> GetHistoryAsync(Guid id, OperationsDbContext operationsDbContext, CancellationToken cancellationToken)
    {
        if (!await operationsDbContext.SupplyShipments.AnyAsync(value => value.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

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
        var payment = new SupplyPayment
        {
            Id = Guid.NewGuid(),
            ShipmentId = id,
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
        await PersistenceBoundary.CommitAsync(dbContext, cancellationToken);
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
        catch (InvalidOperationException) { return Results.Conflict(new { code = "supply-payment-posting-rejected", detail = "The supply payment could not be posted in its current state." }); }
        return Results.Ok(ToPaymentResponse(posted!));
    }

    private static async Task<IResult> RejectPaymentAsync(Guid id, Guid paymentId, SupplyPaymentRejectionRequest request, OperationsDbContext dbContext, ICurrentUser currentUser, IClock clock, CancellationToken cancellationToken)
    {
        var payment = await dbContext.SupplyPayments.SingleOrDefaultAsync(value => value.Id == paymentId && value.ShipmentId == id, cancellationToken);
        if (payment is null) return Results.NotFound();
        if (payment.Status != "PendingReview") return Results.Conflict(new { code = "invalid-transition", detail = "Only submitted supply payments can be rejected." });
        payment.Status = "Rejected"; payment.ReviewedBy = currentUser.UserId ?? Guid.Empty; payment.ReviewedAt = clock.EgyptNow; payment.RejectionReason = TrimToNull(request.Reason) ?? "Rejected by reviewer.";
        await PersistenceBoundary.CommitAsync(dbContext, cancellationToken); return Results.Ok(ToPaymentResponse(payment));
    }

    private static async Task<IResult> CorrectPaymentAsync(Guid id, Guid paymentId, SupplyPaymentRequest request, OperationsDbContext dbContext, FinanceDbContext financeDbContext, ICurrentUser currentUser, IClock clock, CancellationToken cancellationToken)
    {
        var original = await dbContext.SupplyPayments.SingleOrDefaultAsync(value => value.Id == paymentId && value.ShipmentId == id, cancellationToken);
        if (original is null) return Results.NotFound();
        if (original.Status != "Posted" || original.ReplacedByPaymentId is not null) return Results.Conflict(new { code = "supply-payment-not-correctable", detail = "Only an unreplaced posted supply payment can be corrected." });
        var errors = await ValidatePaymentRequestAsync(request, financeDbContext, cancellationToken); if (errors.Count > 0) return Results.ValidationProblem(errors);
        var replacement = new SupplyPayment { Id = Guid.NewGuid(), ShipmentId = id, Category = request.Category!.Trim(), Amount = request.Amount, MovementMethod = request.MovementMethod!.Trim(), FinanceAccountId = request.FinanceAccountId, ExternalReference = FinanceLedgerService.NormalizeExternalReference(request.ExternalReference), Notes = TrimToNull(request.Notes), Status = Draft, CreatedBy = currentUser.UserId ?? Guid.Empty, CreatedAt = clock.EgyptNow, ReversesPaymentId = original.Id, CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? Guid.NewGuid().ToString("N") : request.CorrelationId.Trim() };
        dbContext.SupplyPayments.Add(replacement); await PersistenceBoundary.CommitAsync(dbContext, cancellationToken);
        return Results.Created($"/api/v1/supply/shipments/{id}/payments/{replacement.Id}", ToPaymentResponse(replacement));
    }

    private static async Task<IResult> CreateShipmentAsync(
        SupplyShipmentRequest request,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        InventoryDbContext inventoryDbContext,
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
        AddHistory(operationsDbContext, shipment, "Create", currentUser.UserId ?? Guid.Empty, now, "Shipment created.");
        await PersistenceBoundary.CommitAsync(operationsDbContext, cancellationToken);

        return Results.Created($"/api/v1/supply/shipments/{shipment.Id}", ToDetailResponse(shipment, built.LocationLookup));
    }

    private static async Task<IResult> UpdateShipmentAsync(
        Guid id,
        SupplyShipmentRequest request,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        InventoryDbContext inventoryDbContext,
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
        var oldCosts = await operationsDbContext.SupplyShipmentCosts.Where(value => value.ShipmentId == shipment.Id).ToListAsync(cancellationToken);
        operationsDbContext.SupplyShipmentLines.RemoveRange(oldLines);
        operationsDbContext.SupplyShipmentCosts.RemoveRange(oldCosts);
        await PersistenceBoundary.CommitAsync(operationsDbContext, cancellationToken);

        var now = clock.EgyptNow;
        shipment.SupplierName = request.SupplierName!.Trim();
        shipment.InvoiceNumber = TrimToNull(request.InvoiceNumber);
        shipment.ShipmentDate = request.ShipmentDate ?? now;
        shipment.DestinationLocationId = request.DestinationLocationId;
        shipment.Notes = TrimToNull(request.Notes);
        shipment.UpdatedBy = currentUser.UserId ?? Guid.Empty;
        shipment.UpdatedAt = now;
        shipment.ProductSubtotal = built.Lines.Sum(value => value.LineSubtotal);
        shipment.CostSubtotal = built.Costs.Sum(value => value.Amount);
        shipment.LandedTotal = shipment.ProductSubtotal + shipment.CostSubtotal;
        foreach (var line in built.Lines)
        {
            line.ShipmentId = shipment.Id;
        }

        foreach (var cost in built.Costs)
        {
            cost.ShipmentId = shipment.Id;
        }

        operationsDbContext.SupplyShipmentLines.AddRange(built.Lines);
        operationsDbContext.SupplyShipmentCosts.AddRange(built.Costs);
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
                shipment.ProductSubtotal,
                shipment.CostSubtotal,
                shipment.LandedTotal,
                Lines = built.Lines.Select(line => new { line.SkuId, line.SkuCodeSnapshot, line.Quantity, line.UnitPrice, line.LineSubtotal, line.AllocatedCost, line.LandedUnitCost }),
                Costs = built.Costs.Select(cost => new { cost.CostType, cost.Amount })
            }, JsonOptions)
        });
        await PersistenceBoundary.CommitAsync(operationsDbContext, cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ConfirmShipmentAsync(
        Guid id,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        InventoryDbContext inventoryDbContext,
        StockLedgerService ledgerService,
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

        var confirmErrors = await ValidateShipmentForConfirmationAsync(shipment, inventoryDbContext, catalogDbContext, cancellationToken);
        if (confirmErrors.Count > 0)
        {
            return Results.ValidationProblem(confirmErrors);
        }

        var now = clock.EgyptNow;
        var userId = currentUser.UserId ?? Guid.Empty;
        AllocateCosts(shipment.Lines.ToList(), shipment.Costs.Sum(value => value.Amount));
        await SharedDbTransaction.ExecuteAsync(inventoryDbContext, async () =>
        {
            var operation = new OperationLog
            {
                Id = Guid.NewGuid(),
                OperationNumber = $"OP-{now:yyyyMMddHHmmss}-{RandomNumberGenerator.GetInt32(100, 1000)}",
                OperationType = InventoryReceipt,
                Status = Received,
                DestinationLocationId = shipment.DestinationLocationId,
                Notes = $"Supply {shipment.ShipmentNumber}. {shipment.Notes}".Trim(),
                CreatedBy = userId,
                CreatedAt = now,
                ConfirmedBy = userId,
                ConfirmedAt = now
            };

            foreach (var line in shipment.Lines)
            {
                operation.OperationLines.Add(new OperationLine
                {
                    Id = Guid.NewGuid(),
                    OperationId = operation.Id,
                    SkuId = line.SkuId,
                    ProductNameSnapshot = line.ProductNameSnapshot,
                    SkuCodeSnapshot = line.SkuCodeSnapshot,
                    Section = "Standard",
                    Quantity = line.Quantity,
                    EntryMode = "Packs",
                    BonusQuantity = 0,
                    UnitPrice = line.UnitPrice.GetValueOrDefault(),
                    UnitCost = line.LandedUnitCost,
                    LineTotal = line.LineSubtotal,
                    LotNumber = line.LotNumber,
                    ExpiryDate = line.ExpiryDate,
                    LineNotes = line.Notes
                });

            }

            await ledgerService.ReceiveSupplyBatchAsync(
                shipment.DestinationLocationId,
                shipment.Lines.Select(line => new SupplyReceiptLine(line.SkuId, line.Quantity, line.LotNumber, line.ExpiryDate, line.Notes)).ToArray(),
                userId,
                operation.Id,
                cancellationToken);

            operation.InventoryReceiptHeader = new InventoryReceiptHeader
            {
                Id = Guid.NewGuid(),
                OperationId = operation.Id,
                SupplierName = shipment.SupplierName,
                InvoiceNumber = shipment.InvoiceNumber,
                ReceiptDate = now
            };

            var version = new OperationVersion
            {
                Id = Guid.NewGuid(),
                OperationId = operation.Id,
                VersionNumber = 1,
                SnapshotData = JsonSerializer.Serialize(new { operation.OperationType, operation.Status, operation.DestinationLocationId, Lines = operation.OperationLines.Select(ToOperationLineSnapshot) }, JsonOptions),
                Reason = "Supply received",
                EditedBy = userId,
                EditedAt = now
            };
            operation.OperationVersions.Add(version);

            operationsDbContext.OperationLogs.Add(operation);
            // Persist the operation and its initial version before linking the operation
            // back to that version.  Setting CurrentVersionId before this insert makes
            // OperationLog and OperationVersion depend on each other in one SaveChanges.
            await PersistenceBoundary.CommitAsync(operationsDbContext, cancellationToken);

            operation.CurrentVersionId = version.Id;
            shipment.Status = Received;
            shipment.ConfirmedAt = now;
            shipment.ConfirmedBy = userId;
            shipment.InventoryReceiptOperationId = operation.Id;
            AddHistory(operationsDbContext, shipment, "Confirm", userId, now, $"Shipment received through operation {operation.OperationNumber}.");
            await PersistenceBoundary.CommitAsync(operationsDbContext, cancellationToken);
        }, cancellationToken, operationsDbContext);

        return Results.NoContent();
    }

    private static async Task<IResult> CancelShipmentAsync(
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
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(shipment.Status)] = ["Only draft supply shipments can be cancelled."] });
        }

        var now = clock.EgyptNow;
        shipment.Status = Cancelled;
        shipment.CancelledAt = now;
        shipment.CancelledBy = currentUser.UserId ?? Guid.Empty;
        AddHistory(operationsDbContext, shipment, "Cancel", currentUser.UserId ?? Guid.Empty, now, "Draft shipment cancelled.");
        await PersistenceBoundary.CommitAsync(operationsDbContext, cancellationToken);
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

    private static SupplyPaymentResponse ToPaymentResponse(SupplyPayment value) => new(value.Id, value.ShipmentId, value.Category, value.Amount, value.MovementMethod, value.FinanceAccountId, value.ExternalReference, value.Status, value.Notes, value.CreatedBy, value.CreatedAt, value.SubmittedBy, value.SubmittedAt, value.ReviewedBy, value.ReviewedAt, value.RejectionReason, value.PostedFinanceLedgerEntryId, value.ReversesPaymentId, value.ReplacedByPaymentId, value.CorrelationId);

    private static async Task<SupplyBuildResult> BuildShipmentPartsAsync(SupplyShipmentRequest request, CatalogDbContext catalogDbContext, InventoryDbContext inventoryDbContext, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var lines = request.Lines ?? [];
        var costs = request.Costs ?? [];

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

            if (line.UnitPrice.HasValue && line.UnitPrice.Value <= 0)
            {
                errors[$"{nameof(request.Lines)}[{index}].{nameof(line.UnitPrice)}"] = ["Unit price must be greater than zero when provided."];
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
            var unitPrice = line.UnitPrice.HasValue ? Math.Max(0, line.UnitPrice.Value) : (decimal?)null;
            lineDrafts.Add(new SupplyShipmentLine
            {
                Id = Guid.NewGuid(),
                SkuId = line.SkuId,
                ProductNameSnapshot = sku.Product.Name,
                SkuCodeSnapshot = sku.SkuCode,
                Quantity = quantity,
                UnitPrice = unitPrice,
                LineSubtotal = quantity * (unitPrice ?? 0),
                LotNumber = TrimToNull(line.LotNumber),
                ExpiryDate = line.ExpiryDate,
                Notes = TrimToNull(line.Notes)
            });
        }

        var costDrafts = new List<SupplyShipmentCost>();
        for (var index = 0; index < costs.Count; index++)
        {
            var cost = costs[index];
            if (string.IsNullOrWhiteSpace(cost.CostType))
            {
                errors[$"{nameof(request.Costs)}[{index}].{nameof(cost.CostType)}"] = ["Cost type is required."];
            }
            else if (NormalizeCostType(cost.CostType) is null)
            {
                errors[$"{nameof(request.Costs)}[{index}].{nameof(cost.CostType)}"] = ["Cost type must be Customs, Freight, Clearance, Handling, Insurance, or Other."];
            }

            if (cost.Amount < 0)
            {
                errors[$"{nameof(request.Costs)}[{index}].{nameof(cost.Amount)}"] = ["Cost amount cannot be negative."];
            }

            if (cost.Description is { Length: > 255 })
            {
                errors[$"{nameof(request.Costs)}[{index}].{nameof(cost.Description)}"] = ["Cost description cannot exceed 255 characters."];
            }

            costDrafts.Add(new SupplyShipmentCost
            {
                Id = Guid.NewGuid(),
                CostType = NormalizeCostType(cost.CostType) ?? "Other",
                Description = TrimToNull(cost.Description),
                Amount = Math.Max(0, cost.Amount)
            });
        }

        if (lineDrafts.All(value => value.UnitPrice.HasValue))
        {
            AllocateCosts(lineDrafts, costDrafts.Sum(value => value.Amount));
        }

        var locationLookup = location is null ? new Dictionary<Guid, Location>() : new Dictionary<Guid, Location> { [location.Id] = location };
        return new SupplyBuildResult(errors, lineDrafts, costDrafts, locationLookup);
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

        if (shipment.Lines.Any(line => line.UnitPrice is null or <= 0))
        {
            errors[nameof(shipment.Lines)] = ["Every SKU line needs a unit price greater than zero before confirmation."];
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
        shipment.Costs = built.Costs;
        shipment.ProductSubtotal = built.Lines.Sum(value => value.LineSubtotal);
        shipment.CostSubtotal = built.Costs.Sum(value => value.Amount);
        shipment.LandedTotal = shipment.ProductSubtotal + shipment.CostSubtotal;
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
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);

    private static async Task<SupplyShipment?> LoadShipmentSummaryAsync(OperationsDbContext dbContext, Guid id, CancellationToken cancellationToken) =>
        await dbContext.SupplyShipments.AsNoTracking()
            .Include(value => value.InventoryReceiptOperation)
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);

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
            shipment.ProductSubtotal,
            shipment.CostSubtotal,
            shipment.LandedTotal,
            shipment.InventoryReceiptOperationId,
            shipment.InventoryReceiptOperation?.OperationNumber,
            shipment.CreatedAt);

    private static SupplyShipmentDetailResponse ToDetailResponse(SupplyShipment shipment, IReadOnlyDictionary<Guid, Location> locationLookup, bool includeCollections = true) =>
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
            shipment.Notes,
            shipment.ProductSubtotal,
            shipment.CostSubtotal,
            shipment.LandedTotal,
            shipment.InventoryReceiptOperationId,
            shipment.InventoryReceiptOperation?.OperationNumber,
            shipment.CreatedBy,
            shipment.CreatedAt,
            shipment.ConfirmedBy,
            shipment.ConfirmedAt,
            shipment.CancelledBy,
            shipment.CancelledAt,
            shipment.Lines.Count,
            shipment.Lines.Count(line => line.UnitPrice == null),
            shipment.Lines.Count(line => line.UnitPrice <= 0),
            includeCollections ? shipment.Lines.Select(line => new SupplyLineResponse(line.Id, line.SkuId, line.SkuCodeSnapshot, line.ProductNameSnapshot, line.Quantity, line.UnitPrice, line.LineSubtotal, line.AllocatedCost, line.LandedUnitCost, line.LotNumber, line.ExpiryDate, line.Notes)).ToList() : [],
            includeCollections ? shipment.Costs.Select(cost => new SupplyCostResponse(cost.Id, cost.CostType, cost.Description, cost.Amount)).ToList() : [],
            includeCollections ? shipment.HistoryLogs.OrderByDescending(value => value.CreatedAt).Select(value => new SupplyHistoryResponse(value.Id, value.Action, value.ActorUserId, value.CreatedAt, value.Summary)).ToList() : []);

    private static object ToSnapshot(SupplyShipment shipment) => new
    {
        shipment.ShipmentNumber,
        shipment.SupplierName,
        shipment.InvoiceNumber,
        shipment.ShipmentDate,
        shipment.DestinationLocationId,
        shipment.Status,
        shipment.ProductSubtotal,
        shipment.CostSubtotal,
        shipment.LandedTotal,
        Lines = shipment.Lines.Select(line => new { line.SkuId, line.SkuCodeSnapshot, line.Quantity, line.UnitPrice, line.LineSubtotal, line.AllocatedCost, line.LandedUnitCost }),
        Costs = shipment.Costs.Select(cost => new { cost.CostType, cost.Amount })
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
        string.Equals(value, Received, StringComparison.OrdinalIgnoreCase) ? Received :
        string.Equals(value, Cancelled, StringComparison.OrdinalIgnoreCase) ? Cancelled :
        Draft;

    private static string? NormalizeCostType(string? value) =>
        AllowedCostTypes.FirstOrDefault(costType => string.Equals(costType, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record SupplyBuildResult(Dictionary<string, string[]> Errors, List<SupplyShipmentLine> Lines, List<SupplyShipmentCost> Costs, IReadOnlyDictionary<Guid, Location> LocationLookup);
}

public sealed record SupplyShipmentRequest(
    string? SupplierName,
    string? InvoiceNumber,
    DateTime? ShipmentDate,
    Guid DestinationLocationId,
    string? Notes,
    IReadOnlyList<SupplyShipmentLineRequest>? Lines,
    IReadOnlyList<SupplyShipmentCostRequest>? Costs,
    uint? ExpectedVersion = null);

public sealed record SupplyShipmentLineRequest(Guid SkuId, int Quantity, decimal? UnitPrice, string? LotNumber, DateOnly? ExpiryDate, string? Notes);

public sealed record SupplyShipmentCostRequest(string? CostType, string? Description, decimal Amount);

public sealed record SupplyPaymentRequest(string? Category, decimal Amount, string? MovementMethod, Guid FinanceAccountId, string? ExternalReference, string? Notes, string? CorrelationId = null);

public sealed record SupplyPaymentRejectionRequest(string? Reason);

public sealed record SupplyPaymentResponse(Guid Id, Guid ShipmentId, string Category, decimal Amount, string MovementMethod, Guid FinanceAccountId, string? ExternalReference, string Status, string? Notes, Guid CreatedBy, DateTime CreatedAt, Guid? SubmittedBy, DateTime? SubmittedAt, Guid? ReviewedBy, DateTime? ReviewedAt, string? RejectionReason, Guid? PostedFinanceLedgerEntryId, Guid? ReversesPaymentId, Guid? ReplacedByPaymentId, string CorrelationId);

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
    decimal ProductSubtotal,
    decimal CostSubtotal,
    decimal LandedTotal,
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
    decimal ProductSubtotal,
    decimal CostSubtotal,
    decimal LandedTotal,
    Guid? InventoryReceiptOperationId,
    string? InventoryReceiptOperationNumber,
    Guid CreatedBy,
    DateTime CreatedAt,
    Guid? ConfirmedBy,
    DateTime? ConfirmedAt,
    Guid? CancelledBy,
    DateTime? CancelledAt,
    int LineCount,
    int IncompletePriceCount,
    int InvalidPriceCount,
    IReadOnlyList<SupplyLineResponse> Lines,
    IReadOnlyList<SupplyCostResponse> Costs,
    IReadOnlyList<SupplyHistoryResponse> History);

public sealed record SupplyLineResponse(Guid Id, Guid SkuId, string SkuCode, string ProductName, int Quantity, decimal? UnitPrice, decimal LineSubtotal, decimal AllocatedCost, decimal LandedUnitCost, string? LotNumber, DateOnly? ExpiryDate, string? Notes);

public sealed record SupplyShipmentEditorResponse(Guid Id, string SupplierName, string? InvoiceNumber, DateTime ShipmentDate, string Status, uint ConcurrencyVersion, Guid DestinationLocationId, string? Notes, IReadOnlyList<SupplyEditorLineResponse> Lines, IReadOnlyList<SupplyCostResponse> Costs);

public sealed record SupplyEditorLineResponse(Guid Id, Guid SkuId, string SkuCode, string ProductName, int Quantity, decimal? UnitPrice, string? LotNumber, DateOnly? ExpiryDate, string? Notes);

public sealed record SupplyCostResponse(Guid Id, string CostType, string? Description, decimal Amount);

public sealed record SupplyHistoryResponse(Guid Id, string Action, Guid ActorUserId, DateTime CreatedAt, string? Summary);
