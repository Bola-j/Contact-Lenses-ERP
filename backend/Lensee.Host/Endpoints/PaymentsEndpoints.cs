using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lensee.Host.Infrastructure;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Data;
using Lensee.SharedKernel.Primitives;
using Lensee.SharedKernel.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static class PaymentsEndpoints
{
    private const string WholesaleSale = "WholesaleSale";
    private const string RetailSale = "RetailSale";
    private const string Return = "Return";
    private const string Change = "Change";
    private const string ChangeOut = "ChangeOut";
    private const string ChangeIn = "ChangeIn";
    private const string Completed = "Completed";
    private const string Confirmed = "Confirmed";
    private const string CashReceived = "CashReceived";
    private const string CashRefund = "CashRefund";
    private const string MerchantCredit = "MerchantCredit";
    private const string BalanceReduction = "BalanceReduction";
    private const string Draft = "Draft";
    private const string ConfirmedPayment = "Confirmed";
    private const string Rejected = "Rejected";
    private const string PendingAdmin = "PendingAdmin";
    private const string PendingAccountant = "PendingAccountant";
    private const string PendingAdminReview = "PendingAdminReview";
    private const string PendingApproval = "PendingApproval";
    private const string PaymentCompleted = "Completed";
    private const string IdempotencyPending = "Pending";
    private const string IdempotencyCompleted = "Completed";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> PaymentMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "CashHandToHand",
        "CashTransaction",
        "Installment"
    };

    public static RouteGroupBuilder MapPaymentsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/payments").WithTags("Payments");

        group.MapGet("/", ListPaymentLogsAsync).RequireAuthorization("payments.read");
        group.MapGet("/history", ListPaymentHistoryAsync).RequireAuthorization("payments.read");
        group.MapGet("/{id:guid}", GetPaymentLogAsync).RequireAuthorization("payments.read");
        group.MapGet("/merchants/{merchantId:guid}/balance", GetMerchantBalanceAsync).RequireAuthorization("payments.read");
        group.MapPost("/initialize", InitializePaymentLogAsync).RequireAuthorization("payments.write");
        group.MapPost("/{id:guid}/assign", AssignPaymentLogAsync).RequireAuthorization("payments.write");
        group.MapPost("/{id:guid}/sub-logs", DraftSubLogAsync).RequireAuthorization("payments.draft");
        group.MapPost("/sub-logs/{id:guid}/approve", ApproveSubLogAsync).RequireAuthorization("payments.write");
        group.MapPost("/sub-logs/{id:guid}/reject", RejectSubLogAsync).RequireAuthorization("payments.write");
        group.MapPost("/cash-receipts/{id:guid}/approve", ApproveCashReceiptAsync).RequireAuthorization("payments.approve");
        group.MapPost("/cash-records", CreateCashRecordAsync).RequireAuthorization("payments.write");
        group.MapGet("/adjustments", ListFinancialAdjustmentsAsync).RequireAuthorization("payments.read");
        group.MapPost("/adjustments", CreateFinancialAdjustmentAsync).RequireAuthorization("payments.adjustments.request");
        group.MapPost("/adjustments/{id:guid}/approve", ApproveFinancialAdjustmentAsync).RequireAuthorization("payments.adjustments.approve");
        group.MapPost("/adjustments/{id:guid}/reject", RejectFinancialAdjustmentAsync).RequireAuthorization("payments.adjustments.approve");

        return group;
    }

    private static async Task<IResult> ListPaymentLogsAsync(
        string? status,
        Guid? merchantId,
        Guid? operationId,
        int? page,
        int? pageSize,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        CancellationToken cancellationToken)
    {
        var request = new PageRequest(page ?? 1, pageSize ?? 25);
        var query = paymentsDbContext.MainPaymentLogs
            .Include(log => log.InstallmentSubLogs)
            .Where(log => !log.IsDeleted)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(log => log.Status == status.Trim());
        }
        if (merchantId.HasValue)
        {
            query = query.Where(log => log.MerchantId == merchantId.Value);
        }
        if (operationId.HasValue)
        {
            query = query.Where(log => log.OperationId == operationId.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(log => log.LastModifiedAt)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var userLookup = await LoadUserLookupAsync(identityDbContext, rows, cancellationToken);
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, rows.Select(row => row.OperationId), cancellationToken);
        var responses = rows.Select(log => ToListResponse(log, userLookup, operationLookup)).ToList();

        return Results.Ok(new PagedResult<PaymentLogListResponse>(responses, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> GetPaymentLogAsync(Guid id, PaymentsDbContext paymentsDbContext, OperationsDbContext operationsDbContext, IdentityDbContext identityDbContext, CancellationToken cancellationToken)
    {
        var log = await paymentsDbContext.MainPaymentLogs
            .Include(value => value.InstallmentSubLogs.OrderByDescending(sub => sub.DraftedAt))
            .FirstOrDefaultAsync(value => value.Id == id && !value.IsDeleted, cancellationToken);
        if (log is null)
        {
            return Results.NotFound();
        }

        var cashRecords = await paymentsDbContext.CashRecords
            .Where(value => value.OperationId == log.OperationId)
            .OrderByDescending(value => value.PaymentDate)
            .ToListAsync(cancellationToken);
        var adjustments = await LoadAdjustmentsForLogAsync(paymentsDbContext, log, cancellationToken);

        var userIds = new Guid?[] { log.InitializedBy, log.AssignedTo, log.LastModifiedBy }
            .Concat(log.InstallmentSubLogs.Select(sub => (Guid?)sub.DraftedBy))
            .Concat(log.InstallmentSubLogs.Select(sub => sub.ConfirmedBy))
            .Concat(cashRecords.Select(record => (Guid?)record.CreatedBy))
            .Concat(adjustments.Select(adjustment => (Guid?)adjustment.CreatedBy))
            .Where(value => value.HasValue && value.Value != Guid.Empty)
            .Select(value => value!.Value);
        var userLookup = await LoadUserLookupAsync(identityDbContext, userIds, cancellationToken);
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, [log.OperationId], cancellationToken);
        return Results.Ok(ToDetailResponse(log, cashRecords, adjustments, userLookup, operationLookup));
    }

    private static async Task<IResult> ListPaymentHistoryAsync(
        Guid? merchantId,
        Guid? operationId,
        int? page,
        int? pageSize,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        CrmDbContext crmDbContext,
        IdentityDbContext identityDbContext,
        CancellationToken cancellationToken)
    {
        var request = new PageRequest(page ?? 1, pageSize ?? 100);

        var logsQuery = paymentsDbContext.MainPaymentLogs
            .Include(log => log.InstallmentSubLogs)
            .Where(log => !log.IsDeleted)
            .AsQueryable();
        var cashQuery = paymentsDbContext.CashRecords.AsQueryable();
        var adjustmentsQuery = paymentsDbContext.FinancialAdjustments.AsQueryable();

        if (merchantId.HasValue)
        {
            logsQuery = logsQuery.Where(log => log.MerchantId == merchantId.Value);
            adjustmentsQuery = adjustmentsQuery.Where(adjustment => adjustment.MerchantId == merchantId.Value);
        }
        if (operationId.HasValue)
        {
            logsQuery = logsQuery.Where(log => log.OperationId == operationId.Value);
            cashQuery = cashQuery.Where(record => record.OperationId == operationId.Value);
            adjustmentsQuery = adjustmentsQuery.Where(adjustment => adjustment.OperationId == operationId.Value);
        }

        var logs = await logsQuery.OrderByDescending(log => log.LastModifiedAt).Take(300).ToListAsync(cancellationToken);
        var cashRecords = await cashQuery.OrderByDescending(record => record.PaymentDate).Take(300).ToListAsync(cancellationToken);
        var adjustments = await adjustmentsQuery.OrderByDescending(adjustment => adjustment.CreatedAt).Take(300).ToListAsync(cancellationToken);

        var operationIds = logs.Select(log => log.OperationId)
            .Concat(cashRecords.Select(record => record.OperationId))
            .Concat(adjustments.Where(adjustment => adjustment.OperationId.HasValue).Select(adjustment => adjustment.OperationId!.Value))
            .Distinct()
            .ToArray();
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, operationIds, cancellationToken);

        var merchantIds = logs.Select(log => log.MerchantId)
            .Concat(adjustments.Select(adjustment => (Guid?)adjustment.MerchantId))
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var merchantLookup = await LoadMerchantLookupAsync(crmDbContext, merchantIds, cancellationToken);

        var userIds = logs
            .SelectMany(log => new Guid?[] { log.InitializedBy, log.LastModifiedBy, log.AssignedTo }
                .Concat(log.InstallmentSubLogs.Select(sub => (Guid?)sub.DraftedBy))
                .Concat(log.InstallmentSubLogs.Select(sub => sub.ConfirmedBy)))
            .Concat(cashRecords.Select(record => (Guid?)record.CreatedBy))
            .Concat(adjustments.Select(adjustment => (Guid?)adjustment.CreatedBy))
            .Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        var userLookup = await LoadUserLookupAsync(identityDbContext, userIds, cancellationToken);

        var rows = new List<PaymentHistoryResponse>();
        rows.AddRange(BuildHistoryRowsFromLogs(logs, operationLookup, merchantLookup, userLookup));
        rows.AddRange(BuildHistoryRowsFromCashRecords(cashRecords, operationLookup, merchantLookup, userLookup));
        rows.AddRange(BuildHistoryRowsFromAdjustments(adjustments, operationLookup, merchantLookup, userLookup));

        var ordered = rows
            .OrderByDescending(row => row.HappenedAt)
            .ThenByDescending(row => row.RecordType)
            .ToList();
        var pageItems = ordered
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToList();

        return Results.Ok(new PagedResult<PaymentHistoryResponse>(pageItems, request.Page, request.PageSize, ordered.Count));
    }

    private static async Task<IResult> InitializePaymentLogAsync(
        InitializePaymentRequest request,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var idempotency = await StartPaymentIdempotencyAsync(idempotencyKey, "POST /api/v1/payments/initialize", request, paymentsDbContext, clock, cancellationToken);
        if (idempotency.Result is not null)
        {
            return idempotency.Result;
        }

        var operation = await operationsDbContext.OperationLogs
            .Include(value => value.OperationLines)
            .FirstOrDefaultAsync(value => value.Id == request.OperationId && !value.IsDeleted, cancellationToken);
        if (operation is null)
        {
            return Results.NotFound();
        }
        if (operation.ClientId is not { } merchantId)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.OperationId)] = ["Operation must be linked to a registered merchant."] });
        }

        var total = CalculatePaymentTotal(operation);
        if (total <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.OperationId)] = ["Operation does not create a positive payment amount."] });
        }

        var existing = await paymentsDbContext.MainPaymentLogs.FirstOrDefaultAsync(value => value.OperationId == request.OperationId && !value.IsDeleted, cancellationToken);
        if (existing is not null)
        {
            var existingLookup = await LoadUserLookupAsync(identityDbContext, [existing], cancellationToken);
            var existingOperationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, [existing.OperationId], cancellationToken);
            var existingCashRecords = await LoadCashRecordsForLogAsync(paymentsDbContext, existing, cancellationToken);
            var existingAdjustments = await LoadAdjustmentsForLogAsync(paymentsDbContext, existing, cancellationToken);
            return Results.Ok(ToDetailResponse(existing, existingCashRecords, existingAdjustments, existingLookup, existingOperationLookup));
        }

        var paymentMethod = NormalizePaymentMethod(request.PaymentMethod);
        if (paymentMethod is not null && !PaymentMethods.Contains(paymentMethod))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PaymentMethod)] = ["Payment method must be CashHandToHand, CashTransaction, or Installment."] });
        }

        var now = clock.EgyptNow;
        var log = new MainPaymentLog
        {
            Id = Guid.NewGuid(),
            OperationId = operation.Id,
            MerchantId = merchantId,
            TotalAmount = total,
            AmountPaid = 0,
            PendingAmount = 0,
            PaymentMethod = paymentMethod ?? operation.PaymentMethod ?? "Installment",
            Status = PendingAdmin,
            InitializedBy = currentUser.UserId ?? Guid.Empty,
            InitializedAt = now,
            LastModifiedBy = currentUser.UserId,
            LastModifiedAt = now,
            Notes = request.Notes
        };

        await SharedDbTransaction.ExecuteAsync(paymentsDbContext, async () =>
        {
            paymentsDbContext.MainPaymentLogs.Add(log);
            await AddPaymentAuditAsync(identityDbContext, currentUser, httpContext, "PaymentLogInitialized", log.Id, new { log.OperationId, log.TotalAmount, log.PaymentMethod }, now, cancellationToken);
            await paymentsDbContext.SaveChangesAsync(cancellationToken);
            await identityDbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken, identityDbContext, sharedDbContext);
        var userLookup = await LoadUserLookupAsync(identityDbContext, [log], cancellationToken);
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, [log.OperationId], cancellationToken);
        var cashRecords = await LoadCashRecordsForLogAsync(paymentsDbContext, log, cancellationToken);
        var adjustments = await LoadAdjustmentsForLogAsync(paymentsDbContext, log, cancellationToken);
        return await CompleteIdempotencyAsync(idempotency.Entry, ToDetailResponse(log, cashRecords, adjustments, userLookup, operationLookup), StatusCodes.Status201Created, paymentsDbContext, cancellationToken);
    }

    private static async Task<IResult> AssignPaymentLogAsync(
        Guid id,
        AssignPaymentRequest request,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var idempotency = await StartPaymentIdempotencyAsync(idempotencyKey, $"POST /api/v1/payments/{id}/assign", request, paymentsDbContext, clock, cancellationToken);
        if (idempotency.Result is not null)
        {
            return idempotency.Result;
        }

        MainPaymentLog? log = null;
        IResult? transactionResult = null;
        await SharedDbTransaction.ExecuteAsync(paymentsDbContext, async () =>
        {
            log = await LoadPaymentLogForUpdateAsync(id, paymentsDbContext, cancellationToken);
            if (log is null)
            {
                transactionResult = Results.NotFound();
                return;
            }

            if (log.Status == PaymentCompleted)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Completed payment logs cannot be assigned or reassigned."] });
                return;
            }

            var accountantUserId = request.AccountantUserId == Guid.Empty ? null : request.AccountantUserId;
            if (accountantUserId.HasValue &&
                !await identityDbContext.Users.AnyAsync(
                    user => user.Id == accountantUserId.Value &&
                        user.IsActive &&
                        user.Role == LenseeRoles.Accountant,
                    cancellationToken))
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.AccountantUserId)] = ["Assigned user must be an active Accountant."] });
                return;
            }

            var now = clock.EgyptNow;
            log.AssignedTo = accountantUserId;
            log.AssignedAt = now;
            log.Status = PendingAccountant;
            log.LastModifiedAt = now;
            log.LastModifiedBy = currentUser.UserId;
            await AddPaymentAuditAsync(identityDbContext, currentUser, httpContext, "PaymentAssigned", log.Id, new { log.AssignedTo }, now, cancellationToken);
            await paymentsDbContext.SaveChangesAsync(cancellationToken);
            await identityDbContext.SaveChangesAsync(cancellationToken);
            await eventPublisher.PublishAsync(new PaymentWorkflowChangedEvent(
                log.Id,
                log.MerchantId,
                log.OperationId,
                "PaymentAssigned",
                $"Payment log {log.Id:N} was assigned to accountant workflow.",
                log.AssignedTo,
                log.AssignedTo.HasValue ? null : LenseeRoles.Accountant,
                now),
                cancellationToken);
        }, cancellationToken, identityDbContext, sharedDbContext);

        if (transactionResult is not null)
        {
            return transactionResult;
        }

        var userLookup = await LoadUserLookupAsync(identityDbContext, [log!], cancellationToken);
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, [log!.OperationId], cancellationToken);
        var cashRecords = await LoadCashRecordsForLogAsync(paymentsDbContext, log!, cancellationToken);
        var adjustments = await LoadAdjustmentsForLogAsync(paymentsDbContext, log!, cancellationToken);
        return await CompleteIdempotencyAsync(idempotency.Entry, ToDetailResponse(log!, cashRecords, adjustments, userLookup, operationLookup), StatusCodes.Status200OK, paymentsDbContext, cancellationToken);
    }

    private static async Task<IResult> DraftSubLogAsync(
        Guid id,
        PaymentSubLogRequest request,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var idempotency = await StartPaymentIdempotencyAsync(idempotencyKey, $"POST /api/v1/payments/{id}/sub-logs", request, paymentsDbContext, clock, cancellationToken);
        if (idempotency.Result is not null)
        {
            return idempotency.Result;
        }

        if (request.Amount <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Amount)] = ["Amount must be greater than zero."] });
        }

        var paymentMethod = NormalizePaymentMethod(request.PaymentMethod);
        if (paymentMethod is not null && !PaymentMethods.Contains(paymentMethod))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PaymentMethod)] = ["Payment method must be CashHandToHand, CashTransaction, or Installment."] });
        }

        MainPaymentLog? log = null;
        IResult? transactionResult = null;
        await SharedDbTransaction.ExecuteAsync(paymentsDbContext, async () =>
        {
            log = await LoadPaymentLogForUpdateAsync(id, paymentsDbContext, cancellationToken);
            if (log is null)
            {
                transactionResult = Results.NotFound();
                return;
            }
            if (string.Equals(currentUser.Role, LenseeRoles.Accountant, StringComparison.OrdinalIgnoreCase) &&
                log.AssignedTo.HasValue &&
                log.AssignedTo != currentUser.UserId)
            {
                transactionResult = Results.Forbid();
                return;
            }
            if (log.Status == PaymentCompleted)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Completed payment logs cannot accept new draft entries."] });
                return;
            }

            RecalculateInstallmentAggregates(log);
            var remaining = log.TotalAmount - log.AmountPaid - log.PendingAmount;
            if (request.Amount > remaining)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Amount)] = [$"Amount exceeds remaining payable amount ({remaining:0.####})."] });
                return;
            }

            var now = clock.EgyptNow;
            var subLog = new InstallmentSubLog
            {
                Id = Guid.NewGuid(),
                MainLogId = log.Id,
                Amount = request.Amount,
                PaymentMethod = paymentMethod ?? log.PaymentMethod,
                DateReceived = request.DateReceived ?? DateOnly.FromDateTime(now),
                SubLogStatus = Draft,
                DraftedBy = currentUser.UserId ?? Guid.Empty,
                DraftedAt = now,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? "0" : request.Notes.Trim()
            };
            paymentsDbContext.InstallmentSubLogs.Add(subLog);
            log.PendingAmount += subLog.Amount;
            log.Status = PendingAdminReview;
            log.LastModifiedBy = currentUser.UserId;
            log.LastModifiedAt = now;

            await AddPaymentAuditAsync(identityDbContext, currentUser, httpContext, "PaymentSubLogDrafted", log.Id, new { subLog.Id, request.Amount }, now, cancellationToken);
            await eventPublisher.PublishAsync(new PaymentWorkflowChangedEvent(
                log.Id,
                log.MerchantId,
                log.OperationId,
                "PaymentSubLogDrafted",
                $"A payment sub-log for {request.Amount:0.####} is awaiting Admin review.",
                null,
                LenseeRoles.Admin,
                now),
                cancellationToken);
            await paymentsDbContext.SaveChangesAsync(cancellationToken);
            await identityDbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken, identityDbContext, sharedDbContext);

        if (transactionResult is not null)
        {
            return transactionResult;
        }

        var userLookup = await LoadUserLookupAsync(identityDbContext, [log!], cancellationToken);
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, [log!.OperationId], cancellationToken);
        var cashRecords = await LoadCashRecordsForLogAsync(paymentsDbContext, log!, cancellationToken);
        var adjustments = await LoadAdjustmentsForLogAsync(paymentsDbContext, log!, cancellationToken);
        return await CompleteIdempotencyAsync(idempotency.Entry, ToDetailResponse(log!, cashRecords, adjustments, userLookup, operationLookup), StatusCodes.Status201Created, paymentsDbContext, cancellationToken);
    }

    private static Task<IResult> ApproveSubLogAsync(
        Guid id,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) =>
        SetSubLogStatusAsync(id, ConfirmedPayment, null, paymentsDbContext, operationsDbContext, identityDbContext, sharedDbContext, httpContext, currentUser, eventPublisher, clock, idempotencyKey, cancellationToken);

    private static Task<IResult> RejectSubLogAsync(
        Guid id,
        RejectionRequest request,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) =>
        SetSubLogStatusAsync(id, Rejected, request.Reason, paymentsDbContext, operationsDbContext, identityDbContext, sharedDbContext, httpContext, currentUser, eventPublisher, clock, idempotencyKey, cancellationToken);

    private static async Task<IResult> ApproveCashReceiptAsync(
        Guid id,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var idempotency = await StartPaymentIdempotencyAsync(idempotencyKey, $"POST /api/v1/payments/cash-receipts/{id}/approve", new { id }, paymentsDbContext, clock, cancellationToken);
        if (idempotency.Result is not null)
        {
            return idempotency.Result;
        }

        MainPaymentLog? log = null;
        IResult? transactionResult = null;
        await SharedDbTransaction.ExecuteAsync(paymentsDbContext, async () =>
        {
            log = await LoadPaymentLogForUpdateAsync(id, paymentsDbContext, cancellationToken);
            if (log is null)
            {
                transactionResult = Results.NotFound();
                return;
            }
            if (!string.Equals(log.PaymentMethod, "CashHandToHand", StringComparison.OrdinalIgnoreCase))
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["paymentMethod"] = ["Only cash hand-to-hand receipts require this approval workflow."] });
                return;
            }
            if (log.Status == PaymentCompleted)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["This cash receipt is already approved."] });
                return;
            }

            var cashRecord = await paymentsDbContext.CashRecords
                .FirstOrDefaultAsync(value => value.OperationId == log.OperationId && value.PaymentType == CashReceived, cancellationToken);
            if (cashRecord is null)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["cashRecord"] = ["The cash receipt record was not found."] });
                return;
            }
            if (cashRecord.Status != PendingAccountant)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["This cash receipt was already changed by another user."] });
                return;
            }

            var now = clock.EgyptNow;
            cashRecord.Status = PaymentCompleted;
            log.AmountPaid = log.TotalAmount;
            log.PendingAmount = 0;
            log.Status = PaymentCompleted;
            log.LastModifiedBy = currentUser.UserId;
            log.LastModifiedAt = now;

            await AddPaymentAuditAsync(identityDbContext, currentUser, httpContext, "CashReceiptApproved", log.Id, new { cashRecord.Id, cashRecord.Amount }, now, cancellationToken);
            await eventPublisher.PublishAsync(new PaymentWorkflowChangedEvent(
                log.Id,
                log.MerchantId,
                log.OperationId,
                "CashReceiptApproved",
                $"Cash receipt {log.Id:N} was approved.",
                null,
                null,
                now), cancellationToken);
            await paymentsDbContext.SaveChangesAsync(cancellationToken);
            await identityDbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken, identityDbContext, sharedDbContext);

        if (transactionResult is not null)
        {
            return transactionResult;
        }

        var userLookup = await LoadUserLookupAsync(identityDbContext, [log!], cancellationToken);
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, [log!.OperationId], cancellationToken);
        var cashRecords = await LoadCashRecordsForLogAsync(paymentsDbContext, log!, cancellationToken);
        var adjustments = await LoadAdjustmentsForLogAsync(paymentsDbContext, log!, cancellationToken);
        return await CompleteIdempotencyAsync(idempotency.Entry, ToDetailResponse(log!, cashRecords, adjustments, userLookup, operationLookup), StatusCodes.Status200OK, paymentsDbContext, cancellationToken);
    }

    private static async Task<IResult> SetSubLogStatusAsync(
        Guid id,
        string status,
        string? rejectionReason,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        IClock clock,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var idempotency = await StartPaymentIdempotencyAsync(idempotencyKey, $"POST /api/v1/payments/sub-logs/{id}/{(status == ConfirmedPayment ? "approve" : "reject")}", new { id, status, rejectionReason }, paymentsDbContext, clock, cancellationToken);
        if (idempotency.Result is not null)
        {
            return idempotency.Result;
        }

        if (status == Rejected && string.IsNullOrWhiteSpace(rejectionReason))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(rejectionReason)] = ["Rejection reason is required."] });
        }

        InstallmentSubLog? subLog = null;
        MainPaymentLog? log = null;
        IResult? transactionResult = null;
        await SharedDbTransaction.ExecuteAsync(paymentsDbContext, async () =>
        {
            subLog = await paymentsDbContext.InstallmentSubLogs
                .Include(value => value.MainLog)
                .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
            if (subLog is null)
            {
                transactionResult = Results.NotFound();
                return;
            }

            log = await LoadPaymentLogForUpdateAsync(subLog.MainLogId, paymentsDbContext, cancellationToken);
            if (log is null)
            {
                transactionResult = Results.NotFound();
                return;
            }
            subLog = log.InstallmentSubLogs.Single(value => value.Id == id);
            if (subLog.SubLogStatus != Draft)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Only draft sub-logs can be approved or rejected."] });
                return;
            }

            RecalculateInstallmentAggregates(log);
            if (status == ConfirmedPayment && log.AmountPaid + log.PendingAmount > log.TotalAmount)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["Approving this entry would overpay the payment log."] });
                return;
            }

            var now = clock.EgyptNow;
            subLog.SubLogStatus = status;
            subLog.ConfirmedBy = currentUser.UserId;
            subLog.ConfirmedAt = now;
            subLog.RejectionReason = status == Rejected ? rejectionReason?.Trim() : null;

            RecalculateInstallmentAggregates(log);
            log.LastModifiedBy = currentUser.UserId;
            log.LastModifiedAt = now;

            await AddPaymentAuditAsync(identityDbContext, currentUser, httpContext, status == ConfirmedPayment ? "PaymentSubLogApproved" : "PaymentSubLogRejected", log.Id, new { subLog.Id, subLog.Amount, status }, now, cancellationToken);
            await eventPublisher.PublishAsync(new PaymentWorkflowChangedEvent(
                log.Id,
                log.MerchantId,
                log.OperationId,
                status == ConfirmedPayment ? "PaymentSubLogApproved" : "PaymentSubLogRejected",
                status == ConfirmedPayment
                    ? $"A payment sub-log for {subLog.Amount:0.####} was approved."
                    : $"A payment sub-log for {subLog.Amount:0.####} was rejected.",
                subLog.DraftedBy,
                null,
                now),
                cancellationToken);
            if (log.Status == PaymentCompleted)
            {
                await eventPublisher.PublishAsync(new PaymentWorkflowChangedEvent(
                    log.Id,
                    log.MerchantId,
                    log.OperationId,
                    "PaymentCompleted",
                    $"Payment log {log.Id:N} is completed.",
                    null,
                    LenseeRoles.Admin,
                    now),
                    cancellationToken);
            }

            await paymentsDbContext.SaveChangesAsync(cancellationToken);
            await identityDbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken, identityDbContext, sharedDbContext);

        if (transactionResult is not null)
        {
            return transactionResult;
        }

        var userLookup = await LoadUserLookupAsync(identityDbContext, [log!], cancellationToken);
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, [log!.OperationId], cancellationToken);
        var cashRecords = await LoadCashRecordsForLogAsync(paymentsDbContext, log!, cancellationToken);
        var adjustments = await LoadAdjustmentsForLogAsync(paymentsDbContext, log!, cancellationToken);
        return await CompleteIdempotencyAsync(idempotency.Entry, ToDetailResponse(log!, cashRecords, adjustments, userLookup, operationLookup), StatusCodes.Status200OK, paymentsDbContext, cancellationToken);
    }

    private static async Task<IResult> CreateCashRecordAsync(
        CashRecordRequest request,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var idempotency = await StartPaymentIdempotencyAsync(idempotencyKey, "POST /api/v1/payments/cash-records", request, paymentsDbContext, clock, cancellationToken);
        if (idempotency.Result is not null)
        {
            return idempotency.Result;
        }

        if (request.Amount <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Amount)] = ["Amount must be greater than zero."] });
        }
        if (NormalizeCashType(request.PaymentType) is not { } paymentType)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PaymentType)] = ["Payment type must be CashReceived or CashRefund."] });
        }
        var operation = await ResolveOperationReferenceAsync(operationsDbContext, request.OperationId, cancellationToken);
        if (operation is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.OperationId)] = ["Operation must exist. Use the full operation ID or operation code."] });
        }
        if (paymentType == CashRefund)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PaymentType)] = ["Cash refunds must be submitted through the adjustment approval workflow."] });
        }

        var record = new CashRecord
        {
            Id = Guid.NewGuid(),
            OperationId = operation.Id,
            PaymentType = paymentType,
            SubType = request.SubType,
            Amount = request.Amount,
            Status = PaymentCompleted,
            PaymentDate = clock.EgyptNow,
            CreatedBy = currentUser.UserId ?? Guid.Empty,
            Notes = request.Notes
        };
        await SharedDbTransaction.ExecuteAsync(paymentsDbContext, async () =>
        {
            paymentsDbContext.CashRecords.Add(record);
            await AddPaymentAuditAsync(identityDbContext, currentUser, httpContext, "CashReceiptRecorded", record.Id, new { record.OperationId, record.Amount }, record.PaymentDate, cancellationToken);
            await paymentsDbContext.SaveChangesAsync(cancellationToken);
            await identityDbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken, identityDbContext, sharedDbContext);

        var userLookup = await LoadUserLookupAsync(identityDbContext, [record], cancellationToken);
        return await CompleteIdempotencyAsync(idempotency.Entry, ToCashResponse(record, userLookup), StatusCodes.Status201Created, paymentsDbContext, cancellationToken);
    }

    private static async Task<OperationLog?> ResolveOperationReferenceAsync(
        OperationsDbContext operationsDbContext,
        string? operationReference,
        CancellationToken cancellationToken)
    {
        var reference = operationReference?.Trim();
        if (string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        if (Guid.TryParse(reference, out var operationId))
        {
            return await operationsDbContext.OperationLogs
                .FirstOrDefaultAsync(value => value.Id == operationId && !value.IsDeleted, cancellationToken);
        }

        return await operationsDbContext.OperationLogs
            .FirstOrDefaultAsync(value => value.OperationNumber == reference && !value.IsDeleted, cancellationToken);
    }

    private static async Task<List<CashRecord>> LoadCashRecordsForLogAsync(
        PaymentsDbContext paymentsDbContext,
        MainPaymentLog log,
        CancellationToken cancellationToken) =>
        await paymentsDbContext.CashRecords
            .Where(value => value.OperationId == log.OperationId)
            .OrderByDescending(value => value.PaymentDate)
            .ToListAsync(cancellationToken);

    private static async Task<List<FinancialAdjustment>> LoadAdjustmentsForLogAsync(
        PaymentsDbContext paymentsDbContext,
        MainPaymentLog log,
        CancellationToken cancellationToken) =>
        await paymentsDbContext.FinancialAdjustments
            .Where(value => log.MerchantId.HasValue &&
                value.MerchantId == log.MerchantId.Value &&
                (!value.OperationId.HasValue || value.OperationId == log.OperationId))
            .OrderByDescending(value => value.CreatedAt)
            .ToListAsync(cancellationToken);

    private static async Task<IResult> ListFinancialAdjustmentsAsync(
        Guid? merchantId,
        Guid? operationId,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        CancellationToken cancellationToken)
    {
        var query = paymentsDbContext.FinancialAdjustments.AsQueryable();
        if (merchantId.HasValue)
        {
            query = query.Where(adjustment => adjustment.MerchantId == merchantId.Value);
        }
        if (operationId.HasValue)
        {
            query = query.Where(adjustment => adjustment.OperationId == operationId.Value);
        }

        var rows = await query
            .OrderByDescending(adjustment => adjustment.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);
        var userLookup = await LoadUserLookupAsync(identityDbContext, rows, cancellationToken);
        var responses = rows.Select(adjustment => ToAdjustmentResponse(adjustment, userLookup)).ToList();

        return Results.Ok(responses);
    }

    private static async Task<IResult> CreateFinancialAdjustmentAsync(
        FinancialAdjustmentRequest request,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        CrmDbContext crmDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var idempotency = await StartPaymentIdempotencyAsync(idempotencyKey, "POST /api/v1/payments/adjustments", request, paymentsDbContext, clock, cancellationToken);
        if (idempotency.Result is not null)
        {
            return idempotency.Result;
        }

        if (request.Amount <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Amount)] = ["Amount must be greater than zero."] });
        }
        if (NormalizeAdjustmentType(request.AdjustmentType) is not { } adjustmentType)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.AdjustmentType)] = ["Adjustment type must be MerchantCredit, BalanceReduction, or CashRefund."] });
        }
        if (!await crmDbContext.Merchants.AnyAsync(merchant => merchant.Id == request.MerchantId && !merchant.IsDeleted, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.MerchantId)] = ["Merchant must exist."] });
        }

        var operationReference = request.OperationId?.Trim();
        if (string.IsNullOrWhiteSpace(operationReference))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.OperationId)] = ["Adjustment requests must reference a source operation. Legacy unlinked adjustments are read-only."] });
        }

        var operation = await ResolveOperationReferenceAsync(operationsDbContext, operationReference, cancellationToken);
        if (operation is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.OperationId)] = ["Operation must exist. Use the full operation ID or operation code."] });
        }
        if (operation.ClientId != request.MerchantId)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.OperationId)] = ["Operation must belong to the selected merchant."] });
        }

        MainPaymentLog? paymentLog = null;
        IResult? transactionResult = null;
        FinancialAdjustment? adjustment = null;
        var now = clock.EgyptNow;
        var userId = currentUser.UserId ?? Guid.Empty;
        await SharedDbTransaction.ExecuteAsync(paymentsDbContext, async () =>
        {
            paymentLog = await paymentsDbContext.MainPaymentLogs
                .Include(log => log.InstallmentSubLogs)
                .FirstOrDefaultAsync(log => log.OperationId == operation.Id && log.MerchantId == request.MerchantId && !log.IsDeleted, cancellationToken);
            if (paymentLog is null)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.OperationId)] = ["Adjustment requests must reference an existing source payment log."] });
                return;
            }

            paymentLog = await LoadPaymentLogForUpdateAsync(paymentLog.Id, paymentsDbContext, cancellationToken);
            var cap = await CalculateAdjustmentCapAsync(paymentsDbContext, paymentLog!, adjustmentType, null, cancellationToken);
            if (request.Amount > cap)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Amount)] = [$"Adjustment exceeds the remaining source cap ({cap:0.####})."] });
                return;
            }

            adjustment = new FinancialAdjustment
            {
                Id = Guid.NewGuid(),
                MerchantId = request.MerchantId,
                OperationId = operation.Id,
                PaymentLogId = paymentLog!.Id,
                AdjustmentType = adjustmentType,
                Amount = request.Amount,
                Status = PendingApproval,
                Notes = request.Notes,
                CreatedBy = userId,
                CreatedAt = now,
                LineageKind = "SourceLinked"
            };
            paymentsDbContext.FinancialAdjustments.Add(adjustment);
            await AddPaymentAuditAsync(identityDbContext, currentUser, httpContext, "FinancialAdjustmentRequested", paymentLog.Id, new { adjustment.Id, adjustmentType, request.Amount }, now, cancellationToken);
            await paymentsDbContext.SaveChangesAsync(cancellationToken);
            await identityDbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken, identityDbContext, sharedDbContext);

        if (transactionResult is not null)
        {
            return transactionResult;
        }

        var userLookup = await LoadUserLookupAsync(identityDbContext, [adjustment!], cancellationToken);
        return await CompleteIdempotencyAsync(idempotency.Entry, ToAdjustmentResponse(adjustment!, userLookup), StatusCodes.Status201Created, paymentsDbContext, cancellationToken);
    }

    private static async Task<IResult> ApproveFinancialAdjustmentAsync(
        Guid id,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var idempotency = await StartPaymentIdempotencyAsync(idempotencyKey, $"POST /api/v1/payments/adjustments/{id}/approve", new { id }, paymentsDbContext, clock, cancellationToken);
        if (idempotency.Result is not null)
        {
            return idempotency.Result;
        }
        if (!IsAdjustmentReviewer(currentUser))
        {
            return Results.Forbid();
        }

        FinancialAdjustment? adjustment = null;
        IResult? transactionResult = null;
        await SharedDbTransaction.ExecuteAsync(paymentsDbContext, async () =>
        {
            adjustment = await paymentsDbContext.FinancialAdjustments.FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
            if (adjustment is null)
            {
                transactionResult = Results.NotFound();
                return;
            }
            if (adjustment.Status != PendingApproval)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Only pending adjustment requests can be approved."] });
                return;
            }
            if (adjustment.CreatedBy == currentUser.UserId)
            {
                transactionResult = Results.Forbid();
                return;
            }
            if (adjustment.PaymentLogId is not { } paymentLogId)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["lineage"] = ["Legacy unlinked adjustments cannot be approved or reversed."] });
                return;
            }

            var paymentLog = await LoadPaymentLogForUpdateAsync(paymentLogId, paymentsDbContext, cancellationToken);
            if (paymentLog is null)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["paymentLog"] = ["The source payment log no longer exists."] });
                return;
            }
            var cap = await CalculateAdjustmentCapAsync(paymentsDbContext, paymentLog, adjustment.AdjustmentType, adjustment.Id, cancellationToken);
            if (adjustment.Amount > cap)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = [$"Adjustment exceeds the remaining source cap ({cap:0.####})."] });
                return;
            }

            var now = clock.EgyptNow;
            adjustment.Status = PaymentCompleted;
            adjustment.ReviewedBy = currentUser.UserId;
            adjustment.ReviewedAt = now;

            if (adjustment.AdjustmentType == CashRefund && adjustment.OperationId.HasValue)
            {
                paymentsDbContext.CashRecords.Add(new CashRecord
                {
                    Id = Guid.NewGuid(),
                    OperationId = adjustment.OperationId.Value,
                    PaymentType = CashRefund,
                    SubType = "AdjustmentApproval",
                    Amount = adjustment.Amount,
                    Status = PaymentCompleted,
                    PaymentDate = now,
                    CreatedBy = currentUser.UserId ?? Guid.Empty,
                    Notes = adjustment.Notes
                });
            }

            await AddPaymentAuditAsync(identityDbContext, currentUser, httpContext, "FinancialAdjustmentApproved", paymentLog.Id, new { adjustment.Id, adjustment.AdjustmentType, adjustment.Amount }, now, cancellationToken);
            await paymentsDbContext.SaveChangesAsync(cancellationToken);
            await identityDbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken, identityDbContext, sharedDbContext);

        if (transactionResult is not null)
        {
            return transactionResult;
        }

        var userLookup = await LoadUserLookupAsync(identityDbContext, [adjustment!], cancellationToken);
        return await CompleteIdempotencyAsync(idempotency.Entry, ToAdjustmentResponse(adjustment!, userLookup), StatusCodes.Status200OK, paymentsDbContext, cancellationToken);
    }

    private static async Task<IResult> RejectFinancialAdjustmentAsync(
        Guid id,
        RejectionRequest request,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var idempotency = await StartPaymentIdempotencyAsync(idempotencyKey, $"POST /api/v1/payments/adjustments/{id}/reject", request, paymentsDbContext, clock, cancellationToken);
        if (idempotency.Result is not null)
        {
            return idempotency.Result;
        }
        if (!IsAdjustmentReviewer(currentUser))
        {
            return Results.Forbid();
        }
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Reason)] = ["Rejection reason is required."] });
        }

        FinancialAdjustment? adjustment = null;
        IResult? transactionResult = null;
        await SharedDbTransaction.ExecuteAsync(paymentsDbContext, async () =>
        {
            adjustment = await paymentsDbContext.FinancialAdjustments.FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
            if (adjustment is null)
            {
                transactionResult = Results.NotFound();
                return;
            }
            if (adjustment.Status != PendingApproval)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Only pending adjustment requests can be rejected."] });
                return;
            }
            if (adjustment.CreatedBy == currentUser.UserId)
            {
                transactionResult = Results.Forbid();
                return;
            }

            var now = clock.EgyptNow;
            adjustment.Status = Rejected;
            adjustment.ReviewedBy = currentUser.UserId;
            adjustment.ReviewedAt = now;
            adjustment.RejectionReason = request.Reason.Trim();
            await AddPaymentAuditAsync(identityDbContext, currentUser, httpContext, "FinancialAdjustmentRejected", adjustment.PaymentLogId ?? adjustment.Id, new { adjustment.Id, adjustment.AdjustmentType, adjustment.Amount, adjustment.RejectionReason }, now, cancellationToken);
            await paymentsDbContext.SaveChangesAsync(cancellationToken);
            await identityDbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken, identityDbContext, sharedDbContext);

        if (transactionResult is not null)
        {
            return transactionResult;
        }

        var userLookup = await LoadUserLookupAsync(identityDbContext, [adjustment!], cancellationToken);
        return await CompleteIdempotencyAsync(idempotency.Entry, ToAdjustmentResponse(adjustment!, userLookup), StatusCodes.Status200OK, paymentsDbContext, cancellationToken);
    }

    private static async Task<IResult> GetMerchantBalanceAsync(
        Guid merchantId,
        MerchantBalanceService merchantBalanceService,
        CancellationToken cancellationToken)
    {
        var balance = await merchantBalanceService.CalculateAsync(merchantId, cancellationToken);
        return Results.Ok(balance);
    }

    private static async Task<decimal> CalculateAdjustmentCapAsync(
        PaymentsDbContext paymentsDbContext,
        MainPaymentLog paymentLog,
        string adjustmentType,
        Guid? excludingAdjustmentId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(adjustmentType, CashRefund, StringComparison.OrdinalIgnoreCase))
        {
            var completedReceipts = await paymentsDbContext.CashRecords
                .Where(record => record.OperationId == paymentLog.OperationId &&
                    record.PaymentType == CashReceived &&
                    record.Status == PaymentCompleted)
                .SumAsync(record => record.Amount, cancellationToken);
            var completedRefunds = await paymentsDbContext.CashRecords
                .Where(record => record.OperationId == paymentLog.OperationId &&
                    record.PaymentType == CashRefund &&
                    record.Status == PaymentCompleted)
                .SumAsync(record => record.Amount, cancellationToken);
            var pendingRefunds = await paymentsDbContext.FinancialAdjustments
                .Where(adjustment => adjustment.PaymentLogId == paymentLog.Id &&
                    adjustment.Id != excludingAdjustmentId &&
                    adjustment.AdjustmentType == CashRefund &&
                    adjustment.Status == PendingApproval)
                .SumAsync(adjustment => adjustment.Amount, cancellationToken);

            return Math.Max(completedReceipts - completedRefunds - pendingRefunds, 0);
        }

        if (string.Equals(adjustmentType, MerchantCredit, StringComparison.OrdinalIgnoreCase))
        {
            var priorCredits = await paymentsDbContext.FinancialAdjustments
                .Where(adjustment => adjustment.PaymentLogId == paymentLog.Id &&
                    adjustment.Id != excludingAdjustmentId &&
                    adjustment.AdjustmentType == MerchantCredit &&
                    (adjustment.Status == PendingApproval || adjustment.Status == PaymentCompleted))
                .SumAsync(adjustment => adjustment.Amount, cancellationToken);
            return Math.Max(paymentLog.AmountPaid - priorCredits, 0);
        }

        var priorReductions = await paymentsDbContext.FinancialAdjustments
            .Where(adjustment => adjustment.PaymentLogId == paymentLog.Id &&
                adjustment.Id != excludingAdjustmentId &&
                adjustment.AdjustmentType == BalanceReduction &&
                (adjustment.Status == PendingApproval || adjustment.Status == PaymentCompleted))
            .SumAsync(adjustment => adjustment.Amount, cancellationToken);
        return Math.Max(paymentLog.TotalAmount - paymentLog.AmountPaid - priorReductions, 0);
    }

    private static bool IsAdjustmentReviewer(ICurrentUser currentUser) =>
        string.Equals(currentUser.Role, LenseeRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(currentUser.Role, LenseeRoles.ERPAdmin, StringComparison.OrdinalIgnoreCase);

    public static async Task CreatePaymentArtifactsForCompletedSaleAsync(
        OperationLog operation,
        PaymentsDbContext paymentsDbContext,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (operation.OperationType is not (WholesaleSale or RetailSale) || operation.Status != Completed)
        {
            return;
        }

        var total = operation.OperationLines.Sum(line => line.LineTotal);
        if (total <= 0)
        {
            return;
        }

        if (string.Equals(operation.PaymentMethod, "CashHandToHand", StringComparison.OrdinalIgnoreCase))
        {
            var existingPaymentLog = await paymentsDbContext.MainPaymentLogs
                .FirstOrDefaultAsync(log => log.OperationId == operation.Id && !log.IsDeleted, cancellationToken);
            if (existingPaymentLog is not null)
            {
                return;
            }

            paymentsDbContext.CashRecords.Add(new CashRecord
            {
                Id = Guid.NewGuid(),
                OperationId = operation.Id,
                PaymentType = CashReceived,
                SubType = operation.PaymentMethod,
                Amount = total,
                Status = PendingAccountant,
                PaymentDate = now,
                CreatedBy = userId,
                Notes = "Auto-created from completed cash sale."
            });
            paymentsDbContext.MainPaymentLogs.Add(new MainPaymentLog
            {
                Id = Guid.NewGuid(),
                OperationId = operation.Id,
                MerchantId = operation.ClientId,
                TotalAmount = total,
                AmountPaid = 0,
                PaymentMethod = "CashHandToHand",
                Status = PendingAccountant,
                InitializedBy = userId,
                InitializedAt = now,
                LastModifiedBy = userId,
                LastModifiedAt = now,
                Notes = "Auto-created from completed cash sale."
            });
            await paymentsDbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (operation.ClientId is not { } merchantId)
        {
            return;
        }
        if (await paymentsDbContext.MainPaymentLogs.AnyAsync(log => log.OperationId == operation.Id && !log.IsDeleted, cancellationToken))
        {
            return;
        }

        paymentsDbContext.MainPaymentLogs.Add(new MainPaymentLog
        {
            Id = Guid.NewGuid(),
            OperationId = operation.Id,
            MerchantId = merchantId,
            TotalAmount = total,
            AmountPaid = 0,
            PaymentMethod = operation.PaymentMethod ?? "Installment",
            Status = PendingAdmin,
            InitializedBy = userId,
            InitializedAt = now,
            LastModifiedBy = userId,
            LastModifiedAt = now,
            Notes = "Auto-created from completed sale."
        });
        await paymentsDbContext.SaveChangesAsync(cancellationToken);
    }

    public static async Task<string?> GetRevisionBlockReasonForCompletedSaleAsync(
        Guid operationId,
        PaymentsDbContext paymentsDbContext,
        CancellationToken cancellationToken)
    {
        var paymentLog = await paymentsDbContext.MainPaymentLogs
            .Include(log => log.InstallmentSubLogs)
            .FirstOrDefaultAsync(log => log.OperationId == operationId && !log.IsDeleted, cancellationToken);
        if (paymentLog is not null && paymentLog.InstallmentSubLogs.Count > 0)
        {
            return "This sale already has payment sub-logs and cannot be revised from Operations.";
        }

        var adjustmentsExist = await paymentsDbContext.FinancialAdjustments
            .AnyAsync(adjustment => adjustment.OperationId == operationId && adjustment.Status == PaymentCompleted, cancellationToken);
        if (adjustmentsExist)
        {
            return "This sale already has financial adjustments and cannot be revised from Operations.";
        }

        return null;
    }

    public static async Task RemovePaymentArtifactsForSaleRevisionAsync(
        Guid operationId,
        PaymentsDbContext paymentsDbContext,
        CancellationToken cancellationToken)
    {
        var paymentLog = await paymentsDbContext.MainPaymentLogs
            .Include(log => log.InstallmentSubLogs)
            .FirstOrDefaultAsync(log => log.OperationId == operationId && !log.IsDeleted, cancellationToken);
        if (paymentLog is not null)
        {
            if (paymentLog.InstallmentSubLogs.Count > 0)
            {
                throw new InvalidOperationException("This sale already has payment sub-logs and cannot be revised from Operations.");
            }

            paymentsDbContext.MainPaymentLogs.Remove(paymentLog);
        }

        var adjustments = await paymentsDbContext.FinancialAdjustments
            .Where(adjustment => adjustment.OperationId == operationId && adjustment.Status == PaymentCompleted)
            .ToListAsync(cancellationToken);
        if (adjustments.Count > 0)
        {
            throw new InvalidOperationException("This sale already has financial adjustments and cannot be revised from Operations.");
        }

        var cashRecords = await paymentsDbContext.CashRecords
            .Where(record => record.OperationId == operationId)
            .ToListAsync(cancellationToken);
        if (cashRecords.Count > 1)
        {
            throw new InvalidOperationException("This sale already has multiple cash records and cannot be revised from Operations.");
        }

        if (cashRecords.Count == 1 && cashRecords[0].PaymentType != CashReceived)
        {
            throw new InvalidOperationException("This sale already has a refund cash record and cannot be revised from Operations.");
        }

        await RemoveCashRecordsForRevisionAsync(paymentsDbContext, cashRecords.Select(record => record.Id).ToArray(), cancellationToken);
        await paymentsDbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task RemoveCashRecordsForRevisionAsync(
        PaymentsDbContext paymentsDbContext,
        Guid[] cashRecordIds,
        CancellationToken cancellationToken)
    {
        if (cashRecordIds.Length == 0)
        {
            return;
        }

        if (paymentsDbContext.Database.IsRelational())
        {
            await paymentsDbContext.CashRecords
                .Where(record => cashRecordIds.Contains(record.Id))
                .ExecuteDeleteAsync(cancellationToken);
            foreach (var entry in paymentsDbContext.ChangeTracker.Entries<CashRecord>()
                .Where(entry => cashRecordIds.Contains(entry.Entity.Id))
                .ToList())
            {
                entry.State = EntityState.Detached;
            }
            return;
        }

        var existingRecords = await paymentsDbContext.CashRecords
            .Where(record => cashRecordIds.Contains(record.Id))
            .ToListAsync(cancellationToken);
        paymentsDbContext.CashRecords.RemoveRange(existingRecords);
    }

    private static decimal CalculatePaymentTotal(OperationLog operation)
    {
        if (operation.OperationType is WholesaleSale or RetailSale)
        {
            return operation.OperationLines.Sum(line => line.LineTotal);
        }
        if (operation.OperationType == Change)
        {
            return Math.Max(
                operation.OperationLines.Where(line => line.Section == ChangeIn).Sum(line => line.LineTotal) -
                operation.OperationLines.Where(line => line.Section == ChangeOut).Sum(line => line.LineTotal),
                0);
        }

        return 0;
    }

    private static async Task<Dictionary<Guid, PaymentOperationContext>> LoadPaymentOperationLookupAsync(
        OperationsDbContext operationsDbContext,
        IEnumerable<Guid> operationIds,
        CancellationToken cancellationToken)
    {
        var ids = operationIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await operationsDbContext.OperationLogs
            .Where(operation => ids.Contains(operation.Id))
            .Select(operation => new PaymentOperationContext(operation.Id, operation.OperationNumber, operation.OperationType, operation.ClientName))
            .ToDictionaryAsync(operation => operation.OperationId, cancellationToken);
    }

    private static PaymentLogListResponse ToListResponse(MainPaymentLog log, IReadOnlyDictionary<Guid, User> userLookup, IReadOnlyDictionary<Guid, PaymentOperationContext> operationLookup)
    {
        operationLookup.TryGetValue(log.OperationId, out var operation);
        return
        new(
            log.Id,
            log.OperationId,
            log.MerchantId,
            operation?.OperationNumber,
            operation?.OperationType,
            operation?.BuyerName,
            log.TotalAmount,
            log.AmountPaid,
            Math.Max(log.TotalAmount - log.AmountPaid, 0),
            log.PaymentMethod,
            log.Status,
            log.AssignedTo,
            log.LastModifiedAt,
            GetUserDisplayName(log.InitializedBy, userLookup),
            GetUserDisplayName(log.AssignedTo, userLookup),
            GetUserDisplayName(log.LastModifiedBy, userLookup));
    }

    private static PaymentLogDetailResponse ToDetailResponse(
        MainPaymentLog log,
        IReadOnlyList<CashRecord> cashRecords,
        IReadOnlyList<FinancialAdjustment> adjustments,
        IReadOnlyDictionary<Guid, User> userLookup,
        IReadOnlyDictionary<Guid, PaymentOperationContext> operationLookup)
    {
        var stages = BuildPaymentStages(log, cashRecords, adjustments, userLookup, operationLookup)
            .OrderBy(stage => stage.HappenedAt)
            .ToList();

        return
        new(
            ToListResponse(log, userLookup, operationLookup),
            log.InstallmentSubLogs.OrderByDescending(sub => sub.DraftedAt).Select(sub => new PaymentSubLogResponse(
                sub.Id,
                sub.Amount,
                sub.PaymentMethod,
                sub.DateReceived,
                sub.SubLogStatus,
                sub.DraftedBy,
                sub.DraftedAt,
                sub.ConfirmedBy,
                sub.ConfirmedAt,
                sub.RejectionReason,
                sub.Notes,
                GetUserDisplayName(sub.DraftedBy, userLookup),
                GetUserDisplayName(sub.ConfirmedBy, userLookup))).ToList(),
            cashRecords.OrderByDescending(record => record.PaymentDate).Select(record => ToCashResponse(record, userLookup)).ToList(),
            adjustments.OrderByDescending(adjustment => adjustment.CreatedAt).Select(adjustment => ToAdjustmentResponse(adjustment, userLookup)).ToList(),
            stages,
            log.Notes);
    }

    private static CashRecordResponse ToCashResponse(CashRecord record, IReadOnlyDictionary<Guid, User> userLookup) =>
        new(record.Id, record.OperationId, record.PaymentType, record.SubType, record.Amount, record.Status, record.PaymentDate, record.CreatedBy, GetUserDisplayName(record.CreatedBy, userLookup), record.Notes);

    private static string? NormalizePaymentMethod(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return string.Equals(trimmed, "Cash", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, "\u0646\u0642\u062f\u064a \u0645\u0628\u0627\u0634\u0631", StringComparison.OrdinalIgnoreCase)
            ? "CashHandToHand"
            : string.Equals(trimmed, "Card", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "Wallet", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(trimmed, "BankTransaction", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, "\u062a\u062d\u0648\u064a\u0644 \u0623\u0648 \u0625\u064a\u062f\u0627\u0639 \u0646\u0642\u062f\u064a", StringComparison.OrdinalIgnoreCase)
                    ? "CashTransaction"
                    : string.Equals(trimmed, "\u062a\u0642\u0633\u064a\u0637", StringComparison.OrdinalIgnoreCase)
                        ? "Installment"
                        : trimmed;
    }

    private static string? NormalizeCashType(string? value)
    {
        if (string.Equals(value, CashReceived, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Cash", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "\u0646\u0642\u062f\u064a \u0645\u0628\u0627\u0634\u0631", StringComparison.OrdinalIgnoreCase))
        {
            return CashReceived;
        }
        if (string.Equals(value, CashRefund, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Refund", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "\u0646\u0642\u062f\u064a \u0645\u0628\u0627\u0634\u0631", StringComparison.OrdinalIgnoreCase))
        {
            return CashRefund;
        }

        return null;
    }

    private static string? NormalizeAdjustmentType(string? value)
    {
        if (string.Equals(value, MerchantCredit, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "\u0631\u0635\u064a\u062f \u0644\u0644\u062a\u0627\u062c\u0631", StringComparison.OrdinalIgnoreCase))
        {
            return MerchantCredit;
        }
        if (string.Equals(value, BalanceReduction, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "\u062a\u062e\u0641\u064a\u0636 \u0627\u0644\u0631\u0635\u064a\u062f", StringComparison.OrdinalIgnoreCase))
        {
            return BalanceReduction;
        }
        if (string.Equals(value, CashRefund, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Refund", StringComparison.OrdinalIgnoreCase))
        {
            return CashRefund;
        }

        return null;
    }

    private static FinancialAdjustmentResponse ToAdjustmentResponse(FinancialAdjustment adjustment, IReadOnlyDictionary<Guid, User> userLookup) =>
        new(adjustment.Id, adjustment.MerchantId, adjustment.OperationId, adjustment.AdjustmentType, adjustment.Amount, adjustment.Status, adjustment.Notes, adjustment.CreatedBy, GetUserDisplayName(adjustment.CreatedBy, userLookup), adjustment.CreatedAt);

    private static IReadOnlyList<PaymentStageResponse> BuildPaymentStages(
        MainPaymentLog log,
        IReadOnlyList<CashRecord> cashRecords,
        IReadOnlyList<FinancialAdjustment> adjustments,
        IReadOnlyDictionary<Guid, User> userLookup,
        IReadOnlyDictionary<Guid, PaymentOperationContext> operationLookup)
    {
        operationLookup.TryGetValue(log.OperationId, out var operation);
        var stages = new List<PaymentStageResponse>
        {
            new(
                "PaymentLogOpened",
                log.InitializedAt,
                GetUserDisplayName(log.InitializedBy, userLookup),
                log.TotalAmount,
                log.PaymentMethod,
                log.Status,
                log.Notes,
                operation?.OperationNumber)
        };

        if (log.AssignedAt.HasValue)
        {
            stages.Add(new PaymentStageResponse(
                "PaymentAssigned",
                log.AssignedAt.Value,
                GetUserDisplayName(log.AssignedTo, userLookup) ?? GetUserDisplayName(log.LastModifiedBy, userLookup),
                log.TotalAmount,
                log.PaymentMethod,
                PendingAccountant,
                null,
                operation?.OperationNumber));
        }

        foreach (var subLog in log.InstallmentSubLogs.OrderBy(sub => sub.DraftedAt))
        {
            stages.Add(new PaymentStageResponse(
                "InstallmentDrafted",
                subLog.DraftedAt,
                GetUserDisplayName(subLog.DraftedBy, userLookup),
                subLog.Amount,
                subLog.PaymentMethod ?? log.PaymentMethod,
                subLog.SubLogStatus,
                subLog.Notes,
                operation?.OperationNumber));

            if (subLog.ConfirmedAt.HasValue)
            {
                stages.Add(new PaymentStageResponse(
                    subLog.SubLogStatus == Rejected ? "InstallmentRejected" : "InstallmentApproved",
                    subLog.ConfirmedAt.Value,
                    GetUserDisplayName(subLog.ConfirmedBy, userLookup),
                    subLog.Amount,
                    subLog.PaymentMethod ?? log.PaymentMethod,
                    subLog.SubLogStatus,
                    subLog.RejectionReason ?? subLog.Notes,
                    operation?.OperationNumber));
            }
        }

        foreach (var record in cashRecords.OrderBy(record => record.PaymentDate))
        {
            stages.Add(new PaymentStageResponse(
                record.PaymentType == CashRefund ? "CashRefundRecorded" : "CashReceiptRecorded",
                record.PaymentDate,
                GetUserDisplayName(record.CreatedBy, userLookup),
                record.Amount,
                record.SubType ?? record.PaymentType,
                record.Status,
                record.Notes,
                operation?.OperationNumber));
        }

        if (string.Equals(log.PaymentMethod, "CashHandToHand", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(log.Status, PaymentCompleted, StringComparison.OrdinalIgnoreCase))
        {
            stages.Add(new PaymentStageResponse(
                "CashReceiptApproved",
                log.LastModifiedAt,
                GetUserDisplayName(log.LastModifiedBy, userLookup),
                log.AmountPaid,
                log.PaymentMethod,
                log.Status,
                log.Notes,
                operation?.OperationNumber));
        }

        foreach (var adjustment in adjustments.OrderBy(adjustment => adjustment.CreatedAt))
        {
            stages.Add(new PaymentStageResponse(
                adjustment.AdjustmentType,
                adjustment.CreatedAt,
                GetUserDisplayName(adjustment.CreatedBy, userLookup),
                adjustment.Amount,
                adjustment.AdjustmentType,
                adjustment.Status,
                adjustment.Notes,
                operation?.OperationNumber));
        }

        return stages;
    }

    private static IReadOnlyList<PaymentHistoryResponse> BuildHistoryRowsFromLogs(
        IEnumerable<MainPaymentLog> logs,
        IReadOnlyDictionary<Guid, PaymentOperationContext> operationLookup,
        IReadOnlyDictionary<Guid, Merchant> merchantLookup,
        IReadOnlyDictionary<Guid, User> userLookup)
    {
        var rows = new List<PaymentHistoryResponse>();
        foreach (var log in logs)
        {
            operationLookup.TryGetValue(log.OperationId, out var operation);
            var merchant = log.MerchantId.HasValue && merchantLookup.TryGetValue(log.MerchantId.Value, out var merchantValue)
                ? merchantValue
                : null;

            rows.Add(new PaymentHistoryResponse(
                log.Id,
                "PaymentLogOpened",
                log.OperationId,
                operation?.OperationNumber,
                operation?.OperationType,
                log.MerchantId,
                merchant?.BusinessName ?? operation?.BuyerName,
                operation?.BuyerName,
                log.PaymentMethod,
                log.TotalAmount,
                log.Status,
                log.InitializedAt,
                GetUserDisplayName(log.InitializedBy, userLookup),
                log.Notes));

            foreach (var subLog in log.InstallmentSubLogs)
            {
                rows.Add(new PaymentHistoryResponse(
                    subLog.Id,
                    "InstallmentDrafted",
                    log.OperationId,
                    operation?.OperationNumber,
                    operation?.OperationType,
                    log.MerchantId,
                    merchant?.BusinessName ?? operation?.BuyerName,
                    operation?.BuyerName,
                    subLog.PaymentMethod ?? log.PaymentMethod,
                    subLog.Amount,
                    subLog.SubLogStatus,
                    subLog.DraftedAt,
                    GetUserDisplayName(subLog.DraftedBy, userLookup),
                    subLog.Notes));

                if (subLog.ConfirmedAt.HasValue)
                {
                    rows.Add(new PaymentHistoryResponse(
                        subLog.Id,
                        subLog.SubLogStatus == Rejected ? "InstallmentRejected" : "InstallmentApproved",
                        log.OperationId,
                        operation?.OperationNumber,
                        operation?.OperationType,
                        log.MerchantId,
                        merchant?.BusinessName ?? operation?.BuyerName,
                        operation?.BuyerName,
                        subLog.PaymentMethod ?? log.PaymentMethod,
                        subLog.Amount,
                        subLog.SubLogStatus,
                        subLog.ConfirmedAt.Value,
                        GetUserDisplayName(subLog.ConfirmedBy, userLookup),
                        subLog.RejectionReason ?? subLog.Notes));
                }
            }

            if (string.Equals(log.PaymentMethod, "CashHandToHand", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(log.Status, PaymentCompleted, StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(new PaymentHistoryResponse(
                    log.Id,
                    "CashReceiptApproved",
                    log.OperationId,
                    operation?.OperationNumber,
                    operation?.OperationType,
                    log.MerchantId,
                    merchant?.BusinessName ?? operation?.BuyerName,
                    operation?.BuyerName,
                    log.PaymentMethod,
                    log.AmountPaid,
                    log.Status,
                    log.LastModifiedAt,
                    GetUserDisplayName(log.LastModifiedBy, userLookup),
                    log.Notes));
            }
        }

        return rows;
    }

    private static IReadOnlyList<PaymentHistoryResponse> BuildHistoryRowsFromCashRecords(
        IEnumerable<CashRecord> records,
        IReadOnlyDictionary<Guid, PaymentOperationContext> operationLookup,
        IReadOnlyDictionary<Guid, Merchant> merchantLookup,
        IReadOnlyDictionary<Guid, User> userLookup)
    {
        var rows = new List<PaymentHistoryResponse>();
        foreach (var record in records)
        {
            operationLookup.TryGetValue(record.OperationId, out var operation);

            rows.Add(new PaymentHistoryResponse(
                record.Id,
                record.PaymentType == CashRefund ? "CashRefundRecorded" : "CashReceiptRecorded",
                record.OperationId,
                operation?.OperationNumber,
                operation?.OperationType,
                null,
                operation?.BuyerName,
                operation?.BuyerName,
                record.SubType ?? record.PaymentType,
                record.Amount,
                record.Status,
                record.PaymentDate,
                GetUserDisplayName(record.CreatedBy, userLookup),
                record.Notes));
        }

        return rows;
    }

    private static IReadOnlyList<PaymentHistoryResponse> BuildHistoryRowsFromAdjustments(
        IEnumerable<FinancialAdjustment> adjustments,
        IReadOnlyDictionary<Guid, PaymentOperationContext> operationLookup,
        IReadOnlyDictionary<Guid, Merchant> merchantLookup,
        IReadOnlyDictionary<Guid, User> userLookup)
    {
        var rows = new List<PaymentHistoryResponse>();
        foreach (var adjustment in adjustments)
        {
            var operation = adjustment.OperationId.HasValue && operationLookup.TryGetValue(adjustment.OperationId.Value, out var operationValue)
                ? operationValue
                : null;
            merchantLookup.TryGetValue(adjustment.MerchantId, out var merchant);

            rows.Add(new PaymentHistoryResponse(
                adjustment.Id,
                adjustment.AdjustmentType,
                adjustment.OperationId,
                operation?.OperationNumber,
                operation?.OperationType,
                adjustment.MerchantId,
                merchant?.BusinessName ?? operation?.BuyerName,
                operation?.BuyerName,
                adjustment.AdjustmentType,
                adjustment.Amount,
                adjustment.Status,
                adjustment.CreatedAt,
                GetUserDisplayName(adjustment.CreatedBy, userLookup),
                adjustment.Notes));
        }

        return rows;
    }

    private static async Task<Dictionary<Guid, User>> LoadUserLookupAsync(
        IdentityDbContext identityDbContext,
        IEnumerable<MainPaymentLog> logs,
        CancellationToken cancellationToken)
    {
        var ids = logs
            .SelectMany(log => new Guid?[] { log.InitializedBy, log.AssignedTo, log.LastModifiedBy }
                .Concat(log.InstallmentSubLogs.Select(sub => (Guid?)sub.DraftedBy))
                .Concat(log.InstallmentSubLogs.Select(sub => sub.ConfirmedBy)))
            .Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        return await identityDbContext.Users
            .Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, cancellationToken);
    }

    private static async Task<Dictionary<Guid, User>> LoadUserLookupAsync(
        IdentityDbContext identityDbContext,
        IEnumerable<CashRecord> records,
        CancellationToken cancellationToken)
    {
        var ids = records
            .Select(record => record.CreatedBy)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        return await identityDbContext.Users
            .Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, cancellationToken);
    }

    private static async Task<Dictionary<Guid, User>> LoadUserLookupAsync(
        IdentityDbContext identityDbContext,
        IEnumerable<FinancialAdjustment> adjustments,
        CancellationToken cancellationToken)
    {
        var ids = adjustments
            .Select(adjustment => adjustment.CreatedBy)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        return await identityDbContext.Users
            .Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, cancellationToken);
    }

    private static async Task<Dictionary<Guid, User>> LoadUserLookupAsync(
        IdentityDbContext identityDbContext,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken)
    {
        var distinctIds = ids.Distinct().ToArray();
        if (distinctIds.Length == 0)
        {
            return [];
        }

        return await identityDbContext.Users
            .Where(user => distinctIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, cancellationToken);
    }

    private static async Task<Dictionary<Guid, Merchant>> LoadMerchantLookupAsync(
        CrmDbContext crmDbContext,
        IEnumerable<Guid> merchantIds,
        CancellationToken cancellationToken)
    {
        var ids = merchantIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await crmDbContext.Merchants
            .Where(merchant => ids.Contains(merchant.Id) && !merchant.IsDeleted)
            .ToDictionaryAsync(merchant => merchant.Id, cancellationToken);
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

    private static async Task<IdempotencyStart> StartPaymentIdempotencyAsync(
        string? idempotencyKey,
        string scope,
        object request,
        PaymentsDbContext paymentsDbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(idempotencyKey, out var parsedKey) || parsedKey == Guid.Empty)
        {
            return new(null, Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Idempotency-Key"] = ["A valid UUID Idempotency-Key header is required for payment mutations."]
            }));
        }

        var now = clock.EgyptNow;
        var requestHash = ComputeRequestHash(scope, request);
        var existing = await paymentsDbContext.PaymentIdempotencyKeys
            .FirstOrDefaultAsync(entry => entry.Key == parsedKey && entry.Scope == scope, cancellationToken);
        if (existing is not null)
        {
            existing.LastSeenAt = now;
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return new(null, Results.Conflict(new
                {
                    error = "Idempotency-Key was already used with a different request payload."
                }));
            }

            if (existing.Status == IdempotencyCompleted && existing.ResponseBody is not null && existing.ResponseStatusCode.HasValue)
            {
                using var document = JsonDocument.Parse(existing.ResponseBody);
                return new(null, Results.Json(document.RootElement.Clone(), statusCode: existing.ResponseStatusCode.Value, options: JsonOptions));
            }

            return new(null, Results.Conflict(new
            {
                error = "The idempotent payment request is already in progress."
            }));
        }

        var entry = new PaymentIdempotencyKey
        {
            Id = Guid.NewGuid(),
            Key = parsedKey,
            Scope = scope,
            RequestHash = requestHash,
            Status = IdempotencyPending,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now.AddDays(90)
        };
        paymentsDbContext.PaymentIdempotencyKeys.Add(entry);
        return new(entry, null);
    }

    private static async Task<IResult> CompleteIdempotencyAsync(
        PaymentIdempotencyKey? idempotency,
        object response,
        int statusCode,
        PaymentsDbContext paymentsDbContext,
        CancellationToken cancellationToken)
    {
        if (idempotency is not null)
        {
            idempotency.Status = IdempotencyCompleted;
            idempotency.ResponseStatusCode = statusCode;
            idempotency.ResponseBody = JsonSerializer.Serialize(response, JsonOptions);
            await paymentsDbContext.SaveChangesAsync(cancellationToken);
        }

        return statusCode == StatusCodes.Status201Created
            ? Results.Json(response, statusCode: StatusCodes.Status201Created, options: JsonOptions)
            : Results.Json(response, statusCode: statusCode, options: JsonOptions);
    }

    private static string ComputeRequestHash(string scope, object request)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { scope, request }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static async Task<MainPaymentLog?> LoadPaymentLogForUpdateAsync(
        Guid id,
        PaymentsDbContext paymentsDbContext,
        CancellationToken cancellationToken)
    {
        if (paymentsDbContext.Database.IsRelational())
        {
            await paymentsDbContext.Database.ExecuteSqlInterpolatedAsync(
                $"select 1 from payments.main_payment_logs where id = {id} and is_deleted = false for update",
                cancellationToken);
        }

        return await paymentsDbContext.MainPaymentLogs
            .Include(value => value.InstallmentSubLogs)
            .FirstOrDefaultAsync(value => value.Id == id && !value.IsDeleted, cancellationToken);
    }

    private static void RecalculateInstallmentAggregates(MainPaymentLog log)
    {
        log.AmountPaid = log.InstallmentSubLogs
            .Where(value => value.SubLogStatus == ConfirmedPayment)
            .Sum(value => value.Amount);
        log.PendingAmount = log.InstallmentSubLogs
            .Where(value => value.SubLogStatus == Draft)
            .Sum(value => value.Amount);
        log.Status = log.AmountPaid >= log.TotalAmount ? PaymentCompleted : PendingAccountant;
    }

    private static async Task AddPaymentAuditAsync(
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        HttpContext httpContext,
        string action,
        Guid paymentLogId,
        object changedFields,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return;
        }

        var actor = await identityDbContext.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.FullName, user.Username, user.Role })
            .SingleOrDefaultAsync(cancellationToken);

        identityDbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = "Payment",
            EntityId = paymentLogId,
            Action = action,
            ChangedFields = JsonSerializer.Serialize(changedFields, JsonOptions),
            UserId = userId,
            ActorType = actor?.Role ?? currentUser.Role,
            ActorName = actor is null
                ? "Full name unavailable"
                : string.IsNullOrWhiteSpace(actor.FullName) ? actor.Username : actor.FullName,
            IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = now
        });
        httpContext.Items[AuditMutationMiddleware.AuditWrittenItemKey] = true;
    }
}

internal sealed record IdempotencyStart(PaymentIdempotencyKey? Entry, IResult? Result);

public sealed record InitializePaymentRequest(Guid OperationId, string? PaymentMethod, string? Notes);

public sealed record AssignPaymentRequest(Guid? AccountantUserId);

public sealed record PaymentSubLogRequest(decimal Amount, string? PaymentMethod, DateOnly? DateReceived, string? Notes);

public sealed record RejectionRequest(string? Reason);

public sealed record CashRecordRequest(string? OperationId, string? PaymentType, string? SubType, decimal Amount, string? Notes);

public sealed record FinancialAdjustmentRequest(Guid MerchantId, string? OperationId, string AdjustmentType, decimal Amount, string? Notes);

public sealed record PaymentLogListResponse(Guid Id, Guid OperationId, Guid? MerchantId, string? OperationNumber, string? OperationType, string? BuyerName, decimal TotalAmount, decimal AmountPaid, decimal RemainingAmount, string PaymentMethod, string Status, Guid? AssignedTo, DateTime LastModifiedAt, string? InitializedByName, string? AssignedToName, string? LastModifiedByName);

public sealed record PaymentLogDetailResponse(PaymentLogListResponse Log, IReadOnlyList<PaymentSubLogResponse> SubLogs, IReadOnlyList<CashRecordResponse> CashRecords, IReadOnlyList<FinancialAdjustmentResponse> Adjustments, IReadOnlyList<PaymentStageResponse> Stages, string? Notes);

public sealed record PaymentSubLogResponse(Guid Id, decimal Amount, string? PaymentMethod, DateOnly DateReceived, string Status, Guid DraftedBy, DateTime DraftedAt, Guid? ConfirmedBy, DateTime? ConfirmedAt, string? RejectionReason, string? Notes, string? DraftedByName, string? ConfirmedByName);

public sealed record CashRecordResponse(Guid Id, Guid OperationId, string PaymentType, string? SubType, decimal Amount, string Status, DateTime PaymentDate, Guid CreatedBy, string? CreatedByName, string? Notes);

public sealed record FinancialAdjustmentResponse(Guid Id, Guid MerchantId, Guid? OperationId, string AdjustmentType, decimal Amount, string Status, string? Notes, Guid CreatedBy, string? CreatedByName, DateTime CreatedAt);

public sealed record PaymentHistoryResponse(Guid Id, string RecordType, Guid? OperationId, string? OperationNumber, string? OperationType, Guid? MerchantId, string? MerchantName, string? BuyerName, string? PaymentMethod, decimal Amount, string Status, DateTime HappenedAt, string? ActorName, string? Notes);

public sealed record PaymentStageResponse(string StageType, DateTime HappenedAt, string? ActorName, decimal Amount, string? PaymentMethod, string Status, string? Notes, string? OperationNumber);

internal sealed record PaymentOperationContext(Guid OperationId, string OperationNumber, string OperationType, string? BuyerName);
