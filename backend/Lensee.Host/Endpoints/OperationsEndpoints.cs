using System.Security.Cryptography;
using System.Globalization;
using System.Text.Json;
using Lensee.Host.Infrastructure;
using Lensee.Host.Services;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Catalog.Services;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Finance.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Inventory.Services;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Data;
using Lensee.SharedKernel.Primitives;
using Lensee.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static class OperationsEndpoints
{
    private const string InventoryReceipt = "InventoryReceipt";
    private const string WarehouseTransfer = "WarehouseTransfer";
    private const string WholesaleSale = "WholesaleSale";
    private const string RetailSale = "RetailSale";
    private const string Reserve = "Reserve";
    private const string Return = "Return";
    private const string Change = "Change";
    private const string WriteOff = "WriteOff";
    private const string Standard = "Standard";
    private const string ChangeOut = "ChangeOut";
    private const string ChangeIn = "ChangeIn";
    private const string CodChargeSettingKey = "operations.cod-charge.flat-egp";
    private const string Draft = "Draft";
    private const string Reserved = "Reserved";
    private const string Shipped = "Shipped";
    private const string Dispatched = "Dispatched";
    private const string Delivered = "Delivered";
    private const string FailedDelivery = "FailedDelivery";
    private const string Received = "Received";
    private const string Confirmed = "Confirmed";
    private const string Completed = "Completed";
    private const string Cancelled = "Cancelled";
    private const string PendingApproval = "PendingApproval";
    private const string Approved = "Approved";
    private const string Rejected = "Rejected";
    private const string MainWarehouse = "MainWarehouse";
    private const string Retail = "Retail";
    private const string Online = "Online";

    private static readonly HashSet<string> OperationTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        InventoryReceipt,
        WarehouseTransfer,
        WholesaleSale,
        RetailSale,
        Return,
        WriteOff
    };

    private static readonly HashSet<string> PaymentMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "CashHandToHand",
        "BankTransfer",
        "Wallet",
        "MerchantAccount"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static IResult RetiredReserveResult() => Results.Json(
        new { code = "operation-type-retired", detail = "Representative reserve operations are retired. Historical records remain read-only." },
        statusCode: StatusCodes.Status410Gone);

    public static RouteGroupBuilder MapOperationsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/operations").WithTags("Operations");

        group.MapGet("/cod-charge-settings", GetCodChargeSettingsAsync).RequireAuthorization("operations.read");
        group.MapPut("/cod-charge-settings", UpdateCodChargeSettingsAsync).RequireAuthorization("settings.write");

        group.MapGet("/replenishment", GetReplenishmentAsync).RequireAuthorization("operations.read");
        group.MapPost("/replenishment/reserve", ReserveReplenishmentAsync).RequireAuthorization("operations.write");
        group.MapPost("/replenishment/daily-reset", ReserveReplenishmentAsync).RequireAuthorization("operations.write");
        group.MapGet("/", ListOperationsAsync).RequireAuthorization("operations.read");
        group.MapGet("/{id:guid}", GetOperationAsync).RequireAuthorization("operations.read");
        group.MapGet("/{id:guid}/lines", GetOperationLinesAsync).RequireAuthorization("operations.read");
        group.MapGet("/{id:guid}/allocations", GetOperationAllocationsAsync).RequireAuthorization("operations.read");
        group.MapGet("/{id:guid}/source-allocations", GetOperationSourceAllocationsAsync).RequireAuthorization("operations.read");
        group.MapGet("/source-sales/{sourceOperationId:guid}/lines", GetSourceSaleLinesAsync).RequireAuthorization("operations.read");
        group.MapGet("/{id:guid}/versions", GetOperationVersionsAsync).RequireAuthorization("operations.read");
        group.MapGet("/{id:guid}/editor", GetOperationEditorAsync).RequireAuthorization("operations.write");
        group.MapGet("/batch-options/merchant", ListMerchantBatchOptionsAsync).RequireAuthorization("operations.write");
        group.MapPost("/", CreateOperationAsync).RequireAuthorization("operations.write");
        group.MapPut("/{id:guid}", UpdateOperationAsync).RequireAuthorization("operations.write");
        group.MapPut("/{id:guid}/shopify-allocation", UpdateShopifyAllocationAsync).RequireAuthorization("operations.write");
        group.MapPost("/{id:guid}/revise", ReviseOperationAsync).RequireAuthorization("operations.write");
        group.MapPost("/{id:guid}/confirm", ConfirmOperationAsync).RequireAuthorization("operations.write");
        group.MapPost("/{id:guid}/submit-approval", SubmitReturnApprovalAsync).RequireAuthorization("operations.write");
        group.MapPost("/{id:guid}/approve", ApproveReturnAsync).RequireAuthorization("operations.write");
        group.MapPost("/{id:guid}/reject", RejectReturnAsync).RequireAuthorization("operations.write");
        group.MapPost("/{id:guid}/ship", ShipOperationAsync).RequireAuthorization("operations.write");
        group.MapPost("/{id:guid}/dispatch", ShipOperationAsync).RequireAuthorization("operations.write");
        group.MapPost("/{id:guid}/receive", ReceiveOperationAsync).RequireAuthorization("operations.write");
        group.MapPost("/{id:guid}/complete", CompleteWholesaleOperationAsync).RequireAuthorization("operations.write");
        group.MapPost("/{id:guid}/delivered", ReceiveOperationAsync).RequireAuthorization("operations.write");
        group.MapPost("/{id:guid}/failed-delivery", MarkFailedDeliveryAsync).RequireAuthorization("operations.write");
        group.MapPost("/{id:guid}/cancel", CancelOperationAsync).RequireAuthorization("operations.write");
        group.MapGet("/{id:guid}/corrections", GetCorrectionLineageAsync).RequireAuthorization("operations.read");
        group.MapPost("/{id:guid}/corrections", CreateCorrectionAsync).RequireAuthorization("operations.corrections.request");
        group.MapGet("/corrections/{proposalId:guid}", GetCorrectionAsync).RequireAuthorization("operations.read");
        group.MapPost("/corrections/{proposalId:guid}/submit", SubmitCorrectionAsync).RequireAuthorization("operations.corrections.request");
        group.MapPost("/corrections/{proposalId:guid}/settlement", SubmitCorrectionSettlementAsync).RequireAuthorization("operations.corrections.request");
        group.MapPost("/corrections/{proposalId:guid}/approve", ApproveCorrectionAsync).RequireAuthorization("operations.corrections.approve");
        group.MapPost("/corrections/{proposalId:guid}/reject", RejectCorrectionAsync).RequireAuthorization("operations.corrections.approve");

        return group;
    }

    private static async Task<IResult> SubmitReturnApprovalAsync(
        Guid id,
        ReturnApprovalRequest request,
        OperationsDbContext operationsDbContext,
        NotificationsDbContext notificationsDbContext,
        MerchantBatchHistoryService batchHistoryService,
        ICurrentUser currentUser,
        IClock clock,
        IReturnWorkflowPersistenceService returnPersistence,
        CancellationToken cancellationToken)
    {
        if (!IsOperationsAdministrator(currentUser)) return Results.Forbid();
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Reason)] = ["Approval reason is required."] });
        var operation = await operationsDbContext.OperationLogs
            .Include(value => value.OperationLines)
            .Include(value => value.ReturnApprovalLines)
            .SingleOrDefaultAsync(value => value.Id == id && !value.IsDeleted, cancellationToken);
        if (operation is null) return Results.NotFound();
        if (operation.OperationType != Return) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.OperationType)] = ["Only returns require this workflow."] });
        if (operation.Status == PendingApproval) return Results.Ok(new { operationId = id, status = PendingApproval });
        if (operation.Status != Draft) return Results.Conflict(new { detail = "Only draft returns can be submitted for approval." });
        if (operation.CreatedBy != (currentUser.UserId ?? Guid.Empty) && !IsOperationsAdministrator(currentUser)) return Results.Forbid();
        var now = clock.EgyptNow;
        operation.Status = PendingApproval;
        operation.ReturnSubmissionReason = request.Reason.Trim();
        operation.ReturnSubmittedBy = currentUser.UserId;
        operation.ReturnSubmittedAt = now;
        operation.ReturnApprovalReason = request.Reason.Trim();
        operationsDbContext.ReturnApprovalLines.RemoveRange(operation.ReturnApprovalLines);
        var varianceWarnings = await BuildMerchantSalesVarianceWarningsAsync(operation, operationsDbContext, batchHistoryService, cancellationToken);
        foreach (var line in operation.OperationLines)
        {
            var lineWarnings = varianceWarnings.Where(value =>
                value.SkuId == line.SkuId &&
                value.LotNumber == NormalizeBlank(line.LotNumber) &&
                value.ExpiryDate == line.ExpiryDate).ToList();
            if (lineWarnings.Count == 0)
            {
                lineWarnings.Add(new MerchantSalesVarianceWarning(
                    line.SkuId, line.SkuCodeSnapshot, line.ProductNameSnapshot, line.LotNumber, line.ExpiryDate,
                    line.Quantity, 0, 0, 0, request.Reason.Trim(), line.PiecesPerPackSnapshot, "ApprovalRequired"));
            }
            foreach (var warning in lineWarnings)
            {
                operationsDbContext.ReturnApprovalLines.Add(new ReturnApprovalLine
                {
                    Id = Guid.NewGuid(),
                    OperationId = operation.Id,
                    OperationLineId = line.Id,
                    SkuId = line.SkuId,
                    RequestedQuantity = line.Quantity,
                    EligibleQuantity = warning.AlertType == "ApprovalRequired" ? null : Math.Max(0, warning.SoldQuantity - warning.ReturnedQuantity),
                    BatchName = line.LotNumber,
                    ExpiryDate = line.ExpiryDate,
                    MismatchType = warning.AlertType,
                    Detail = warning.Message,
                    CreatedAt = now
                });
            }
        }
        await returnPersistence.SaveAsync(operationsDbContext, cancellationToken);
        await NotifyReturnWorkflowAsync(notificationsDbContext, operation, PendingApproval, now, cancellationToken);
        return Results.Ok(new { operationId = id, status = operation.Status });
    }

    private static async Task<IResult> ApproveReturnAsync(
        Guid id,
        ReturnApprovalRequest request,
        OperationsDbContext operationsDbContext,
        NotificationsDbContext notificationsDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IReturnWorkflowPersistenceService returnPersistence,
        CancellationToken cancellationToken)
    {
        if (!IsOperationsAdministrator(currentUser)) return Results.Forbid();
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Reason)] = ["Approval reason is required."] });
        var operation = await operationsDbContext.OperationLogs.SingleOrDefaultAsync(value => value.Id == id && !value.IsDeleted, cancellationToken);
        if (operation is null) return Results.NotFound();
        if (operation.OperationType != Return) return Results.BadRequest();
        if (operation.Status == Approved) return Results.Ok(new { operationId = id, status = Approved });
        if (operation.Status != PendingApproval) return Results.Conflict(new { detail = "Only pending returns can be approved." });
        operation.Status = Approved;
        operation.ReturnApprovalReason = request.Reason.Trim();
        operation.ReturnApprovedBy = currentUser.UserId;
        operation.ReturnApprovedAt = clock.EgyptNow;
        await returnPersistence.SaveAsync(operationsDbContext, cancellationToken);
        await NotifyReturnWorkflowAsync(notificationsDbContext, operation, Approved, clock.EgyptNow, cancellationToken);
        return Results.Ok(new { operationId = id, status = operation.Status });
    }

    private static async Task<IResult> RejectReturnAsync(
        Guid id,
        ReturnApprovalRequest request,
        OperationsDbContext operationsDbContext,
        NotificationsDbContext notificationsDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IReturnWorkflowPersistenceService returnPersistence,
        CancellationToken cancellationToken)
    {
        if (!IsOperationsAdministrator(currentUser)) return Results.Forbid();
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Reason)] = ["Rejection reason is required."] });
        var operation = await operationsDbContext.OperationLogs.SingleOrDefaultAsync(value => value.Id == id && !value.IsDeleted, cancellationToken);
        if (operation is null) return Results.NotFound();
        if (operation.Status == Rejected) return Results.Ok(new { operationId = id, status = Rejected });
        if (operation.Status != PendingApproval) return Results.Conflict(new { detail = "Only pending returns can be rejected." });
        operation.Status = Rejected;
        operation.ReturnApprovalReason = request.Reason.Trim();
        operation.ReturnRejectionReason = request.Reason.Trim();
        operation.ReturnRejectedBy = currentUser.UserId;
        operation.ReturnRejectedAt = clock.EgyptNow;
        await returnPersistence.SaveAsync(operationsDbContext, cancellationToken);
        await NotifyReturnWorkflowAsync(notificationsDbContext, operation, Rejected, clock.EgyptNow, cancellationToken);
        return Results.Ok(new { operationId = id, status = operation.Status });
    }

    private static async Task<IResult> CreateCorrectionAsync(
        Guid id,
        CreateOperationCorrectionCommand request,
        OperationCorrectionService service,
        ICurrentUser currentUser,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(id, request, currentUser.UserId ?? Guid.Empty, currentUser.Role, cancellationToken);
        return ToCorrectionResult(result, httpContext);
    }

    private static async Task<IResult> SubmitCorrectionSettlementAsync(
        Guid proposalId,
        SubmitOperationCorrectionSettlementCommand request,
        OperationCorrectionService service,
        ICurrentUser currentUser,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.SubmitSettlementAsync(proposalId, request, currentUser.UserId ?? Guid.Empty, currentUser.Role, cancellationToken);
        return ToCorrectionResult(result, httpContext);
    }

    private static async Task<IResult> ApproveCorrectionAsync(
        Guid proposalId,
        OperationCorrectionService service,
        ICurrentUser currentUser,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.ApproveAsync(proposalId, currentUser.UserId ?? Guid.Empty, currentUser.Role, cancellationToken);
        return ToCorrectionResult(result, httpContext);
    }

    private static async Task<IResult> RejectCorrectionAsync(
        Guid proposalId,
        RejectOperationCorrectionCommand request,
        OperationCorrectionService service,
        ICurrentUser currentUser,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.RejectAsync(proposalId, request, currentUser.UserId ?? Guid.Empty, currentUser.Role, cancellationToken);
        return ToCorrectionResult(result, httpContext);
    }

    private static async Task<IResult> GetCorrectionAsync(
        Guid proposalId,
        OperationCorrectionService service,
        OperationsDbContext operationsDbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var proposal = await service.GetAsync(proposalId, cancellationToken);
        if (proposal is null)
        {
            return Results.NotFound();
        }

        var operation = await operationsDbContext.OperationLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == proposal.OperationId && !value.IsDeleted, cancellationToken);
        return operation is null || !CanReadOperation(currentUser, operation)
            ? Results.NotFound()
            : Results.Ok(proposal);
    }

    private static async Task<IResult> GetCorrectionLineageAsync(
        Guid id,
        OperationCorrectionService service,
        OperationsDbContext operationsDbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var operation = await operationsDbContext.OperationLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == id && !value.IsDeleted, cancellationToken);
        if (operation is null || !CanReadOperation(currentUser, operation))
        {
            return Results.NotFound();
        }

        return Results.Ok(await service.GetLineageAsync(id, cancellationToken));
    }

    private static IResult ToCorrectionResult(CorrectionCommandResult result, HttpContext httpContext)
    {
        if (result.Value is not null)
        {
            httpContext.Items[AuditMutationMiddleware.AuditWrittenItemKey] = true;
            return result.StatusCode == StatusCodes.Status201Created
                ? Results.Created($"/api/v1/operations/corrections/{result.Value.Id}", result.Value)
                : Results.Ok(result.Value);
        }

        return result.StatusCode switch
        {
            StatusCodes.Status404NotFound => Results.NotFound(),
            StatusCodes.Status403Forbidden => Results.Forbid(),
            StatusCodes.Status409Conflict => Results.Conflict(new { code = result.Code ?? "transition-conflict", detail = result.Error }),
            _ => Results.ValidationProblem(
                new Dictionary<string, string[]> { [result.ErrorField ?? "correction"] = [result.Error ?? "The correction request is invalid."] },
                statusCode: result.StatusCode)
        };
    }

    private static async Task<IResult> GetReplenishmentAsync(
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        OperationsDbContext operationsDbContext,
        ICurrentUser currentUser,
        bool? paged,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        if (IsWarehouseClerk(currentUser) && currentUser.LocationId is null)
        {
            return Results.Forbid();
        }

        var request = new PageRequest(page ?? 1, pageSize ?? 50);
        var result = await BuildReplenishmentRowsAsync(inventoryDbContext, catalogDbContext, operationsDbContext, currentUser, null, null, paged == true ? request : null, cancellationToken);
        if (paged != true) return Results.Ok(result.Rows);
        return Results.Ok(new PagedResult<ReplenishmentRowResponse>(result.Rows, request.Page, request.PageSize, result.Total));
    }

    private static async Task<IResult> ReserveReplenishmentAsync(
        ReplenishmentReserveRequest request,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        NotificationsDbContext notificationsDbContext,
        StockLedgerService ledgerService,
        TargetReplenishmentService replenishmentService,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!LenseeRoles.Normalize(currentUser.Role).Equals(LenseeRoles.Admin, StringComparison.OrdinalIgnoreCase) &&
            !LenseeRoles.Normalize(currentUser.Role).Equals(LenseeRoles.ERPAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Forbid();
        }

        // The Inventory button is an explicit manual run. It must remain usable even
        // when the midnight scheduled run has already completed for the same Cairo day.
        var serviceResult = await replenishmentService.RunAsync("Manual", request.LocationId, request.SkuId, cancellationToken);
        return Results.Ok(new ReplenishmentReserveResponse(serviceResult.CreatedOperations, serviceResult.UncoveredQuantity, [], []));
    }

    private static async Task<IResult> ListOperationsAsync(
        int? page,
        int? pageSize,
        bool? includeCompleted,
        string? search,
        string? operationType,
        string? status,
        Guid? sourceLocationId,
        Guid? destinationLocationId,
        DateTime? createdFrom,
        DateTime? createdTo,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        PaymentsDbContext paymentsDbContext,
        CrmDbContext crmDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var safePage = Math.Max(page ?? 1, 1);
        var safePageSize = pageSize is 25 or 50 or 100 or 250 ? pageSize.Value : 25;
        var query = operationsDbContext.OperationLogs
            .AsNoTracking()
            .Include(operation => operation.ShopifyOrderLink)
            .Where(operation => !operation.IsDeleted)
            .AsQueryable();

        if (includeCompleted == false)
        {
            // "Received" is final for a transfer or inventory receipt, but not for
            // a wholesale sale: that sale still needs its explicit Complete step to
            // create the Payments work item. Keep this queue filter aligned with
            // each operation type's terminal state rather than status alone.
            query = query.Where(operation => !(
                operation.Status == Cancelled ||
                (operation.OperationType == WholesaleSale && operation.Status == Completed) ||
                (operation.OperationType == RetailSale && (operation.Status == Completed || operation.Status == Delivered || operation.Status == FailedDelivery)) ||
                ((operation.OperationType == WarehouseTransfer || operation.OperationType == InventoryReceipt) && operation.Status == Received) ||
                (operation.OperationType == Return && (operation.Status == Confirmed || operation.Status == Rejected)) ||
                (operation.OperationType == WriteOff && operation.Status == Confirmed) ||
                ((operation.OperationType == Change || operation.OperationType == Reserve) && (operation.Status == Confirmed || operation.Status == Received || operation.Status == Completed))));
        }

        if (!string.IsNullOrWhiteSpace(operationType)) query = query.Where(operation => operation.OperationType == operationType.Trim());
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(operation => operation.Status == status.Trim());
        if (sourceLocationId.HasValue) query = query.Where(operation => operation.SourceLocationId == sourceLocationId.Value);
        if (destinationLocationId.HasValue) query = query.Where(operation => operation.DestinationLocationId == destinationLocationId.Value);
        if (createdFrom.HasValue) query = query.Where(operation => operation.CreatedAt >= createdFrom.Value);
        if (createdTo.HasValue) query = query.Where(operation => operation.CreatedAt < createdTo.Value.Date.AddDays(1));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var pattern = $"%{term}%";
            var paymentOperationIds = await paymentsDbContext.MainPaymentLogs.AsNoTracking()
                .Where(payment => !payment.IsDeleted && (EF.Functions.ILike(payment.Notes ?? "", pattern) || EF.Functions.ILike(payment.PaymentMethod, pattern)))
                .Select(payment => payment.OperationId)
                .ToListAsync(cancellationToken);
            paymentOperationIds.AddRange(await paymentsDbContext.InstallmentSubLogs.AsNoTracking()
                .Where(sub => EF.Functions.ILike(sub.TransactionReference ?? "", pattern))
                .Select(sub => sub.MainLog.OperationId)
                .ToListAsync(cancellationToken));
            paymentOperationIds.AddRange(await paymentsDbContext.CashRecords.AsNoTracking()
                .Where(record => EF.Functions.ILike(record.TransactionReference ?? "", pattern))
                .Where(record => record.OperationId.HasValue)
                .Select(record => record.OperationId!.Value)
                .ToListAsync(cancellationToken));
            paymentOperationIds.AddRange(await paymentsDbContext.MerchantAccountEntries.AsNoTracking()
                .Where(entry => EF.Functions.ILike(entry.TransactionReference ?? "", pattern))
                .Where(entry => entry.OperationId.HasValue)
                .Select(entry => entry.OperationId!.Value)
                .ToListAsync(cancellationToken));
            var merchantIds = await crmDbContext.Merchants.AsNoTracking()
                .Where(merchant => !merchant.IsDeleted && EF.Functions.ILike(merchant.BusinessName, pattern))
                .Select(merchant => merchant.Id)
                .ToListAsync(cancellationToken);
            query = query.Where(operation =>
                EF.Functions.ILike(operation.OperationNumber, pattern) ||
                EF.Functions.ILike(operation.ExternalReference ?? "", pattern) ||
                EF.Functions.ILike(operation.ClientName ?? "", pattern) ||
                EF.Functions.ILike(operation.Notes ?? "", pattern) ||
                operation.OperationLines.Any(line => EF.Functions.ILike(line.SkuCodeSnapshot ?? "", pattern) || EF.Functions.ILike(line.ProductNameSnapshot ?? "", pattern)) ||
                (operation.ClientId.HasValue && merchantIds.Contains(operation.ClientId.Value)) ||
                paymentOperationIds.Contains(operation.Id));
        }

        if (IsWarehouseClerk(currentUser))
        {
            if (currentUser.LocationId is not { } locationId)
            {
                return Results.Forbid();
            }

            query = query.Where(operation => operation.SourceLocationId == locationId || operation.DestinationLocationId == locationId);
        }

        var total = await query.CountAsync(cancellationToken);
        var operations = await query
            .OrderByDescending(operation => operation.CreatedAt)
            .ThenByDescending(operation => operation.Id)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);
        var pageIds = operations.Select(value => value.Id).ToArray();
        var allocationPendingIds = await operationsDbContext.OperationLines.AsNoTracking()
            .Where(line => pageIds.Contains(line.OperationId) && line.ExpiryDate == null)
            .Select(line => line.OperationId).Distinct().ToListAsync(cancellationToken);
        var allocationPending = allocationPendingIds.ToHashSet();
        var locationLookup = await LoadLocationLookupAsync(inventoryDbContext, operations, cancellationToken);
        var userLookup = await LoadUserLookupAsync(identityDbContext, operations, cancellationToken);

        return Results.Ok(new PagedResult<OperationListResponse>(
            operations.Select(operation => ToListResponse(operation, locationLookup, userLookup, currentUser, allocationPending.Contains(operation.Id))).ToList(),
            safePage,
            safePageSize,
            total));
    }

    private static async Task<IResult> GetOperationAsync(
        Guid id,
        bool? includeCollections,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        InventoryDbContext inventoryDbContext,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var operation = includeCollections == false
            ? await LoadOperationSummaryAsync(operationsDbContext, id, cancellationToken)
            : await LoadOperationAsync(operationsDbContext, id, cancellationToken);
        if (operation is null)
        {
            return Results.NotFound();
        }
        if (!CanReadOperation(currentUser, operation))
        {
            return Results.Forbid();
        }

        var locationLookup = await LoadLocationLookupAsync(inventoryDbContext, [operation], cancellationToken);
        var userLookup = await LoadUserLookupAsync(identityDbContext, [operation], cancellationToken);
        var wearCycles = includeCollections == false
            ? new Dictionary<Guid, WearCycleInfo>()
            : await LoadWearCyclesBySkuAsync(catalogDbContext, operation.OperationLines, cancellationToken);
        var allocationPending = includeCollections == false
            ? await operationsDbContext.OperationLines.AsNoTracking().AnyAsync(line => line.OperationId == id && line.ExpiryDate == null, cancellationToken)
            : IsAllocationPending(operation);
        var returnFinancialOutcome = await GetReturnFinancialOutcomeAsync(operation, operationsDbContext, paymentsDbContext, cancellationToken);
        return Results.Ok(ToDetailResponse(operation, locationLookup, userLookup, wearCycles, currentUser, includeCollections != false, allocationPending, returnFinancialOutcome));
    }

    private static async Task<IResult> GetOperationLinesAsync(Guid id, int? page, int? pageSize, OperationsDbContext operationsDbContext, CatalogDbContext catalogDbContext, InventoryDbContext inventoryDbContext, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var operation = await LoadOperationSummaryAsync(operationsDbContext, id, cancellationToken);
        if (operation is null) return Results.NotFound();
        if (!CanReadOperation(currentUser, operation)) return Results.Forbid();
        var request = new PageRequest(page ?? 1, pageSize ?? 50);
        var query = operationsDbContext.OperationLines.AsNoTracking().Where(line => line.OperationId == id);
        var total = await query.CountAsync(cancellationToken);
        var entities = await query.OrderBy(line => line.Id).Skip(request.Skip).Take(request.PageSize).ToListAsync(cancellationToken);
        var wearCycles = await LoadWearCyclesBySkuAsync(catalogDbContext, entities, cancellationToken);
        var rows = entities.Select(line =>
        {
            wearCycles.TryGetValue(line.SkuId, out var wearCycle);
            return ToLineResponse(line, wearCycle);
        }).ToList();
        return Results.Ok(new PagedResult<OperationLineResponse>(rows, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> GetOperationVersionsAsync(Guid id, int? page, int? pageSize, OperationsDbContext dbContext, InventoryDbContext inventoryDbContext, IdentityDbContext identityDbContext, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var operation = await LoadOperationSummaryAsync(dbContext, id, cancellationToken);
        if (operation is null) return Results.NotFound();
        if (!CanReadOperation(currentUser, operation)) return Results.Forbid();
        var request = new PageRequest(page ?? 1, pageSize ?? 25);
        var query = dbContext.OperationVersions.AsNoTracking().Where(value => value.OperationId == id);
        var total = await query.CountAsync(cancellationToken);
        var versions = await query.OrderByDescending(value => value.VersionNumber).ThenByDescending(value => value.Id).Skip(request.Skip).Take(request.PageSize)
            .Select(value => new { value.Id, value.VersionNumber, value.Reason, value.EditedAt, value.EditedBy, value.EditedActorName })
            .ToListAsync(cancellationToken);
        var editedByIds = versions.Select(version => version.EditedBy).Distinct().ToArray();
        var users = await identityDbContext.Users.AsNoTracking().Where(value => editedByIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, cancellationToken);
        var rows = versions.Select(version => new OperationVersionResponse(version.Id, version.VersionNumber, version.Reason, version.EditedAt, GetActorDisplayName(version.EditedActorName, version.EditedBy, users), operation.CurrentVersionId == version.Id, false, false, false, false)).ToList();
        return Results.Ok(new PagedResult<OperationVersionResponse>(rows, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> GetOperationAllocationsAsync(Guid id, int? page, int? pageSize, OperationsDbContext dbContext, InventoryDbContext inventoryDbContext, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var operation = await LoadOperationSummaryAsync(dbContext, id, cancellationToken);
        if (operation is null) return Results.NotFound();
        if (!CanReadOperation(currentUser, operation)) return Results.Forbid();
        var currentVersion = operation.CurrentVersionId.HasValue
            ? await dbContext.OperationVersions.AsNoTracking().FirstOrDefaultAsync(value => value.Id == operation.CurrentVersionId.Value, cancellationToken)
            : await dbContext.OperationVersions.AsNoTracking().Where(value => value.OperationId == id).OrderByDescending(value => value.VersionNumber).FirstOrDefaultAsync(cancellationToken);
        if (currentVersion is not null) operation.OperationVersions.Add(currentVersion);
        var rows = ReadTransferAllocations(operation).SelectMany(allocation => allocation.Allocations.Select(batch => new OperationAllocationResponse(allocation.SkuId, null, null, batch.BatchId, batch.Quantity, batch.LotNumber, batch.ExpiryDate))).OrderBy(value => value.SkuId).ThenBy(value => value.BatchId).ToList();
        var request = new PageRequest(page ?? 1, pageSize ?? 50);
        return Results.Ok(new PagedResult<OperationAllocationResponse>(rows.Skip(request.Skip).Take(request.PageSize).ToList(), request.Page, request.PageSize, rows.Count));
    }

    private static async Task<IResult> GetOperationEditorAsync(Guid id, OperationsDbContext dbContext, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var operation = await dbContext.OperationLogs.AsNoTracking().Include(value => value.OperationLines).Include(value => value.InventoryReceiptHeader).Include(value => value.ShopifyOrderLink).FirstOrDefaultAsync(value => value.Id == id && !value.IsDeleted, cancellationToken);
        if (operation is null) return Results.NotFound();
        if (!CanReadOperation(currentUser, operation)) return Results.Forbid();
        var lines = operation.OperationLines
            .OrderBy(value => value.Id)
            .Select(line => new OperationEditorLineResponse(
                line.Id,
                line.SkuId,
                line.SkuCodeSnapshot,
                line.ProductNameSnapshot,
                line.EntryMode,
                line.Section,
                line.Quantity,
                line.BonusQuantity,
                line.UnitPrice,
                line.LineTotal,
                line.LotNumber,
                line.ExpiryDate,
                line.MerchantNameSnapshot,
                line.RepresentativeNameSnapshot,
                line.LineNotes,
                line.ShopifyLineItemId,
                line.ShopifyVariantId,
                line.ShopifySkuSnapshot,
                line.ShopifyTitleSnapshot,
                line.ShopifyVariantTitleSnapshot,
                line.ShopifyPropertiesSnapshot,
                line.ReturnDisposition,
                line.PricingMode,
                line.LineType ?? ResolveLegacyLineType(line.PricingMode, line.BonusQuantity)))
            .ToList();

        return Results.Ok(new OperationEditorResponse(
            operation.Id, operation.OperationType, operation.ConcurrencyVersion, operation.SourceLocationId, operation.DestinationLocationId,
            operation.ClientId, operation.ClientName, operation.RepresentativeId, operation.PaymentMethod, operation.Notes,
            operation.InventoryReceiptHeader is null ? null : new ReceiptResponse(operation.InventoryReceiptHeader.SupplierName, operation.InventoryReceiptHeader.InvoiceNumber),
            operation.SalesChannel, operation.BuyerPhone,
            lines.Count,
            lines, operation.FinanceAccountId, operation.ExternalReference, operation.ReturnReferenceOperationId));
    }

    private static async Task<IResult> ListMerchantBatchOptionsAsync(
        Guid merchantId,
        Guid skuId,
        Guid? locationId,
        OperationsDbContext operationsDbContext,
        MerchantBatchHistoryService historyService,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (IsWarehouseClerk(currentUser))
        {
            if (currentUser.LocationId is not { } clerkLocationId || locationId != clerkLocationId)
            {
                return Results.Forbid();
            }

            var canAccessMerchant = await operationsDbContext.OperationLogs.AnyAsync(operation =>
                !operation.IsDeleted &&
                operation.ClientId == merchantId &&
                (operation.SourceLocationId == clerkLocationId || operation.DestinationLocationId == clerkLocationId),
                cancellationToken);
            if (!canAccessMerchant)
            {
                return Results.NotFound();
            }
        }

        var rows = (await historyService.LoadAsync(merchantId, cancellationToken: cancellationToken))
            .Where(row => row.Key.SkuId == skuId &&
                !string.IsNullOrWhiteSpace(row.Key.LotNumber) &&
                row.Key.ExpiryDate.HasValue)
            .Select(row => new MerchantBatchOptionResponse(
                row.Key.LotNumber!,
                row.Key.ExpiryDate!.Value,
                row.SoldQuantity,
                row.ReturnedQuantity,
                row.RecordedBalanceQuantity,
                row.SoldPhysicalPieces,
                row.ReturnedPhysicalPieces,
                row.RecordedBalancePhysicalPieces,
                row.HasLegacyUnitUnknown))
            .OrderBy(row => row.ExpiryDate)
            .ThenBy(row => row.LotNumber)
            .ToList();

        return Results.Ok(rows);
    }

    private static async Task<IResult> GetCodChargeSettingsAsync(SharedDbContext sharedDbContext, CancellationToken cancellationToken)
    {
        var setting = await sharedDbContext.SystemSettings.AsNoTracking()
            .FirstOrDefaultAsync(value => value.Key == CodChargeSettingKey, cancellationToken);
        var amount = setting is not null && decimal.TryParse(setting.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : (decimal?)null;
        return Results.Ok(new CodChargeSettingsResponse(amount.HasValue, amount));
    }

    private static async Task<IResult> UpdateCodChargeSettingsAsync(
        CodChargeSettingsRequest request,
        SharedDbContext sharedDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId;
        var isPrimaryAdmin = actorId is { } id && await identityDbContext.Users.AsNoTracking()
            .AnyAsync(user => user.Id == id && user.IsActive && user.Role == LenseeRoles.Admin && user.IsPrimaryAdmin, cancellationToken);
        if (!isPrimaryAdmin) return Results.Forbid();
        if (request.FlatAmount is < 0m || request.FlatAmount is { } amount && decimal.Round(amount, 2) != amount)
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.FlatAmount)] = ["COD charge must be zero or greater and use no more than two decimal places."] });

        var setting = await sharedDbContext.SystemSettings.SingleOrDefaultAsync(value => value.Key == CodChargeSettingKey, cancellationToken);
        if (request.FlatAmount is null)
        {
            if (setting is not null) sharedDbContext.SystemSettings.Remove(setting);
        }
        else if (setting is null)
        {
            sharedDbContext.SystemSettings.Add(new SystemSetting { Key = CodChargeSettingKey, Value = request.FlatAmount.Value.ToString("0.##", CultureInfo.InvariantCulture), Description = "Default flat COD charge in EGP for online sale orders.", UpdatedAt = clock.EgyptNow });
        }
        else
        {
            setting.Value = request.FlatAmount.Value.ToString("0.##", CultureInfo.InvariantCulture);
            setting.Description = "Default flat COD charge in EGP for online sale orders.";
            setting.UpdatedAt = clock.EgyptNow;
        }
        await sharedDbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new CodChargeSettingsResponse(request.FlatAmount.HasValue, request.FlatAmount));
    }

    private static async Task<CodChargeResolution> ResolveDraftCodChargeAsync(
        OperationRequest request,
        SharedDbContext sharedDbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var onlineCod = NormalizeOperationType(request.OperationType) == RetailSale &&
                        NormalizeSalesChannel(request.SalesChannel) == "Online" &&
                        (string.Equals(request.PaymentStatus, "DueOnDelivery", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(request.PaymentMethod, "CashHandToHand", StringComparison.OrdinalIgnoreCase));
        if (!onlineCod)
        {
            if (request.CodCharge is > 0m || !string.IsNullOrWhiteSpace(request.CodChargeOverrideReason))
                return new CodChargeResolution(0m, null, "A COD charge can be set only for an online COD order.");
            return new CodChargeResolution(0m, null, null);
        }

        var overrideReason = TrimToNull(request.CodChargeOverrideReason);
        if (overrideReason is not null || request.CodCharge.HasValue)
        {
            if (LenseeRoles.Normalize(currentUser.Role) is not (LenseeRoles.Admin or LenseeRoles.ERPAdmin))
                return new CodChargeResolution(0m, null, "Only an Operations administrator may set an order-specific COD charge override.");
            if (overrideReason is null || request.CodCharge is not { } overrideAmount || overrideAmount < 0m || decimal.Round(overrideAmount, 2) != overrideAmount)
                return new CodChargeResolution(0m, null, "A non-negative COD override amount with at most two decimal places is required with the override reason.");
            return new CodChargeResolution(overrideAmount, overrideReason, null);
        }

        var setting = await sharedDbContext.SystemSettings.AsNoTracking().FirstOrDefaultAsync(value => value.Key == CodChargeSettingKey, cancellationToken);
        if (setting is not null && decimal.TryParse(setting.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var configured))
            return new CodChargeResolution(configured, null, null);
        return new CodChargeResolution(0m, null, null);
    }

    private static IResult? ValidateCodChargeAtConfirmation(OperationLog operation)
    {
        if (!IsOnlineSale(operation) ||
            !(string.Equals(operation.PaymentStatus, "DueOnDelivery", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(operation.PaymentMethod, "CashHandToHand", StringComparison.OrdinalIgnoreCase)))
            return null;

        if (operation.CodCharge < 0m || decimal.Round(operation.CodCharge, 2) != operation.CodCharge)
            return Results.Conflict(new { code = "cod-charge-invalid", detail = "The COD charge is invalid." });
        return null;
    }

    private static async Task<IResult> CreateOperationAsync(
        OperationRequest request,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        CrmDbContext crmDbContext,
        InventoryDbContext inventoryDbContext,
        PaymentsDbContext paymentsDbContext,
        SharedDbContext sharedDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        IReturnWorkflowPersistenceService returnPersistence,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateDraftAsync(request, catalogDbContext, crmDbContext, inventoryDbContext, operationsDbContext, currentUser, isCreate: true, cancellationToken);
        var codChargeResolution = await ResolveDraftCodChargeAsync(request, sharedDbContext, currentUser, cancellationToken);
        if (codChargeResolution.Error is not null)
            validation.Errors[nameof(request.CodCharge)] = [codChargeResolution.Error];
        if (validation.Errors.Count > 0)
        {
            return Results.ValidationProblem(validation.Errors);
        }

        if (!CanCreateDraft(currentUser, request, validation.SourceLocation, validation.DestinationLocation))
        {
            return Results.Forbid();
        }

        var clientRequestId = TrimToNull(request.ClientRequestId);
        if (clientRequestId is not null)
        {
            var existing = await operationsDbContext.OperationLogs.AsNoTracking()
                .FirstOrDefaultAsync(value => value.ClientRequestId == clientRequestId && !value.IsDeleted, cancellationToken);
            if (existing is not null)
            {
                return Results.Conflict(new { code = "operation-request-replayed", detail = "This client request was already accepted.", operationId = existing.Id });
            }
        }

        var now = clock.EgyptNow;
        var operation = new OperationLog
        {
            Id = Guid.NewGuid(),
            OperationNumber = $"OP-{now:yyyyMMddHHmmss}-{RandomNumberGenerator.GetInt32(100, 1000)}",
            ExternalReference = NormalizeOperationReference(request.ExternalReference),
            OperationType = NormalizeOperationType(request.OperationType),
            Status = Draft,
            SourceLocationId = request.SourceLocationId,
            DestinationLocationId = request.DestinationLocationId,
            ClientId = validation.CorrectionSource?.ClientId ?? validation.Merchant?.Id,
            ClientName = validation.CorrectionSource?.ClientName ?? validation.Merchant?.BusinessName ?? TrimToNull(request.BuyerName),
            BuyerPhone = validation.CorrectionSource?.BuyerPhone ?? TrimToNull(request.BuyerPhone),
            SalesChannel = NormalizeSalesChannel(request.SalesChannel),
            ShippingAddress = TrimToNull(request.ShippingAddress),
            DeliveryFee = request.DeliveryFee ?? 0,
            CodCharge = codChargeResolution.Amount,
            CodChargeOverrideReason = codChargeResolution.OverrideReason,
            BuyerContactVerified = request.BuyerContactVerified == true,
            ExternalPaymentProvider = TrimToNull(request.ExternalPaymentProvider),
            ExternalPaymentReference = TrimToNull(request.ExternalPaymentReference),
            ExternalPaymentVerified = request.ExternalPaymentVerified == true,
            PaymentStatus = NormalizePaymentStatus(request.PaymentStatus),
            ReturnReferenceOperationId = request.ReturnReferenceOperationId,
            ClientRequestId = clientRequestId,
            RepresentativeId = validation.Representative?.Id,
            PaymentMethod = validation.CorrectionSource?.PaymentMethod ?? NormalizeStoredPaymentMethod(request.PaymentMethod),
            // Operations captures the intended payment track only.  A receiving
            // Finance account belongs to the later Payments collection.
            FinanceAccountId = null,
            Notes = request.Notes,
            CreatedBy = currentUser.UserId ?? Guid.Empty,
            CreatedAt = now
        };

        await SharedDbTransaction.ExecuteAsync(operationsDbContext, async () =>
        {
            operationsDbContext.OperationLogs.Add(operation);
            AddLines(operation, validation.SkusById, request.Lines, validation.Merchant, validation.Representative);
            AddSourceAllocations(operationsDbContext, operation, request.Lines, now);
            if (operation.OperationType == InventoryReceipt)
            {
                operationsDbContext.InventoryReceiptHeaders.Add(new InventoryReceiptHeader
                {
                    Id = Guid.NewGuid(),
                    OperationId = operation.Id,
                    SupplierName = request.Receipt?.SupplierName?.Trim() ?? "Supplier",
                    InvoiceNumber = TrimToNull(request.Receipt?.InvoiceNumber),
                    ReceiptDate = now
                });
            }

            await operationsDbContext.SaveChangesAsync(cancellationToken);
            await AddVersionAsync(operationsDbContext, operation, "Initial", currentUser.UserId ?? Guid.Empty, CreateSnapshot(operation), now, cancellationToken);
            await operationsDbContext.SaveChangesAsync(cancellationToken);
            if (operation.OperationType == RetailSale)
            {
                await EnsureRetailDraftPaymentLogAsync(operation, paymentsDbContext, currentUser.UserId ?? Guid.Empty, now, cancellationToken);
            }
            await auditLogWriter.WriteAsync(
                "Operation",
                operation.Id,
                "Create",
                new { operation.OperationNumber, operation.OperationType, operation.Status },
                cancellationToken: cancellationToken);
        }, cancellationToken, identityDbContext, paymentsDbContext);
        var created = await LoadOperationAsync(operationsDbContext, operation.Id, cancellationToken);
        var locationLookup = await LoadLocationLookupAsync(inventoryDbContext, [created!], cancellationToken);
        var userLookup = await LoadUserLookupAsync(identityDbContext, [created!], cancellationToken);
        var wearCycles = await LoadWearCyclesBySkuAsync(catalogDbContext, created!.OperationLines, cancellationToken);

        return Results.Created($"/api/v1/operations/{operation.Id}", ToDetailResponse(created!, locationLookup, userLookup, wearCycles, currentUser));
    }

    private static async Task EnsureRetailDraftPaymentLogAsync(
        OperationLog operation,
        PaymentsDbContext paymentsDbContext,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (operation.OperationType != RetailSale || operation.Status is not (Draft or Completed)) return;
        var isOnlineCod = IsOnlineSale(operation) &&
                          (string.Equals(operation.PaymentStatus, "DueOnDelivery", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(operation.PaymentMethod, "CashHandToHand", StringComparison.OrdinalIgnoreCase));
        var total = operation.OperationLines.Sum(line => line.LineTotal) + (isOnlineCod ? operation.CodCharge : 0m);
        var log = await paymentsDbContext.MainPaymentLogs
            .FirstOrDefaultAsync(value => value.OperationId == operation.Id && !value.IsDeleted, cancellationToken);
        if (log is null)
        {
            paymentsDbContext.MainPaymentLogs.Add(new MainPaymentLog
            {
                Id = Guid.NewGuid(),
                OperationId = operation.Id,
                MerchantId = operation.ClientId,
                Scope = operation.ClientId.HasValue ? "MerchantAccount" : "OtherPayments",
                TotalAmount = total,
                AmountPaid = 0m,
                PaymentMethod = operation.PaymentMethod,
                Status = "PendingAccountant",
                InitializedBy = userId,
                InitializedAt = now,
                LastModifiedBy = userId,
                LastModifiedAt = now,
                Notes = operation.Status == Draft
                    ? "Created from a retail sale draft. No collection is recorded until it is submitted and reviewed in Payments."
                    : "Created from a completed retail sale. No collection is recorded until it is submitted and reviewed in Payments."
            });
        }
        else
        {
            // Draft edits can update the expected amount and intended track,
            // but must never create a second payment work item or record money.
            log.MerchantId = operation.ClientId;
            log.Scope = operation.ClientId.HasValue ? "MerchantAccount" : "OtherPayments";
            log.TotalAmount = total;
            log.PaymentMethod = operation.PaymentMethod;
            log.LastModifiedBy = userId;
            log.LastModifiedAt = now;
        }
        await paymentsDbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<IResult> UpdateOperationAsync(
        Guid id,
        OperationRequest request,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        CrmDbContext crmDbContext,
        InventoryDbContext inventoryDbContext,
        PaymentsDbContext paymentsDbContext,
        SharedDbContext sharedDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        IReturnWorkflowPersistenceService returnPersistence,
        CancellationToken cancellationToken)
    {
        var operation = await LoadOperationForDraftUpdateAsync(operationsDbContext, id, cancellationToken);
        if (operation is null)
        {
            return Results.NotFound();
        }
        if (operation.OperationType is Change or Reserve)
            return Results.Json(new { code = "operation-type-retired", detail = "Historical Change and representative-reserve operations are read-only." }, statusCode: StatusCodes.Status410Gone);
        if (operation.Status != Draft)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.Status)] = ["Only draft operations can be edited."] });
        }
        if (operationsDbContext.Database.IsRelational() && request.ExpectedVersion is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.ExpectedVersion)] = ["expectedVersion is required when editing a draft operation."] });
        }

        var validation = await ValidateDraftAsync(request, catalogDbContext, crmDbContext, inventoryDbContext, operationsDbContext, currentUser, isCreate: false, cancellationToken);
        var codChargeResolution = await ResolveDraftCodChargeAsync(request, sharedDbContext, currentUser, cancellationToken);
        if (codChargeResolution.Error is not null)
            validation.Errors[nameof(request.CodCharge)] = [codChargeResolution.Error];
        if (validation.Errors.Count > 0)
        {
            return Results.ValidationProblem(validation.Errors);
        }
        if (!CanCreateDraft(currentUser, request, validation.SourceLocation, validation.DestinationLocation))
        {
            return Results.Forbid();
        }
        if (operation.SalesChannel == "Shopify")
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request)] = ["Shopify commercial data is read-only. Use the Shopify allocation endpoint to select batch and expiry."]
            });
        }

        IResult? conflictResult = null;
        try
        {
            await SharedDbTransaction.ExecuteAsync(operationsDbContext, async () =>
            {
                await LockOperationAsync(operationsDbContext, operation.Id, cancellationToken);
                await operationsDbContext.Entry(operation).ReloadAsync(cancellationToken);
                if (operation.Status != Draft)
                {
                    conflictResult = Results.Conflict(new { code = "transition-conflict", detail = "The operation is no longer a draft." });
                    return;
                }
                if (request.ExpectedVersion.HasValue && operation.ConcurrencyVersion != request.ExpectedVersion.Value)
                {
                    conflictResult = Results.Conflict(new { code = "stale-version", detail = "The operation has been changed by another request." });
                    return;
                }

                operation.OperationType = NormalizeOperationType(request.OperationType);
                operation.ExternalReference = NormalizeOperationReference(request.ExternalReference);
                operation.SourceLocationId = request.SourceLocationId;
                operation.DestinationLocationId = request.DestinationLocationId;
                operation.ClientId = validation.CorrectionSource?.ClientId ?? validation.Merchant?.Id;
                operation.ClientName = validation.CorrectionSource?.ClientName ?? validation.Merchant?.BusinessName ?? TrimToNull(request.BuyerName);
                operation.BuyerPhone = validation.CorrectionSource?.BuyerPhone ?? TrimToNull(request.BuyerPhone);
                operation.SalesChannel = NormalizeSalesChannel(request.SalesChannel);
                operation.ShippingAddress = TrimToNull(request.ShippingAddress);
                operation.DeliveryFee = request.DeliveryFee ?? 0;
                operation.CodCharge = codChargeResolution.Amount;
                operation.CodChargeOverrideReason = codChargeResolution.OverrideReason;
                operation.BuyerContactVerified = request.BuyerContactVerified == true;
                operation.ExternalPaymentProvider = TrimToNull(request.ExternalPaymentProvider);
                operation.ExternalPaymentReference = TrimToNull(request.ExternalPaymentReference);
                operation.ExternalPaymentVerified = request.ExternalPaymentVerified == true;
                operation.PaymentStatus = NormalizePaymentStatus(request.PaymentStatus);
                operation.ReturnReferenceOperationId = request.ReturnReferenceOperationId;
                operation.RepresentativeId = validation.Representative?.Id;
                operation.PaymentMethod = validation.CorrectionSource?.PaymentMethod ?? NormalizeStoredPaymentMethod(request.PaymentMethod);
                operation.FinanceAccountId = null;
                operation.Notes = request.Notes;
                // Editing a return changes the facts that were assessed for approval.
                // Clear the previous line-level assessment before replacing the
                // operation lines; otherwise the tracked approval rows still point
                // at lines removed by ReplaceOperationLinesAsync and PostgreSQL can
                // reject the aggregate save with a generic DbUpdateException.
                if (operation.OperationType == Return && operation.ReturnApprovalLines.Count > 0)
                {
                    operationsDbContext.ReturnApprovalLines.RemoveRange(operation.ReturnApprovalLines);
                    operation.ReturnApprovalLines.Clear();
                    operation.ReturnSubmissionReason = null;
                    operation.ReturnSubmittedBy = null;
                    operation.ReturnSubmittedAt = null;
                    operation.ReturnApprovalReason = null;
                    operation.ReturnRejectionReason = null;
                    operation.ReturnApprovedBy = null;
                    operation.ReturnApprovedAt = null;
                    operation.ReturnRejectedBy = null;
                    operation.ReturnRejectedAt = null;
                }
                await ReplaceOperationLinesAsync(operationsDbContext, operation, cancellationToken);
                AddLines(operation, validation.SkusById, request.Lines, validation.Merchant, validation.Representative);
                operationsDbContext.OperationLines.AddRange(operation.OperationLines);
                AddSourceAllocations(operationsDbContext, operation, request.Lines, clock.EgyptNow);
                await SaveChangesIgnoringStaleDeletedOperationLinesAsync(operationsDbContext, cancellationToken);
                await AddVersionAsync(operationsDbContext, operation, "Draft update", currentUser.UserId ?? Guid.Empty, CreateSnapshot(operation), clock.EgyptNow, cancellationToken);
                await SaveChangesIgnoringStaleDeletedOperationLinesAsync(operationsDbContext, cancellationToken);
                if (operation.OperationType == RetailSale)
                {
                    await EnsureRetailDraftPaymentLogAsync(operation, paymentsDbContext, currentUser.UserId ?? Guid.Empty, clock.EgyptNow, cancellationToken);
                }
                await auditLogWriter.WriteAsync(
                    "Operation",
                    operation.Id,
                    "Update",
                    new { operation.OperationType, operation.Status },
                    cancellationToken: cancellationToken);
            }, cancellationToken, identityDbContext, paymentsDbContext);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { code = "stale-version", detail = "The operation has been changed by another request." });
        }
        if (conflictResult is not null) return conflictResult;
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateShopifyAllocationAsync(
        Guid id,
        ShopifyAllocationRequest request,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var operation = await LoadOperationForDraftUpdateAsync(operationsDbContext, id, cancellationToken);
        if (operation is null) return Results.NotFound();
        if (operationsDbContext.Database.IsRelational() && request.ExpectedVersion is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.ExpectedVersion)] = ["expectedVersion is required when editing a Shopify allocation."] });
        }
        if (operation.Status != Draft || operation.SalesChannel != "Shopify" || operation.OperationType != RetailSale)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(id)] = ["Only draft Shopify retail sales can receive batch allocation."] });
        }
        if (request.SourceLocationId is not { } sourceLocationId)
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.SourceLocationId)] = ["A SubWarehouse must be selected before a Shopify order can be allocated."] });
        var sourceLocation = await inventoryDbContext.Locations.AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == sourceLocationId && value.IsActive && value.LocationType == "SubWarehouse", cancellationToken);
        if (sourceLocation is null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.SourceLocationId)] = ["The selected source must be an active SubWarehouse."] });
        if (!IsOperationsAdministrator(currentUser) && (!IsWarehouseClerk(currentUser) || currentUser.LocationId != sourceLocationId))
            return Results.Forbid();

        var lines = request.Lines ?? [];
        var errors = new Dictionary<string, string[]>();
        if (lines.Count != operation.OperationLines.Count || lines.Select(line => line.OperationLineId).Distinct().Count() != lines.Count || lines.Any(line => !operation.OperationLines.Any(operationLine => operationLine.Id == line.OperationLineId)))
        {
            errors[nameof(request.Lines)] = ["Allocation must include each Shopify operation line exactly once."];
        }
        if (lines.Any(line => string.IsNullOrWhiteSpace(line.LotNumber) || line.ExpiryDate is null))
        {
            errors[nameof(request.Lines)] = ["Each Shopify line requires a batch code and expiry date."];
        }

        var lineById = operation.OperationLines.ToDictionary(line => line.Id);
        foreach (var line in lines.Where(line => lineById.ContainsKey(line.OperationLineId) && line.ExpiryDate is not null))
        {
            var operationLine = lineById[line.OperationLineId];
            var lotNumber = NormalizeBlank(line.LotNumber);
            var exists = await inventoryDbContext.InventoryBatches.AnyAsync(batch =>
                batch.LocationId == sourceLocationId &&
                batch.SkuId == operationLine.SkuId &&
                batch.LotNumber == lotNumber &&
                batch.ExpiryDate == line.ExpiryDate,
                cancellationToken);
            if (!exists)
            {
                errors[$"lines[{line.OperationLineId}]"] = [$"The selected batch does not exist for {operationLine.SkuCodeSnapshot} at the Online location."];
            }
        }
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        IResult? allocationConflict = null;
        try
        {
            await SharedDbTransaction.ExecuteAsync(operationsDbContext, async () =>
            {
                await LockOperationAsync(operationsDbContext, operation.Id, cancellationToken);
                await operationsDbContext.Entry(operation).ReloadAsync(cancellationToken);
                if (operation.Status != Draft || (request.ExpectedVersion.HasValue && operation.ConcurrencyVersion != request.ExpectedVersion.Value))
                {
                    allocationConflict = Results.Conflict(new { code = operation.Status == Draft ? "stale-version" : "transition-conflict", detail = "The Shopify operation changed before allocation could be saved." });
                    return;
                }
                foreach (var line in lines)
                {
                    var operationLine = lineById[line.OperationLineId];
                    operationLine.LotNumber = NormalizeBlank(line.LotNumber);
                    operationLine.ExpiryDate = line.ExpiryDate;
                }
                operation.SourceLocationId = sourceLocationId;
                await operationsDbContext.SaveChangesAsync(cancellationToken);
                await AddVersionAsync(operationsDbContext, operation, "Shopify batch allocation updated", currentUser.UserId ?? Guid.Empty, CreateSnapshot(operation), clock.EgyptNow, cancellationToken);
                await operationsDbContext.SaveChangesAsync(cancellationToken);
                await auditLogWriter.WriteAsync(
                    "Operation",
                    operation.Id,
                    "UpdateAllocation",
                    new { operation.OperationType, operation.Status },
                    cancellationToken: cancellationToken);
            }, cancellationToken, identityDbContext);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { code = "stale-version", detail = "The Shopify operation changed before allocation could be saved." });
        }
        if (allocationConflict is not null) return allocationConflict;
        return Results.NoContent();
    }

    private static async Task AddReplenishmentNotificationsAsync(
        NotificationsDbContext notifications,
        OperationLog operation,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (var role in new[] { LenseeRoles.Admin, LenseeRoles.ERPAdmin, LenseeRoles.WarehouseClerk })
        {
            var exists = await notifications.NotificationLogs.AnyAsync(value =>
                value.AlertType == "Replenishment" && value.ReferenceId == operation.Id && value.TargetRole == role && !value.IsRead,
                cancellationToken);
            if (exists) continue;
            var id = Guid.NewGuid();
            notifications.NotificationLogs.Add(new NotificationLog
            {
                Id = id,
                AlertType = "Replenishment",
                Message = $"Replenishment {operation.OperationNumber} was created as a Draft. Review and confirm the warehouse transfer.",
                ReferenceId = operation.Id,
                ReferenceType = "Operation",
                ReferenceCode = operation.OperationNumber,
                ReferenceTitle = operation.OperationNumber,
                TargetRole = role,
                Channel = "InApp",
                CreatedAt = now,
                NotificationNumber = $"NOT-{id:N}".ToUpperInvariant()
            });
        }
        await notifications.SaveChangesAsync(cancellationToken);
    }

    private static async Task NotifyReturnWorkflowAsync(
        NotificationsDbContext notifications,
        OperationLog operation,
        string status,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existing = await notifications.NotificationLogs
            .Where(value => value.AlertType == "ReturnApproval" && value.ReferenceId == operation.Id && !value.IsRead)
            .ToListAsync(cancellationToken);
        foreach (var item in existing)
        {
            item.IsRead = true;
        }

        var message = status switch
        {
            PendingApproval => $"Return {operation.OperationNumber} requires administrator approval. Review all affected lines before approval.",
            Approved => $"Return {operation.OperationNumber} was approved and is ready for physical confirmation.",
            Rejected => $"Return {operation.OperationNumber} was rejected. Review the rejection reason.",
            Confirmed => $"Return {operation.OperationNumber} was physically confirmed. Finance may review its held entitlement.",
            _ => $"Return {operation.OperationNumber} changed to {status}."
        };

        var context = JsonSerializer.Serialize(new { status, operation.OperationType });
        var targets = new List<(Guid? UserId, string? Role)>();
        if (operation.CreatedBy != Guid.Empty)
        {
            targets.Add((operation.CreatedBy, null));
        }
        if (status == PendingApproval)
        {
            targets.Add((null, LenseeRoles.Admin));
            targets.Add((null, LenseeRoles.ERPAdmin));
        }
        else if (status is Approved or Rejected)
        {
            targets.Add((null, LenseeRoles.Accountant));
        }
        else if (status == Confirmed)
        {
            targets.Add((null, LenseeRoles.Accountant));
        }

        foreach (var target in targets.Distinct())
        {
            var id = Guid.NewGuid();
            notifications.NotificationLogs.Add(new NotificationLog
            {
                Id = id,
                AlertType = "ReturnApproval",
                Message = message,
                ReferenceId = operation.Id,
                ReferenceType = "Operation",
                ReferenceCode = operation.OperationNumber,
                ReferenceTitle = operation.OperationNumber,
                ReferenceContextJson = context,
                TargetUserId = target.UserId,
                TargetRole = target.Role,
                Channel = "InApp",
                CreatedAt = now,
                NotificationNumber = $"NOT-{id:N}".ToUpperInvariant()
            });
        }

        await notifications.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureReplenishmentStageNotificationAsync(NotificationsDbContext notifications, OperationLog operation, DateTime now, CancellationToken cancellationToken)
    {
        var old = await notifications.NotificationLogs.Where(value => value.AlertType == "Replenishment" && value.ReferenceId == operation.Id && !value.IsRead).ToListAsync(cancellationToken);
        foreach (var item in old) item.IsRead = true;
        var stageMessage = operation.Status switch
        {
            Reserved => $"Replenishment {operation.OperationNumber} is Reserved. Source clerk should ship it.",
            Shipped => $"Replenishment {operation.OperationNumber} is Shipped. Destination clerk should receive it.",
            Received => $"Replenishment {operation.OperationNumber} was Received.",
            _ => $"Replenishment {operation.OperationNumber} is {operation.Status}. Review the warehouse transfer."
        };
        foreach (var role in new[] { LenseeRoles.Admin, LenseeRoles.ERPAdmin, LenseeRoles.WarehouseClerk })
        {
            var id = Guid.NewGuid();
            notifications.NotificationLogs.Add(new NotificationLog { Id = id, AlertType = "Replenishment", Message = stageMessage, ReferenceId = operation.Id, ReferenceType = "Operation", ReferenceCode = operation.OperationNumber, ReferenceTitle = operation.OperationNumber, ReferenceContextJson = System.Text.Json.JsonSerializer.Serialize(new { operation.Status }), TargetRole = role, Channel = "InApp", CreatedAt = now, NotificationNumber = $"NOT-{id:N}".ToUpperInvariant() });
        }
        await notifications.SaveChangesAsync(cancellationToken);
    }

    private static async Task<IResult> ReviseOperationAsync(
        Guid id,
        OperationRevisionRequest request,
        HttpContext httpContext,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        CrmDbContext crmDbContext,
        InventoryDbContext inventoryDbContext,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        MerchantAccountService merchantAccountService,
        StockLedgerService ledgerService,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var operation = await LoadOperationAsync(operationsDbContext, id, cancellationToken);
        if (operation is null)
        {
            return Results.NotFound();
        }

        if (!IsOperationsAdministrator(currentUser))
        {
            return Results.Forbid();
        }
        if (operationsDbContext.Database.IsRelational() && request.Operation.ExpectedVersion is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Operation.ExpectedVersion)] = ["expectedVersion is required when revising an operation."] });
        }

        // Compare before requiring a reason or validating the request. An unchanged
        // revision is a true no-op (including no fallback audit event).
        if (string.Equals(BuildRevisionFingerprint(operation), BuildRevisionFingerprint(request.Operation), StringComparison.Ordinal))
        {
            httpContext.Items[AuditMutationMiddleware.AuditWrittenItemKey] = true;
            return Results.NoContent();
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Reason)] = ["Revision reason is required."]
            });
        }

        if (operation.Status == Cancelled)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(operation.Status)] = ["Cancelled operations cannot be revised."]
            });
        }
        if (IsFinalizedForCorrection(operation.Status))
        {
            return Results.Conflict(new
            {
                detail = "Finalized operations are immutable. Create a correction proposal instead.",
                correctionRoute = $"/api/v1/operations/{operation.Id}/corrections"
            });
        }
        if (operation.SalesChannel == "Shopify")
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(operation.SalesChannel)] = ["Shopify commercial data cannot be revised. Use the Shopify cancellation/refund exception workflow for commercial changes."]
            });
        }

        if (!string.Equals(NormalizeOperationType(request.Operation.OperationType), operation.OperationType, StringComparison.OrdinalIgnoreCase))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Operation.OperationType)] = ["Operation type cannot change during revision."]
            });
        }

        var revisionBlock = await GetRevisionBlockReasonAsync(operation, paymentsDbContext, cancellationToken);
        if (revisionBlock is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(operation.Status)] = [revisionBlock]
            });
        }

        var validation = await ValidateDraftAsync(request.Operation, catalogDbContext, crmDbContext, inventoryDbContext, operationsDbContext, currentUser, isCreate: false, cancellationToken);
        if (validation.Errors.Count > 0)
        {
            return Results.ValidationProblem(validation.Errors);
        }

        var now = clock.EgyptNow;
        var userId = currentUser.UserId ?? Guid.Empty;
        var originalStatus = operation.Status;
        var originalAllocations = ReadTransferAllocations(operation);
        IResult? revisionConflict = null;
        try
        {
            await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, paymentsDbContext, crmDbContext, identityDbContext, async () =>
            {
                await LockOperationAsync(operationsDbContext, operation.Id, cancellationToken);
                await operationsDbContext.Entry(operation).ReloadAsync(cancellationToken);
                if (request.Operation.ExpectedVersion.HasValue && operation.ConcurrencyVersion != request.Operation.ExpectedVersion.Value)
                {
                    revisionConflict = Results.Conflict(new { code = "stale-version", detail = "The operation changed before revision could be applied." });
                    return;
                }
                await ReverseOperationEffectsAsync(operation, originalAllocations, inventoryDbContext, paymentsDbContext, ledgerService, userId, cancellationToken);

                operation.SourceLocationId = request.Operation.SourceLocationId;
                operation.DestinationLocationId = request.Operation.DestinationLocationId;
                operation.ClientId = validation.CorrectionSource?.ClientId ?? validation.Merchant?.Id;
                operation.ClientName = validation.CorrectionSource?.ClientName ?? validation.Merchant?.BusinessName ?? TrimToNull(request.Operation.BuyerName);
                operation.BuyerPhone = validation.CorrectionSource?.BuyerPhone ?? TrimToNull(request.Operation.BuyerPhone);
                operation.RepresentativeId = validation.Representative?.Id;
                operation.PaymentMethod = validation.CorrectionSource?.PaymentMethod ?? NormalizeStoredPaymentMethod(request.Operation.PaymentMethod);
                operation.FinanceAccountId = null;
                operation.Notes = request.Operation.Notes;

                await ReplaceOperationLinesAsync(operationsDbContext, operation, cancellationToken);
                AddLines(operation, validation.SkusById, request.Operation.Lines, validation.Merchant, validation.Representative);
                operationsDbContext.OperationLines.AddRange(operation.OperationLines);
                AddSourceAllocations(operationsDbContext, operation, request.Operation.Lines, clock.EgyptNow);

                if (operation.InventoryReceiptHeader is not null)
                {
                    if (operation.OperationType == InventoryReceipt)
                    {
                        operation.InventoryReceiptHeader.SupplierName = request.Operation.Receipt?.SupplierName?.Trim() ?? "Supplier";
                        operation.InventoryReceiptHeader.InvoiceNumber = TrimToNull(request.Operation.Receipt?.InvoiceNumber);
                        operation.InventoryReceiptHeader.ReceiptDate = now;
                    }
                    else
                    {
                        operationsDbContext.InventoryReceiptHeaders.Remove(operation.InventoryReceiptHeader);
                        operation.InventoryReceiptHeader = null;
                    }
                }
                else if (operation.OperationType == InventoryReceipt)
                {
                    operation.InventoryReceiptHeader = new InventoryReceiptHeader
                    {
                        Id = Guid.NewGuid(),
                        OperationId = operation.Id,
                        SupplierName = request.Operation.Receipt?.SupplierName?.Trim() ?? "Supplier",
                        InvoiceNumber = TrimToNull(request.Operation.Receipt?.InvoiceNumber),
                        ReceiptDate = now
                    };
                    operationsDbContext.InventoryReceiptHeaders.Add(operation.InventoryReceiptHeader);
                }

                operation.ConfirmedBy = ShouldSetConfirmedActorAfterRevision(originalStatus) ? userId : null;
                operation.ConfirmedAt = ShouldSetConfirmedActorAfterRevision(originalStatus) ? now : null;
                operation.Status = Draft;

                var revisedAllocations = await ReapplyOperationToStatusAsync(
                    operation,
                    originalStatus,
                    operationsDbContext,
                    inventoryDbContext,
                    catalogDbContext,
                    crmDbContext,
                    paymentsDbContext,
                    merchantAccountService,
                    ledgerService,
                    userId,
                    now,
                    cancellationToken);

                await AddVersionAsync(
                    operationsDbContext,
                    operation,
                    request.Reason.Trim(),
                    userId,
                    CreateSnapshot(operation, revisedAllocations),
                    now,
                    cancellationToken);
                await SaveChangesIgnoringStaleDeletedOperationLinesAsync(operationsDbContext, cancellationToken);
                await auditLogWriter.WriteAsync(
                    "Operation",
                    operation.Id,
                    "Revise",
                    new { operation.OperationType, operation.Status, Reason = request.Reason.Trim() },
                    cancellationToken: cancellationToken);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { code = "stale-version", detail = "The operation changed before revision could be applied." });
        }
        if (revisionConflict is not null) return revisionConflict;

        var revised = await LoadOperationAsync(operationsDbContext, operation.Id, cancellationToken);
        var locationLookup = await LoadLocationLookupAsync(inventoryDbContext, [revised!], cancellationToken);
        var userLookup = await LoadUserLookupAsync(identityDbContext, [revised!], cancellationToken);
        var wearCycles = await LoadWearCyclesBySkuAsync(catalogDbContext, revised!.OperationLines, cancellationToken);
        return Results.Ok(ToDetailResponse(revised!, locationLookup, userLookup, wearCycles, currentUser));
    }

    private static async Task<IResult> ConfirmOperationAsync(
        Guid id,
        HttpContext httpContext,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        StockLedgerService ledgerService,
        NotificationsDbContext notificationsDbContext,
        MerchantBatchHistoryService batchHistoryService,
        MerchantExpiryRecallService recallService,
        MerchantAccountService merchantAccountService,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        IReturnWorkflowPersistenceService returnPersistence,
        CancellationToken cancellationToken)
    {
        try
        {
            var operation = await LoadOperationAsync(operationsDbContext, id, cancellationToken);
            if (operation is null)
            {
                return Results.NotFound();
            }
            if (operation.OperationType == Reserve)
            {
                return RetiredReserveResult();
            }
            if (operation.Status != Draft && !(operation.OperationType == Return && operation.Status == Approved))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.Status)] = ["Only draft operations or approved returns can be confirmed."] });
            }
            if (operation.OperationType == Return && operation.Status == Draft && operation.ClientId is null && !operation.OperationLines.Any(line => line.SourceAllocations.Count > 0))
            {
                return Results.Conflict(new { code = "return-approval-submission-required", status = Draft, detail = "This unlinked retail/online return must be submitted by an Admin or ERPAdmin before physical confirmation." });
            }
            if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "confirm", cancellationToken))
            {
                return Results.Forbid();
            }

            var codChargeResult = ValidateCodChargeAtConfirmation(operation);
            if (codChargeResult is not null) return codChargeResult;

            // The route branch is selected from this preflight read. The aggregate is
            // locked and loaded again inside the shared transaction before any stock
            // mutation, so a concurrent draft edit cannot make this branch operate on
            // obsolete lines or a different operation type.
            var requestedOperationType = operation.OperationType;
            IResult? confirmationConflict = null;
            async Task<bool> LockDraftForConfirmationAsync()
            {
                var lockedOperation = await LockAndLoadOperationAsync(operationsDbContext, id, cancellationToken);
                if (lockedOperation is null ||
                    (lockedOperation.Status != Draft && !(lockedOperation.OperationType == Return && lockedOperation.Status == Approved)) ||
                    !string.Equals(lockedOperation.OperationType, requestedOperationType, StringComparison.Ordinal))
                {
                    confirmationConflict = Results.Conflict(new
                    {
                        code = "transition-conflict",
                        detail = "The operation changed before confirmation could be applied. Reload and retry."
                    });
                    return false;
                }

                operation = lockedOperation;
                await NormalizeLegacySealedWholesalePieceLinesAsync(operation, catalogDbContext, cancellationToken);
                if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "confirm", cancellationToken))
                {
                    confirmationConflict = Results.Forbid();
                    return false;
                }

                return true;
            }

            OperationConfirmationRequest confirmationRequest;
            try
            {
                confirmationRequest = await ReadConfirmationRequestAsync(httpContext.Request, cancellationToken);
            }
            catch (JsonException)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["Confirmation request is not valid JSON."] });
            }

            // Return exceptions are operational acceptance exceptions, not an
            // in-app finance approval. Any permitted return operator may record the
            // required verbal authorization; settlement remains Finance-only.
            var canBypassSalesVariance = operation.OperationType == Return || LenseeRoles.Normalize(currentUser.Role) is LenseeRoles.Admin or LenseeRoles.ERPAdmin;
            var salesVarianceReason = NormalizeBlank(confirmationRequest.SalesVarianceReason);
            var verbalAuthorizer = NormalizeBlank(confirmationRequest.VerbalAuthorizer);
            var salesVarianceOverrideRequested = canBypassSalesVariance &&
                confirmationRequest.AcknowledgeSalesVariance == true &&
                salesVarianceReason is { Length: > 0 and <= 500 } &&
                verbalAuthorizer is { Length: > 0 and <= 255 };

            // New returns are manually entered and have no source-line allocation.
            // They must therefore pass the explicit Operations approval workflow
            // before physical confirmation, regardless of their entered amount.
            var hasExplicitReturnException = operation.OperationType == Return;

            IReadOnlyList<MerchantSalesVarianceWarning> salesVarianceWarnings = [];
            if (operation.OperationType is Return or Change)
            {
                salesVarianceWarnings = await BuildMerchantSalesVarianceWarningsAsync(operation, operationsDbContext, batchHistoryService, cancellationToken);
                if (operation.OperationType == Return &&
                    (salesVarianceWarnings.Count > 0 || hasExplicitReturnException) &&
                    operation.Status == Draft &&
                    !salesVarianceOverrideRequested)
                {
                    return Results.Conflict(new
                    {
                        code = "return-approval-submission-required",
                        status = Draft,
                        detail = "This return has exceptions and must be submitted for Admin or ERPAdmin approval before physical confirmation."
                    });
                }

                if ((salesVarianceWarnings.Count > 0 || hasExplicitReturnException) &&
                    operation.Status == Draft &&
                    !salesVarianceOverrideRequested)
                    return Results.Conflict(CreateMerchantSalesVarianceGate(salesVarianceWarnings, canBypassSalesVariance));
            }

            var now = clock.EgyptNow;
            var userId = currentUser.UserId ?? Guid.Empty;
            var salesVarianceBypassed = false;
            Task WriteConfirmAuditAsync() => auditLogWriter.WriteAsync(
                "Operation",
                operation.Id,
                "Confirm",
                new
                {
                    operation.OperationType,
                    operation.Status,
                    SalesVarianceBypassed = salesVarianceBypassed,
                    SalesVarianceReason = salesVarianceBypassed ? salesVarianceReason : null,
                    VerbalAuthorizer = salesVarianceBypassed ? verbalAuthorizer : null,
                    SalesVarianceOverrideRole = salesVarianceBypassed ? LenseeRoles.Normalize(currentUser.Role) : null
                },
                cancellationToken: cancellationToken);
            if (operation.OperationType == InventoryReceipt)
            {
                await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, identityDbContext, async () =>
                {
                    if (!await LockDraftForConfirmationAsync()) return;
                    foreach (var line in operation.OperationLines)
                    {
                        if (line.EntryMode == "Pieces")
                        {
                            await ledgerService.ReceivePiecesAsync(
                                operation.DestinationLocationId!.Value,
                                line.SkuId,
                                line.Quantity,
                                userId,
                                InventoryTransactionTypes.Receipt,
                                line.LotNumber,
                                line.ExpiryDate,
                                line.LineNotes,
                                operation.Id,
                                cancellationToken);
                            continue;
                        }
                        await ledgerService.ReceiveAsync(
                            operation.DestinationLocationId!.Value,
                            line.SkuId,
                            line.Quantity,
                            userId,
                            line.LotNumber,
                            line.ExpiryDate,
                            line.LineNotes,
                            operation.Id,
                            cancellationToken);
                    }

                    operation.Status = Received;
                    operation.ConfirmedAt = now;
                    operation.ConfirmedBy = userId;
                    await AddVersionAsync(operationsDbContext, operation, "Received", userId, CreateSnapshot(operation), now, cancellationToken);
                    await operationsDbContext.SaveChangesAsync(cancellationToken);
                    await WriteConfirmAuditAsync();
                }, cancellationToken);
            }
            else if (operation.OperationType == WarehouseTransfer)
            {
                IReadOnlyList<TransferAllocationSnapshot> allocations;
                try
                {
                    allocations = await BuildSelectedPackAllocationsAsync(operation, inventoryDbContext, operationsDbContext, clock, cancellationToken);
                }
                catch (InvalidOperationException exception)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.OperationLines)] = [$"The selected inventory allocation is not valid: {exception.Message}"] });
                }

                await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, identityDbContext, async () =>
                {
                    if (!await LockDraftForConfirmationAsync()) return;
                    await ReserveSelectedAllocationsAsync(operation.SourceLocationId!.Value, allocations, ledgerService, userId, operation.Id, cancellationToken);

                    operation.Status = Reserved;
                    operation.ConfirmedAt = now;
                    operation.ConfirmedBy = userId;
                    await AddVersionAsync(operationsDbContext, operation, "Reserved", userId, CreateSnapshot(operation, allocations), now, cancellationToken);
                    await operationsDbContext.SaveChangesAsync(cancellationToken);
                    await WriteConfirmAuditAsync();
                }, cancellationToken);
            }
            else if (operation.OperationType is WholesaleSale or RetailSale)
            {
                IReadOnlyList<TransferAllocationSnapshot> allocations = [];
                try
                {
                    await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, paymentsDbContext, identityDbContext, async () =>
                    {
                        if (!await LockDraftForConfirmationAsync()) return;
                        allocations = await BuildSelectedPackAllocationsAsync(operation, inventoryDbContext, operationsDbContext, clock, cancellationToken);
                        await ReserveSelectedAllocationsAsync(operation.SourceLocationId!.Value, allocations, ledgerService, userId, operation.Id, cancellationToken);

                        operation.Status = Reserved;
                        operation.ConfirmedAt = now;
                        operation.ConfirmedBy = userId;
                        if (operation.OperationType == RetailSale && !IsOnlineSale(operation))
                        {
                            // Counter sales have no shipping hand-off. Reservation,
                            // physical issue and creation of the Payments work item
                            // commit as one checkout transaction.
                            await ShipSaleOutAsync(operation, allocations, operationsDbContext, catalogDbContext, ledgerService, userId, now, cancellationToken);
                            operation.Status = Completed;
                            if (operation.ClientId is { } merchantId)
                                await merchantAccountService.PostSaleAsync(merchantId, operation.Id, operation.OperationLines.Sum(line => line.LineTotal), userId, now, "Retail checkout.", cancellationToken);
                            await EnsureRetailDraftPaymentLogAsync(operation, paymentsDbContext, userId, now, cancellationToken);
                            await PaymentsEndpoints.CreatePaymentArtifactsForCompletedSaleAsync(operation, paymentsDbContext, identityDbContext, merchantAccountService, null, userId, now, cancellationToken);
                            await AddVersionAsync(operationsDbContext, operation, "Completed retail checkout", userId, CreateSnapshot(operation, allocations), now, cancellationToken);
                        }
                        else
                        {
                            await AddVersionAsync(operationsDbContext, operation, "Reserved sale", userId, CreateSnapshot(operation, allocations), now, cancellationToken);
                        }
                        await returnPersistence.SaveAsync(operationsDbContext, cancellationToken);
                        await WriteConfirmAuditAsync();
                    }, cancellationToken);
                }
                catch (InvalidOperationException exception)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.OperationLines)] = [$"The selected inventory allocation is not valid: {exception.Message}"] });
                }
            }
            else if (operation.OperationType == Return)
            {
                try
                {
                    await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, paymentsDbContext, identityDbContext, async () =>
                    {
                        if (!await LockDraftForConfirmationAsync()) return;
                        await AcquireMerchantReturnLocksAsync(operation, operationsDbContext, cancellationToken);
                        var lockedWarnings = await BuildMerchantSalesVarianceWarningsAsync(operation, operationsDbContext, batchHistoryService, cancellationToken);
                        if ((lockedWarnings.Count > 0 || hasExplicitReturnException) && operation.Status != Approved)
                        {
                            confirmationConflict = Results.Conflict(new { code = "return-approval-submission-required", status = Draft, detail = "The return changed and now requires Admin or ERPAdmin approval before physical confirmation." });
                            return;
                        }
                        salesVarianceBypassed = (lockedWarnings.Count > 0 || hasExplicitReturnException) && salesVarianceOverrideRequested;
                        await PopulateReturnValueSplitAsync(operation, operationsDbContext, returnPersistence, cancellationToken);
                        operation.ReturnExceptionReason = salesVarianceBypassed ? salesVarianceReason : null;
                        operation.ReturnVerbalAuthorizer = salesVarianceBypassed ? verbalAuthorizer : null;
                        operation.ReturnNormalValue = operation.OperationLines.Sum(line => line.NormalValue);
                        operation.ReturnExceptionValue = operation.OperationLines.Sum(line => line.ExceptionValue);
                        foreach (var line in operation.OperationLines)
                        {
                            if (string.IsNullOrWhiteSpace(line.LotNumber) && line.ExpiryDate is { } returnExpiry)
                                line.LotNumber = $"return-{returnExpiry.ToString("MMMyy", CultureInfo.InvariantCulture)}".ToLowerInvariant();
                            var disposition = NormalizeReturnDisposition(line.ReturnDisposition);
                            if (line.EntryMode == "Pieces")
                            {
                                if (disposition == "WriteOff")
                                {
                                    line.WriteOffReason = "CustomerReturnDisposition";
                                    line.WriteOffReasonText = NormalizeBlank(line.LineNotes) ?? "Returned goods were marked for write-off.";
                                    continue;
                                }
                                await ledgerService.ReceiveReturnPiecesAsync(operation.SourceLocationId!.Value, line.SkuId, line.Quantity, userId, line.LotNumber, line.ExpiryDate, line.LineNotes, operation.Id, cancellationToken);
                                continue;
                            }
                            if (line.ExpiryDate is { } expiry && expiry < DateOnly.FromDateTime(now))
                            {
                                disposition = "WriteOff";
                                line.WriteOffReason = "ExpiredMerchantReturn";
                                line.WriteOffReasonText = "Expired merchant return was received and written off in the same operation.";
                                line.ReturnDisposition = disposition;
                                await ledgerService.ReceiveReturnAndWriteOffAsync(
                                    operation.SourceLocationId!.Value,
                                    line.SkuId,
                                    line.Quantity,
                                    userId,
                                    line.LotNumber,
                                    line.ExpiryDate,
                                    line.LineNotes,
                                    operation.Id,
                                    cancellationToken);
                            }
                            else if (disposition == "Sellable")
                            {
                                await ledgerService.ReceiveReturnAsync(
                                    operation.SourceLocationId!.Value,
                                    line.SkuId,
                                    line.Quantity,
                                    userId,
                                    line.LotNumber,
                                    line.ExpiryDate,
                                    line.LineNotes,
                                    operation.Id,
                                    cancellationToken);
                            }
                            else if (disposition == "Quarantine")
                            {
                                await ledgerService.ReceiveQuarantinedReturnAsync(
                                    operation.SourceLocationId!.Value, line.SkuId, line.Quantity, userId,
                                    line.LotNumber, line.ExpiryDate, line.LineNotes, operation.Id, cancellationToken);
                            }
                            else
                            {
                                line.WriteOffReason = "CustomerReturnDisposition";
                                line.WriteOffReasonText = NormalizeBlank(line.LineNotes) ?? "Returned goods were marked for write-off.";
                                await ledgerService.ReceiveReturnAndWriteOffAsync(
                                    operation.SourceLocationId!.Value, line.SkuId, line.Quantity, userId,
                                    line.LotNumber, line.ExpiryDate, line.LineNotes, operation.Id, cancellationToken);
                            }
                        }

                        operation.Status = Confirmed;
                        operation.ConfirmedAt = now;
                        operation.ConfirmedBy = userId;
                        await CreateReturnEntitlementAsync(operation, operationsDbContext, paymentsDbContext, userId, now, cancellationToken);
                        await recallService.ApplyConfirmedReturnAsync(operation, cancellationToken);
                        await AddVersionAsync(operationsDbContext, operation, "Confirmed return", userId, CreateSnapshot(operation), now, cancellationToken);
                        // The entitlement is a Payments fact enlisted in the same
                        // inventory/operations transaction; explicitly flush it so
                        // stock receipt and held financial entitlement commit together.
                        await returnPersistence.SaveAsync(paymentsDbContext, cancellationToken);
                        await returnPersistence.SaveAsync(operationsDbContext, cancellationToken);
                        await WriteConfirmAuditAsync();
                    }, cancellationToken);
                }
                catch (MerchantSalesVarianceException exception)
                {
                    return Results.Conflict(CreateMerchantSalesVarianceGate(exception.Warnings, canBypassSalesVariance));
                }
                catch (SourceAllocationCapException exception)
                {
                    return Results.Conflict(new { code = "source-allocation-cap", detail = "The selected source allocation exceeds available stock.", violations = exception.Violations });
                }
                if (operation.OperationType == Return && operation.Status == Confirmed)
                {
                    await NotifyReturnWorkflowAsync(notificationsDbContext, operation, Confirmed, clock.EgyptNow, cancellationToken);
                }

                if (operation.MerchantExpiryRecallId is { } recallId)
                {
                    await recallService.SynchronizeResolvedNotificationAsync(recallId, cancellationToken);
                }
            }
            else if (operation.OperationType == Change)
            {
                var allocations = new List<TransferAllocationSnapshot>();
                try
                {
                    await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, paymentsDbContext, identityDbContext, async () =>
                    {
                        if (!await LockDraftForConfirmationAsync()) return;
                        await EnforceSourceAllocationCapsAsync(operation, operationsDbContext, inventoryDbContext, cancellationToken);
                        await AcquireMerchantReturnLocksAsync(operation, operationsDbContext, cancellationToken);
                        var lockedWarnings = await BuildMerchantSalesVarianceWarningsAsync(operation, operationsDbContext, batchHistoryService, cancellationToken);
                        if (lockedWarnings.Count > 0 && !salesVarianceOverrideRequested)
                        {
                            throw new MerchantSalesVarianceException(lockedWarnings);
                        }
                        salesVarianceBypassed = lockedWarnings.Count > 0 && salesVarianceOverrideRequested;
                        var sourceAllocations = await LoadSourceAllocationByTargetLineAsync(operation, operationsDbContext, cancellationToken);
                        foreach (var line in operation.OperationLines.Where(line => line.Section == ChangeOut))
                        {
                            if (line.EntryMode == "Pieces")
                            {
                                if (!sourceAllocations.TryGetValue(line.Id, out var sourceAllocation) || sourceAllocation.SourceOpenedPieceLotId is not { } openedPieceLotId)
                                    throw new SourceAllocationCapException("A piece exchange return must reference its original opened piece lot.", []);
                                await ledgerService.RestorePiecesToOpenedLotAsync(openedPieceLotId, line.Quantity, userId, operation.Id, cancellationToken);
                                continue;
                            }
                            await ledgerService.ReceiveChangeOutAsync(
                                operation.SourceLocationId!.Value,
                                line.SkuId,
                                line.Quantity,
                                userId,
                                line.LotNumber,
                                line.ExpiryDate,
                                line.LineNotes,
                                operation.Id,
                                cancellationToken);
                        }

                        foreach (var line in operation.OperationLines.Where(line => line.Section == ChangeIn))
                        {
                            if (line.EntryMode == "Pieces")
                            {
                                var piecesPerPack = await LoadPiecesPerPackBySkuAsync(catalogDbContext, [line], cancellationToken);
                                if (!piecesPerPack.TryGetValue(line.SkuId, out var packPieces) || packPieces <= 0)
                                    throw new InvalidOperationException("SKU pieces per pack is required for replacement pieces.");
                                await ledgerService.IssuePiecesFromSelectionAsync(
                                    operation.SourceLocationId!.Value,
                                    line.SkuId,
                                    line.Quantity,
                                    packPieces,
                                    line.LotNumber,
                                    line.ExpiryDate,
                                    userId,
                                    operation.Id,
                                    cancellationToken);
                                continue;
                            }
                            var lineAllocation = await IssueSelectedOrFefoAsync(operation.SourceLocationId!.Value, line, InventoryTransactionTypes.ChangeIn, ledgerService, userId, operation.Id, cancellationToken);
                            allocations.Add(new TransferAllocationSnapshot(line.SkuId, [lineAllocation]));
                        }

                        operation.Status = Confirmed;
                        operation.ConfirmedAt = now;
                        operation.ConfirmedBy = userId;
                        await AddVersionAsync(operationsDbContext, operation, "Confirmed change", userId, CreateSnapshot(operation, allocations), now, cancellationToken);
                        await returnPersistence.SaveAsync(operationsDbContext, cancellationToken);
                        await WriteConfirmAuditAsync();
                    }, cancellationToken);
                }
                catch (MerchantSalesVarianceException exception)
                {
                    return Results.Conflict(CreateMerchantSalesVarianceGate(exception.Warnings, canBypassSalesVariance));
                }
                catch (SourceAllocationCapException exception)
                {
                    return Results.Conflict(new { code = "source-allocation-cap", detail = "The selected source allocation exceeds available stock.", violations = exception.Violations });
                }
            }
            else if (operation.OperationType == WriteOff)
            {
                var allocations = new List<TransferAllocationSnapshot>();
                await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, identityDbContext, async () =>
                {
                    if (!await LockDraftForConfirmationAsync()) return;
                    foreach (var line in operation.OperationLines)
                    {
                        if (line.EntryMode == "Pieces")
                        {
                            var piecesPerPack = await LoadPiecesPerPackBySkuAsync(catalogDbContext, [line], cancellationToken);
                            if (!piecesPerPack.TryGetValue(line.SkuId, out var packPieces) || packPieces <= 0)
                                throw new InvalidOperationException("SKU pieces per pack is required for piece write-offs.");
                            await ledgerService.IssuePiecesFromSelectionAsync(
                                operation.SourceLocationId!.Value,
                                line.SkuId,
                                line.Quantity,
                                packPieces,
                                line.LotNumber,
                                line.ExpiryDate,
                                userId,
                                operation.Id,
                                cancellationToken,
                                InventoryTransactionTypes.WriteOff);
                            continue;
                        }
                        var lineAllocation = await IssueSelectedOrFefoAsync(operation.SourceLocationId!.Value, line, InventoryTransactionTypes.WriteOff, ledgerService, userId, operation.Id, cancellationToken);
                        allocations.Add(new TransferAllocationSnapshot(line.SkuId, [lineAllocation]));
                    }

                    operation.Status = Confirmed;
                    operation.ConfirmedAt = now;
                    operation.ConfirmedBy = userId;
                    await AddVersionAsync(operationsDbContext, operation, "Confirmed write-off", userId, CreateSnapshot(operation, allocations), now, cancellationToken);
                    await operationsDbContext.SaveChangesAsync(cancellationToken);
                    await WriteConfirmAuditAsync();
                }, cancellationToken);
            }
            if (confirmationConflict is not null) return confirmationConflict;
            if (operation.AutomationType == "TargetReplenishment") await EnsureReplenishmentStageNotificationAsync(notificationsDbContext, operation, clock.EgyptNow, cancellationToken);
            return Results.NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { code = "operation-confirmation-concurrency-conflict", detail = "The operation or inventory changed during confirmation. Refresh and try again." });
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { code = "operation-confirmation-save-conflict", detail = "The operation confirmation could not be saved." });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { code = "operation-confirmation-rejected", detail = exception.Message });
        }
    }

    private static async Task MarkReturnPendingApprovalAsync(OperationLog operation, OperationsDbContext operationsDbContext, DateTime now, string reason, CancellationToken cancellationToken, IReadOnlyList<MerchantSalesVarianceWarning>? warnings = null)
    {
        operation.Status = PendingApproval;
        operation.ReturnApprovalReason = reason;
        if (!operation.ReturnApprovalLines.Any())
        {
            foreach (var line in operation.OperationLines)
            {
                var warning = warnings?.FirstOrDefault(value => value.SkuId == line.SkuId && value.LotNumber == line.LotNumber && value.ExpiryDate == line.ExpiryDate);
                operationsDbContext.ReturnApprovalLines.Add(new ReturnApprovalLine
                {
                    Id = Guid.NewGuid(),
                    OperationId = operation.Id,
                    OperationLineId = line.Id,
                    SkuId = line.SkuId,
                    RequestedQuantity = line.Quantity,
                    EligibleQuantity = warning?.SoldQuantity - warning?.ReturnedQuantity,
                    BatchName = line.LotNumber,
                    ExpiryDate = line.ExpiryDate,
                    MismatchType = warning?.AlertType ?? "UnlinkedReturn",
                    Detail = warning?.Message ?? reason,
                    CreatedAt = now
                });
            }
        }
        await operationsDbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<IResult> ShipOperationAsync(
        Guid id,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        IdentityDbContext identityDbContext,
        StockLedgerService ledgerService,
        NotificationsDbContext notificationsDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        try
        {
            var operation = await LoadOperationAsync(operationsDbContext, id, cancellationToken);
            if (operation is null)
            {
                return Results.NotFound();
            }
            if (operation.OperationType == Reserve)
            {
                return RetiredReserveResult();
            }
            if (operation.OperationType is not (WarehouseTransfer or WholesaleSale or RetailSale) || operation.Status != Reserved)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.Status)] = ["Only reserved transfers or sales can be shipped."] });
            }
            if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "ship", cancellationToken))
            {
                return Results.Forbid();
            }

            var userId = currentUser.UserId ?? Guid.Empty;
            IReadOnlyList<TransferAllocationSnapshot> allocations = ReadTransferAllocations(operation);
            if (LinesRequiringAllocation(operation).Any(line => allocations.All(value => value.SkuId != line.SkuId)))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.OperationLines)] = ["Transfer allocation snapshot is missing."] });
            }

            IResult? shipConflict = null;

            await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, identityDbContext, async () =>
            {
                var lockedOperation = await LockAndLoadOperationAsync(operationsDbContext, id, cancellationToken);
                if (lockedOperation is null ||
                    lockedOperation.OperationType is not (WarehouseTransfer or WholesaleSale or RetailSale) ||
                    lockedOperation.Status != Reserved)
                {
                    shipConflict = Results.Conflict(new
                    {
                        code = "transition-conflict",
                        detail = "The operation changed before shipping could be applied. Reload and retry."
                    });
                    return;
                }

                operation = lockedOperation;
                if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "ship", cancellationToken))
                {
                    shipConflict = Results.Forbid();
                    return;
                }

                allocations = ReadTransferAllocations(operation);
                if (LinesRequiringAllocation(operation).Any(line => allocations.All(value => value.SkuId != line.SkuId)))
                {
                    shipConflict = Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.OperationLines)] = ["Transfer allocation snapshot is missing."] });
                    return;
                }

                if (operation.OperationType == WarehouseTransfer)
                {
                    await CommitTransferOutAsync(operation, allocations, operationsDbContext, ledgerService, userId, InventoryTransactionTypes.SupplyOut, cancellationToken);
                }
                else if (operation.OperationType is WholesaleSale or RetailSale)
                {
                    await ShipSaleOutAsync(operation, allocations, operationsDbContext, catalogDbContext, ledgerService, userId, clock.EgyptNow, cancellationToken);
                }
                var dispatchedOnline = IsOnlineSale(operation);
                operation.Status = dispatchedOnline ? Dispatched : Shipped;
                await AddVersionAsync(operationsDbContext, operation, dispatchedOnline ? "Dispatched" : "Shipped", userId, CreateSnapshot(operation, allocations), clock.EgyptNow, cancellationToken);
                await operationsDbContext.SaveChangesAsync(cancellationToken);
                await auditLogWriter.WriteAsync(
                    "Operation",
                    operation.Id,
                    dispatchedOnline ? "Dispatch" : "Ship",
                    new { operation.OperationType, operation.Status },
                    cancellationToken: cancellationToken);
            }, cancellationToken);

            if (shipConflict is not null) return shipConflict;

            if (operation.AutomationType == "TargetReplenishment") await EnsureReplenishmentStageNotificationAsync(notificationsDbContext, operation, clock.EgyptNow, cancellationToken);

            return Results.NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { code = "operation-shipping-concurrency-conflict", detail = "The operation or inventory changed during shipping. Refresh and try again." });
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { code = "operation-shipping-save-conflict", detail = "The operation shipping could not be saved." });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { code = "operation-shipping-rejected", detail = exception.Message });
        }
    }

    private static async Task<IResult> ReceiveOperationAsync(
        Guid id,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        CrmDbContext crmDbContext,
        PaymentsDbContext paymentsDbContext,
        FinanceDbContext financeDbContext,
        IdentityDbContext identityDbContext,
        StockLedgerService ledgerService,
        NotificationsDbContext notificationsDbContext,
        IAppEventPublisher eventPublisher,
        MerchantAccountService merchantAccountService,
        FinanceLedgerService financeLedgerService,
        OperationCompletionService operationCompletionService,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        try
        {
            var operation = await LoadOperationAsync(operationsDbContext, id, cancellationToken);
            if (operation is null)
            {
                return Results.NotFound();
            }
            if (operation.OperationType == Reserve)
            {
                return RetiredReserveResult();
            }
            var onlineDelivery = IsOnlineSale(operation);
            if (operation.OperationType is not (WarehouseTransfer or WholesaleSale or RetailSale) ||
                (operation.OperationType == WholesaleSale && operation.Status != Shipped) ||
                (onlineDelivery && operation.Status != Dispatched) ||
                (!onlineDelivery && operation.OperationType is not WholesaleSale && operation.Status is not (Reserved or Shipped)))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.Status)] = ["Wholesale sales must be shipped before receipt/complete. Other reserved transfers or sales can be received."] });
            }
            if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "receive", cancellationToken))
            {
                return Results.Forbid();
            }

            var userId = currentUser.UserId ?? Guid.Empty;
            IReadOnlyList<TransferAllocationSnapshot> allocations = ReadTransferAllocations(operation);
            if (LinesRequiringAllocation(operation).Any(line => allocations.All(value => value.SkuId != line.SkuId)))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.OperationLines)] = ["Transfer allocation snapshot is missing."] });
            }

            IResult? receiveConflict = null;

            await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, paymentsDbContext, financeDbContext, crmDbContext, identityDbContext, async () =>
            {
                var lockedOperation = await LockAndLoadOperationAsync(operationsDbContext, id, cancellationToken);
                if (lockedOperation is null ||
                    lockedOperation.OperationType is not (WarehouseTransfer or WholesaleSale or RetailSale) ||
                    (lockedOperation.OperationType == WholesaleSale && lockedOperation.Status != Shipped) ||
                    (IsOnlineSale(lockedOperation) && lockedOperation.Status != Dispatched) ||
                    (!IsOnlineSale(lockedOperation) && lockedOperation.OperationType is not WholesaleSale && lockedOperation.Status is not (Reserved or Shipped)))
                {
                    receiveConflict = Results.Conflict(new
                    {
                        code = "transition-conflict",
                        detail = "The operation changed before receiving could be applied. Reload and retry."
                    });
                    return;
                }

                operation = lockedOperation;
                if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "receive", cancellationToken))
                {
                    receiveConflict = Results.Forbid();
                    return;
                }

                allocations = ReadTransferAllocations(operation);
                if (LinesRequiringAllocation(operation).Any(line => allocations.All(value => value.SkuId != line.SkuId)))
                {
                    receiveConflict = Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.OperationLines)] = ["Transfer allocation snapshot is missing."] });
                    return;
                }

                if (operation.Status == Reserved)
                {
                    if (operation.OperationType == WarehouseTransfer)
                    {
                        await CommitTransferOutAsync(operation, allocations, operationsDbContext, ledgerService, userId, InventoryTransactionTypes.SupplyOut, cancellationToken);
                    }
                    else if (operation.OperationType is WholesaleSale or RetailSale)
                    {
                        await ShipSaleOutAsync(operation, allocations, operationsDbContext, catalogDbContext, ledgerService, userId, clock.EgyptNow, cancellationToken);
                    }
                }

                if (operation.OperationType == WarehouseTransfer)
                {
                    var allocationLookup = BuildAllocationLookupBySku(allocations);
                    foreach (var line in operation.OperationLines)
                    {
                        if (line.EntryMode == "Pieces")
                        {
                            if (line.PiecesPerPackSnapshot is not > 0)
                            {
                                throw new InvalidOperationException($"{line.SkuCodeSnapshot} requires a valid pieces-per-pack snapshot.");
                            }

                            await ledgerService.ReceivePiecesAsync(
                                operation.DestinationLocationId!.Value,
                                line.SkuId,
                                line.Quantity,
                                userId,
                                InventoryTransactionTypes.SupplyIn,
                                line.LotNumber,
                                line.ExpiryDate,
                                line.LineNotes,
                                operation.Id,
                                cancellationToken);
                            continue;
                        }

                        if (!allocationLookup.TryGetValue(line.SkuId, out var lineAllocations))
                        {
                            throw new InvalidOperationException("Transfer allocation snapshot is missing.");
                        }

                        var lineLotNumber = NormalizeBlank(line.LotNumber);
                        var matchingAllocations = lineAllocations
                            .Where(allocation =>
                                (lineLotNumber is null || NormalizeBlank(allocation.LotNumber) == lineLotNumber) &&
                                (!line.ExpiryDate.HasValue || allocation.ExpiryDate == line.ExpiryDate))
                            .ToList();
                        if (matchingAllocations.Count == 0)
                        {
                            throw new InvalidOperationException("Transfer allocation does not match the revised line batch.");
                        }

                        foreach (var allocation in matchingAllocations)
                        {
                            await ledgerService.ReceiveSupplyAsync(
                                operation.DestinationLocationId!.Value,
                                line.SkuId,
                                allocation.Quantity,
                                userId,
                                allocation.LotNumber,
                                allocation.ExpiryDate,
                                line.LineNotes,
                                operation.Id,
                                cancellationToken);
                        }

                    }

                    operation.Status = Received;
                    await AddVersionAsync(operationsDbContext, operation, "Received", userId, CreateSnapshot(operation, allocations), clock.EgyptNow, cancellationToken);
                }
                else
                {
                    operation.Status = IsOnlineSale(operation) ? Delivered : operation.OperationType == WholesaleSale ? Received : operation.OperationType == RetailSale ? Completed : Confirmed;
                    operation.ConfirmedAt = clock.EgyptNow;
                    operation.ConfirmedBy = userId;
                    await AddVersionAsync(operationsDbContext, operation, operation.OperationType == Reserve ? "Representative received stock" : "Customer completed sale", userId, CreateSnapshot(operation, allocations), clock.EgyptNow, cancellationToken);
                }

                await operationCompletionService.SaveAsync(cancellationToken);
                if (operation.OperationType is WholesaleSale or RetailSale && operation.Status is (Completed or Delivered))
                {
                    await operationCompletionService.SaveAsync(cancellationToken);
                    if (operation.ClientId is { } merchantId)
                    {
                        await merchantAccountService.PostSaleAsync(merchantId, operation.Id, operation.OperationLines.Sum(line => line.LineTotal), userId, clock.EgyptNow, "Completed sale.", cancellationToken);
                    }
                    await PaymentsEndpoints.CreatePaymentArtifactsForCompletedSaleAsync(operation, paymentsDbContext, identityDbContext, merchantAccountService, financeLedgerService, userId, clock.EgyptNow, cancellationToken);
                }
                await auditLogWriter.WriteAsync(
                    "Operation",
                    operation.Id,
                    "Receive",
                    new { operation.OperationType, operation.Status },
                    cancellationToken: cancellationToken);
            }, cancellationToken);

            if (receiveConflict is not null) return receiveConflict;

            if (!operation.ClientId.HasValue && operation.OperationType is (WholesaleSale or RetailSale) &&
                operation.PaymentMethod is "CashHandToHand" or "BankTransfer")
            {
                var paymentLog = await paymentsDbContext.MainPaymentLogs
                    .FirstOrDefaultAsync(log => log.OperationId == operation.Id && !log.IsDeleted, cancellationToken);
                if (paymentLog is not null)
                {
                    await eventPublisher.PublishAsync(new PaymentWorkflowChangedEvent(
                        paymentLog.Id,
                        paymentLog.MerchantId,
                        operation.Id,
                        "CollectionRequired",
                        $"Sale {operation.OperationNumber} is complete and its collection must be recorded and approved.",
                        null,
                        LenseeRoles.Accountant,
                        clock.EgyptNow), cancellationToken);
                }
            }

            if (operation.AutomationType == "TargetReplenishment") await EnsureReplenishmentStageNotificationAsync(notificationsDbContext, operation, clock.EgyptNow, cancellationToken);

            return Results.NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { code = "operation-receiving-concurrency-conflict", detail = "The operation, inventory, or financial record changed during receiving. Refresh and try again." });
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { code = "operation-receiving-save-conflict", detail = "The operation receiving workflow could not be saved." });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { code = "operation-receiving-rejected", detail = exception.Message });
        }
    }

    private static async Task<IResult> CompleteWholesaleOperationAsync(
        Guid id,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        MerchantAccountService merchantAccountService,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var operation = await LoadOperationAsync(operationsDbContext, id, cancellationToken);
        if (operation is null) return Results.NotFound();
        if (operation.OperationType != WholesaleSale || operation.Status != Received)
            return Results.Conflict(new { code = "transition-conflict", detail = "Only a received wholesale sale can be completed." });
        if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "complete", cancellationToken)) return Results.Forbid();

        IResult? failure = null;
        await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, paymentsDbContext, identityDbContext, async () =>
        {
            var locked = await LockAndLoadOperationAsync(operationsDbContext, id, cancellationToken);
            if (locked is null || locked.OperationType != WholesaleSale || locked.Status != Received)
            {
                failure = Results.Conflict(new { code = "transition-conflict", detail = "The wholesale sale changed before completion could be applied. Reload and retry." });
                return;
            }
            if (!await CanMutateOperationAsync(currentUser, locked, inventoryDbContext, "complete", cancellationToken)) { failure = Results.Forbid(); return; }
            var now = clock.EgyptNow;
            var userId = currentUser.UserId ?? Guid.Empty;
            locked.Status = Completed;
            if (locked.ClientId is { } merchantId)
                await merchantAccountService.PostSaleAsync(merchantId, locked.Id, locked.OperationLines.Sum(line => line.LineTotal), userId, now, "Completed wholesale sale.", cancellationToken);
            await PaymentsEndpoints.CreatePaymentArtifactsForCompletedSaleAsync(locked, paymentsDbContext, identityDbContext, merchantAccountService, null, userId, now, cancellationToken);
            await AddVersionAsync(operationsDbContext, locked, "Completed wholesale sale", userId, CreateSnapshot(locked), now, cancellationToken);
            await operationsDbContext.SaveChangesAsync(cancellationToken);
            await auditLogWriter.WriteAsync("Operation", locked.Id, "Complete", new { locked.OperationType, locked.Status }, cancellationToken: cancellationToken);
        }, cancellationToken);
        return failure ?? Results.NoContent();
    }

    private static async Task<IResult> MarkFailedDeliveryAsync(
        Guid id,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var operation = await LoadOperationAsync(operationsDbContext, id, cancellationToken);
        if (operation is null) return Results.NotFound();
        if (!IsOnlineSale(operation) || operation.Status != Dispatched)
            return Results.Conflict(new { code = "transition-conflict", detail = "Only a dispatched online sale can be marked as failed delivery." });
        if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "failed-delivery", cancellationToken)) return Results.Forbid();

        IResult? failure = null;
        await SharedDbTransaction.ExecuteAsync(operationsDbContext, async () =>
        {
            var locked = await LockAndLoadOperationAsync(operationsDbContext, id, cancellationToken);
            if (locked is null || !IsOnlineSale(locked) || locked.Status != Dispatched)
            {
                failure = Results.Conflict(new { code = "transition-conflict", detail = "The online order changed before failed delivery could be recorded. Reload and retry." });
                return;
            }
            if (!await CanMutateOperationAsync(currentUser, locked, inventoryDbContext, "failed-delivery", cancellationToken))
            {
                failure = Results.Forbid();
                return;
            }
            locked.Status = FailedDelivery;
            await AddVersionAsync(operationsDbContext, locked, "Failed delivery", currentUser.UserId ?? Guid.Empty, CreateSnapshot(locked), clock.EgyptNow, cancellationToken);
            await operationsDbContext.SaveChangesAsync(cancellationToken);
            await auditLogWriter.WriteAsync("Operation", locked.Id, "FailedDelivery", new { locked.OperationType, locked.SalesChannel, locked.Status }, cancellationToken: cancellationToken);
        }, cancellationToken, identityDbContext);
        return failure ?? Results.NoContent();
    }

    private static async Task<IResult> CancelOperationAsync(
        Guid id,
        OperationCancellationRequest request,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        IdentityDbContext identityDbContext,
        StockLedgerService ledgerService,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Reason)] = ["A cancellation reason is required."] });
        }

        try
        {
            var operation = await LoadOperationAsync(operationsDbContext, id, cancellationToken);
            if (operation is null)
            {
                return Results.NotFound();
            }
            if (operation.OperationType == Reserve)
            {
                return RetiredReserveResult();
            }
            if (IsFinalizedForCorrection(operation.Status))
            {
                return Results.Conflict(new
                {
                    detail = "Finalized operations are immutable. Create a correction proposal instead.",
                    correctionRoute = $"/api/v1/operations/{operation.Id}/corrections"
                });
            }
            if (operation.Status == Shipped)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.Status)] = ["Shipped transfers cannot be cancelled because stock has already left the main warehouse. Receive it, then use a return/transfer correction if needed."] });
            }
            if (IsOnlineSale(operation) && operation.Status is Dispatched or Delivered or FailedDelivery)
            {
                return Results.Conflict(new
                {
                    code = "online-delivery-recovery-required",
                    detail = "A dispatched online order cannot be cancelled. Record the delivery outcome; if goods return physically, create a linked return operation."
                });
            }
            if (operation.Status == Cancelled)
            {
                return Results.NoContent();
            }
            if (operation.OperationType == Return && operation.Status == PendingApproval && !IsOperationsAdministrator(currentUser))
            {
                return Results.Forbid();
            }
            if (operation.OperationType == Return && operation.Status == Draft && operation.CreatedBy != (currentUser.UserId ?? Guid.Empty) && !IsOperationsAdministrator(currentUser))
            {
                return Results.Forbid();
            }
            if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "cancel", cancellationToken))
            {
                return Results.Forbid();
            }

            var userId = currentUser.UserId ?? Guid.Empty;
            IResult? cancelConflict = null;
            await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, identityDbContext, async () =>
            {
                var lockedOperation = await LockAndLoadOperationAsync(operationsDbContext, id, cancellationToken);
                if (lockedOperation is null)
                {
                    cancelConflict = Results.Conflict(new
                    {
                        code = "transition-conflict",
                        detail = "The operation changed before cancellation could be applied. Reload and retry."
                    });
                    return;
                }

                operation = lockedOperation;
                if (IsFinalizedForCorrection(operation.Status))
                {
                    cancelConflict = Results.Conflict(new
                    {
                        code = "transition-conflict",
                        detail = "Finalized operations are immutable. Create a correction proposal instead.",
                        correctionRoute = $"/api/v1/operations/{operation.Id}/corrections"
                    });
                    return;
                }
                if (operation.Status == Shipped)
                {
                    cancelConflict = Results.Conflict(new
                    {
                        code = "transition-conflict",
                        detail = "The operation was shipped before cancellation could be applied."
                    });
                    return;
                }
                if (operation.Status == Cancelled)
                {
                    return;
                }
                if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "cancel", cancellationToken))
                {
                    cancelConflict = Results.Forbid();
                    return;
                }

                if (operation.OperationType == WarehouseTransfer && operation.Status is Reserved or Shipped)
                {
                    foreach (var group in operation.OperationLines.GroupBy(line => line.SkuId))
                    {
                        await ledgerService.ReleaseInWarehouseAsync(operation.SourceLocationId!.Value, group.Key, group.Sum(line => line.Quantity), userId, operation.Id, cancellationToken);
                    }
                }
                if (operation.OperationType is WholesaleSale or RetailSale && operation.Status == Reserved)
                {
                    foreach (var group in operation.OperationLines.Where(line => line.EntryMode == "Packs").GroupBy(line => line.SkuId))
                    {
                        await ledgerService.ReleaseInWarehouseAsync(operation.SourceLocationId!.Value, group.Key, group.Sum(line => line.Quantity), userId, operation.Id, cancellationToken);
                    }
                }

                operation.Status = Cancelled;
                operation.Notes = $"{operation.Notes}\nCancellation reason: {request.Reason.Trim()}".Trim();
                await AddVersionAsync(operationsDbContext, operation, "Cancelled", userId, CreateSnapshot(operation, ReadTransferAllocations(operation)), clock.EgyptNow, cancellationToken);
                await operationsDbContext.SaveChangesAsync(cancellationToken);
                await auditLogWriter.WriteAsync(
                    "Operation",
                    operation.Id,
                    "Cancel",
                    new { operation.OperationType, operation.Status },
                    cancellationToken: cancellationToken);
            }, cancellationToken);
            if (cancelConflict is not null) return cancelConflict;
            return Results.NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { code = "operation-cancellation-concurrency-conflict", detail = "The operation or inventory changed during cancellation. Refresh and try again." });
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { code = "operation-cancellation-save-conflict", detail = "The operation cancellation could not be saved." });
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(new { code = "operation-cancellation-rejected", detail = exception.Message });
        }
    }

    private static async Task<(IReadOnlyList<ReplenishmentRowResponse> Rows, int Total)> BuildReplenishmentRowsAsync(
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        OperationsDbContext operationsDbContext,
        ICurrentUser currentUser,
        Guid? locationFilter,
        Guid? skuFilter,
        PageRequest? paging,
        CancellationToken cancellationToken)
    {
        var locationsQuery = inventoryDbContext.Locations
            .Where(location => location.IsActive && location.LocationType != MainWarehouse);
        if (IsWarehouseClerk(currentUser))
        {
            var clerkLocationId = currentUser.LocationId!.Value;
            locationsQuery = locationsQuery.Where(location => location.Id == clerkLocationId);
        }
        if (locationFilter.HasValue)
        {
            locationsQuery = locationsQuery.Where(location => location.Id == locationFilter.Value);
        }

        var locations = await locationsQuery
            .OrderBy(location => location.Name)
            .ToListAsync(cancellationToken);
        var locationIds = locations.Select(location => location.Id).ToArray();
        if (locationIds.Length == 0)
        {
            return ([], 0);
        }

        var balancesQuery = inventoryDbContext.StockBalances
            .Where(balance => locationIds.Contains(balance.LocationId) && balance.TargetQty != null);
        if (skuFilter.HasValue)
        {
            balancesQuery = balancesQuery.Where(balance => balance.SkuId == skuFilter.Value);
        }

        var total = paging is null ? 0 : await balancesQuery.CountAsync(cancellationToken);
        var orderedBalances = balancesQuery.OrderBy(balance => balance.LocationId).ThenBy(balance => balance.SkuId).ThenBy(balance => balance.Id);
        var balances = await (paging is null ? orderedBalances : orderedBalances.Skip(paging.Skip).Take(paging.PageSize)).ToListAsync(cancellationToken);
        if (paging is null) total = balances.Count;
        var skuIds = balances.Select(balance => balance.SkuId).Distinct().ToArray();
        if (skuIds.Length == 0)
        {
            return ([], total);
        }

        var skus = await catalogDbContext.Skus
            .Include(sku => sku.Product)
            .Where(sku => skuIds.Contains(sku.Id))
            .ToDictionaryAsync(
                sku => sku.Id,
                sku => new ReplenishmentSkuLookup(sku.SkuCode, sku.Product.Name, sku.Product.PiecesPerPack),
                cancellationToken);

        var incoming = await operationsDbContext.OperationLogs
            .Include(operation => operation.OperationLines)
            .Where(operation =>
                !operation.IsDeleted &&
                operation.OperationType == WarehouseTransfer &&
                operation.AutomationType == "TargetReplenishment" &&
                (operation.Status == Draft || operation.Status == Reserved || operation.Status == Shipped) &&
                operation.DestinationLocationId.HasValue &&
                locationIds.Contains(operation.DestinationLocationId.Value))
            .SelectMany(
                operation => operation.OperationLines,
                (operation, line) => new { DestinationLocationId = operation.DestinationLocationId!.Value, line.SkuId, line.Quantity })
            // Replenishment is rendered a page at a time.  Limiting this aggregate to
            // the displayed SKUs avoids recalculating every catalog SKU for each page.
            .Where(value => skuIds.Contains(value.SkuId))
            .GroupBy(value => new { value.DestinationLocationId, value.SkuId })
            .Select(group => new { group.Key.DestinationLocationId, group.Key.SkuId, Quantity = group.Sum(value => value.Quantity) })
            .ToDictionaryAsync(value => (value.DestinationLocationId, value.SkuId), value => value.Quantity, cancellationToken);

        var mainLocationId = await inventoryDbContext.Locations
            .Where(location => location.IsActive && location.LocationType == MainWarehouse)
            .Select(location => (Guid?)location.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var mainStock = mainLocationId.HasValue
            ? await inventoryDbContext.StockBalances
                .Where(balance => balance.LocationId == mainLocationId.Value && skuIds.Contains(balance.SkuId))
                .ToDictionaryAsync(balance => balance.SkuId, balance => balance.AvailableQty, cancellationToken)
            : [];

        var rows = balances
            .Select(balance =>
            {
                var location = locations.First(location => location.Id == balance.LocationId);
                skus.TryGetValue(balance.SkuId, out var sku);
                incoming.TryGetValue((balance.LocationId, balance.SkuId), out var incomingPacks);
                mainStock.TryGetValue(balance.SkuId, out var mainAvailable);
                var target = balance.TargetQty ?? 0;
                var shortage = Math.Max(target - balance.AvailableQty - incomingPacks, 0);
                return new ReplenishmentRowResponse(
                    balance.LocationId,
                    location.Name,
                    location.LocationType,
                    balance.SkuId,
                    sku?.SkuCode,
                    sku?.ProductName,
                    sku?.PiecesPerPack,
                    balance.AvailableQty,
                    ToPieces(balance.AvailableQty, sku?.PiecesPerPack, location.LocationType),
                    incomingPacks,
                    ToPieces(incomingPacks, sku?.PiecesPerPack, location.LocationType),
                    target,
                    ToPieces(target, sku?.PiecesPerPack, location.LocationType),
                    shortage,
                    ToPieces(shortage, sku?.PiecesPerPack, location.LocationType),
                    mainAvailable);
            })
            .OrderByDescending(row => row.ShortagePacks)
            .ThenBy(row => row.DestinationLocationName)
            .ThenBy(row => row.SkuCode ?? row.SkuId.ToString())
            .ToList();
        return (rows, total);
    }

    private static async Task<Dictionary<Guid, int>> LoadPiecesPerPackBySkuAsync(
        CatalogDbContext catalogDbContext,
        IEnumerable<OperationLine> lines,
        CancellationToken cancellationToken)
    {
        var skuIds = lines.Select(line => line.SkuId).Distinct().ToArray();
        if (skuIds.Length == 0)
        {
            return [];
        }

        return await catalogDbContext.Skus
            .Include(sku => sku.Product)
            .Where(sku => skuIds.Contains(sku.Id) && sku.Product.PiecesPerPack != null)
            .ToDictionaryAsync(sku => sku.Id, sku => sku.Product.PiecesPerPack!.Value, cancellationToken);
    }

    // Drafts created before sealed-pack piece multiples were normalized can
    // still be marked as Pieces. They represent whole packs and must reserve
    // and ship their selected batch, never consume opened loose pieces.
    private static async Task NormalizeLegacySealedWholesalePieceLinesAsync(
        OperationLog operation,
        CatalogDbContext catalogDbContext,
        CancellationToken cancellationToken)
    {
        if (operation.OperationType != WholesaleSale)
        {
            return;
        }

        var candidateSkuIds = operation.OperationLines
            .Where(line => line.EntryMode == "Pieces" && line.Quantity > 0)
            .Select(line => line.SkuId)
            .Distinct()
            .ToArray();
        if (candidateSkuIds.Length == 0)
        {
            return;
        }

        var piecesPerPackBySku = await catalogDbContext.Skus
            .Include(sku => sku.Product)
            .Where(sku => candidateSkuIds.Contains(sku.Id) &&
                sku.Product.SellMode == "SealedPackOnly" &&
                sku.Product.PiecesPerPack != null && sku.Product.PiecesPerPack > 0)
            .ToDictionaryAsync(sku => sku.Id, sku => sku.Product.PiecesPerPack!.Value, cancellationToken);

        foreach (var line in operation.OperationLines.Where(line => line.EntryMode == "Pieces"))
        {
            if (!piecesPerPackBySku.TryGetValue(line.SkuId, out var piecesPerPack) || line.Quantity % piecesPerPack != 0)
            {
                continue;
            }

            var wasBonus = line.LineType == "Bonus" || line.BonusQuantity > 0;
            line.EntryMode = "Packs";
            line.Quantity /= piecesPerPack;
            line.UnitPrice *= piecesPerPack;
            line.LineTotal = line.UnitPrice * line.Quantity;
            line.BonusQuantity = wasBonus ? line.Quantity : 0;
        }
    }

    private static async Task<IReadOnlyList<TransferAllocationSnapshot>> BuildSelectedPackAllocationsAsync(
        OperationLog operation,
        InventoryDbContext inventoryDbContext,
        OperationsDbContext operationsDbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var packLines = operation.OperationLines.Where(line => line.EntryMode == "Packs").ToList();
        if (packLines.Count == 0)
        {
            return [];
        }

        var sourceLocationId = operation.SourceLocationId ?? throw new InvalidOperationException("Sale source location is required.");
        var today = DateOnly.FromDateTime(clock.EgyptNow);
        var skuIds = packLines.Select(line => line.SkuId).Distinct().ToArray();
        var batches = await inventoryDbContext.InventoryBatches
            .Where(batch =>
                batch.LocationId == sourceLocationId &&
                skuIds.Contains(batch.SkuId) &&
                batch.Quantity > 0 &&
                !batch.IsQuarantined &&
                (batch.ExpiryDate == null || batch.ExpiryDate >= today))
            .ToListAsync(cancellationToken);
        var reservedByBatch = await LoadReservedBatchQuantitiesAsync(operationsDbContext, sourceLocationId, operation.Id, cancellationToken);
        var plannedByBatch = new Dictionary<Guid, int>();
        var allocations = new List<TransferAllocationSnapshot>();

        foreach (var line in packLines)
        {
            if (line.ExpiryDate is null)
            {
                throw new InvalidOperationException($"{line.SkuCodeSnapshot} requires a selected batch expiry.");
            }

            var matchingBatches = batches
                .Where(value => value.SkuId == line.SkuId &&
                    NormalizeBlank(value.LotNumber) == NormalizeBlank(line.LotNumber) &&
                    value.ExpiryDate == line.ExpiryDate)
                .OrderBy(value => value.CreatedAt)
                .ThenBy(value => value.Id)
                .ToList();
            if (matchingBatches.Count == 0)
            {
                throw new InvalidOperationException($"{line.SkuCodeSnapshot} selected batch was not found or is expired.");
            }

            var remaining = line.Quantity;
            var lineAllocations = new List<BatchAllocation>();
            foreach (var batch in matchingBatches)
            {
                reservedByBatch.TryGetValue(batch.Id, out var reservedQuantity);
                plannedByBatch.TryGetValue(batch.Id, out var plannedQuantity);
                var availableQuantity = Math.Max(batch.Quantity - reservedQuantity - plannedQuantity, 0);
                if (availableQuantity == 0) continue;
                var take = Math.Min(availableQuantity, remaining);
                plannedByBatch[batch.Id] = plannedQuantity + take;
                lineAllocations.Add(new BatchAllocation(batch.Id, take, batch.LotNumber, batch.ExpiryDate));
                remaining -= take;
                if (remaining == 0) break;
            }

            if (remaining > 0)
            {
                throw new InvalidOperationException($"{line.SkuCodeSnapshot} selected batch has only {line.Quantity - remaining} available pack(s).");
            }

            allocations.Add(new TransferAllocationSnapshot(line.SkuId, lineAllocations));
        }

        return allocations;
    }

    private static async Task ReserveSelectedAllocationsAsync(
        Guid locationId,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        StockLedgerService ledgerService,
        Guid userId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        foreach (var skuAllocations in allocations.GroupBy(value => value.SkuId))
        {
            await ledgerService.ReserveInWarehouseAsync(
                locationId,
                skuAllocations.Key,
                skuAllocations.SelectMany(value => value.Allocations).Sum(value => value.Quantity),
                userId,
                operationId,
                cancellationToken);
        }
    }

    private static async Task<Dictionary<Guid, int>> LoadReservedBatchQuantitiesAsync(
        OperationsDbContext dbContext,
        Guid locationId,
        Guid excludeOperationId,
        CancellationToken cancellationToken)
    {
        var operations = await dbContext.OperationLogs
            .Include(operation => operation.OperationVersions)
            .Where(operation =>
                operation.Id != excludeOperationId &&
                !operation.IsDeleted &&
                operation.SourceLocationId == locationId &&
                operation.Status == Reserved)
            .ToListAsync(cancellationToken);

        var reserved = new Dictionary<Guid, int>();
        foreach (var operation in operations)
        {
            foreach (var allocation in ReadTransferAllocations(operation))
            {
                foreach (var batch in allocation.Allocations)
                {
                    reserved.TryGetValue(batch.BatchId, out var current);
                    reserved[batch.BatchId] = current + batch.Quantity;
                }
            }
        }

        return reserved;
    }

    private static async Task<OperationLog?> LoadOperationAsync(OperationsDbContext dbContext, Guid id, CancellationToken cancellationToken) =>
        await dbContext.OperationLogs
            .Include(operation => operation.OperationLines)
            .ThenInclude(line => line.SourceAllocations)
            .Include(operation => operation.InventoryReceiptHeader)
            .Include(operation => operation.OperationVersions)
            .Include(operation => operation.ReturnApprovalLines)
            .Include(operation => operation.ShopifyOrderLink)
            .FirstOrDefaultAsync(operation => operation.Id == id && !operation.IsDeleted, cancellationToken);

    private static async Task<OperationLog?> LoadOperationSummaryAsync(OperationsDbContext dbContext, Guid id, CancellationToken cancellationToken) =>
        await dbContext.OperationLogs.AsNoTracking()
            .Include(operation => operation.InventoryReceiptHeader)
            .Include(operation => operation.ReturnApprovalLines)
            .Include(operation => operation.ShopifyOrderLink)
            .FirstOrDefaultAsync(operation => operation.Id == id && !operation.IsDeleted, cancellationToken);

    private static async Task<OperationLog?> LoadOperationForDraftUpdateAsync(OperationsDbContext dbContext, Guid id, CancellationToken cancellationToken) =>
        await dbContext.OperationLogs
            .Include(operation => operation.OperationLines)
            .ThenInclude(line => line.SourceAllocations)
            .Include(operation => operation.InventoryReceiptHeader)
            .Include(operation => operation.OperationVersions)
            .Include(operation => operation.ReturnApprovalLines)
            .Include(operation => operation.ShopifyOrderLink)
            .FirstOrDefaultAsync(operation => operation.Id == id && !operation.IsDeleted, cancellationToken);

    private static async Task LockOperationAsync(OperationsDbContext dbContext, Guid id, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational()) return;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"select 1 from operations.operation_logs where id = {id} for update",
            cancellationToken);
    }

    /// <summary>
    /// Claims the operation row before tracking its aggregate.  Transition commands
    /// must use this after their inexpensive preflight reads so concurrent draft
    /// edits and terminal transitions cannot apply stock effects from stale lines.
    /// </summary>
    private static async Task<OperationLog?> LockAndLoadOperationAsync(OperationsDbContext dbContext, Guid id, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await LockOperationAsync(dbContext, id, cancellationToken);
        return await LoadOperationAsync(dbContext, id, cancellationToken);
    }

    private static async Task<DraftValidationResult> ValidateDraftAsync(
        OperationRequest request,
        CatalogDbContext catalogDbContext,
        CrmDbContext crmDbContext,
        InventoryDbContext inventoryDbContext,
        OperationsDbContext operationsDbContext,
        ICurrentUser currentUser,
        bool isCreate,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var operationType = NormalizeOperationType(request.OperationType);
        if (operationType is not (InventoryReceipt or WarehouseTransfer or WholesaleSale or RetailSale or Return or WriteOff))
        {
            errors[nameof(request.OperationType)] = ["Operation type must be InventoryReceipt, WarehouseTransfer, WholesaleSale, RetailSale, Return, or WriteOff."];
        }
        if (request.Lines.Count == 0)
        {
            errors[nameof(request.Lines)] = ["At least one line is required."];
        }
        if (request.ExternalReference?.Trim().Length > 200)
        {
            errors[nameof(request.ExternalReference)] = ["Operation reference must be 200 characters or fewer."];
        }
        if (request.Lines.Any(line => GetLineQuantity(operationType, line) <= 0))
        {
            errors[nameof(request.Lines)] = ["Line quantity must be greater than zero."];
        }
        if (request.Lines.Any(line => (operationType != Return && string.IsNullOrWhiteSpace(line.LotNumber)) || line.ExpiryDate is null))
        {
            errors[nameof(request.Lines)] = ["Every operation line must include a batch code and expiry date selected through the batch / expiry control."];
        }
        if (request.Lines.Select(line => GetLineUniquenessKey(operationType, line)).Distinct().Count() != request.Lines.Count)
        {
            errors[nameof(request.Lines)] = ["Duplicate SKU lines must differ by side, sale bonus flag, entry mode, lot, or batch expiry."];
        }
        if (operationType is WholesaleSale or RetailSale)
        {
            var saleLineGroups = request.Lines.GroupBy(line => (
                line.SkuId,
                LotNumber: NormalizeBlank(line.LotNumber),
                line.ExpiryDate));
            if (saleLineGroups.Any(group => group.Count() > 3 || group.GroupBy(line => ResolveLineType(line)).Any(kind => kind.Count() > 1)))
                errors[nameof(request.Lines)] = ["For each SKU and batch, use each line kind (Paid, Bonus, Offer) at most once."];
            if (request.Lines.Any(line => string.Equals(line.PricingMode, "Promotion50", StringComparison.OrdinalIgnoreCase) && line.LineType is null))
                errors[nameof(request.Lines)] = ["This draft uses the legacy 50% promotion format. Reload it and enter a fixed Offer price before saving."];
        }
        var paymentMethod = NormalizePaymentMethod(request.PaymentMethod);
        var isFinancialOperation = operationType is WholesaleSale or RetailSale;
        if (paymentMethod is null && !string.IsNullOrWhiteSpace(request.PaymentMethod))
        {
            errors[nameof(request.PaymentMethod)] = ["Payment method must be CashHandToHand, BankTransfer, Wallet, or MerchantAccount."];
        }

        var isNonfinancialOperation = operationType is InventoryReceipt or WarehouseTransfer or WriteOff;
        var customerResolution = await CustomerPaymentTrackResolver.ResolveAsync(
                crmDbContext,
                request.MerchantId,
                request.BuyerName,
                request.BuyerPhone,
                request.CustomerKind,
                request.PaymentTrack,
                cancellationToken);
        foreach (var error in isNonfinancialOperation ? new Dictionary<string, string[]>() : customerResolution.Errors)
        {
            errors[error.Key] = error.Value;
        }
        if (operationType is WholesaleSale or RetailSale)
        {
            var invalidPriceLine = request.Lines.FirstOrDefault(line =>
                (ResolveLineType(line) == "Bonus" && (line.UnitPrice ?? 0m) != 0m) ||
                (ResolveLineType(line) != "Bonus" && (line.UnitPrice ?? 0m) < 0m));
            if (invalidPriceLine is not null)
            {
                errors[nameof(request.Lines)] = ["Sale and offer prices cannot be negative; zero is allowed for free or discounted items. Bonus price must be zero."];
            }
        }
        if (operationType is not (WholesaleSale or RetailSale) && request.Lines.Any(line => line.IsBonus == true || line.LineType is not null || !string.IsNullOrWhiteSpace(line.PricingMode)))
        {
            errors[nameof(request.Lines)] = ["Bonus lines are allowed only for sale operations."];
        }

        var source = request.SourceLocationId.HasValue
            ? await inventoryDbContext.Locations.FirstOrDefaultAsync(location => location.Id == request.SourceLocationId && location.IsActive, cancellationToken)
            : null;
        var destination = request.DestinationLocationId.HasValue
            ? await inventoryDbContext.Locations.FirstOrDefaultAsync(location => location.Id == request.DestinationLocationId && location.IsActive, cancellationToken)
            : null;

        if (operationType == InventoryReceipt)
        {
            if (destination is null || !IsPhysicalWarehouse(destination))
            {
                errors[nameof(request.DestinationLocationId)] = ["Inventory receipt destination must be an active physical warehouse."];
            }
        }
        if (operationType == WarehouseTransfer)
        {
            if (source is null || !IsPhysicalWarehouse(source))
            {
                errors[nameof(request.SourceLocationId)] = ["Warehouse transfer source must be an active physical warehouse."];
            }
            if (destination is null || !IsTransferDestination(destination) || destination.Id == source?.Id)
            {
                errors[nameof(request.DestinationLocationId)] = ["Warehouse transfer destination must be a different active physical warehouse or Online location."];
            }
        }
        if (operationType == WholesaleSale)
        {
            if (source is null)
            {
                errors[nameof(request.SourceLocationId)] = ["Wholesale sale source location is required."];
            }
            if (!request.MerchantId.HasValue)
            {
                errors[nameof(request.MerchantId)] = ["Wholesale sale requires an active merchant."];
            }
        }
        if (operationType == RetailSale)
        {
            if (source is null || !IsRetailSaleLocation(source))
            {
                errors[nameof(request.SourceLocationId)] = ["Retail sale source must be a retail or online location."];
            }
        }
        var requiresPacksOnly = operationType == InventoryReceipt ||
            (operationType == WarehouseTransfer &&
             (string.Equals(source?.LocationType, "MainWarehouse", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(destination?.LocationType, "MainWarehouse", StringComparison.OrdinalIgnoreCase)));
        if (requiresPacksOnly && request.Lines.Any(line => NormalizeEntryMode(line.EntryMode) == "Pieces"))
        {
            errors[nameof(request.Lines)] = [operationType == WarehouseTransfer
                ? "Transfers involving the Main Warehouse use packs only. Edit the affected line to Packs."
                : $"{operationType} uses packs only. Edit the affected line to Packs."];
        }
        var isOnline = operationType == RetailSale && NormalizeSalesChannel(request.SalesChannel) == "Online";
        if (isOnline)
        {
            if (source is null || !string.Equals(source.LocationType, "SubWarehouse", StringComparison.OrdinalIgnoreCase))
                errors[nameof(request.SourceLocationId)] = ["Online sale source must be an active SubWarehouse."];
            if (string.IsNullOrWhiteSpace(request.BuyerName) || string.IsNullOrWhiteSpace(request.BuyerPhone) || string.IsNullOrWhiteSpace(request.ShippingAddress))
                errors[nameof(request)] = ["Online sales require buyer name, phone, and delivery address."];
            if (request.BuyerContactVerified != true)
                errors[nameof(request.BuyerContactVerified)] = ["Phone and address must be verified before confirming an online sale."];
            if (request.DeliveryFee is < 0 || request.CodCharge is < 0)
                errors[nameof(request)] = ["Delivery fee and COD charge cannot be negative."];
            var isCod = string.Equals(request.PaymentStatus, "DueOnDelivery", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(request.PaymentMethod, "CashHandToHand", StringComparison.OrdinalIgnoreCase);
            if (!isCod && (string.IsNullOrWhiteSpace(request.ExternalPaymentProvider) || string.IsNullOrWhiteSpace(request.ExternalPaymentReference) || request.ExternalPaymentVerified != true))
                errors[nameof(request.ExternalPaymentReference)] = ["Gateway/wallet online sales require provider, payment reference, and operator verification."];
        }
        if (operationType == Return)
        {
            if (source is null || !IsPhysicalWarehouse(source))
            {
                errors[nameof(request.SourceLocationId)] = ["Return receiving location must be an active physical warehouse."];
            }
            if (request.Lines.Any(line => NormalizeReturnDisposition(line.ReturnDisposition) is "Invalid" or "Quarantine"))
                errors[nameof(request.Lines)] = ["Return disposition must be Sellable or WriteOff."];
            // Piece returns use the same source/lot lineage validation as pack returns.
            // A write-off is a valid disposition for either entry mode.
            if (request.Lines.Any(line => line.NormalQuantity is < 0 || line.NormalQuantity > GetLineQuantity(operationType, line) || line.NormalValue is < 0 || line.ExceptionValue is < 0))
                errors[nameof(request.Lines)] = ["Return normal quantity and requested values must be non-negative and normal quantity cannot exceed returned quantity."];
            if (request.Lines.Any(line => line.SourceOperationId.HasValue || line.SourceOperationLineId.HasValue || line.SourceBatchId.HasValue || line.SourceOpenedPieceLotId.HasValue))
                errors[nameof(request.Lines)] = ["Return lines cannot link to original sale lines or batches. Use the optional return operation reference instead."];
            if (request.ReturnReferenceOperationId.HasValue)
            {
                var referencedOperation = await operationsDbContext.OperationLogs.AsNoTracking()
                    .FirstOrDefaultAsync(value => value.Id == request.ReturnReferenceOperationId.Value && !value.IsDeleted, cancellationToken);
                if (referencedOperation is null || referencedOperation.OperationType is not (WholesaleSale or RetailSale) || !IsFinalizedForCorrection(referencedOperation.Status))
                    errors[nameof(request.ReturnReferenceOperationId)] = ["Return operation reference must identify a finalized wholesale or retail sale."];
            }
        }
        if (operationType == Change)
        {
            if (source is null)
            {
                errors[nameof(request.SourceLocationId)] = ["Change location is required."];
            }
            if (!request.Lines.Any(line => NormalizeLineSection(operationType, line.Section) == ChangeOut))
            {
                errors[nameof(request.Lines)] = ["Change requires at least one returned line."];
            }
            if (!request.Lines.Any(line => NormalizeLineSection(operationType, line.Section) == ChangeIn))
            {
                errors[nameof(request.Lines)] = ["Change requires at least one replacement line."];
            }
        }
        if (operationType == WriteOff)
        {
            if (source is null)
            {
                errors[nameof(request.SourceLocationId)] = ["Write-off source location is required."];
            }
            if (!IsOperationsAdministrator(currentUser))
            {
                errors[nameof(request.OperationType)] = ["Write-off requires an Operations administrator."];
            }
            // Write-off accepts both packs and pieces. Piece lineage is validated
            // by the stock ledger during confirmation.
        }

        var merchant = customerResolution.Result?.Merchant;

        Representative? representative = null;
        if (request.RepresentativeId.HasValue)
        {
            representative = await crmDbContext.Representatives.FirstOrDefaultAsync(value => value.Id == request.RepresentativeId && !value.IsDeleted && value.Status == "Active", cancellationToken);
            if (representative is null)
            {
                errors[nameof(request.RepresentativeId)] = ["Representative must exist and be active."];
            }
        }

        var skuIds = request.Lines.Select(line => line.SkuId).Distinct().ToArray();
        var skus = await catalogDbContext.Skus
            .Include(sku => sku.Product)
            .Where(sku => skuIds.Contains(sku.Id) && sku.IsActive && sku.DeletedAt == null && sku.Product.IsActive && sku.Product.DeletedAt == null)
            .ToDictionaryAsync(sku => sku.Id, cancellationToken);
        if (skus.Count != skuIds.Length)
        {
            errors[nameof(request.Lines)] = ["All operation SKUs must exist and be active."];
        }
        else if (request.Lines.FirstOrDefault(line =>
                     NormalizeEntryMode(line.EntryMode) == "Pieces" &&
                     string.Equals(skus[line.SkuId].Product.SellMode, "SealedPackOnly", StringComparison.OrdinalIgnoreCase) &&
                     operationType is not (WholesaleSale or Return)) is { } sealedPackLine)
        {
            errors[nameof(request.Lines)] = [$"SKU {skus[sealedPackLine.SkuId].SkuCode} is sold in sealed packs only. Change the line to Packs."];
        }
        else if (request.Lines.FirstOrDefault(line =>
                     NormalizeEntryMode(line.EntryMode) == "Pieces" &&
                     string.Equals(skus[line.SkuId].Product.SellMode, "SealedPackOnly", StringComparison.OrdinalIgnoreCase) &&
                     (operationType is WholesaleSale or Return) &&
                     (skus[line.SkuId].Product.PiecesPerPack is not > 0 || GetLineQuantity(operationType, line) % skus[line.SkuId].Product.PiecesPerPack!.Value != 0 ||
                      (operationType == Return && line.NormalQuantity.HasValue && line.NormalQuantity.Value % skus[line.SkuId].Product.PiecesPerPack!.Value != 0))) is { } incompleteSealedPackLine)
        {
            var sku = skus[incompleteSealedPackLine.SkuId];
            var piecesPerPack = sku.Product.PiecesPerPack;
            errors[nameof(request.Lines)] = [piecesPerPack is > 0
                ? $"SKU {sku.SkuCode} is sealed-pack only: piece quantities must be exact multiples of {piecesPerPack.Value} pieces per pack."
                : $"SKU {sku.SkuCode} is sealed-pack only but has no valid pieces-per-pack setting."];
        }

        OperationCustomerSnapshot? correctionSource = null;

        // A return's commercial party is explicit. Its optional operation
        // reference is traceability only and never inherits a source-line's
        // customer, price, batch, or quantity entitlement.
        var inheritsMerchantAccount = correctionSource?.ClientId is not null;
        if (IsMerchantAccountMethod(paymentMethod) &&
            customerResolution.Result is not { PaymentTrack: PaymentTracks.MerchantAccount } &&
            !inheritsMerchantAccount)
            errors[nameof(request.PaymentMethod)] = ["MerchantAccount is available only for a selected merchant account."];
        if (paymentMethod is null && isFinancialOperation &&
            customerResolution.Result is not { PaymentTrack: PaymentTracks.MerchantAccount } &&
            !inheritsMerchantAccount)
            errors[nameof(request.PaymentMethod)] = ["A real movement method is required when the customer is not a selected merchant."];

        return new DraftValidationResult(errors, source, destination, skus, merchant, representative, correctionSource);
    }

    private static async Task<IReadOnlyList<MerchantSalesVarianceWarning>> BuildMerchantSalesVarianceWarningsAsync(
        OperationLog operation,
        OperationsDbContext operationsDbContext,
        MerchantBatchHistoryService historyService,
        CancellationToken cancellationToken)
    {
        var warnings = new List<MerchantSalesVarianceWarning>();
        if (operation.OperationType is not (Return or Change))
        {
            return warnings;
        }

        if (operation.ClientId is null)
        {
            // Unlinked retail/online returns are governed by the explicit return
            // approval workflow. They must never be compared with company-wide
            // SKU sales because that creates an unrelated global entitlement cap.
            return warnings;
        }

        var history = await historyService.LoadAsync(operation.ClientId.Value, operation.Id, cancellationToken);
        var facts = history.ToDictionary(row => (row.Key.SkuId, NormalizeBlank(row.Key.LotNumber), row.Key.ExpiryDate));
        var lines = operation.OperationLines
            .Where(line => operation.OperationType == Return || line.Section == ChangeOut);
        foreach (var group in lines.GroupBy(line => (line.SkuId, NormalizeBlank(line.LotNumber), line.ExpiryDate, line.EntryMode)))
        {
            var requested = group.Sum(line => line.EntryMode == "Pieces"
                ? line.Quantity
                : line.Quantity * (line.PiecesPerPackSnapshot ?? 0));
            facts.TryGetValue((group.Key.SkuId, group.Key.Item2, group.Key.ExpiryDate), out var fact);
            var line = group.First();
            if (fact is null)
            {
                var skuFacts = history.Where(row => row.Key.SkuId == group.Key.SkuId).ToList();
                var sameBatchExists = skuFacts.Any(row => NormalizeBlank(row.Key.LotNumber) == group.Key.Item2);
                var sameExpiryExists = skuFacts.Any(row => row.Key.ExpiryDate == group.Key.ExpiryDate);
                var mismatchType = skuFacts.Count == 0
                    ? "SkuMismatch"
                    : sameBatchExists && !sameExpiryExists
                        ? "ExpiryMismatch"
                        : !sameBatchExists && sameExpiryExists
                            ? "BatchMismatch"
                            : "BatchExpiryMismatch";
                var detail = mismatchType switch
                {
                    "SkuMismatch" => "The returned SKU was not found in this merchant's recorded sales.",
                    "ExpiryMismatch" => "The returned batch exists in recorded sales, but its expiry date does not match.",
                    "BatchMismatch" => "The returned expiry date exists in recorded sales, but its batch does not match.",
                    _ => "The returned batch and expiry date do not match any recorded sale for this SKU."
                };
                warnings.Add(new MerchantSalesVarianceWarning(
                    line.SkuId, line.SkuCodeSnapshot, line.ProductNameSnapshot, group.Key.Item2, group.Key.ExpiryDate,
                    requested, 0, 0, requested, detail, line.PiecesPerPackSnapshot, mismatchType));
            }
            var recordedBalance = fact?.RecordedBalancePhysicalPieces ?? 0;
            var soldQuantity = fact?.SoldPhysicalPieces ?? 0;
            var returnedQuantity = fact?.ReturnedPhysicalPieces ?? 0;
            var excessQuantity = Math.Max(returnedQuantity + requested - soldQuantity, 0);
            if (requested > recordedBalance)
            {
                warnings.Add(new MerchantSalesVarianceWarning(
                    line.SkuId,
                    line.SkuCodeSnapshot,
                    line.ProductNameSnapshot,
                    group.Key.Item2,
                    group.Key.ExpiryDate,
                    requested,
                    soldQuantity,
                    returnedQuantity,
                    excessQuantity,
                    $"Confirmed returns would be {excessQuantity} physical piece(s) above recorded sales for this merchant batch.",
                    line.PiecesPerPackSnapshot, "QuantityExcess", "Piece"));
            }

            var soldValue = fact?.SoldValue ?? 0m;
            var returnedValue = fact?.ReturnedValue ?? 0m;
            var requestedValue = group.Sum(value => Math.Max(0m, value.NormalValue) + Math.Max(0m, value.ExceptionValue));
            var excessValue = Math.Max(returnedValue + requestedValue - soldValue, 0m);
            if (excessValue > 0m)
            {
                warnings.Add(new MerchantSalesVarianceWarning(
                    line.SkuId,
                    line.SkuCodeSnapshot,
                    line.ProductNameSnapshot,
                    group.Key.Item2,
                    group.Key.Item3,
                    requested,
                    soldQuantity,
                    returnedQuantity,
                    excessQuantity,
                    $"Confirmed returns would be {excessValue:0.00} EGP above the recorded sale value for this merchant batch.",
                    line.PiecesPerPackSnapshot, "ValueExcess"));
            }
        }
        return warnings;
    }

    private static MerchantSalesVarianceGateResponse CreateMerchantSalesVarianceGate(
        IReadOnlyList<MerchantSalesVarianceWarning> warnings,
        bool canBypass) =>
        new(
            "MerchantSalesVariance",
        "Return traceability exception",
            "One or more returned SKUs, batches, expiry dates, quantities, or values do not match recorded merchant sales. An Admin or ERPAdmin must provide a reason to approve this exception.",
            canBypass,
            canBypass,
            warnings);

    private static async Task<OperationConfirmationRequest> ReadConfirmationRequestAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength is null or 0)
        {
            return new OperationConfirmationRequest(null, null);
        }

        return await JsonSerializer.DeserializeAsync<OperationConfirmationRequest>(request.Body, JsonOptions, cancellationToken)
            ?? new OperationConfirmationRequest(null, null);
    }

    private static async Task AcquireMerchantReturnLocksAsync(
        OperationLog operation,
        OperationsDbContext operationsDbContext,
        CancellationToken cancellationToken)
    {
        if (!operationsDbContext.Database.IsRelational())
        {
            return;
        }

        var keys = operation.OperationLines
            .Where(line => operation.OperationType == Return || line.Section == ChangeOut)
            .Select(line => $"{(operation.ClientId?.ToString("N") ?? "retail")}|{line.SkuId:N}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        foreach (var key in keys)
        {
            await operationsDbContext.Database.ExecuteSqlInterpolatedAsync(
                $"select pg_advisory_xact_lock(hashtextextended({key}, 0))",
                cancellationToken);
        }
    }

    private static bool CanReadOperation(ICurrentUser currentUser, OperationLog operation) =>
        !IsWarehouseClerk(currentUser) ||
        (currentUser.LocationId.HasValue &&
            (operation.SourceLocationId == currentUser.LocationId || operation.DestinationLocationId == currentUser.LocationId));

    private static bool CanCreateDraft(ICurrentUser currentUser, OperationRequest request, Location? source, Location? destination)
    {
        if (IsOperationsAdministrator(currentUser))
        {
            return true;
        }
        if (!IsWarehouseClerk(currentUser) || currentUser.LocationId is not { } clerkLocationId)
        {
            return false;
        }

        var operationType = NormalizeOperationType(request.OperationType);
        if (operationType == InventoryReceipt)
        {
            return destination?.Id == clerkLocationId && destination is not null && IsPhysicalWarehouse(destination);
        }
        if (operationType == WarehouseTransfer)
        {
            // Clerks participate in the destination receiving stage only. A
            // transfer draft can move stock and must remain administrator-owned.
            return false;
        }
        if (operationType == WriteOff)
        {
            return false;
        }

        return operationType == Return &&
            (source?.Id == clerkLocationId || destination?.Id == clerkLocationId) &&
            (source is null || IsPhysicalWarehouse(source)) &&
            (destination is null || IsPhysicalWarehouse(destination));
    }

    private static async Task<bool> CanMutateOperationAsync(ICurrentUser currentUser, OperationLog operation, InventoryDbContext dbContext, string action, CancellationToken cancellationToken)
    {
        // Retired workflows remain readable for audit, but cannot be edited,
        // confirmed, shipped, received, or cancelled through legacy routes.
        if (operation.OperationType is Change or Reserve)
            return false;
        if (IsOperationsAdministrator(currentUser))
        {
            return true;
        }
        if (!IsWarehouseClerk(currentUser) || currentUser.LocationId is not { } clerkLocationId)
        {
            return false;
        }

        var source = operation.SourceLocationId.HasValue
            ? await dbContext.Locations.FindAsync([operation.SourceLocationId.Value], cancellationToken)
            : null;
        if (operation.OperationType == InventoryReceipt)
        {
            return action is "confirm" or "cancel" && operation.DestinationLocationId == clerkLocationId && operation.DestinationLocationId.HasValue;
        }
        if (operation.OperationType == WarehouseTransfer)
        {
            return action == "receive" && operation.DestinationLocationId == clerkLocationId;
        }
        if (operation.OperationType == WriteOff)
        {
            return false;
        }
        if (operation.OperationType == Return)
        {
            return action is "confirm" or "cancel" &&
                (operation.SourceLocationId == clerkLocationId || operation.DestinationLocationId == clerkLocationId);
        }

        return false;
    }

    private static OperationWorkflowResponse BuildWorkflowProjection(ICurrentUser currentUser, OperationLog operation)
    {
        var actions = new List<OperationWorkflowActionResponse>();
        var isAdministrator = IsOperationsAdministrator(currentUser);
        var isAssignedClerk = IsWarehouseClerk(currentUser) && currentUser.LocationId.HasValue &&
            (operation.SourceLocationId == currentUser.LocationId || operation.DestinationLocationId == currentUser.LocationId);

        if (isAdministrator)
        {
            if (operation.Status == Draft)
            {
                actions.Add(new("edit-draft", false));
                if (operation.OperationType == Return)
                {
                    actions.Add(new("submit-approval", true));
                }
                actions.Add(new("confirm", false));
                actions.Add(new("cancel", operation.OperationType == Return));
            }
            else if (operation.OperationType == Return && operation.Status == PendingApproval)
            {
                actions.Add(new("approve", true));
                actions.Add(new("reject", true));
                actions.Add(new("cancel", true));
            }
            else if (operation.OperationType == Return && operation.Status == Approved)
            {
                actions.Add(new("confirm", false));
            }
            else if (operation.Status == Reserved)
            {
                actions.Add(new("ship", false));
                actions.Add(new("cancel", false));
            }
            else if (operation.Status == Shipped)
            {
                actions.Add(new("receive", false));
            }
            else if (operation.OperationType == WholesaleSale && operation.Status == Received)
            {
                actions.Add(new("complete", false));
            }
            else if (operation.Status == Dispatched)
            {
                actions.Add(new("delivered", false));
                actions.Add(new("failed-delivery", false));
            }
        }
        else if (isAssignedClerk)
        {
            if (operation.OperationType == InventoryReceipt && operation.Status == Draft && operation.DestinationLocationId == currentUser.LocationId)
            {
                actions.Add(new("edit-draft", false));
                actions.Add(new("confirm", false));
                actions.Add(new("cancel", false));
            }
            else if (operation.OperationType == WarehouseTransfer && operation.Status == Shipped && operation.DestinationLocationId == currentUser.LocationId)
            {
                actions.Add(new("receive", false));
            }
            else if (operation.OperationType == Return && operation.Status == Draft)
            {
                actions.Add(new("edit-draft", false));
                actions.Add(new("confirm", false));
                actions.Add(new("cancel", false));
            }
            else if (operation.OperationType == Return && operation.Status == Approved)
            {
                actions.Add(new("confirm", false));
            }
        }

        return new(operation.Status, actions);
    }

    private static async Task<IResult> GetOperationSourceAllocationsAsync(Guid id, OperationsDbContext dbContext, InventoryDbContext inventoryDbContext, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var operation = await LoadOperationSummaryAsync(dbContext, id, cancellationToken);
        if (operation is null) return Results.NotFound();
        if (!CanReadOperation(currentUser, operation)) return Results.Forbid();

        var rows = await (
                from allocation in dbContext.OperationLineSourceAllocations.AsNoTracking()
                join targetLine in dbContext.OperationLines.AsNoTracking() on allocation.TargetOperationLineId equals targetLine.Id
                join sourceLine in dbContext.OperationLines.AsNoTracking() on allocation.SourceOperationLineId equals sourceLine.Id
                join sourceOperation in dbContext.OperationLogs.AsNoTracking() on allocation.SourceOperationId equals sourceOperation.Id
                where targetLine.OperationId == id
                orderby allocation.CreatedAt, allocation.Id
                select new OperationLineSourceAllocationResponse(
                    allocation.Id, allocation.TargetOperationLineId, allocation.SourceOperationId, sourceOperation.OperationNumber,
                    allocation.SourceOperationLineId, allocation.SkuId, allocation.SourceBatchId, allocation.SourceOpenedPieceLotId,
                    allocation.EntryMode, allocation.LotNumber, allocation.ExpiryDate, allocation.Quantity,
                    sourceLine.Quantity, allocation.CreatedAt))
            .ToListAsync(cancellationToken);
        return Results.Ok(rows);
    }

    private static async Task<IResult> GetSourceSaleLinesAsync(
        Guid sourceOperationId,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var sourceOperation = await operationsDbContext.OperationLogs.AsNoTracking()
            .Include(operation => operation.OperationLines)
            .FirstOrDefaultAsync(operation => operation.Id == sourceOperationId && !operation.IsDeleted, cancellationToken);
        if (sourceOperation is null) return Results.NotFound();
        if (!CanReadOperation(currentUser, sourceOperation)) return Results.Forbid();
        if (sourceOperation.OperationType is not (WholesaleSale or RetailSale) || !IsFinalizedForCorrection(sourceOperation.Status))
        {
            return Results.Conflict(new { code = "invalid-source-sale", detail = "Only finalized wholesale or retail sales can be used as a return or exchange source." });
        }

        var lineIds = sourceOperation.OperationLines.Select(line => line.Id).ToArray();
        var consumed = await (
                from allocation in operationsDbContext.OperationLineSourceAllocations.AsNoTracking()
                join targetLine in operationsDbContext.OperationLines.AsNoTracking() on allocation.TargetOperationLineId equals targetLine.Id
                join targetOperation in operationsDbContext.OperationLogs.AsNoTracking() on targetLine.OperationId equals targetOperation.Id
                where lineIds.Contains(allocation.SourceOperationLineId) &&
                      !targetOperation.IsDeleted &&
                      (targetOperation.Status == Confirmed || targetOperation.Status == Completed || targetOperation.Status == Received)
                group allocation by allocation.SourceOperationLineId into grouped
                select new { SourceOperationLineId = grouped.Key, Quantity = grouped.Sum(value => value.Quantity) })
            .ToDictionaryAsync(value => value.SourceOperationLineId, value => value.Quantity, cancellationToken);

        var skuIds = sourceOperation.OperationLines.Select(line => line.SkuId).Distinct().ToArray();
        var batches = await inventoryDbContext.InventoryBatches.AsNoTracking()
            .Where(batch => skuIds.Contains(batch.SkuId) && batch.LocationId == sourceOperation.SourceLocationId)
            .ToListAsync(cancellationToken);
        var batchIds = batches.Select(batch => batch.Id).ToArray();
        var pieceLots = await inventoryDbContext.OpenedPieceLots.AsNoTracking()
            .Where(lot => batchIds.Contains(lot.SourceBatchId))
            .ToListAsync(cancellationToken);

        var rows = sourceOperation.OperationLines.OrderBy(line => line.Id).Select(line =>
        {
            var matchingBatches = batches
                .Where(batch => batch.SkuId == line.SkuId &&
                    string.Equals(NormalizeBlank(batch.LotNumber), NormalizeBlank(line.LotNumber), StringComparison.Ordinal) &&
                    batch.ExpiryDate == line.ExpiryDate)
                .OrderBy(batch => batch.Id)
                .Select(batch => new SourceSaleBatchCandidateResponse(batch.Id, batch.LotNumber, batch.ExpiryDate,
                    pieceLots.Where(lot => lot.SourceBatchId == batch.Id).OrderBy(lot => lot.Id)
                        .Select(lot => new SourceSalePieceLotCandidateResponse(lot.Id, lot.LoosePieceQuantity, lot.PieceExpiryDate)).ToList()))
                .ToList();
            consumed.TryGetValue(line.Id, out var consumedQuantity);
            return new SourceSaleLineResponse(sourceOperation.Id, sourceOperation.OperationNumber, line.Id, line.SkuId,
                line.SkuCodeSnapshot, line.ProductNameSnapshot, line.EntryMode, line.Quantity, consumedQuantity,
                Math.Max(0, line.Quantity - consumedQuantity), line.LotNumber, line.ExpiryDate, matchingBatches);
        }).ToList();
        return Results.Ok(rows);
    }

    private static async Task<IResult> SubmitCorrectionAsync(
        Guid proposalId,
        OperationCorrectionService service,
        ICurrentUser currentUser,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.SubmitAsync(proposalId, currentUser.UserId ?? Guid.Empty, currentUser.Role, cancellationToken);
        return ToCorrectionResult(result, httpContext);
    }

    private static async Task EnforceSourceAllocationCapsAsync(
        OperationLog operation,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        CancellationToken cancellationToken,
        bool allowReturnException = false)
    {
        if (operation.OperationType is not (Return or Change))
        {
            return;
        }

        var targetLineIds = operation.OperationLines
            .Where(line => operation.OperationType == Return || line.Section == ChangeOut)
            .Select(line => line.Id)
            .OrderBy(id => id)
            .ToArray();
        if (targetLineIds.Length == 0)
        {
            return;
        }

        var proposed = await operationsDbContext.OperationLineSourceAllocations
            .Where(allocation => targetLineIds.Contains(allocation.TargetOperationLineId))
            .OrderBy(allocation => allocation.SourceOperationLineId)
            .ThenBy(allocation => allocation.Id)
            .ToListAsync(cancellationToken);
        var targetLineIdSet = targetLineIds.ToHashSet();
        if (proposed.Any(allocation => !targetLineIdSet.Contains(allocation.TargetOperationLineId)))
        {
            throw new SourceAllocationCapException("Return source allocations are invalid.", []);
        }
        if (proposed.Count == 0)
        {
            return;
        }

        var sourceLineIds = proposed.Select(allocation => allocation.SourceOperationLineId).Distinct().OrderBy(id => id).ToArray();
        if (operationsDbContext.Database.IsRelational())
        {
            foreach (var sourceLineId in sourceLineIds)
            {
                await operationsDbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"select id from operations.operation_lines where id = {sourceLineId} for update",
                    cancellationToken);
                await operationsDbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"select id from operations.operation_line_source_allocations where source_operation_line_id = {sourceLineId} for update",
                    cancellationToken);
            }

            foreach (var batchId in proposed.Where(value => value.SourceBatchId.HasValue).Select(value => value.SourceBatchId!.Value).Distinct().OrderBy(id => id))
            {
                await inventoryDbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"select id from inventory.inventory_batches where id = {batchId} for update",
                    cancellationToken);
            }
            foreach (var pieceLotId in proposed.Where(value => value.SourceOpenedPieceLotId.HasValue).Select(value => value.SourceOpenedPieceLotId!.Value).Distinct().OrderBy(id => id))
            {
                await inventoryDbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"select id from inventory.opened_piece_lots where id = {pieceLotId} for update",
                    cancellationToken);
            }
        }

        var sourceLines = await operationsDbContext.OperationLines
            .AsNoTracking()
            .Where(line => sourceLineIds.Contains(line.Id))
            .ToDictionaryAsync(line => line.Id, cancellationToken);
        var postedAllocationTotals = await (
                from allocation in operationsDbContext.OperationLineSourceAllocations.AsNoTracking()
                join targetLine in operationsDbContext.OperationLines.AsNoTracking() on allocation.TargetOperationLineId equals targetLine.Id
                join targetOperation in operationsDbContext.OperationLogs.AsNoTracking() on targetLine.OperationId equals targetOperation.Id
                where sourceLineIds.Contains(allocation.SourceOperationLineId) &&
                      targetOperation.Id != operation.Id &&
                      !targetOperation.IsDeleted &&
                      (targetOperation.Status == Confirmed || targetOperation.Status == Completed || targetOperation.Status == Received)
                group allocation by allocation.SourceOperationLineId into groupBySource
                select new { SourceOperationLineId = groupBySource.Key, Quantity = groupBySource.Sum(value => value.Quantity) })
            .ToDictionaryAsync(value => value.SourceOperationLineId, value => value.Quantity, cancellationToken);

        var currentTotals = proposed.GroupBy(value => value.SourceOperationLineId)
            .ToDictionary(group => group.Key, group => group.Sum(value => value.Quantity));

        if (operation.OperationType == Return)
        {
            // The source allocation is only the ordinary, source-backed part
            // of a physical return. If that ordinary quantity exceeds the
            // remaining sold quantity, cap the allocation and preserve the
            // received overage on the return line as explicit exception qty.
            var acceptedByTargetLine = new Dictionary<Guid, int>();
            var sourceCapWarnings = new List<MerchantSalesVarianceWarning>();
            foreach (var sourceLineId in sourceLineIds)
            {
                if (!sourceLines.TryGetValue(sourceLineId, out var sourceLine))
                    throw new SourceAllocationCapException("Return source allocation does not identify an original sale line.", []);

                var alreadyAllocated = postedAllocationTotals.GetValueOrDefault(sourceLineId);
                var requestedForSource = proposed.Where(value => value.SourceOperationLineId == sourceLineId).Sum(value => value.Quantity);
                var available = Math.Max(0, sourceLine.Quantity - alreadyAllocated);
                if (requestedForSource > available)
                {
                    var targetLine = operation.OperationLines.First(line => proposed.Any(value => value.SourceOperationLineId == sourceLineId && value.TargetOperationLineId == line.Id));
                    sourceCapWarnings.Add(new MerchantSalesVarianceWarning(
                        targetLine.SkuId,
                        targetLine.SkuCodeSnapshot,
                        targetLine.ProductNameSnapshot,
                        targetLine.LotNumber,
                        targetLine.ExpiryDate,
                        requestedForSource,
                        sourceLine.Quantity,
                        alreadyAllocated,
                        requestedForSource - available,
                        "This linked return exceeds the remaining quantity on its original sale line."));
                }
                foreach (var allocation in proposed.Where(value => value.SourceOperationLineId == sourceLineId)
                             .OrderBy(value => value.TargetOperationLineId).ThenBy(value => value.Id))
                {
                    var accepted = Math.Min(available, allocation.Quantity);
                    available -= accepted;
                    acceptedByTargetLine[allocation.TargetOperationLineId] = acceptedByTargetLine.GetValueOrDefault(allocation.TargetOperationLineId) + accepted;
                    if (accepted == 0)
                    {
                        operationsDbContext.OperationLineSourceAllocations.Remove(allocation);
                    }
                    else
                    {
                        allocation.Quantity = accepted;
                    }
                }
            }

            if (sourceCapWarnings.Count > 0 && !allowReturnException)
                throw new MerchantSalesVarianceException(sourceCapWarnings);

            foreach (var line in operation.OperationLines)
            {
                var normalQuantity = Math.Clamp(acceptedByTargetLine.GetValueOrDefault(line.Id), 0, line.Quantity);
                line.NormalQuantity = normalQuantity;
                line.ExceptionQuantity = line.Quantity - normalQuantity;
            }
            await operationsDbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var violations = new List<SourceAllocationCapViolation>();
        foreach (var sourceLineId in sourceLineIds)
        {
            if (!sourceLines.TryGetValue(sourceLineId, out var sourceLine))
            {
                violations.Add(new SourceAllocationCapViolation(sourceLineId, 0, 0, currentTotals[sourceLineId]));
                continue;
            }

            var alreadyAllocated = postedAllocationTotals.GetValueOrDefault(sourceLineId);
            var requested = currentTotals[sourceLineId];
            if (alreadyAllocated + requested > sourceLine.Quantity)
            {
                violations.Add(new SourceAllocationCapViolation(sourceLineId, sourceLine.Quantity, alreadyAllocated, requested));
            }
        }

        if (violations.Count > 0)
        {
            throw new SourceAllocationCapException("Returned and exchanged quantity cannot exceed the original sold source line.", violations);
        }
    }

    private static async Task CreateReturnEntitlementAsync(
        OperationLog operation,
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // The unique return_operation_id index is the durable last line of
        // defence. This pre-check also keeps ordinary confirmation retries
        // idempotent before the database has to reject a duplicate.
        if (await paymentsDbContext.ReturnEntitlements.AnyAsync(value => value.ReturnOperationId == operation.Id, cancellationToken))
            return;

        var originalSale = operation.ReturnReferenceOperationId is { } referenceId
            ? await operationsDbContext.OperationLogs.AsNoTracking()
                .SingleOrDefaultAsync(value => value.Id == referenceId && !value.IsDeleted, cancellationToken)
            : null;
        var recordedReturnValue = operation.OperationLines.Sum(line => line.LineTotal) + operation.ReturnExceptionValue;
        var isFailedDelivery = string.Equals(originalSale?.Status, FailedDelivery, StringComparison.Ordinal);
        var confirmedCollection = 0m;
        if (isFailedDelivery && originalSale is not null)
        {
            // Failed delivery is a logistics result, not proof that money was
            // collected.  In particular, an operator-verified gateway field
            // must never manufacture a refund claim by itself.
            confirmedCollection = await FailedDeliveryReturnFinanceRepairService.GetConfirmedCollectionAsync(
                paymentsDbContext, originalSale.Id, cancellationToken);
            if (confirmedCollection <= 0m)
            {
                return;
            }
        }
        var paymentTrack = originalSale is null
            ? null
            : isFailedDelivery
                ? !string.IsNullOrWhiteSpace(originalSale.ExternalPaymentProvider) && !string.IsNullOrWhiteSpace(originalSale.ExternalPaymentReference)
                    ? FailedDeliveryReturnFinanceRepairService.GatewayRefundReviewPaymentTrack
                    : FailedDeliveryReturnFinanceRepairService.RefundReviewPaymentTrack
            : string.Equals(originalSale.PaymentStatus, "DueOnDelivery", StringComparison.OrdinalIgnoreCase)
                ? "COD"
                : originalSale.ExternalPaymentVerified && !string.IsNullOrWhiteSpace(originalSale.ExternalPaymentReference)
                    ? "GatewayOrWallet"
                    : originalSale.ClientId.HasValue &&
                      (IsMerchantAccountMethod(originalSale.PaymentMethod) || string.IsNullOrWhiteSpace(originalSale.PaymentMethod))
                        ? "MerchantAccount"
                        : !string.IsNullOrWhiteSpace(originalSale.PaymentMethod) ? "Retail" : null;
        var originalPaymentReference = originalSale is { ExternalPaymentVerified: true }
            ? TrimToNull(originalSale.ExternalPaymentReference)
            : null;

        paymentsDbContext.ReturnEntitlements.Add(new ReturnEntitlement
        {
            Id = Guid.NewGuid(),
            ReturnOperationId = operation.Id,
            OriginalSaleOperationId = originalSale?.Id,
            MerchantId = operation.ClientId,
            BeneficiaryKind = operation.ClientId.HasValue ? "Merchant" : "Anonymous",
            BuyerName = operation.ClientName,
            PaymentTrack = paymentTrack,
            OriginalPaymentReference = originalPaymentReference,
            // A collected failed delivery remains reviewable, but only up to
            // the money actually confirmed by Payments. Its physical return
            // value remains recorded on the Operations document.
            NormalAmount = isFailedDelivery ? Math.Min(recordedReturnValue, confirmedCollection) : operation.OperationLines.Sum(line => line.LineTotal),
            ExceptionAmount = isFailedDelivery ? 0m : operation.ReturnExceptionValue,
            ReservedAmount = 0m,
            SettledAmount = 0m,
            Status = !isFailedDelivery && operation.ReturnExceptionValue > 0m ? "PendingValuation" : "Held",
            IdempotencyKey = $"return-confirm:{operation.Id:N}",
            CreatedBy = userId,
            CreatedAt = now,
            Notes = isFailedDelivery
                ? AppendReturnFinanceNote(operation.ReturnExceptionReason, $"Failed-delivery refund review. Confirmed collection cap: {confirmedCollection:0.####}.")
                : operation.ReturnExceptionReason
        });
    }

    private static string AppendReturnFinanceNote(string? existing, string note) =>
        string.IsNullOrWhiteSpace(existing) ? note : $"{existing.Trim()}\n{note}";

    private static Task PopulateReturnValueSplitAsync(
        OperationLog operation,
        OperationsDbContext operationsDbContext,
        IReturnWorkflowPersistenceService returnPersistence,
        CancellationToken cancellationToken)
    {
        foreach (var line in operation.OperationLines)
        {
            // The entered value is authoritative for both linked and unlinked
            // returns. Source-sale price is lineage evidence, never a hidden
            // valuation override.
            line.NormalValue = Math.Max(0, line.NormalValue);
            // ExceptionValue is supplied deliberately in the draft and may be
            // zero for a physical-only exception; it is never inferred.
            line.ExceptionValue = Math.Max(0, line.ExceptionValue);
        }
        return returnPersistence.SaveAsync(operationsDbContext, cancellationToken);
    }

    private static async Task<Dictionary<Guid, OperationLineSourceAllocation>> LoadSourceAllocationByTargetLineAsync(
        OperationLog operation,
        OperationsDbContext operationsDbContext,
        CancellationToken cancellationToken)
    {
        var lineIds = operation.OperationLines.Select(line => line.Id).ToArray();
        return await operationsDbContext.OperationLineSourceAllocations
            .Where(value => lineIds.Contains(value.TargetOperationLineId))
            .ToDictionaryAsync(value => value.TargetOperationLineId, cancellationToken);
    }

    private static bool IsOperationsAdministrator(ICurrentUser currentUser) =>
        LenseeRoles.Normalize(currentUser.Role) is LenseeRoles.Admin or LenseeRoles.ERPAdmin;

    private static async Task<Dictionary<Guid, Location>> LoadLocationLookupAsync(InventoryDbContext dbContext, IReadOnlyCollection<OperationLog> operations, CancellationToken cancellationToken)
    {
        var ids = operations.SelectMany(operation => new[] { operation.SourceLocationId, operation.DestinationLocationId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await dbContext.Locations.Where(location => ids.Contains(location.Id)).ToDictionaryAsync(location => location.Id, cancellationToken);
    }

    private static async Task<Dictionary<Guid, User>> LoadUserLookupAsync(IdentityDbContext dbContext, IReadOnlyCollection<OperationLog> operations, CancellationToken cancellationToken)
    {
        var ids = operations
            .SelectMany(operation => new Guid?[] { operation.CreatedBy, operation.ConfirmedBy }
                .Concat(operation.OperationVersions.Select(version => (Guid?)version.EditedBy)))
            .Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        return await dbContext.Users
            .Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, cancellationToken);
    }

    private static string? GetUserDisplayName(Guid? userId, IReadOnlyDictionary<Guid, User> userLookup)
    {
        if (!userId.HasValue || userId.Value == Guid.Empty)
        {
            return null;
        }

        return userLookup.TryGetValue(userId.Value, out var user)
            ? (string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName)
            : userId.Value.ToString();
    }

    private static string? GetActorDisplayName(string? actorName, Guid? userId, IReadOnlyDictionary<Guid, User> userLookup) =>
        !string.IsNullOrWhiteSpace(actorName) ? actorName : GetUserDisplayName(userId, userLookup);

    private static void AddLines(OperationLog operation, IReadOnlyDictionary<Guid, Sku> skusById, IReadOnlyList<OperationLineRequest> lines, Merchant? merchant, Representative? representative)
    {
        foreach (var line in lines)
        {
            var sku = skusById[line.SkuId];
            var requestedEntryMode = NormalizeEntryMode(line.EntryMode);
            var requestedQuantity = GetLineQuantity(operation.OperationType, line);
            var convertSealedPieceMultiple = requestedEntryMode == "Pieces" &&
                (operation.OperationType is WholesaleSale or Return) &&
                string.Equals(sku.Product.SellMode, "SealedPackOnly", StringComparison.OrdinalIgnoreCase) &&
                sku.Product.PiecesPerPack is > 0 &&
                requestedQuantity % sku.Product.PiecesPerPack.Value == 0;
            var entryMode = convertSealedPieceMultiple ? "Packs" : requestedEntryMode;
            var quantity = convertSealedPieceMultiple ? requestedQuantity / sku.Product.PiecesPerPack!.Value : requestedQuantity;
            var isSale = operation.OperationType is WholesaleSale or RetailSale;
            var lineType = isSale ? ResolveLineType(line) : null;
            var isBonus = lineType == "Bonus";
            var unitPrice = isBonus ? 0m : (line.UnitPrice ?? 0m) * (convertSealedPieceMultiple && isSale ? sku.Product.PiecesPerPack!.Value : 1);
            var section = NormalizeLineSection(operation.OperationType, line.Section);
            var normalQuantity = operation.OperationType == Return
                ? Math.Clamp((line.NormalQuantity ?? requestedQuantity) / (convertSealedPieceMultiple ? sku.Product.PiecesPerPack!.Value : 1), 0, quantity)
                : quantity;
            var exceptionQuantity = operation.OperationType == Return ? quantity - normalQuantity : 0;
            var normalValue = operation.OperationType == Return ? Math.Max(0, line.NormalValue ?? 0) : unitPrice * quantity;
            var exceptionValue = operation.OperationType == Return ? Math.Max(0, line.ExceptionValue ?? 0) : 0;
            operation.OperationLines.Add(new OperationLine
            {
                Id = Guid.NewGuid(),
                OperationId = operation.Id,
                SkuId = line.SkuId,
                ProductNameSnapshot = sku.Product.Name,
                SkuCodeSnapshot = sku.SkuCode,
                MerchantNameSnapshot = merchant?.BusinessName,
                RepresentativeNameSnapshot = representative?.Name,
                Section = section,
                Quantity = quantity,
                EntryMode = entryMode,
                PiecesPerPackSnapshot = sku.Product.PiecesPerPack,
                BonusQuantity = isBonus ? quantity : 0,
                UnitPrice = unitPrice,
                LineTotal = unitPrice * quantity,
                PricingMode = isSale ? lineType switch { "Paid" => "FullPrice", "Bonus" => "PromotionFree", "Offer" => "Promotion50", _ => null } : null,
                LineType = lineType,
                NormalQuantity = normalQuantity,
                ExceptionQuantity = exceptionQuantity,
                NormalValue = normalValue,
                ExceptionValue = exceptionValue,
                ReturnDisposition = operation.OperationType == Return ? NormalizeReturnDisposition(line.ReturnDisposition) : null,
                LotNumber = TrimToNull(line.LotNumber),
                ExpiryDate = line.ExpiryDate,
                LineNotes = TrimToNull(line.Notes)
            });
        }
    }

    private static void AddSourceAllocations(OperationsDbContext dbContext, OperationLog operation, IReadOnlyList<OperationLineRequest> requests, DateTime now)
    {
        foreach (var pair in operation.OperationLines.Zip(requests))
        {
            var line = pair.First;
            var request = pair.Second;
            // New returns deliberately have only a header-level reference.
            // Preserve source allocations solely for the historic Change flow.
            if (!(operation.OperationType == Change && line.Section == ChangeOut)) continue;
            if (request.SourceOperationId is not { } sourceOperationId || request.SourceOperationLineId is not { } sourceLineId) continue;
            if (line.NormalQuantity <= 0) continue;
            dbContext.OperationLineSourceAllocations.Add(new OperationLineSourceAllocation
            {
                Id = Guid.NewGuid(),
                TargetOperationLineId = line.Id,
                SourceOperationId = sourceOperationId,
                SourceOperationLineId = sourceLineId,
                SkuId = line.SkuId,
                SourceBatchId = request.SourceBatchId,
                SourceOpenedPieceLotId = request.SourceOpenedPieceLotId,
                EntryMode = line.EntryMode,
                LotNumber = line.LotNumber,
                ExpiryDate = line.ExpiryDate,
                Quantity = line.NormalQuantity,
                CreatedAt = now
            });
        }
    }

    private static int GetLineQuantity(string operationType, OperationLineRequest line) =>
        NormalizeEntryMode(line.EntryMode) == "Pieces"
            ? line.PieceQuantity ?? line.PackQuantity
            : line.PackQuantity;

    private static string NormalizeEntryMode(string? value) =>
        string.Equals(value, "Pieces", StringComparison.OrdinalIgnoreCase) ? "Pieces" : "Packs";

    private static string NormalizeSalesChannel(string? value) =>
        string.Equals(value, "Online", StringComparison.OrdinalIgnoreCase) ? "Online" : "Manual";

    private static string? NormalizePaymentStatus(string? value) =>
        value?.Trim() switch
        {
            "DueOnDelivery" => "DueOnDelivery",
            "PaidAwaitingReconciliation" => "PaidAwaitingReconciliation",
            "VerifiedPendingSettlement" => "VerifiedPendingSettlement",
            "Paid" => "Paid",
            _ => null
        };

    private static string ResolveLineType(OperationLineRequest line)
    {
        if (!string.IsNullOrWhiteSpace(line.LineType))
        {
            if (string.Equals(line.PricingMode, "Promotion50", StringComparison.OrdinalIgnoreCase)) return "Invalid";
            return line.LineType.Trim() switch
            {
                "Paid" => "Paid",
                "Bonus" => "Bonus",
                "Offer" => "Offer",
                _ => "Invalid"
            };
        }

        if (string.Equals(line.PricingMode, "PromotionFree", StringComparison.OrdinalIgnoreCase) || line.IsBonus == true) return "Bonus";
        if (string.Equals(line.PricingMode, "Promotion50", StringComparison.OrdinalIgnoreCase)) return "Offer";
        return "Paid";
    }

    private static string ResolveLegacyLineType(string? pricingMode, int bonusQuantity) =>
        string.Equals(pricingMode, "PromotionFree", StringComparison.OrdinalIgnoreCase) || bonusQuantity > 0 ? "Bonus" :
        string.Equals(pricingMode, "Promotion50", StringComparison.OrdinalIgnoreCase) ? "Offer" : "Paid";

    private static string NormalizeLineSection(string operationType, string? value)
    {
        if (operationType == Change)
        {
            return string.Equals(value, ChangeIn, StringComparison.OrdinalIgnoreCase) ? ChangeIn : ChangeOut;
        }

        return Standard;
    }

    private static (Guid SkuId, string Section, string LineType, string EntryMode, string? LotNumber, DateOnly? ExpiryDate) GetLineUniquenessKey(string operationType, OperationLineRequest line) =>
        (
            line.SkuId,
            NormalizeLineSection(operationType, line.Section),
            operationType is WholesaleSale or RetailSale ? ResolveLineType(line) : "Standard",
            NormalizeEntryMode(line.EntryMode),
            NormalizeBlank(line.LotNumber),
            line.ExpiryDate);

    private static async Task ReplaceOperationLinesAsync(
        OperationsDbContext dbContext,
        OperationLog operation,
        CancellationToken cancellationToken)
    {
        var trackedLines = operation.OperationLines.ToList();

        if (dbContext.Database.IsRelational())
        {
            await dbContext.OperationLineSourceAllocations
                .Where(allocation => dbContext.OperationLines.Where(line => line.OperationId == operation.Id).Select(line => line.Id).Contains(allocation.TargetOperationLineId))
                .ExecuteDeleteAsync(cancellationToken);
            await dbContext.OperationLines
                .Where(line => line.OperationId == operation.Id)
                .ExecuteDeleteAsync(cancellationToken);

            foreach (var entry in dbContext.ChangeTracker.Entries<OperationLine>()
                .Where(entry => entry.Entity.OperationId == operation.Id)
                .ToList())
            {
                entry.State = EntityState.Detached;
            }
        }
        else
        {
            var existingLines = await dbContext.OperationLines
                .Where(line => line.OperationId == operation.Id)
                .ToListAsync(cancellationToken);
            var allocationIds = existingLines.Select(line => line.Id).ToArray();
            var existingAllocations = await dbContext.OperationLineSourceAllocations.Where(value => allocationIds.Contains(value.TargetOperationLineId)).ToListAsync(cancellationToken);
            dbContext.OperationLineSourceAllocations.RemoveRange(existingAllocations);
            dbContext.OperationLines.RemoveRange(existingLines);
            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var line in trackedLines.Concat(existingLines).DistinctBy(line => line.Id))
            {
                dbContext.Entry(line).State = EntityState.Detached;
            }
        }

        operation.OperationLines = new List<OperationLine>();
    }

    private static async Task SaveChangesIgnoringStaleDeletedOperationLinesAsync(
        OperationsDbContext dbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception) when (IsOnlyStaleReplacedOperationLines(exception))
        {
            foreach (var entry in exception.Entries)
            {
                entry.State = EntityState.Detached;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool IsOnlyStaleReplacedOperationLines(DbUpdateConcurrencyException exception) =>
        exception.Entries.Count > 0 &&
        exception.Entries.All(entry => entry.Entity is OperationLine && entry.State != EntityState.Added);

    private static Task AddVersionAsync(OperationsDbContext dbContext, OperationLog operation, string reason, Guid userId, OperationSnapshot snapshot, DateTime now, CancellationToken cancellationToken)
    {
        var version = new OperationVersion
        {
            Id = Guid.NewGuid(),
            OperationId = operation.Id,
            VersionNumber = operation.OperationVersions.Count == 0
                ? 1
                : operation.OperationVersions.Max(value => value.VersionNumber) + 1,
            SnapshotData = JsonSerializer.Serialize(snapshot, JsonOptions),
            Reason = reason,
            EditedBy = userId,
            EditedAt = now
        };
        operation.OperationVersions.Add(version);
        dbContext.OperationVersions.Add(version);
        operation.CurrentVersionId = version.Id;
        return Task.CompletedTask;
    }

    private static OperationSnapshot CreateSnapshot(OperationLog operation, IReadOnlyList<TransferAllocationSnapshot>? allocations = null) =>
        new(
            operation.OperationType,
            operation.Status,
            operation.SourceLocationId,
            operation.DestinationLocationId,
            operation.ClientId,
            operation.ClientName,
            operation.RepresentativeId,
            operation.ExternalReference,
            operation.PaymentMethod,
            operation.Notes,
            operation.OperationLines
                .Select(line => new OperationLineSnapshot(line.SkuId, line.SkuCodeSnapshot, line.ProductNameSnapshot, line.Section, line.Quantity, line.EntryMode, line.BonusQuantity, line.UnitPrice, line.LineTotal, line.LotNumber, line.ExpiryDate, line.LineNotes, line.ShopifyLineItemId, line.ShopifyVariantId, line.ShopifySkuSnapshot, line.ShopifyTitleSnapshot, line.ShopifyVariantTitleSnapshot, line.ShopifyPropertiesSnapshot, line.ReturnDisposition, line.PricingMode, line.LineType))
                .ToList(),
            allocations ?? []);

    private static string BuildRevisionFingerprint(OperationLog operation) =>
        JsonSerializer.Serialize(new
        {
            operation.OperationType,
            operation.SourceLocationId,
            operation.DestinationLocationId,
            ClientId = operation.ClientId,
            operation.ExternalReference,
            BuyerName = operation.ClientId.HasValue ? null : TrimToNull(operation.ClientName),
            operation.RepresentativeId,
            PaymentMethod = NormalizePaymentMethod(operation.PaymentMethod),
            BuyerPhone = TrimToNull(operation.BuyerPhone),
            Notes = TrimToNull(operation.Notes),
            Receipt = operation.InventoryReceiptHeader is null ? null : new
            {
                SupplierName = TrimToNull(operation.InventoryReceiptHeader.SupplierName) ?? "Supplier",
                InvoiceNumber = TrimToNull(operation.InventoryReceiptHeader.InvoiceNumber)
            },
            Lines = operation.OperationLines.Select(line => new
            {
                line.SkuId,
                Section = NormalizeLineSection(operation.OperationType, line.Section),
                EntryMode = NormalizeEntryMode(line.EntryMode),
                Quantity = line.Quantity,
                BonusQuantity = line.BonusQuantity,
                LineType = line.LineType ?? ResolveLegacyLineType(line.PricingMode, line.BonusQuantity),
                UnitPrice = line.UnitPrice,
                LotNumber = NormalizeBlank(line.LotNumber),
                line.ExpiryDate,
                Notes = TrimToNull(line.LineNotes)
            }).OrderBy(line => line.SkuId).ThenBy(line => line.Section).ThenBy(line => line.EntryMode).ThenBy(line => line.Quantity).ThenBy(line => line.LotNumber).ToArray()
        }, JsonOptions);

    private static string BuildRevisionFingerprint(OperationRequest request) =>
        JsonSerializer.Serialize(new
        {
            OperationType = NormalizeOperationType(request.OperationType),
            request.SourceLocationId,
            request.DestinationLocationId,
            ClientId = request.MerchantId,
            ExternalReference = NormalizeOperationReference(request.ExternalReference),
            BuyerName = request.MerchantId.HasValue ? null : TrimToNull(request.BuyerName),
            request.RepresentativeId,
            PaymentMethod = NormalizePaymentMethod(request.PaymentMethod),
            BuyerPhone = TrimToNull(request.BuyerPhone),
            Notes = TrimToNull(request.Notes),
            Receipt = request.Receipt is null ? null : new
            {
                SupplierName = TrimToNull(request.Receipt.SupplierName) ?? "Supplier",
                InvoiceNumber = TrimToNull(request.Receipt.InvoiceNumber)
            },
            Lines = request.Lines.Select(line =>
            {
                var entryMode = NormalizeEntryMode(line.EntryMode);
                var quantity = GetLineQuantity(NormalizeOperationType(request.OperationType), line);
                var isSale = NormalizeOperationType(request.OperationType) is WholesaleSale or RetailSale;
                var lineType = isSale ? ResolveLineType(line) : null;
                var isBonus = lineType == "Bonus";
                return new
                {
                    line.SkuId,
                    Section = NormalizeLineSection(NormalizeOperationType(request.OperationType), line.Section),
                    EntryMode = entryMode,
                    Quantity = quantity,
                    BonusQuantity = isBonus ? quantity : 0,
                    UnitPrice = isBonus ? 0 : line.UnitPrice ?? 0,
                    LineType = lineType,
                    LotNumber = NormalizeBlank(line.LotNumber),
                    line.ExpiryDate,
                    Notes = TrimToNull(line.Notes)
                };
            }).OrderBy(line => line.SkuId).ThenBy(line => line.Section).ThenBy(line => line.EntryMode).ThenBy(line => line.Quantity).ThenBy(line => line.LotNumber).ToArray()
        }, JsonOptions);

    private static Dictionary<Guid, List<BatchAllocation>> BuildAllocationLookupBySku(IReadOnlyList<TransferAllocationSnapshot> allocations) =>
        allocations
            .GroupBy(value => value.SkuId)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(value => value.Allocations).ToList());

    private static IReadOnlyList<TransferAllocationSnapshot> ReadTransferAllocations(OperationLog operation)
    {
        var snapshot = operation.OperationVersions
            .OrderByDescending(version => version.VersionNumber)
            .Select(version =>
            {
                try
                {
                    return JsonSerializer.Deserialize<OperationSnapshot>(version.SnapshotData, JsonOptions);
                }
                catch
                {
                    return null;
                }
            })
            .FirstOrDefault(value => value?.TransferAllocations?.Count > 0);

        return snapshot?.TransferAllocations ?? [];
    }

    private static async Task CommitTransferOutAsync(
        OperationLog operation,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        OperationsDbContext operationsDbContext,
        StockLedgerService ledgerService,
        Guid userId,
        string transactionType,
        CancellationToken cancellationToken)
    {
        var allocationLookup = BuildAllocationLookupBySku(allocations);
        foreach (var line in operation.OperationLines.Where(line => line.EntryMode == "Packs").GroupBy(line => line.SkuId).Select(group => new { SkuId = group.Key }))
        {
            if (!allocationLookup.TryGetValue(line.SkuId, out var lineAllocations))
            {
                throw new InvalidOperationException("Transfer allocation snapshot is missing.");
            }

            await ledgerService.CommitReservedInWarehouseOutAsync(
                operation.SourceLocationId!.Value,
                line.SkuId,
                lineAllocations,
                userId,
                operation.Id,
                transactionType,
                cancellationToken);
        }

        foreach (var line in operation.OperationLines.Where(line => line.EntryMode == "Pieces"))
        {
            if (line.PiecesPerPackSnapshot is not > 0)
            {
                throw new InvalidOperationException($"{line.SkuCodeSnapshot} requires a valid pieces-per-pack snapshot.");
            }

            var pieceAllocations = await ledgerService.IssuePiecesFromSelectionAsync(
                operation.SourceLocationId!.Value,
                line.SkuId,
                line.Quantity,
                line.PiecesPerPackSnapshot.Value,
                line.LotNumber,
                line.ExpiryDate,
                userId,
                operation.Id,
                cancellationToken,
                transactionType);
            PersistPieceAllocations(operationsDbContext, line, pieceAllocations, line.PiecesPerPackSnapshot.Value, DateTime.UtcNow);
        }
    }

    private static async Task ShipSaleOutAsync(
        OperationLog operation,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        StockLedgerService ledgerService,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (var allocation in allocations)
        {
            await ledgerService.CommitReservedInWarehouseOutAsync(
                operation.SourceLocationId!.Value,
                allocation.SkuId,
                allocation.Allocations,
                userId,
                operation.Id,
                InventoryTransactionTypes.Sale,
                cancellationToken);
        }

        var pieceLines = operation.OperationLines.Where(line => line.EntryMode == "Pieces").ToList();
        if (pieceLines.Count == 0)
        {
            return;
        }

        var sellModes = await catalogDbContext.Skus
            .Include(sku => sku.Product)
            .Where(sku => pieceLines.Select(line => line.SkuId).Contains(sku.Id))
            .ToDictionaryAsync(sku => sku.Id, sku => sku.Product.SellMode, cancellationToken);
        foreach (var line in pieceLines)
        {
            if (line.PiecesPerPackSnapshot is not > 0)
            {
                throw new InvalidOperationException("SKU pieces per pack is required for piece sales.");
            }
            if (line.ExpiryDate is null)
            {
                throw new InvalidOperationException($"{line.SkuCodeSnapshot} requires a selected batch expiry.");
            }

            var pieceAllocations = await ledgerService.IssuePiecesFromSelectionAsync(
                operation.SourceLocationId!.Value,
                line.SkuId,
                line.Quantity,
                line.PiecesPerPackSnapshot.Value,
                line.LotNumber,
                line.ExpiryDate,
                userId,
                operation.Id,
                cancellationToken,
                allowOpeningSelectedPacks: !string.Equals(sellModes.GetValueOrDefault(line.SkuId), "SealedPackOnly", StringComparison.OrdinalIgnoreCase));
            PersistPieceAllocations(operationsDbContext, line, pieceAllocations, line.PiecesPerPackSnapshot.Value, now);
        }
    }

    private static void PersistPieceAllocations(
        OperationsDbContext operationsDbContext,
        OperationLine line,
        IReadOnlyList<PieceAllocation> allocations,
        int piecesPerPackSnapshot,
        DateTime now)
    {
        if (allocations.Count == 0 || allocations.Sum(allocation => allocation.Quantity) != line.Quantity)
            throw new InvalidOperationException("Piece issue lineage does not match the operation line quantity.");

        foreach (var allocation in allocations)
        {
            if (allocation.PieceIssueTransactionId is not { } pieceTransactionId)
                throw new InvalidOperationException("Piece issue transaction evidence is required.");
            operationsDbContext.OperationLinePieceAllocations.Add(new OperationLinePieceAllocation
            {
                Id = Guid.NewGuid(),
                OperationLineId = line.Id,
                OpenedPieceLotId = allocation.OpenedPieceLotId,
                SourceBatchId = allocation.SourceBatchId,
                IssuedPieceQuantity = allocation.Quantity,
                PiecesPerPackSnapshot = piecesPerPackSnapshot,
                PieceIssueTransactionId = pieceTransactionId,
                PackOpenTransactionId = allocation.PackOpenTransactionId,
                LotNumber = allocation.LotNumber,
                ExpiryDate = allocation.BatchExpiryDate,
                CreatedAt = now
            });
        }
    }

    private static async Task ShipRepresentativeReserveAsync(
        OperationLog operation,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        StockLedgerService ledgerService,
        Guid userId,
        CancellationToken cancellationToken)
    {
        foreach (var allocation in allocations)
        {
            await ledgerService.MoveReservedInWarehouseToRepresentativeAsync(
                operation.SourceLocationId!.Value,
                allocation.SkuId,
                allocation.Allocations,
                userId,
                operation.Id,
                cancellationToken);
        }
    }

    private static IEnumerable<OperationLine> LinesRequiringAllocation(OperationLog operation) =>
        operation.OperationType switch
        {
            WarehouseTransfer or Reserve => operation.OperationLines.Where(line => line.EntryMode == "Packs"),
            WholesaleSale or RetailSale => operation.OperationLines.Where(line => line.EntryMode == "Packs"),
            _ => []
        };

    private static bool IsShopifyAllocationOnlyUpdate(OperationLog operation, OperationRequest request)
    {
        if (NormalizeOperationType(request.OperationType) != operation.OperationType ||
            request.SourceLocationId != operation.SourceLocationId ||
            request.DestinationLocationId != operation.DestinationLocationId ||
            request.MerchantId != operation.ClientId ||
            request.RepresentativeId != operation.RepresentativeId ||
            NormalizeStoredPaymentMethod(request.PaymentMethod) != operation.PaymentMethod ||
            TrimToNull(request.BuyerName) != operation.ClientName ||
            TrimToNull(request.BuyerPhone) != operation.BuyerPhone ||
            TrimToNull(request.Notes) != operation.Notes ||
            request.Lines.Count != operation.OperationLines.Count)
        {
            return false;
        }

        var existing = operation.OperationLines
            .GroupBy(line => (line.SkuId, line.Section, IsBonus: line.BonusQuantity > 0, line.EntryMode, line.Quantity, line.UnitPrice))
            .ToDictionary(group => group.Key, group => group.Count());
        var requested = request.Lines
            .GroupBy(line => (line.SkuId, Section: NormalizeLineSection(operation.OperationType, line.Section), IsBonus: line.IsBonus == true, EntryMode: NormalizeEntryMode(line.EntryMode), Quantity: GetLineQuantity(operation.OperationType, line), UnitPrice: line.IsBonus == true ? 0m : line.UnitPrice ?? 0m))
            .ToDictionary(group => group.Key, group => group.Count());
        return existing.Count == requested.Count && existing.All(item => requested.TryGetValue(item.Key, out var count) && count == item.Value);
    }

    private static bool IsAllocationPending(OperationLog operation) =>
        operation.SalesChannel == "Shopify" && operation.OperationLines.Any(line => line.ExpiryDate is null);

    private static OperationListResponse ToListResponse(OperationLog operation, IReadOnlyDictionary<Guid, Location> locationLookup, IReadOnlyDictionary<Guid, User> userLookup, ICurrentUser currentUser, bool? allocationPendingOverride = null) =>
        new(
            operation.Id,
            operation.OperationNumber,
            operation.ExternalReference,
            operation.OperationType,
            operation.Status,
            operation.SourceLocationId,
            GetLocationName(operation.SourceLocationId, locationLookup),
            operation.DestinationLocationId,
            GetLocationName(operation.DestinationLocationId, locationLookup),
            operation.ClientId,
            operation.ClientName,
            operation.RepresentativeId,
            operation.PaymentMethod,
            operation.CreatedAt,
            operation.ConfirmedAt,
            GetActorDisplayName(operation.CreatedActorName, operation.CreatedBy, userLookup),
            GetUserDisplayName(operation.ConfirmedBy, userLookup),
            operation.OperationVersions.OrderByDescending(version => version.VersionNumber).Select(version => GetActorDisplayName(version.EditedActorName, version.EditedBy, userLookup)).FirstOrDefault(),
            operation.Status == Draft,
            false,
            null,
            operation.SalesChannel,
            operation.BuyerPhone,
            operation.BuyerEmail,
            operation.ShippingAddress,
            operation.ShopifyOrderLink?.ShopifyOrderId,
            operation.ShopifyOrderLink?.ShopifyOrderNumber,
            allocationPendingOverride ?? IsAllocationPending(operation),
            operation.CorrectionReason,
            operation.FinancialClosureStatus,
            BuildWorkflowProjection(currentUser, operation));

    private static OperationDetailResponse ToDetailResponse(
        OperationLog operation,
        IReadOnlyDictionary<Guid, Location> locationLookup,
        IReadOnlyDictionary<Guid, User> userLookup,
        IReadOnlyDictionary<Guid, WearCycleInfo> wearCycles,
        ICurrentUser currentUser,
        bool includeCollections = true,
        bool? allocationPendingOverride = null,
        string? returnFinancialOutcome = null)
    {
        var lines = includeCollections ? operation.OperationLines
            .Select(line =>
            {
                wearCycles.TryGetValue(line.SkuId, out var wearCycle);
                return ToLineResponse(line, wearCycle);
            })
            .ToList() : [];
        var allocations = includeCollections ? ReadTransferAllocations(operation)
            .SelectMany(allocation => allocation.Allocations.Select(batch => new OperationAllocationResponse(
                allocation.SkuId,
                operation.OperationLines.FirstOrDefault(line => line.SkuId == allocation.SkuId)?.SkuCodeSnapshot,
                operation.OperationLines.FirstOrDefault(line => line.SkuId == allocation.SkuId)?.ProductNameSnapshot,
                batch.BatchId,
                batch.Quantity,
                batch.LotNumber,
                batch.ExpiryDate)))
            .ToList() : [];
        var versions = includeCollections ? BuildVersionResponses(operation, userLookup) : [];
        var approvalLines = operation.ReturnApprovalLines.Select(line => new ReturnApprovalLineResponse(line.Id, line.OperationLineId, line.SkuId, line.RequestedQuantity, line.EligibleQuantity, line.BatchName, line.ExpiryDate, line.MismatchType, line.Detail)).ToList();

        return new(
            operation.Id,
            operation.OperationNumber,
            operation.ExternalReference,
            operation.OperationType,
            operation.Status,
            operation.SourceLocationId,
            GetLocationName(operation.SourceLocationId, locationLookup),
            operation.DestinationLocationId,
            GetLocationName(operation.DestinationLocationId, locationLookup),
            operation.ClientId,
            operation.ClientName,
            operation.RepresentativeId,
            operation.OperationLines.Select(line => line.RepresentativeNameSnapshot).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            operation.PaymentMethod,
            operation.Notes,
            operation.CreatedAt,
            operation.ConfirmedAt,
            GetActorDisplayName(operation.CreatedActorName, operation.CreatedBy, userLookup),
            GetUserDisplayName(operation.ConfirmedBy, userLookup),
            operation.OperationVersions.OrderByDescending(version => version.VersionNumber).Select(version => GetActorDisplayName(version.EditedActorName, version.EditedBy, userLookup)).FirstOrDefault(),
            operation.CurrentVersion?.VersionNumber ?? operation.OperationVersions.OrderByDescending(version => version.VersionNumber).Select(version => (int?)version.VersionNumber).FirstOrDefault(),
            operation.ConcurrencyVersion,
            operation.Status == Draft,
            false,
            null,
            operation.InventoryReceiptHeader is null ? null : new ReceiptResponse(operation.InventoryReceiptHeader.SupplierName, operation.InventoryReceiptHeader.InvoiceNumber),
            lines,
            allocations,
            versions,
            operation.SalesChannel,
            operation.BuyerPhone,
            operation.BuyerEmail,
            operation.ShippingAddress,
            operation.ShopifyOrderLink?.ShopifyOrderId,
            operation.ShopifyOrderLink?.ShopifyOrderNumber,
            allocationPendingOverride ?? IsAllocationPending(operation),
            operation.FinancialClosureStatus,
            operation.DeliveryFee,
            operation.CodCharge,
            operation.PaymentStatus,
            operation.ExternalPaymentProvider,
            operation.ExternalPaymentReference,
            operation.ExternalPaymentVerified,
            operation.BuyerContactVerified,
            operation.ReturnExceptionReason,
            operation.ReturnVerbalAuthorizer,
            operation.ReturnNormalValue,
            operation.ReturnExceptionValue,
            operation.ReturnApprovalReason,
            operation.ReturnApprovedBy,
            operation.ReturnApprovedAt,
            operation.ReturnRejectedBy,
            operation.ReturnRejectedAt,
            approvalLines,
            BuildWorkflowProjection(currentUser, operation),
            operation.ReturnReferenceOperationId,
            returnFinancialOutcome);
    }

    private static async Task<string?> GetReturnFinancialOutcomeAsync(
        OperationLog operation,
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        CancellationToken cancellationToken)
    {
        if (operation.OperationType != Return || operation.Status != Confirmed || operation.ReturnReferenceOperationId is not { } sourceId)
            return null;
        var sourceStatus = await operationsDbContext.OperationLogs.AsNoTracking()
            .Where(value => value.Id == sourceId && !value.IsDeleted)
            .Select(value => value.Status)
            .SingleOrDefaultAsync(cancellationToken);
        if (!string.Equals(sourceStatus, FailedDelivery, StringComparison.Ordinal)) return null;
        return await paymentsDbContext.ReturnEntitlements.AsNoTracking()
            .AnyAsync(value => value.ReturnOperationId == operation.Id && value.Status != "Cancelled", cancellationToken)
            ? "RefundReviewRequired"
            : "NoRefundDue";
    }

    private static OperationLineResponse ToLineResponse(OperationLine line, WearCycleInfo? wearCycle)
    {
        var piecesPerPack = line.PiecesPerPackSnapshot;
        var packQuantity = string.Equals(line.EntryMode, "Pieces", StringComparison.OrdinalIgnoreCase) ? 0 : line.Quantity;
        var pieceQuantity = string.Equals(line.EntryMode, "Pieces", StringComparison.OrdinalIgnoreCase) ? line.Quantity : 0;
        var totalPieces = piecesPerPack is > 0
            ? checked(packQuantity * piecesPerPack.Value + pieceQuantity)
            : line.Quantity;
        var displayQuantity = piecesPerPack is > 0
            ? $"{totalPieces / piecesPerPack.Value} packs + {totalPieces % piecesPerPack.Value} pieces"
            : $"{line.Quantity} {line.EntryMode}";

        return new(line.Id, line.SkuId, line.SkuCodeSnapshot, line.ProductNameSnapshot, line.Section, line.Quantity, line.EntryMode,
            line.BonusQuantity, line.UnitPrice, line.LineTotal, line.LotNumber, line.ExpiryDate, line.MerchantNameSnapshot,
            line.RepresentativeNameSnapshot, line.LineNotes, wearCycle?.Cycle, wearCycle?.Duration, line.ShopifyLineItemId,
            line.ShopifyVariantId, line.ShopifySkuSnapshot, line.ShopifyTitleSnapshot, line.ShopifyVariantTitleSnapshot,
            line.ShopifyPropertiesSnapshot, line.ReturnDisposition, line.PricingMode, line.NormalQuantity, line.ExceptionQuantity,
            line.NormalValue, line.ExceptionValue, line.LineType ?? ResolveLegacyLineType(line.PricingMode, line.BonusQuantity),
            piecesPerPack, packQuantity, pieceQuantity, totalPieces, displayQuantity);
    }

    private static async Task<IReadOnlyDictionary<Guid, WearCycleInfo>> LoadWearCyclesBySkuAsync(
        CatalogDbContext catalogDbContext,
        IEnumerable<OperationLine> lines,
        CancellationToken cancellationToken)
    {
        var skuIds = lines.Select(line => line.SkuId).Distinct().ToArray();
        if (skuIds.Length == 0) return new Dictionary<Guid, WearCycleInfo>();

        var skus = await catalogDbContext.Skus.AsNoTracking()
            .Where(sku => skuIds.Contains(sku.Id))
            .Select(sku => new WearCycleLookup(sku.Id, sku.Product.ProductType, sku.Product.OpenedExpiryRate, sku.Product.OpenedExpiryDuration))
            .ToListAsync(cancellationToken);
        return skus.ToDictionary(sku => sku.SkuId, sku => ToWearCycleInfo(sku.ProductType, sku.OpenedExpiryRate, sku.OpenedExpiryDuration));
    }

    private static WearCycleInfo ToWearCycleInfo(string productType, string? openedExpiryRate, string? openedExpiryDuration)
    {
        if (!CatalogValidation.IsLensProduct(productType)) return new WearCycleInfo("NotApplicable", null);
        return CatalogValidation.HasValidOpenedExpiryRate(openedExpiryRate)
            ? new WearCycleInfo(openedExpiryRate, openedExpiryDuration)
            : new WearCycleInfo(null, null);
    }

    private static IReadOnlyList<OperationVersionResponse> BuildVersionResponses(
        OperationLog operation,
        IReadOnlyDictionary<Guid, User> userLookup)
    {
        var snapshots = operation.OperationVersions
            .OrderBy(version => version.VersionNumber)
            .Select(version => new
            {
                Version = version,
                Snapshot = SafeReadSnapshot(version.SnapshotData)
            })
            .ToList();

        var currentVersionId = operation.CurrentVersionId;
        var responses = new List<OperationVersionResponse>(snapshots.Count);
        for (var index = 0; index < snapshots.Count; index++)
        {
            var current = snapshots[index];
            var previous = index > 0 ? snapshots[index - 1].Snapshot : null;
            var snapshot = current.Snapshot;
            responses.Add(new OperationVersionResponse(
                current.Version.Id,
                current.Version.VersionNumber,
                current.Version.Reason,
                current.Version.EditedAt,
                GetActorDisplayName(current.Version.EditedActorName, current.Version.EditedBy, userLookup),
                currentVersionId == current.Version.Id,
                previous is not null && snapshot is not null && (previous.SourceLocationId != snapshot.SourceLocationId || previous.DestinationLocationId != snapshot.DestinationLocationId),
                previous is not null && snapshot is not null && (previous.ClientId != snapshot.ClientId || previous.ClientName != snapshot.ClientName || previous.RepresentativeId != snapshot.RepresentativeId),
                previous is not null && snapshot is not null && (previous.PaymentMethod != snapshot.PaymentMethod || !SameFinancialLines(previous.Lines, snapshot.Lines)),
                previous is not null && snapshot is not null && !SameLines(previous.Lines, snapshot.Lines)));
        }

        return responses;
    }

    private static OperationSnapshot? SafeReadSnapshot(string snapshotData)
    {
        try
        {
            return JsonSerializer.Deserialize<OperationSnapshot>(snapshotData, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static bool SameLines(IReadOnlyList<OperationLineSnapshot> left, IReadOnlyList<OperationLineSnapshot> right)
    {
        left ??= [];
        right ??= [];
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!Equals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameFinancialLines(IReadOnlyList<OperationLineSnapshot> left, IReadOnlyList<OperationLineSnapshot> right)
    {
        left ??= [];
        right ??= [];
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index].UnitPrice != right[index].UnitPrice ||
                left[index].LineTotal != right[index].LineTotal ||
                left[index].BonusQuantity != right[index].BonusQuantity)
            {
                return false;
            }
        }

        return true;
    }

    private static string? GetLocationName(Guid? locationId, IReadOnlyDictionary<Guid, Location> lookup) =>
        locationId.HasValue && lookup.TryGetValue(locationId.Value, out var location) ? location.Name : null;

    private static string NormalizeOperationType(string value)
    {
        var trimmed = value.Trim();
        return OperationTypes.FirstOrDefault(type => string.Equals(type, trimmed, StringComparison.OrdinalIgnoreCase))
            ?? trimmed;
    }

    private static bool IsMainWarehouse(Location location) =>
        string.Equals(location.LocationType, MainWarehouse, StringComparison.OrdinalIgnoreCase);

    private static bool IsPhysicalWarehouse(Location location) =>
        IsMainWarehouse(location) || string.Equals(location.LocationType, "SubWarehouse", StringComparison.OrdinalIgnoreCase);

    private static bool IsTransferDestination(Location location) =>
        IsPhysicalWarehouse(location) || string.Equals(location.LocationType, Online, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReturnDisposition(string? value) => value?.Trim() switch
    {
        null or "" or "Sellable" => "Sellable",
        "Quarantine" => "Quarantine",
        "WriteOff" => "WriteOff",
        _ => "Invalid"
    };

    private static bool IsRetailSaleLocation(Location location) =>
        !IsMainWarehouse(location) &&
        (string.Equals(location.LocationType, Retail, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(location.LocationType, Online, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(location.LocationType, "SubWarehouse", StringComparison.OrdinalIgnoreCase) ||
            location.Name.Contains("Retail", StringComparison.OrdinalIgnoreCase) ||
            location.Name.Contains("Online", StringComparison.OrdinalIgnoreCase));

    private static bool IsWarehouseClerk(ICurrentUser currentUser) =>
        string.Equals(currentUser.Role, LenseeRoles.WarehouseClerk, StringComparison.OrdinalIgnoreCase);

    private static bool IsTrustedFinancialActor(ICurrentUser currentUser) =>
        LenseeRoles.Normalize(currentUser.Role) is LenseeRoles.Accountant or LenseeRoles.Admin or LenseeRoles.ERPAdmin or LenseeRoles.CLevel;

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeOperationReference(string? value) =>
        TrimToNull(value);

    private static string? NormalizeBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizePaymentMethod(string? value)
    {
        var trimmed = TrimToNull(value);
        if (trimmed is null)
        {
            return null;
        }

        if (string.Equals(trimmed, "CashTransaction", StringComparison.OrdinalIgnoreCase)) return "BankTransfer";
        return PaymentMethods.FirstOrDefault(method => string.Equals(method, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    // MerchantAccount is a deferred receivable track, not a Finance cash movement.
    // Operations retain the established null payment method and derive the track from ClientId.
    private static string? NormalizeStoredPaymentMethod(string? value) =>
        IsMerchantAccountMethod(value) ? null : NormalizePaymentMethod(value);

    private static bool IsMerchantAccountMethod(string? value) =>
        string.Equals(NormalizePaymentMethod(value), PaymentTracks.MerchantAccount, StringComparison.Ordinal);

    private static int? ToPieces(int packs, int? piecesPerPack, string locationType) =>
        !string.Equals(locationType, MainWarehouse, StringComparison.OrdinalIgnoreCase) && piecesPerPack is > 0
            ? packs * piecesPerPack.Value
            : null;

    private static ReplenishmentAlertResponse ToReplenishmentAlert(ReplenishmentRowResponse shortage, string message) =>
        new(
            shortage.DestinationLocationId,
            shortage.DestinationLocationName,
            shortage.SkuId,
            shortage.SkuCode,
            shortage.ProductName,
            shortage.ShortagePacks,
            shortage.MainAvailablePacks,
            message);

    private static async Task WriteReplenishmentAlertsAsync(
        NotificationsDbContext notificationsDbContext,
        IReadOnlyCollection<ReplenishmentAlertResponse> alerts,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (var alert in alerts)
        {
            var message = $"{alert.DestinationLocationName}: {alert.SkuCode ?? alert.SkuId.ToString()} needs {alert.ShortagePacks} pack(s). {alert.Message}";
            foreach (var role in new[] { LenseeRoles.Admin, LenseeRoles.CLevel })
            {
                var alreadyExists = await notificationsDbContext.NotificationLogs.AnyAsync(notification =>
                    notification.AlertType == "TargetReplenishmentLowMainStock" &&
                    notification.TargetRole == role &&
                    notification.ReferenceId == alert.SkuId &&
                    notification.ReferenceType == "Sku" &&
                    notification.Message == message &&
                    notification.CreatedAt.Date == now.Date,
                    cancellationToken);
                if (alreadyExists)
                {
                    continue;
                }

                notificationsDbContext.NotificationLogs.Add(new NotificationLog
                {
                    Id = Guid.NewGuid(),
                    AlertType = "TargetReplenishmentLowMainStock",
                    Message = message,
                    ReferenceId = alert.SkuId,
                    ReferenceType = "Sku",
                    TargetRole = role,
                    Channel = "InApp",
                    IsRead = false,
                    CreatedAt = now
                });
            }
        }

        await notificationsDbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<string?> GetRevisionBlockReasonAsync(
        OperationLog operation,
        PaymentsDbContext paymentsDbContext,
        CancellationToken cancellationToken)
    {
        if (operation.OperationType == Return &&
            operation.Status == Confirmed &&
            operation.OperationLines.Any(line => line.WriteOffReason == "ExpiredMerchantReturn"))
        {
            return "Expired merchant returns cannot be revised because receipt and write-off were posted together.";
        }

        if (operation.OperationType is RetailSale &&
            operation.Status is Shipped or Completed &&
            operation.OperationLines.Any(line => line.EntryMode == "Pieces"))
        {
            return "Retail piece sales cannot be revised after shipment because exact loose-piece reversal is not yet supported.";
        }

        if (operation.OperationType is Change or WriteOff &&
            operation.Status == Confirmed &&
            ReadTransferAllocations(operation).Count == 0 &&
            operation.OperationLines.Any(line => line.Section == ChangeIn || operation.OperationType == WriteOff))
        {
            return "This operation was confirmed before allocation snapshots were captured and cannot be safely revised.";
        }

        if (operation.OperationType is WholesaleSale or RetailSale && operation.Status == Completed)
        {
            var paymentBlock = await PaymentsEndpoints.GetRevisionBlockReasonForCompletedSaleAsync(operation.Id, paymentsDbContext, cancellationToken);
            if (paymentBlock is not null)
            {
                return paymentBlock;
            }
        }

        return null;
    }

    private static bool IsOnlineSale(OperationLog operation) =>
        operation.OperationType == RetailSale &&
        (string.Equals(operation.SalesChannel, "Online", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(operation.SalesChannel, "Shopify", StringComparison.OrdinalIgnoreCase));

    private static bool IsFinalizedForCorrection(string status) => status is Confirmed or Completed or Received or Dispatched or Delivered or FailedDelivery;

    private static bool ShouldSetConfirmedActorAfterRevision(string status) =>
        status is not Draft and not Cancelled;

    private static async Task ReverseOperationEffectsAsync(
        OperationLog operation,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        InventoryDbContext inventoryDbContext,
        PaymentsDbContext paymentsDbContext,
        StockLedgerService ledgerService,
        Guid userId,
        CancellationToken cancellationToken)
    {
        switch (operation.Status)
        {
            case Draft:
            case Cancelled:
                return;
            case Reserved:
                await ReverseReservedOperationAsync(operation, ledgerService, userId, cancellationToken);
                break;
            case Shipped:
                await ReverseShippedOperationAsync(operation, allocations, ledgerService, userId, cancellationToken);
                break;
            case Received:
                await ReverseReceivedOperationAsync(operation, allocations, inventoryDbContext, ledgerService, userId, cancellationToken);
                break;
            case Completed:
                await ReverseCompletedSaleAsync(operation, allocations, paymentsDbContext, inventoryDbContext, ledgerService, userId, cancellationToken);
                break;
            case Confirmed:
                await ReverseConfirmedOperationAsync(operation, allocations, inventoryDbContext, ledgerService, userId, cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Revision is not supported for status {operation.Status}.");
        }
    }

    private static async Task ReverseReservedOperationAsync(
        OperationLog operation,
        StockLedgerService ledgerService,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (operation.OperationType is WarehouseTransfer or WholesaleSale or RetailSale or Reserve)
        {
            foreach (var group in operation.OperationLines.Where(line => line.EntryMode != "Pieces").GroupBy(line => line.SkuId))
            {
                await ledgerService.ReleaseInWarehouseUpToAsync(operation.SourceLocationId!.Value, group.Key, group.Sum(line => line.Quantity), userId, operation.Id, cancellationToken);
            }
        }
    }

    private static async Task ReverseShippedOperationAsync(
        OperationLog operation,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        StockLedgerService ledgerService,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (operation.OperationType == WarehouseTransfer)
        {
            await RestoreBatchAllocationsAsync(operation.SourceLocationId!.Value, allocations, ledgerService, userId, operation.Id, cancellationToken);
            return;
        }

        if (operation.OperationType == Reserve)
        {
            foreach (var allocation in allocations)
            {
                await ledgerService.ReleaseWithRepUpToAsync(operation.SourceLocationId!.Value, allocation.SkuId, allocation.Allocations.Sum(batch => batch.Quantity), userId, operation.Id, cancellationToken);
                await RestoreBatchAllocationsAsync(operation.SourceLocationId!.Value, [allocation], ledgerService, userId, operation.Id, cancellationToken);
            }
            return;
        }

        if (operation.OperationType is WholesaleSale or RetailSale)
        {
            await RestoreBatchAllocationsAsync(operation.SourceLocationId!.Value, allocations, ledgerService, userId, operation.Id, cancellationToken);
        }
    }

    private static async Task ReverseReceivedOperationAsync(
        OperationLog operation,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        InventoryDbContext inventoryDbContext,
        StockLedgerService ledgerService,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (operation.OperationType != WarehouseTransfer)
        {
            throw new InvalidOperationException("Only received warehouse transfers can be reversed from received status.");
        }

        await RemoveBatchAllocationsAsync(operation.DestinationLocationId!.Value, allocations, ledgerService, userId, operation.Id, cancellationToken);
        await RestoreBatchAllocationsAsync(operation.SourceLocationId!.Value, allocations, ledgerService, userId, operation.Id, cancellationToken);
    }

    private static async Task ReverseCompletedSaleAsync(
        OperationLog operation,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        PaymentsDbContext paymentsDbContext,
        InventoryDbContext inventoryDbContext,
        StockLedgerService ledgerService,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await PaymentsEndpoints.RemovePaymentArtifactsForSaleRevisionAsync(operation.Id, paymentsDbContext, cancellationToken);
        await ReverseShippedOperationAsync(operation, allocations, ledgerService, userId, cancellationToken);
    }

    private static async Task ReverseConfirmedOperationAsync(
        OperationLog operation,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        InventoryDbContext inventoryDbContext,
        StockLedgerService ledgerService,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (operation.OperationType == InventoryReceipt)
        {
            await RemoveLineBatchesAsync(operation.SourceLocationId ?? operation.DestinationLocationId!.Value, operation.OperationLines, ledgerService, userId, operation.Id, cancellationToken);
            return;
        }

        if (operation.OperationType == Return)
        {
            await RemoveLineBatchesAsync(operation.SourceLocationId!.Value, operation.OperationLines, ledgerService, userId, operation.Id, cancellationToken);
            return;
        }

        if (operation.OperationType == Change)
        {
            await RemoveLineBatchesAsync(operation.SourceLocationId!.Value, operation.OperationLines.Where(line => line.Section == ChangeOut), ledgerService, userId, operation.Id, cancellationToken);
            await RestoreBatchAllocationsAsync(operation.SourceLocationId!.Value, allocations, ledgerService, userId, operation.Id, cancellationToken);
            return;
        }

        if (operation.OperationType == WriteOff)
        {
            await RestoreBatchAllocationsAsync(operation.SourceLocationId!.Value, allocations, ledgerService, userId, operation.Id, cancellationToken);
            return;
        }

        if (operation.OperationType == Reserve)
        {
            foreach (var group in operation.OperationLines.GroupBy(line => line.SkuId))
            {
                await ledgerService.ReleaseWithRepUpToAsync(operation.SourceLocationId!.Value, group.Key, group.Sum(line => line.Quantity), userId, operation.Id, cancellationToken);
            }
        }
    }

    private static async Task<IReadOnlyList<TransferAllocationSnapshot>> ReapplyOperationToStatusAsync(
        OperationLog operation,
        string originalStatus,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        CrmDbContext crmDbContext,
        PaymentsDbContext paymentsDbContext,
        MerchantAccountService merchantAccountService,
        StockLedgerService ledgerService,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var allocations = new List<TransferAllocationSnapshot>();

        if (originalStatus == Draft)
        {
            operation.Status = Draft;
            return allocations;
        }

        if (operation.OperationType == InventoryReceipt)
        {
            foreach (var line in operation.OperationLines)
            {
                await ledgerService.ReceiveAsync(
                    operation.DestinationLocationId!.Value,
                    line.SkuId,
                    line.Quantity,
                    userId,
                    line.LotNumber,
                    line.ExpiryDate,
                    line.LineNotes,
                    operation.Id,
                    cancellationToken);
            }
            operation.Status = Received;
            operation.ConfirmedAt = now;
            operation.ConfirmedBy = userId;
            return allocations;
        }

        if (operation.OperationType == WarehouseTransfer)
        {
            foreach (var line in operation.OperationLines)
            {
                var lineAllocation = await ReserveSelectedOrFefoAsync(operation.SourceLocationId!.Value, line, ledgerService, userId, operation.Id, cancellationToken);
                allocations.Add(new TransferAllocationSnapshot(line.SkuId, [lineAllocation]));
            }
            operation.Status = Reserved;
            operation.ConfirmedAt = now;
            operation.ConfirmedBy = userId;
            return allocations;
        }

        if (operation.OperationType is WholesaleSale or RetailSale)
        {
            allocations.AddRange(await BuildSelectedPackAllocationsAsync(operation, inventoryDbContext, operationsDbContext, new ClockStub(now), cancellationToken));
            foreach (var allocation in allocations)
            {
                await ledgerService.ReserveSelectedBatchInWarehouseAsync(operation.SourceLocationId!.Value, allocation.SkuId, allocation.Allocations.Sum(batch => batch.Quantity), allocation.Allocations[0].LotNumber, allocation.Allocations[0].ExpiryDate, userId, operation.Id, cancellationToken);
            }
            operation.Status = Reserved;
            operation.ConfirmedAt = now;
            operation.ConfirmedBy = userId;
            if (originalStatus is Shipped or Completed)
            {
                await ShipSaleOutAsync(operation, allocations, operationsDbContext, catalogDbContext, ledgerService, userId, now, cancellationToken);
                operation.Status = Shipped;
            }
            if (originalStatus == Completed)
            {
                operation.Status = Completed;
                if (operation.ClientId is { } merchantId)
                {
                    await merchantAccountService.PostSaleAsync(
                        merchantId,
                        operation.Id,
                        operation.OperationLines.Sum(line => line.LineTotal),
                        userId,
                        now,
                        "Completed sale restored during revision.",
                        cancellationToken);
                }
                await PaymentsEndpoints.CreatePaymentArtifactsForCompletedSaleAsync(operation, paymentsDbContext, userId, now, cancellationToken);
            }
            return allocations;
        }

        if (operation.OperationType == Return)
        {
            foreach (var line in operation.OperationLines)
            {
                await ledgerService.ReceiveReturnAsync(operation.SourceLocationId!.Value, line.SkuId, line.Quantity, userId, line.LotNumber, line.ExpiryDate, line.LineNotes, operation.Id, cancellationToken);
            }
            operation.Status = Confirmed;
            operation.ConfirmedAt = now;
            operation.ConfirmedBy = userId;
            return allocations;
        }

        if (operation.OperationType == Change)
        {
            foreach (var line in operation.OperationLines.Where(line => line.Section == ChangeOut))
            {
                await ledgerService.ReceiveChangeOutAsync(operation.SourceLocationId!.Value, line.SkuId, line.Quantity, userId, line.LotNumber, line.ExpiryDate, line.LineNotes, operation.Id, cancellationToken);
            }
            foreach (var line in operation.OperationLines.Where(line => line.Section == ChangeIn))
            {
                var lineAllocation = await IssueSelectedOrFefoAsync(operation.SourceLocationId!.Value, line, InventoryTransactionTypes.ChangeIn, ledgerService, userId, operation.Id, cancellationToken);
                allocations.Add(new TransferAllocationSnapshot(line.SkuId, [lineAllocation]));
            }
            operation.Status = Confirmed;
            operation.ConfirmedAt = now;
            operation.ConfirmedBy = userId;
            return allocations;
        }

        if (operation.OperationType == WriteOff)
        {
            foreach (var line in operation.OperationLines)
            {
                var lineAllocation = await IssueSelectedOrFefoAsync(operation.SourceLocationId!.Value, line, InventoryTransactionTypes.WriteOff, ledgerService, userId, operation.Id, cancellationToken);
                allocations.Add(new TransferAllocationSnapshot(line.SkuId, [lineAllocation]));
            }
            operation.Status = Confirmed;
            operation.ConfirmedAt = now;
            operation.ConfirmedBy = userId;
            return allocations;
        }

        if (operation.OperationType == Reserve)
        {
            foreach (var group in operation.OperationLines.GroupBy(line => line.SkuId))
            {
                foreach (var line in group)
                {
                    var lineAllocation = await ReserveSelectedOrFefoAsync(operation.SourceLocationId!.Value, line, ledgerService, userId, operation.Id, cancellationToken);
                    allocations.Add(new TransferAllocationSnapshot(group.Key, [lineAllocation]));
                }
            }
            operation.Status = Reserved;
            operation.ConfirmedAt = now;
            operation.ConfirmedBy = userId;
            if (originalStatus is Shipped or Confirmed)
            {
                await ShipRepresentativeReserveAsync(operation, allocations, ledgerService, userId, cancellationToken);
                operation.Status = Shipped;
            }
            if (originalStatus == Confirmed)
            {
                operation.Status = Confirmed;
            }
            return allocations;
        }

        throw new InvalidOperationException($"Reapply is not supported for operation type {operation.OperationType}.");
    }

    private static async Task<BatchAllocation> ReserveSelectedOrFefoAsync(
        Guid locationId,
        OperationLine line,
        StockLedgerService ledgerService,
        Guid userId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (line.ExpiryDate is not null)
        {
            return await ledgerService.ReserveSelectedBatchInWarehouseAsync(
                locationId,
                line.SkuId,
                line.Quantity,
                line.LotNumber,
                line.ExpiryDate,
                userId,
                operationId,
                cancellationToken);
        }

        var allocations = await ledgerService.ReserveInWarehouseFefoAsync(locationId, line.SkuId, line.Quantity, userId, operationId, null, cancellationToken);
        return allocations.Single();
    }

    private static async Task<BatchAllocation> IssueSelectedOrFefoAsync(
        Guid locationId,
        OperationLine line,
        string transactionType,
        StockLedgerService ledgerService,
        Guid userId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (line.ExpiryDate is not null)
        {
            return await ledgerService.IssueSelectedBatchAsync(
                locationId,
                line.SkuId,
                line.Quantity,
                transactionType,
                line.LotNumber,
                line.ExpiryDate,
                userId,
                operationId,
                cancellationToken);
        }

        var allocations = await ledgerService.IssueFefoAsync(locationId, line.SkuId, line.Quantity, transactionType, userId, operationId, null, cancellationToken);
        return allocations.Single();
    }

    private static async Task RestoreBatchAllocationsAsync(
        Guid locationId,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        StockLedgerService ledgerService,
        Guid userId,
        Guid referenceOperationId,
        CancellationToken cancellationToken)
    {
        foreach (var allocation in allocations)
        {
            foreach (var batch in allocation.Allocations)
            {
                await ledgerService.AdjustStocktakeBatchAsync(locationId, allocation.SkuId, batch.LotNumber, batch.ExpiryDate, batch.Quantity, userId, referenceOperationId, cancellationToken);
            }
        }
    }

    private static async Task RemoveBatchAllocationsAsync(
        Guid locationId,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        StockLedgerService ledgerService,
        Guid userId,
        Guid referenceOperationId,
        CancellationToken cancellationToken)
    {
        foreach (var allocation in allocations)
        {
            foreach (var batch in allocation.Allocations)
            {
                await ledgerService.AdjustStocktakeBatchAsync(locationId, allocation.SkuId, batch.LotNumber, batch.ExpiryDate, -batch.Quantity, userId, referenceOperationId, cancellationToken);
            }
        }
    }

    private static async Task RemoveLineBatchesAsync(
        Guid locationId,
        IEnumerable<OperationLine> lines,
        StockLedgerService ledgerService,
        Guid userId,
        Guid referenceOperationId,
        CancellationToken cancellationToken)
    {
        foreach (var line in lines)
        {
            await ledgerService.AdjustStocktakeBatchAsync(locationId, line.SkuId, line.LotNumber, line.ExpiryDate, -line.Quantity, userId, referenceOperationId, cancellationToken);
        }
    }

    private static async Task ExecuteInventoryOperationTransactionAsync(
        InventoryDbContext inventoryDbContext,
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        FinanceDbContext financeDbContext,
        CrmDbContext crmDbContext,
        IdentityDbContext identityDbContext,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        await SharedDbTransaction.ExecuteAsync(inventoryDbContext, action, cancellationToken, operationsDbContext, paymentsDbContext, financeDbContext, crmDbContext, identityDbContext);
    }

    private static async Task ExecuteInventoryOperationTransactionAsync(
        InventoryDbContext inventoryDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        await SharedDbTransaction.ExecuteAsync(inventoryDbContext, action, cancellationToken, operationsDbContext, identityDbContext);
    }

    private static async Task ExecuteInventoryOperationTransactionAsync(
        InventoryDbContext inventoryDbContext,
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        await SharedDbTransaction.ExecuteAsync(inventoryDbContext, action, cancellationToken, operationsDbContext, paymentsDbContext, identityDbContext);
    }

    private static async Task ExecuteInventoryOperationTransactionAsync(
        InventoryDbContext inventoryDbContext,
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        CrmDbContext crmDbContext,
        IdentityDbContext identityDbContext,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        await SharedDbTransaction.ExecuteAsync(inventoryDbContext, action, cancellationToken, operationsDbContext, paymentsDbContext, crmDbContext, identityDbContext);
    }

    private sealed record DraftValidationResult(Dictionary<string, string[]> Errors, Location? SourceLocation, Location? DestinationLocation, Dictionary<Guid, Sku> SkusById, Merchant? Merchant, Representative? Representative, OperationCustomerSnapshot? CorrectionSource);
    private sealed record CodChargeResolution(decimal Amount, string? OverrideReason, string? Error);

    private sealed record OperationCustomerSnapshot(Guid? ClientId, string? ClientName, string? BuyerPhone, string? PaymentMethod);

    private sealed record ReplenishmentLineDraft(Guid SkuId, string? SkuCode, string? ProductName, int PackQuantity);

    private sealed record ReplenishmentSkuLookup(string SkuCode, string ProductName, int? PiecesPerPack);

    private sealed class MerchantSalesVarianceException : Exception
    {
        public MerchantSalesVarianceException(IReadOnlyList<MerchantSalesVarianceWarning> warnings)
            : base("Merchant return quantities changed while the operation was being confirmed.")
        {
            Warnings = warnings;
        }

        public IReadOnlyList<MerchantSalesVarianceWarning> Warnings { get; }
    }

    private sealed class SourceAllocationCapException : Exception
    {
        public SourceAllocationCapException(string message, IReadOnlyList<SourceAllocationCapViolation> violations)
            : base(message)
        {
            Violations = violations;
        }

        public IReadOnlyList<SourceAllocationCapViolation> Violations { get; }
    }

    private sealed record SourceAllocationCapViolation(Guid SourceOperationLineId, int SoldQuantity, int AlreadyReturnedOrExchanged, int RequestedQuantity);

    private sealed record OperationSnapshot(
        string OperationType,
        string Status,
        Guid? SourceLocationId,
        Guid? DestinationLocationId,
        Guid? ClientId,
        string? ClientName,
        Guid? RepresentativeId,
        string? ExternalReference,
        string? PaymentMethod,
        string? Notes,
        IReadOnlyList<OperationLineSnapshot> Lines,
        IReadOnlyList<TransferAllocationSnapshot> TransferAllocations);

    private sealed record OperationLineSnapshot(Guid SkuId, string SkuCode, string ProductName, string Section, int Quantity, string EntryMode, int BonusQuantity, decimal UnitPrice, decimal LineTotal, string? LotNumber, DateOnly? ExpiryDate, string? Notes, string? ShopifyLineItemId, string? ShopifyVariantId, string? ShopifySku, string? ShopifyTitle, string? ShopifyVariantTitle, string? ShopifyProperties, string? ReturnDisposition = null, string? PricingMode = null, string? LineType = null);

    private sealed record WearCycleInfo(string? Cycle, string? Duration);

    private sealed record WearCycleLookup(Guid SkuId, string ProductType, string? OpenedExpiryRate, string? OpenedExpiryDuration);

    private sealed record TransferAllocationSnapshot(Guid SkuId, IReadOnlyList<BatchAllocation> Allocations);

    private sealed class ClockStub : IClock
    {
        private readonly DateTime _egyptNow;

        public ClockStub(DateTime egyptNow)
        {
            _egyptNow = egyptNow;
        }

        public DateTime UtcNow => _egyptNow.ToUniversalTime();

        public DateTime EgyptNow => _egyptNow;
    }
}

public sealed record OperationRequest(
    string OperationType,
    Guid? SourceLocationId,
    Guid? DestinationLocationId,
    Guid? MerchantId,
    Guid? RepresentativeId,
    string? BuyerName,
    string? BuyerPhone,
    string? PaymentMethod,
    string? Notes,
    ReceiptRequest? Receipt,
    IReadOnlyList<OperationLineRequest> Lines,
    uint? ExpectedVersion = null,
    string? CustomerKind = null,
    string? PaymentTrack = null,
    Guid? FinanceAccountId = null,
    decimal? DeliveryFee = null,
    decimal? CodCharge = null,
    string? CodChargeOverrideReason = null,
    bool? BuyerContactVerified = null,
    string? ExternalPaymentProvider = null,
    string? ExternalPaymentReference = null,
    bool? ExternalPaymentVerified = null,
    string? PaymentStatus = null,
    string? ClientRequestId = null,
    string? SalesChannel = null,
    string? ShippingAddress = null,
    string? ExternalReference = null,
    Guid? ReturnReferenceOperationId = null);

public sealed record OperationRevisionRequest(OperationRequest Operation, string Reason);

public sealed record OperationConfirmationRequest(bool? AcknowledgeSalesVariance, string? SalesVarianceReason, string? VerbalAuthorizer = null);
public sealed record OperationCancellationRequest(string? Reason);
public sealed record CodChargeSettingsRequest(decimal? FlatAmount);
public sealed record CodChargeSettingsResponse(bool Configured, decimal? FlatAmount);

public sealed record MerchantSalesVarianceGateResponse(
    string Code,
    string Title,
    string Detail,
    bool CanBypass,
    bool ReasonRequired,
    IReadOnlyList<MerchantSalesVarianceWarning> Warnings);

public sealed record OperationLineSourceAllocationResponse(
    Guid Id,
    Guid TargetOperationLineId,
    Guid SourceOperationId,
    string SourceOperationNumber,
    Guid SourceOperationLineId,
    Guid SkuId,
    Guid? SourceBatchId,
    Guid? SourceOpenedPieceLotId,
    string EntryMode,
    string? LotNumber,
    DateOnly? ExpiryDate,
    int Quantity,
    int OriginalSoldQuantity,
    DateTime CreatedAt);

public sealed record SourceSaleLineResponse(
    Guid SourceOperationId,
    string SourceOperationNumber,
    Guid SourceOperationLineId,
    Guid SkuId,
    string SkuCode,
    string ProductName,
    string EntryMode,
    int OriginalSoldQuantity,
    int ReturnedOrExchangedQuantity,
    int RemainingEligibleQuantity,
    string? LotNumber,
    DateOnly? ExpiryDate,
    IReadOnlyList<SourceSaleBatchCandidateResponse> SourceBatches);

public sealed record SourceSaleBatchCandidateResponse(
    Guid SourceBatchId,
    string? LotNumber,
    DateOnly? ExpiryDate,
    IReadOnlyList<SourceSalePieceLotCandidateResponse> OpenedPieceLots);

public sealed record SourceSalePieceLotCandidateResponse(
    Guid SourceOpenedPieceLotId,
    int LoosePieceQuantity,
    DateOnly? PieceExpiryDate);

public sealed record MerchantSalesVarianceWarning(
    Guid SkuId,
    string SkuCode,
    string ProductName,
    string? LotNumber,
    DateOnly? ExpiryDate,
    int RequestedQuantity,
    int SoldQuantity,
    int ReturnedQuantity,
    int ExcessQuantity,
    string Message,
    int? PiecesPerPack = null,
    string AlertType = "QuantityExcess",
    string QuantityUnit = "Piece");

public sealed record ReceiptRequest(string? SupplierName, string? InvoiceNumber);

public sealed record ReceiptResponse(string? SupplierName, string? InvoiceNumber);

public sealed record OperationLineRequest(
    Guid SkuId, int PackQuantity, int? PieceQuantity, string? EntryMode, string? Section, decimal? UnitPrice, bool? IsBonus,
    string? LotNumber, DateOnly? ExpiryDate, string? Notes, Guid? SourceOperationId = null, Guid? SourceOperationLineId = null,
    Guid? SourceBatchId = null, Guid? SourceOpenedPieceLotId = null, string? ReturnDisposition = null,
    string? PricingMode = null, int? NormalQuantity = null, decimal? NormalValue = null, decimal? ExceptionValue = null, string? LineType = null);

public sealed record OperationListResponse(
    Guid Id,
    string OperationNumber,
    string? ExternalReference,
    string OperationType,
    string Status,
    Guid? SourceLocationId,
    string? SourceLocationName,
    Guid? DestinationLocationId,
    string? DestinationLocationName,
    Guid? ClientId,
    string? ClientName,
    Guid? RepresentativeId,
    string? PaymentMethod,
    DateTime CreatedAt,
    DateTime? ConfirmedAt,
    string? CreatedByName,
    string? ConfirmedByName,
    string? LastEditedByName,
    bool CanEditDraft,
    bool CanRevise,
    string? RevisionBlockReason,
    string SalesChannel,
    string? BuyerPhone,
    string? BuyerEmail,
    string? ShippingAddress,
    string? ShopifyOrderId,
    string? ShopifyOrderNumber,
    bool AllocationPending,
    string? CorrectionReason = null,
    string FinancialClosureStatus = "Open",
    OperationWorkflowResponse? Workflow = null);

public sealed record OperationDetailResponse(
    Guid Id,
    string OperationNumber,
    string? ExternalReference,
    string OperationType,
    string Status,
    Guid? SourceLocationId,
    string? SourceLocationName,
    Guid? DestinationLocationId,
    string? DestinationLocationName,
    Guid? ClientId,
    string? ClientName,
    Guid? RepresentativeId,
    string? RepresentativeName,
    string? PaymentMethod,
    string? Notes,
    DateTime CreatedAt,
    DateTime? ConfirmedAt,
    string? CreatedByName,
    string? ConfirmedByName,
    string? LastEditedByName,
    int? CurrentVersionNumber,
    uint ConcurrencyVersion,
    bool CanEditDraft,
    bool CanRevise,
    string? RevisionBlockReason,
    ReceiptResponse? Receipt,
    IReadOnlyList<OperationLineResponse> Lines,
    IReadOnlyList<OperationAllocationResponse> Allocations,
    IReadOnlyList<OperationVersionResponse> Versions,
    string SalesChannel,
    string? BuyerPhone,
    string? BuyerEmail,
    string? ShippingAddress,
    string? ShopifyOrderId,
    string? ShopifyOrderNumber,
    bool AllocationPending,
    string FinancialClosureStatus = "Open",
    decimal DeliveryFee = 0,
    decimal CodCharge = 0,
    string? PaymentStatus = null,
    string? ExternalPaymentProvider = null,
    string? ExternalPaymentReference = null,
    bool ExternalPaymentVerified = false,
    bool BuyerContactVerified = false,
    string? ReturnExceptionReason = null,
    string? ReturnVerbalAuthorizer = null,
    decimal ReturnNormalValue = 0,
    decimal ReturnExceptionValue = 0,
    string? ReturnApprovalReason = null,
    Guid? ReturnApprovedBy = null,
    DateTime? ReturnApprovedAt = null,
    Guid? ReturnRejectedBy = null,
    DateTime? ReturnRejectedAt = null,
    IReadOnlyList<ReturnApprovalLineResponse>? ReturnApprovalLines = null,
    OperationWorkflowResponse? Workflow = null,
    Guid? ReturnReferenceOperationId = null,
    string? ReturnFinancialOutcome = null);

public sealed record ReturnApprovalLineResponse(Guid Id, Guid OperationLineId, Guid SkuId, int RequestedQuantity, int? EligibleQuantity, string? BatchName, DateOnly? ExpiryDate, string MismatchType, string Detail);
public sealed record OperationWorkflowResponse(string Status, IReadOnlyList<OperationWorkflowActionResponse> Actions);
public sealed record OperationWorkflowActionResponse(string Key, bool RequiresReason, string? DisabledExplanation = null);

public sealed record OperationLineResponse(Guid Id, Guid SkuId, string SkuCode, string ProductName, string Section, int Quantity, string EntryMode, int BonusQuantity, decimal UnitPrice, decimal LineTotal, string? LotNumber, DateOnly? ExpiryDate, string? MerchantNameSnapshot, string? RepresentativeNameSnapshot, string? Notes, string? WearCycle, string? WearDuration, string? ShopifyLineItemId, string? ShopifyVariantId, string? ShopifySku, string? ShopifyTitle, string? ShopifyVariantTitle, string? ShopifyProperties, string? ReturnDisposition = null, string? PricingMode = null, int NormalQuantity = 0, int ExceptionQuantity = 0, decimal NormalValue = 0, decimal ExceptionValue = 0, string? LineType = null, int? PiecesPerPack = null, int PackQuantity = 0, int PieceQuantity = 0, int TotalPieces = 0, string? DisplayQuantity = null);
public sealed record OperationEditorResponse(Guid Id, string OperationType, uint ConcurrencyVersion, Guid? SourceLocationId, Guid? DestinationLocationId, Guid? ClientId, string? ClientName, Guid? RepresentativeId, string? PaymentMethod, string? Notes, ReceiptResponse? Receipt, string SalesChannel, string? BuyerPhone, int TotalLineCount, IReadOnlyList<OperationEditorLineResponse> Lines, Guid? FinanceAccountId = null, string? ExternalReference = null, Guid? ReturnReferenceOperationId = null);
public sealed record OperationEditorLineResponse(Guid Id, Guid SkuId, string SkuCode, string ProductName, string EntryMode, string Section, int Quantity, int BonusQuantity, decimal UnitPrice, decimal LineTotal, string? LotNumber, DateOnly? ExpiryDate, string? MerchantNameSnapshot, string? RepresentativeNameSnapshot, string? Notes, string? ShopifyLineItemId, string? ShopifyVariantId, string? ShopifySku, string? ShopifyTitle, string? ShopifyVariantTitle, string? ShopifyProperties, string? ReturnDisposition = null, string? PricingMode = null, string? LineType = null);
public sealed record ShopifyAllocationRequest(IReadOnlyList<ShopifyAllocationLineRequest>? Lines, uint? ExpectedVersion = null, Guid? SourceLocationId = null);
public sealed record ShopifyAllocationLineRequest(Guid OperationLineId, string? LotNumber, DateOnly? ExpiryDate);
public sealed record ReturnApprovalRequest(string? Reason);

public sealed record MerchantBatchOptionResponse(
    string LotNumber,
    DateOnly ExpiryDate,
    int SoldQuantity,
    int ReturnedQuantity,
    int RecordedBalanceQuantity,
    int SoldPhysicalPieces,
    int ReturnedPhysicalPieces,
    int RecordedBalancePhysicalPieces,
    bool HasLegacyUnitUnknown);

public sealed record OperationAllocationResponse(Guid SkuId, string? SkuCode, string? ProductName, Guid BatchId, int Quantity, string? LotNumber, DateOnly? ExpiryDate);

public sealed record OperationVersionResponse(Guid Id, int VersionNumber, string Reason, DateTime EditedAt, string? EditedByName, bool IsCurrent, bool ChangedRoute, bool ChangedParty, bool ChangedFinancial, bool ChangedLines);

public sealed record ReplenishmentReserveRequest(Guid? LocationId, Guid? SkuId);

public sealed record ReplenishmentRowResponse(
    Guid DestinationLocationId,
    string DestinationLocationName,
    string DestinationLocationType,
    Guid SkuId,
    string? SkuCode,
    string? ProductName,
    int? PiecesPerPack,
    int AvailablePacks,
    int? AvailablePieces,
    int IncomingPacks,
    int? IncomingPieces,
    int TargetPacks,
    int? TargetPieces,
    int ShortagePacks,
    int? ShortagePieces,
    int MainAvailablePacks);

public sealed record ReplenishmentReserveResponse(
    int CreatedOperations,
    int UnfilledPacks,
    IReadOnlyList<ReplenishmentOperationResponse> Operations,
    IReadOnlyList<ReplenishmentAlertResponse> Alerts);

public sealed record ReplenishmentOperationResponse(Guid Id, string OperationNumber, Guid DestinationLocationId, int ReservedPacks);

public sealed record ReplenishmentAlertResponse(
    Guid DestinationLocationId,
    string DestinationLocationName,
    Guid SkuId,
    string? SkuCode,
    string? ProductName,
    int ShortagePacks,
    int MainAvailablePacks,
    string Message);
