using Lensee.Host.Infrastructure;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Primitives;
using Lensee.SharedKernel.Security;
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
    private const string PaymentCompleted = "Completed";

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
        group.MapPost("/adjustments", CreateFinancialAdjustmentAsync).RequireAuthorization("payments.write");

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
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
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
            PaymentMethod = paymentMethod ?? operation.PaymentMethod ?? "Installment",
            Status = PendingAdmin,
            InitializedBy = currentUser.UserId ?? Guid.Empty,
            InitializedAt = now,
            LastModifiedBy = currentUser.UserId,
            LastModifiedAt = now,
            Notes = request.Notes
        };

        paymentsDbContext.MainPaymentLogs.Add(log);
        await paymentsDbContext.SaveChangesAsync(cancellationToken);
        var userLookup = await LoadUserLookupAsync(identityDbContext, [log], cancellationToken);
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, [log.OperationId], cancellationToken);
        var cashRecords = await LoadCashRecordsForLogAsync(paymentsDbContext, log, cancellationToken);
        var adjustments = await LoadAdjustmentsForLogAsync(paymentsDbContext, log, cancellationToken);
        return Results.Created($"/api/v1/payments/{log.Id}", ToDetailResponse(log, cashRecords, adjustments, userLookup, operationLookup));
    }

    private static async Task<IResult> AssignPaymentLogAsync(
        Guid id,
        AssignPaymentRequest request,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var log = await paymentsDbContext.MainPaymentLogs.FirstOrDefaultAsync(value => value.Id == id && !value.IsDeleted, cancellationToken);
        if (log is null)
        {
            return Results.NotFound();
        }

        if (log.Status == PaymentCompleted)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Completed payment logs cannot be assigned or reassigned."] });
        }

        var accountantUserId = request.AccountantUserId == Guid.Empty ? null : request.AccountantUserId;
        if (accountantUserId.HasValue &&
            !await identityDbContext.Users.AnyAsync(
                user => user.Id == accountantUserId.Value &&
                    user.IsActive &&
                    user.Role == LenseeRoles.Accountant,
                cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.AccountantUserId)] = ["Assigned user must be an active Accountant."] });
        }

        log.AssignedTo = accountantUserId;
        log.AssignedAt = clock.EgyptNow;
        log.Status = PendingAccountant;
        log.LastModifiedAt = clock.EgyptNow;
        log.LastModifiedBy = currentUser.UserId;
        await paymentsDbContext.SaveChangesAsync(cancellationToken);
        await eventPublisher.PublishAsync(new PaymentWorkflowChangedEvent(
            log.Id,
            log.MerchantId,
            log.OperationId,
            "PaymentAssigned",
            $"Payment log {log.Id:N} was assigned to accountant workflow.",
            log.AssignedTo,
            log.AssignedTo.HasValue ? null : LenseeRoles.Accountant,
            clock.EgyptNow),
            cancellationToken);
        var userLookup = await LoadUserLookupAsync(identityDbContext, [log], cancellationToken);
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, [log.OperationId], cancellationToken);
        var cashRecords = await LoadCashRecordsForLogAsync(paymentsDbContext, log, cancellationToken);
        var adjustments = await LoadAdjustmentsForLogAsync(paymentsDbContext, log, cancellationToken);
        return Results.Ok(ToDetailResponse(log, cashRecords, adjustments, userLookup, operationLookup));
    }

    private static async Task<IResult> DraftSubLogAsync(
        Guid id,
        PaymentSubLogRequest request,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (request.Amount <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Amount)] = ["Amount must be greater than zero."] });
        }

        var paymentMethod = NormalizePaymentMethod(request.PaymentMethod);
        if (paymentMethod is not null && !PaymentMethods.Contains(paymentMethod))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PaymentMethod)] = ["Payment method must be CashHandToHand, CashTransaction, or Installment."] });
        }

        var log = await paymentsDbContext.MainPaymentLogs
            .Include(value => value.InstallmentSubLogs)
            .FirstOrDefaultAsync(value => value.Id == id && !value.IsDeleted, cancellationToken);
        if (log is null)
        {
            return Results.NotFound();
        }
        if (string.Equals(currentUser.Role, LenseeRoles.Accountant, StringComparison.OrdinalIgnoreCase) &&
            log.AssignedTo.HasValue &&
            log.AssignedTo != currentUser.UserId)
        {
            return Results.Forbid();
        }
        if (log.Status == PaymentCompleted)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Completed payment logs cannot accept new draft entries."] });
        }
        var openAmount = log.InstallmentSubLogs
            .Where(subLog => subLog.SubLogStatus == Draft || subLog.SubLogStatus == ConfirmedPayment)
            .Sum(subLog => subLog.Amount);
        var remaining = log.TotalAmount - openAmount;
        if (request.Amount > remaining)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Amount)] = [$"Amount exceeds remaining payable amount ({remaining:0.####})."] });
        }

        var subLog = new InstallmentSubLog
        {
            Id = Guid.NewGuid(),
            MainLogId = log.Id,
            Amount = request.Amount,
            PaymentMethod = paymentMethod ?? log.PaymentMethod,
            DateReceived = request.DateReceived ?? DateOnly.FromDateTime(clock.EgyptNow),
            SubLogStatus = Draft,
            DraftedBy = currentUser.UserId ?? Guid.Empty,
            DraftedAt = clock.EgyptNow,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? "0" : request.Notes.Trim()
        };
        paymentsDbContext.InstallmentSubLogs.Add(subLog);
        log.Status = PendingAdminReview;
        log.LastModifiedBy = currentUser.UserId;
        log.LastModifiedAt = clock.EgyptNow;
        await paymentsDbContext.SaveChangesAsync(cancellationToken);
        await eventPublisher.PublishAsync(new PaymentWorkflowChangedEvent(
            log.Id,
            log.MerchantId,
            log.OperationId,
            "PaymentSubLogDrafted",
            $"A payment sub-log for {request.Amount:0.####} is awaiting Admin review.",
            null,
            LenseeRoles.Admin,
            clock.EgyptNow),
            cancellationToken);
        var userLookup = await LoadUserLookupAsync(identityDbContext, [log], cancellationToken);
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, [log.OperationId], cancellationToken);
        var cashRecords = await LoadCashRecordsForLogAsync(paymentsDbContext, log, cancellationToken);
        var adjustments = await LoadAdjustmentsForLogAsync(paymentsDbContext, log, cancellationToken);
        return Results.Created($"/api/v1/payments/{log.Id}", ToDetailResponse(log, cashRecords, adjustments, userLookup, operationLookup));
    }

    private static Task<IResult> ApproveSubLogAsync(
        Guid id,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        IClock clock,
        CancellationToken cancellationToken) =>
        SetSubLogStatusAsync(id, ConfirmedPayment, null, paymentsDbContext, operationsDbContext, identityDbContext, currentUser, eventPublisher, clock, cancellationToken);

    private static Task<IResult> RejectSubLogAsync(
        Guid id,
        RejectionRequest request,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        IClock clock,
        CancellationToken cancellationToken) =>
        SetSubLogStatusAsync(id, Rejected, request.Reason, paymentsDbContext, operationsDbContext, identityDbContext, currentUser, eventPublisher, clock, cancellationToken);

    private static async Task<IResult> ApproveCashReceiptAsync(
        Guid id,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var log = await paymentsDbContext.MainPaymentLogs
            .Include(value => value.InstallmentSubLogs)
            .FirstOrDefaultAsync(value => value.Id == id && !value.IsDeleted, cancellationToken);
        if (log is null)
        {
            return Results.NotFound();
        }
        if (!string.Equals(log.PaymentMethod, "CashHandToHand", StringComparison.OrdinalIgnoreCase))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["paymentMethod"] = ["Only cash hand-to-hand receipts require this approval workflow."] });
        }
        if (log.Status == PaymentCompleted)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["This cash receipt is already approved."] });
        }

        var cashRecord = await paymentsDbContext.CashRecords
            .FirstOrDefaultAsync(value => value.OperationId == log.OperationId && value.PaymentType == CashReceived, cancellationToken);
        if (cashRecord is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["cashRecord"] = ["The cash receipt record was not found."] });
        }

        var now = clock.EgyptNow;
        if (paymentsDbContext.Database.IsRelational())
        {
            var updatedRows = await paymentsDbContext.CashRecords
                .Where(value => value.Id == cashRecord.Id && value.Status == PendingAccountant)
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.Status, PaymentCompleted), cancellationToken);
            if (updatedRows == 0 && cashRecord.Status != PaymentCompleted)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["This cash receipt was already changed by another user."] });
            }
        }

        cashRecord.Status = PaymentCompleted;
        log.AmountPaid = log.TotalAmount;
        log.Status = PaymentCompleted;
        log.LastModifiedBy = currentUser.UserId;
        log.LastModifiedAt = now;
        await paymentsDbContext.SaveChangesAsync(cancellationToken);
        await eventPublisher.PublishAsync(new PaymentWorkflowChangedEvent(
            log.Id,
            log.MerchantId,
            log.OperationId,
            "CashReceiptApproved",
            $"Cash receipt {log.Id:N} was approved.",
            null,
            null,
            now), cancellationToken);

        var userLookup = await LoadUserLookupAsync(identityDbContext, [log], cancellationToken);
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, [log.OperationId], cancellationToken);
        var cashRecords = await LoadCashRecordsForLogAsync(paymentsDbContext, log, cancellationToken);
        var adjustments = await LoadAdjustmentsForLogAsync(paymentsDbContext, log, cancellationToken);
        return Results.Ok(ToDetailResponse(log, cashRecords, adjustments, userLookup, operationLookup));
    }

    private static async Task<IResult> SetSubLogStatusAsync(
        Guid id,
        string status,
        string? rejectionReason,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var subLog = await paymentsDbContext.InstallmentSubLogs
            .Include(value => value.MainLog)
            .ThenInclude(log => log.InstallmentSubLogs)
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (subLog is null)
        {
            return Results.NotFound();
        }
        if (status == Rejected && string.IsNullOrWhiteSpace(rejectionReason))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(rejectionReason)] = ["Rejection reason is required."] });
        }
        if (subLog.SubLogStatus != Draft)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Only draft sub-logs can be approved or rejected."] });
        }

        var log = subLog.MainLog;
        if (status == ConfirmedPayment)
        {
            var confirmedBefore = log.InstallmentSubLogs
                .Where(value => value.Id != subLog.Id && value.SubLogStatus == ConfirmedPayment)
                .Sum(value => value.Amount);
            if (confirmedBefore + subLog.Amount > log.TotalAmount)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["Approving this entry would overpay the payment log."] });
            }
        }

        var now = clock.EgyptNow;
        if (paymentsDbContext.Database.IsRelational())
        {
            var updatedRows = await paymentsDbContext.InstallmentSubLogs
                .Where(value => value.Id == id && value.SubLogStatus == Draft)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(value => value.SubLogStatus, status)
                        .SetProperty(value => value.ConfirmedBy, currentUser.UserId)
                        .SetProperty(value => value.ConfirmedAt, now)
                        .SetProperty(value => value.RejectionReason, status == Rejected ? rejectionReason!.Trim() : null),
                    cancellationToken);
            if (updatedRows == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Only draft sub-logs can be approved or rejected."] });
            }
        }

        subLog.SubLogStatus = status;
        subLog.ConfirmedBy = currentUser.UserId;
        subLog.ConfirmedAt = now;
        subLog.RejectionReason = status == Rejected ? rejectionReason?.Trim() : null;

        log.AmountPaid = await paymentsDbContext.InstallmentSubLogs
            .Where(value => value.MainLogId == log.Id && value.SubLogStatus == ConfirmedPayment)
            .SumAsync(value => value.Amount, cancellationToken);
        log.Status = log.AmountPaid >= log.TotalAmount ? PaymentCompleted : PendingAccountant;
        log.LastModifiedBy = currentUser.UserId;
        log.LastModifiedAt = now;
        await paymentsDbContext.SaveChangesAsync(cancellationToken);
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
            clock.EgyptNow),
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
                clock.EgyptNow),
                cancellationToken);
        }
        var userLookup = await LoadUserLookupAsync(identityDbContext, [log], cancellationToken);
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, [log.OperationId], cancellationToken);
        var cashRecords = await LoadCashRecordsForLogAsync(paymentsDbContext, log, cancellationToken);
        var adjustments = await LoadAdjustmentsForLogAsync(paymentsDbContext, log, cancellationToken);
        return Results.Ok(ToDetailResponse(log, cashRecords, adjustments, userLookup, operationLookup));
    }

    private static async Task<IResult> CreateCashRecordAsync(
        CashRecordRequest request,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
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
        paymentsDbContext.CashRecords.Add(record);
        await paymentsDbContext.SaveChangesAsync(cancellationToken);
        var userLookup = await LoadUserLookupAsync(identityDbContext, [record], cancellationToken);
        return Results.Created($"/api/v1/payments/cash-records/{record.Id}", ToCashResponse(record, userLookup));
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
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
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

        OperationLog? operation = null;
        var operationReference = request.OperationId?.Trim();
        if (!string.IsNullOrWhiteSpace(operationReference))
        {
            operation = await ResolveOperationReferenceAsync(operationsDbContext, operationReference, cancellationToken);
            if (operation is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.OperationId)] = ["Operation must exist. Use the full operation ID or operation code."] });
            }
            if (operation.ClientId != request.MerchantId)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.OperationId)] = ["Operation must belong to the selected merchant."] });
            }
        }

        var now = clock.EgyptNow;
        var userId = currentUser.UserId ?? Guid.Empty;
        var adjustment = new FinancialAdjustment
        {
            Id = Guid.NewGuid(),
            MerchantId = request.MerchantId,
            OperationId = operation?.Id,
            AdjustmentType = adjustmentType,
            Amount = request.Amount,
            Status = PaymentCompleted,
            Notes = request.Notes,
            CreatedBy = userId,
            CreatedAt = now
        };
        paymentsDbContext.FinancialAdjustments.Add(adjustment);

        if (adjustmentType == CashRefund)
        {
            if (operation is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.OperationId)] = ["Cash refund requires an operation."] });
            }

            paymentsDbContext.CashRecords.Add(new CashRecord
            {
                Id = Guid.NewGuid(),
                OperationId = operation.Id,
                PaymentType = CashRefund,
                SubType = "HandToHand",
                Amount = request.Amount,
                Status = PaymentCompleted,
                PaymentDate = now,
                CreatedBy = userId,
                Notes = request.Notes
            });
        }

        await paymentsDbContext.SaveChangesAsync(cancellationToken);
        var userLookup = await LoadUserLookupAsync(identityDbContext, [adjustment], cancellationToken);
        return Results.Created($"/api/v1/payments/adjustments/{adjustment.Id}", ToAdjustmentResponse(adjustment, userLookup));
    }

    private static async Task<IResult> GetMerchantBalanceAsync(
        Guid merchantId,
        MerchantBalanceService merchantBalanceService,
        CancellationToken cancellationToken)
    {
        var balance = await merchantBalanceService.CalculateAsync(merchantId, cancellationToken);
        return Results.Ok(balance);
    }

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
}

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
