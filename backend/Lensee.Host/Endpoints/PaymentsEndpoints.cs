using System.Numerics;
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
    private const string AdditionalCharge = "AdditionalCharge";
    private const string BalanceReduction = "BalanceReduction";
    private const string Draft = "Draft";
    private const string ConfirmedPayment = "Confirmed";
    private const string Rejected = "Rejected";
    private const string PendingAdmin = "PendingAdmin";
    private const string PendingAccountant = "PendingAccountant";
    private const string PendingAdminReview = "PendingAdminReview";
    private const string PendingApproval = "PendingApproval";
    private const string PaymentCompleted = "Completed";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> PaymentMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "CashHandToHand",
        "CashTransaction",
        "MerchantAccount",
        "Installment"
    };

    public static RouteGroupBuilder MapPaymentsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/payments").WithTags("Payments");

        group.MapGet("/", ListPaymentLogsAsync).RequireAuthorization("payments.read");
        group.MapGet("/history", ListPaymentHistoryAsync).RequireAuthorization("payments.read");
        group.MapGet("/merchant-account-payments", ListPaymentLogsAsync).RequireAuthorization("payments.read");
        group.MapGet("/merchant-account-payments/history", ListPaymentHistoryAsync).RequireAuthorization("payments.read");
        group.MapGet("/other-payments", ListPaymentLogsAsync).RequireAuthorization("payments.read");
        group.MapGet("/other-payments/history", ListPaymentHistoryAsync).RequireAuthorization("payments.read");
        group.MapGet("/collection-work", ListCollectionWorkAsync).RequireAuthorization("payments.read");
        group.MapPost("/collections", RecordCollectionAsync).RequireAuthorization("payments.draft");
        group.MapPost("/collections/{id:guid}/submit", SubmitCollectionAsync).RequireAuthorization("payments.draft");
        group.MapPost("/collections/{id:guid}/approve", ApproveCollectionAsync).RequireAuthorization("payments.approve");
        group.MapPost("/collections/{id:guid}/reject", RejectCollectionAsync).RequireAuthorization("payments.approve");
        group.MapPost("/collections/{id:guid}/reassign", ReassignCollectionWorkAsync).RequireAuthorization("payments.write");
        group.MapGet("/collections/resolve", ResolveCollectionIdempotencyAsync).RequireAuthorization("payments.read");
        group.MapGet("/operations/resolve", ResolvePaymentOperationAsync).RequireAuthorization("payments.read");
        group.MapGet("/merchant-accounts", ListMerchantAccountsAsync).RequireAuthorization("payments.read");
        group.MapGet("/merchant-accounts/{merchantId:guid}", GetMerchantAccountAsync).RequireAuthorization("payments.read");
        group.MapGet("/merchant-accounts/{merchantId:guid}/statement", GetMerchantStatementAsync).RequireAuthorization("payments.read");
        group.MapGet("/merchant-accounts/{merchantId:guid}/orders", GetMerchantOrdersAsync).RequireAuthorization("payments.read");
        group.MapGet("/merchant-accounts/{merchantId:guid}/financial-closure/eligible", ListEligibleFinancialClosuresAsync).RequireAuthorization("payments.read");
        group.MapGet("/financial-closure/proposals", ListFinancialClosureProposalsAsync).RequireAuthorization("payments.read");
        group.MapGet("/merchant-accounts/{merchantId:guid}/financial-closure/proposals", ListMerchantFinancialClosureProposalsAsync).RequireAuthorization("payments.read");
        group.MapPost("/merchant-accounts/{merchantId:guid}/financial-closure/proposals", CreateFinancialClosureProposalAsync).RequireAuthorization("payments.draft");
        group.MapPost("/financial-closure/proposals/{id:guid}/review", ReviewFinancialClosureProposalAsync).RequireAuthorization("payments.approve");
        group.MapGet("/merchant-accounts/{merchantId:guid}/collections/preview", PreviewMerchantCollectionAsync).RequireAuthorization("payments.read");
        group.MapPost("/merchant-accounts/{merchantId:guid}/collections", RecordMerchantCollectionAsync).RequireAuthorization("payments.draft");
        group.MapGet("/merchant-account-collections", ListMerchantAccountCollectionDraftsAsync).RequireAuthorization("payments.read");
        group.MapPost("/merchant-account-collections/{id:guid}/approve", ApproveMerchantAccountCollectionAsync).RequireAuthorization("payments.approve");
        group.MapPost("/merchant-account-collections/{id:guid}/reject", RejectMerchantAccountCollectionAsync).RequireAuthorization("payments.approve");
        group.MapPost("/merchant-account-collections/{id:guid}/submit", SubmitMerchantAccountCollectionAsync).RequireAuthorization("payments.draft");
        group.MapGet("/audit", ListPaymentAuditAsync).RequireAuthorization("payments.read");
        group.MapPost("/merchant-accounts/{merchantId:guid}/refund-requests", RequestMerchantRefundAsync).RequireAuthorization("payments.write");
        group.MapPost("/merchant-accounts/refund-requests/{id:guid}/approve", ApproveMerchantRefundAsync).RequireAuthorization("payments.adjustments.approve");
        group.MapPost("/merchant-accounts/refund-requests/{id:guid}/payout", PayoutMerchantRefundAsync).RequireAuthorization("payments.write");
        group.MapGet("/merchant-account-classification-settings", GetMerchantAccountClassificationSettingsAsync).RequireAuthorization("payments.read");
        group.MapGet("/merchant-accounts/{merchantId:guid}/classification-history", GetMerchantAccountClassificationHistoryAsync).RequireAuthorization("payments.read");
        group.MapPut("/merchant-account-classification-settings", UpdateMerchantAccountClassificationSettingsAsync).RequireAuthorization("settings.write");
        group.MapGet("/{id:guid}", GetPaymentLogAsync).RequireAuthorization("payments.read");
        group.MapGet("/merchants/{merchantId:guid}/balance", GetMerchantBalanceAsync).RequireAuthorization("payments.read");
        group.MapPost("/initialize", InitializePaymentLogAsync).RequireAuthorization("payments.write");
        group.MapPost("/{id:guid}/assign", AssignPaymentLogAsync).RequireAuthorization("payments.write");
        group.MapPost("/{id:guid}/sub-logs", DraftSubLogAsync).RequireAuthorization("payments.draft");
        group.MapPost("/sub-logs/{id:guid}/approve", ApproveSubLogAsync).RequireAuthorization("payments.approve");
        group.MapPost("/sub-logs/{id:guid}/reject", RejectSubLogAsync).RequireAuthorization("payments.approve");
        group.MapPost("/sub-logs/{id:guid}/submit", SubmitSubLogAsync).RequireAuthorization("payments.draft");
        group.MapPost("/cash-receipts/{id:guid}/approve", ApproveCashReceiptAsync).RequireAuthorization("payments.approve");
        group.MapPost("/cash-receipts/{id:guid}/reject", RejectCashReceiptAsync).RequireAuthorization("payments.approve");
        group.MapPost("/cash-records", CreateCashRecordAsync).RequireAuthorization("payments.draft");
        group.MapGet("/adjustments", ListFinancialAdjustmentsAsync).RequireAuthorization("payments.read");
        group.MapPost("/adjustments", CreateFinancialAdjustmentAsync).RequireAuthorization("payments.adjustments.request");
        group.MapPost("/adjustments/{id:guid}/approve", ApproveFinancialAdjustmentAsync).RequireAuthorization("payments.adjustments.approve");
        group.MapPost("/adjustments/{id:guid}/payout", PayoutCashRefundAsync).RequireAuthorization("payments.adjustments.approve");
        group.MapPost("/adjustments/{id:guid}/reject", RejectFinancialAdjustmentAsync).RequireAuthorization("payments.adjustments.approve");

        return group;
    }

    private static string DocumentRecordCode(string prefix, Guid id)
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var value = new BigInteger(id.ToByteArray(), isUnsigned: true, isBigEndian: false);
        var characters = new char[26];
        for (var index = characters.Length - 1; index >= 0; index--)
        {
            characters[index] = alphabet[(int)(value & 31)];
            value >>= 5;
        }

        return $"{prefix}-{new string(characters)}";
    }

    private static string DescribePaymentMethod(string? value) => value switch
    {
        null or "" => "-",
        "CashHandToHand" => "Cash hand to hand",
        "CashTransaction" => "Cash transaction",
        "BankTransfer" => "Bank transfer",
        "Wallet" => "Wallet",
        "MerchantAccount" or "Installment" or "Installlaugment" => "Merchant account",
        _ => value
    };

    private static string DescribeMerchantAccountEntry(string entryType) => entryType switch
    {
        "SaleCharge" => "Sale added",
        "ReturnCredit" => "Return accepted",
        "ExchangeSurcharge" => "Exchange amount added",
        "ExchangeCredit" => "Exchange credit added",
        "Collection" => "Money received",
        "RefundPayout" => "Money refunded",
        "AdditionalCharge" => "Additional charge added",
        "BalanceReduction" => "Amount reduced",
        _ => "Account activity"
    };

    private static async Task<IResult> ListMerchantAccountsAsync(
        string? search,
        CrmDbContext crmDbContext,
        MerchantAccountService merchantAccountService,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var merchants = await crmDbContext.Merchants.AsNoTracking()
            .Where(value => !value.IsDeleted && (string.IsNullOrWhiteSpace(search) || EF.Functions.ILike(value.BusinessName, $"%{search.Trim()}%")))
            .OrderBy(value => value.BusinessName).ToListAsync(cancellationToken);
        var rows = new List<MerchantAccountListResponse>();
        foreach (var merchant in merchants)
        {
            var snapshot = await merchantAccountService.GetSnapshotAsync(merchant.Id, cancellationToken);
            if (snapshot is null) continue;
            var breakdown = await merchantAccountService.GetFinancialBreakdownAsync(merchant.Id, cancellationToken);
            var classification = await merchantAccountService.GetClassificationAsync(merchant.Id, clock.EgyptNow, cancellationToken);
            var moneyReceived = breakdown?.PaymentsReceived ?? 0m;
            var netCollected = moneyReceived - (breakdown?.CashRefunded ?? 0m);
            rows.Add(new MerchantAccountListResponse(merchant.Id, merchant.BusinessName, snapshot.AmountDue, snapshot.CreditAvailable, snapshot.ReservedRefunds, moneyReceived, netCollected, snapshot.OpenedAt, classification));
        }
        return Results.Ok(rows.OrderByDescending(value => value.AmountDue).ThenBy(value => value.BusinessName));
    }

    private static async Task<IResult> GetMerchantAccountAsync(
    Guid merchantId,
    CrmDbContext crmDbContext,
    MerchantAccountService merchantAccountService,
    ICurrentUser currentUser,
    IClock clock,
    CancellationToken cancellationToken)
{
    var merchant = await crmDbContext.Merchants
        .AsNoTracking()
        .SingleOrDefaultAsync(
            value => value.Id == merchantId && !value.IsDeleted,
            cancellationToken);

    if (merchant is null)
        return Results.NotFound();

    var snapshot = await merchantAccountService.GetSnapshotAsync(
        merchantId,
        cancellationToken);

    if (snapshot is null)
    {
        await merchantAccountService.GetOrCreateForUpdateAsync(
            merchantId,
            currentUser.UserId ?? Guid.Empty,
            clock.EgyptNow,
            cancellationToken);

        snapshot = await merchantAccountService.GetSnapshotAsync(
            merchantId,
            cancellationToken);
    }

    if (snapshot is null)
        return Results.Problem(
            "Merchant account could not be initialized.",
            statusCode: StatusCodes.Status500InternalServerError);

    var breakdown =
        await merchantAccountService.GetFinancialBreakdownAsync(
            merchantId,
            cancellationToken);

    var classification =
        await merchantAccountService.GetClassificationAsync(
            merchantId,
            clock.EgyptNow,
            cancellationToken);

    var netCollected =
        (breakdown?.PaymentsReceived ?? 0m)
        - (breakdown?.CashRefunded ?? 0m);

    return Results.Ok(new MerchantAccountDetailResponse(
        merchant.Id,
        merchant.BusinessName,
        snapshot,
        breakdown,
        netCollected,
        classification,
        new MerchantAccountProfileResponse(
            merchant.ContactPersonName,
            merchant.PhoneNumbers,
            merchant.Email,
            merchant.Address,
            merchant.BusinessType,
            merchant.Status)));
    }

    private static async Task<IResult> GetMerchantStatementAsync(
        Guid merchantId,
        int? take,
        DateTime? from,
        DateTime? to,
        bool? includeSummary,
        MerchantAccountService merchantAccountService,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!from.HasValue && !to.HasValue)
        {
            var today = clock.EgyptNow.Date;
            from = new DateTime(today.Year, today.Month, 1);
            to = from.Value.AddMonths(1).AddDays(-1);
        }
        var statement = await merchantAccountService.GetStatementAsync(merchantId, take ?? 200, cancellationToken, from, to);
        var operationIds = statement.Where(value => value.OperationId.HasValue).Select(value => value.OperationId!.Value).Distinct().ToArray();
        var paymentIds = statement.Where(value => value.PaymentId.HasValue).Select(value => value.PaymentId!.Value).Distinct().ToArray();
        var actorIds = statement.Select(value => value.PostedBy).Where(value => value != Guid.Empty).Distinct().ToArray();
        var operationReferences = operationIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await operationsDbContext.OperationLogs.AsNoTracking()
                .Where(value => operationIds.Contains(value.Id))
                .ToDictionaryAsync(value => value.Id, value => value.OperationNumber, cancellationToken);
        var paymentReferences = paymentIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await paymentsDbContext.MainPaymentLogs.AsNoTracking()
                .Where(value => paymentIds.Contains(value.Id))
                .ToDictionaryAsync(value => value.Id, value => $"PAY-{value.Id:N}"[..12].ToUpperInvariant(), cancellationToken);
        var actorNames = actorIds.Length == 0
            ? new Dictionary<Guid, string>()
            : await identityDbContext.Users.AsNoTracking()
                .Where(value => actorIds.Contains(value.Id))
                .ToDictionaryAsync(value => value.Id, value => string.IsNullOrWhiteSpace(value.FullName) ? value.Username : value.FullName, cancellationToken);

        var displayRows = statement.Select(row =>
        {
            var references = new List<string>(2);
            if (row.OperationId is { } operationId && operationReferences.TryGetValue(operationId, out var operationNumber)) references.Add(operationNumber);
            if (row.PaymentId is { } paymentId && paymentReferences.TryGetValue(paymentId, out var paymentReference)) references.Add(paymentReference);
            var sourceReference = references.Count > 0
                ? string.Join(" · ", references)
                : row.EntryType == "Collection" ? "Account collection" : "Related account activity";

            return new MerchantStatementDisplayRow(
                row.Id, row.Sequence, row.PostedAt, row.EntryType, DescribeMerchantEntry(row.EntryType),
                row.OperationId, row.PaymentId, sourceReference,
                row.DebitAmount, row.CreditAmount, row.RunningBalance,
                row.PaymentMethod, DescribeMovementMethod(row.PaymentMethod), row.TransactionReference, row.Status,
                row.Notes, actorNames.GetValueOrDefault(row.PostedBy, "Historical record"));
        }).ToList();
        if (includeSummary != true) return Results.Ok(displayRows);
        var opening = await merchantAccountService.GetOpeningBalanceAsync(merchantId, from, cancellationToken);
        var closing = await merchantAccountService.GetClosingBalanceAsync(merchantId, to, cancellationToken);
        return Results.Ok(new { items = displayRows, openingBalance = opening, closingBalance = closing, from, to });
    }

    private static async Task<IResult> GetMerchantOrdersAsync(
        Guid merchantId,
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        CancellationToken cancellationToken)
    {
        var operations = await operationsDbContext.OperationLogs.AsNoTracking()
            .Where(value => value.ClientId == merchantId && !value.IsDeleted)
            .OrderByDescending(value => value.CreatedAt)
            .Take(200)
            .Select(value => new
            {
                value.Id,
                value.OperationNumber,
                value.OperationType,
                value.Status,
                value.FinancialClosureStatus,
                Date = value.ConfirmedAt ?? value.CreatedAt,
                Lines = value.OperationLines.Select(line => new MerchantOrderLineResponse(line.ProductNameSnapshot, line.SkuCodeSnapshot, line.Quantity)).ToList()
            })
            .ToListAsync(cancellationToken);

        if (operations.Count == 0) return Results.Ok(Array.Empty<MerchantOrderResponse>());

        var operationIds = operations.Select(value => value.Id).ToArray();
        var entries = await paymentsDbContext.MerchantAccountEntries.AsNoTracking()
            .Where(value => value.OperationId.HasValue && operationIds.Contains(value.OperationId.Value) && value.Status == "Posted")
            .Select(value => new { value.OperationId, value.EntryType, value.DebitAmount, value.CreditAmount })
            .ToListAsync(cancellationToken);
        var obligations = await paymentsDbContext.MerchantOperationObligations.AsNoTracking()
            .Where(value => operationIds.Contains(value.OperationId))
            .Select(value => new { value.Id, value.OperationId })
            .ToListAsync(cancellationToken);
        var allocationTotals = await paymentsDbContext.MerchantEntryAllocations.AsNoTracking()
            .Where(value => obligations.Select(item => item.Id).Contains(value.ObligationId))
            .GroupBy(value => value.ObligationId)
            .Select(group => new { ObligationId = group.Key, Amount = group.Sum(value => value.Amount) })
            .ToListAsync(cancellationToken);
        var allocatedByOperation = allocationTotals
            .Join(obligations, allocation => allocation.ObligationId, obligation => obligation.Id, (allocation, obligation) => new { obligation.OperationId, allocation.Amount })
            .GroupBy(value => value.OperationId)
            .ToDictionary(group => group.Key, group => group.Sum(value => value.Amount));

        var result = operations.Select(operation =>
        {
            var related = entries.Where(value => value.OperationId == operation.Id).ToList();
            var saleTotal = related.Where(value => value.EntryType == "SaleCharge").Sum(value => value.DebitAmount);
            var acceptedReturns = related.Where(value => value.EntryType == "ReturnCredit").Sum(value => value.CreditAmount);
            var additionalCharges = related.Where(value => value.EntryType is "ExchangeSurcharge" or "AdditionalCharge").Sum(value => value.DebitAmount);
            var amountReductions = related.Where(value => value.EntryType == "BalanceReduction").Sum(value => value.CreditAmount);
            var refunds = related.Where(value => value.EntryType == "RefundPayout").Sum(value => value.DebitAmount);
            var collectionsAllocated = allocatedByOperation.GetValueOrDefault(operation.Id);
            var remaining = Math.Max(saleTotal + additionalCharges - acceptedReturns - amountReductions - collectionsAllocated - refunds, 0m);
            return new MerchantOrderResponse(operation.Id, operation.OperationNumber, operation.OperationType, operation.Status, operation.Date, operation.Lines,
                saleTotal, collectionsAllocated, acceptedReturns, additionalCharges, amountReductions, refunds, remaining, operation.FinancialClosureStatus);
        }).ToList();
        return Results.Ok(result);
    }

    private static async Task<IResult> ListEligibleFinancialClosuresAsync(Guid merchantId, PaymentsDbContext payments, OperationsDbContext operations, CancellationToken ct)
    {
        var account = await payments.MerchantReceivableAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.MerchantId == merchantId, ct);
        if (account is null) return Results.Ok(Array.Empty<object>());
        var rows = await (from o in operations.OperationLogs.AsNoTracking()
                          join obligation in payments.MerchantOperationObligations.AsNoTracking() on o.Id equals obligation.OperationId
                          where obligation.AccountId == account.Id && !o.IsDeleted && o.Status == Completed && (o.OperationType == WholesaleSale || o.OperationType == RetailSale) && o.FinancialClosureStatus == "Open"
                          let allocated = payments.MerchantEntryAllocations.Where(a => a.ObligationId == obligation.Id).Sum(a => (decimal?)a.Amount) ?? 0m
                          where obligation.OriginalAmount - allocated <= 0.0001m
                          select new { o.Id, o.OperationNumber, o.OperationType, o.Status, settlementAmount = obligation.OriginalAmount, allocatedAmount = allocated, remainingAmount = Math.Max(obligation.OriginalAmount - allocated, 0m), o.FinancialClosureStatus }).Take(500).ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> ListFinancialClosureProposalsAsync(PaymentsDbContext payments, CancellationToken ct) => Results.Ok(await payments.MerchantFinancialClosureProposals.AsNoTracking().Include(x => x.Items).OrderByDescending(x => x.SubmittedAt).Take(200).ToListAsync(ct));

    private static async Task<IResult> ListMerchantFinancialClosureProposalsAsync(Guid merchantId, PaymentsDbContext payments, CancellationToken ct)
    {
        var account = await payments.MerchantReceivableAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.MerchantId == merchantId, ct);
        if (account is null) return Results.Ok(Array.Empty<object>());
        return Results.Ok(await payments.MerchantFinancialClosureProposals.AsNoTracking().Include(x => x.Items).Where(x => x.AccountId == account.Id).OrderByDescending(x => x.SubmittedAt).ToListAsync(ct));
    }

    private static async Task<IResult> CreateFinancialClosureProposalAsync(Guid merchantId, FinancialClosureProposalRequest request, PaymentsDbContext payments, OperationsDbContext operations, ICurrentUser currentUser, IClock clock, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        var ids = request.OperationIds?.Distinct().ToArray() ?? [];
        if (ids.Length == 0) return Results.ValidationProblem(new Dictionary<string, string[]> { ["operationIds"] = ["Select at least one settled merchant sale."] });
        var account = await payments.MerchantReceivableAccounts.FirstOrDefaultAsync(x => x.MerchantId == merchantId, ct);
        if (account is null) return Results.NotFound(new { detail = "Merchant account was not found." });
        var normalizedIdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (normalizedIdempotencyKey is not null)
        {
            var replay = await payments.MerchantFinancialClosureProposals.AsNoTracking().Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.AccountId == account.Id && x.IdempotencyKey == normalizedIdempotencyKey, ct);
            if (replay is not null) return Results.Ok(replay);
        }
        var rows = await (from o in operations.OperationLogs where ids.Contains(o.Id) && o.ClientId == merchantId && !o.IsDeleted && o.Status == Completed && (o.OperationType == WholesaleSale || o.OperationType == RetailSale) && o.FinancialClosureStatus == "Open" join obligation in payments.MerchantOperationObligations on o.Id equals obligation.OperationId where obligation.AccountId == account.Id let allocated = payments.MerchantEntryAllocations.Where(a => a.ObligationId == obligation.Id).Sum(a => (decimal?)a.Amount) ?? 0m select new { o, obligation, allocated }).ToListAsync(ct);
        if (rows.Count != ids.Length || rows.Any(x => x.obligation.OriginalAmount - x.allocated > 0.0001m)) return Results.Conflict(new ProblemDetails { Title = "Operation is not eligible", Detail = "Every selected sale must be completed, open, belong to this merchant, and fully settled." });
        var proposal = new MerchantFinancialClosureProposal { Id = Guid.NewGuid(), AccountId = account.Id, SubmittedBy = currentUser.UserId ?? Guid.Empty, SubmittedAt = clock.EgyptNow, Notes = request.Notes?.Trim(), IdempotencyKey = normalizedIdempotencyKey };
        foreach (var row in rows) proposal.Items.Add(new MerchantFinancialClosureItem { Id = Guid.NewGuid(), OperationId = row.o.Id, OperationNumber = row.o.OperationNumber, SettlementAmount = row.obligation.OriginalAmount, RemainingAmount = Math.Max(row.obligation.OriginalAmount - row.allocated, 0m) });
        payments.MerchantFinancialClosureProposals.Add(proposal); await payments.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/payments/financial-closure/proposals/{proposal.Id}", proposal);
    }

    private static async Task<IResult> ReviewFinancialClosureProposalAsync(Guid id, FinancialClosureReviewRequest request, PaymentsDbContext payments, OperationsDbContext operations, ICurrentUser currentUser, IClock clock, CancellationToken ct)
    {
        var proposal = await payments.MerchantFinancialClosureProposals.Include(x => x.Items).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (proposal is null) return Results.NotFound();
        if (proposal.Status != "PendingAdminReview") return Results.Conflict(new ProblemDetails { Title = "Proposal already reviewed", Detail = "This closure proposal has already received a decision." });
        var approved = (request.ApprovedOperationIds ?? []).ToHashSet(); var selected = proposal.Items.Where(x => approved.Contains(x.OperationId)).ToList(); var rejected = proposal.Items.Where(x => !approved.Contains(x.OperationId)).ToList();
        var operationIds = proposal.Items.Select(x => x.OperationId).ToArray(); var ops = await operations.OperationLogs.Where(x => operationIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        if (ops.Count != proposal.Items.Count || ops.Values.Any(x => x.Status != Completed || x.FinancialClosureStatus != "Open")) return Results.Conflict(new ProblemDetails { Title = "Settlement changed", Detail = "One or more operations are no longer eligible for closure." });
        var accountId = proposal.AccountId;
        var settlementRows = await payments.MerchantOperationObligations.AsNoTracking()
            .Where(x => x.AccountId == accountId && operationIds.Contains(x.OperationId))
            .Select(x => new { x.OperationId, x.OriginalAmount, Allocated = payments.MerchantEntryAllocations.Where(a => a.ObligationId == x.Id).Sum(a => (decimal?)a.Amount) ?? 0m })
            .ToListAsync(ct);
        if (settlementRows.Count != proposal.Items.Count || settlementRows.Any(x => x.OriginalAmount - x.Allocated > 0.0001m)) return Results.Conflict(new ProblemDetails { Title = "Settlement changed", Detail = "One or more operations are no longer fully settled." });
        foreach (var item in selected) { var op = ops[item.OperationId]; op.FinancialClosureStatus = "FinanciallyClosed"; op.FinancialClosureProposalId = proposal.Id; op.FinanciallyClosedBy = currentUser.UserId; op.FinanciallyClosedAt = clock.EgyptNow; item.Decision = "Approved"; item.DecidedBy = currentUser.UserId; item.DecidedAt = clock.EgyptNow; }
        foreach (var item in rejected) { item.Decision = "Rejected"; item.RejectionReason = request.RejectionReason?.Trim() ?? "Not selected by the approver."; item.DecidedBy = currentUser.UserId; item.DecidedAt = clock.EgyptNow; }
        proposal.Status = selected.Count == 0 ? "Rejected" : rejected.Count == 0 ? "Approved" : "PartiallyApproved"; proposal.ReviewedBy = currentUser.UserId; proposal.ReviewedAt = clock.EgyptNow; proposal.ReviewReason = request.ReviewReason?.Trim();
        await SharedDbTransaction.ExecuteAsync(payments, async () => { await payments.SaveChangesAsync(ct); await operations.SaveChangesAsync(ct); }, ct, operations);
        return Results.Ok(proposal);
    }

    private static async Task<IResult> PreviewMerchantCollectionAsync(Guid merchantId, decimal amount, MerchantAccountService merchantAccountService, CancellationToken cancellationToken)
    {
        if (amount <= 0) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(amount)] = ["Amount must be greater than zero."] });
        return Results.Ok(await merchantAccountService.PreviewAllocationsAsync(merchantId, amount, cancellationToken));
    }

    private static async Task<IResult> ResolveCollectionIdempotencyAsync(
        Guid key,
        PaymentsDbContext paymentsDbContext,
        CancellationToken cancellationToken)
    {
        if (key == Guid.Empty) return Results.BadRequest(new { detail = "A valid collection idempotency key is required." });
        var record = await paymentsDbContext.PaymentIdempotencyKeys.AsNoTracking()
            .Where(value => value.Key == key && value.Status == "Completed" && value.ResponseBody != null)
            .OrderByDescending(value => value.LastSeenAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (record?.ResponseBody is null) return Results.NotFound();
        using var document = JsonDocument.Parse(record.ResponseBody);
        return Results.Json(document.RootElement.Clone(), statusCode: record.ResponseStatusCode ?? StatusCodes.Status200OK);
    }

    private static async Task<IResult> RecordMerchantCollectionAsync(
        Guid merchantId,
        MerchantCollectionRequest request,
        CrmDbContext crmDbContext,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        MerchantAccountService merchantAccountService,
        PaymentIdempotencyService paymentIdempotencyService,
        ICurrentUser currentUser,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var idempotency = await paymentIdempotencyService.StartAsync(idempotencyKey, $"POST /api/v1/payments/merchant-accounts/{merchantId}/collections", request, cancellationToken);
        if (idempotency.Result is not null) return idempotency.Result;
        if (!await crmDbContext.Merchants.AsNoTracking().AnyAsync(value => value.Id == merchantId && !value.IsDeleted, cancellationToken))
            return await PaymentIdempotencyService.AbortAsync(idempotency, Results.NotFound());
        if (request.SourceOperationId is { } sourceOperationId)
        {
            var sourceOperation = await operationsDbContext.OperationLogs.AsNoTracking()
                .Include(value => value.OperationLines)
                .FirstOrDefaultAsync(value => value.Id == sourceOperationId && value.ClientId == merchantId && !value.IsDeleted, cancellationToken);
            if (sourceOperation is null)
                return await PaymentIdempotencyService.AbortAsync(idempotency, Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.SourceOperationId)] = ["The source operation does not belong to the selected merchant."] }));
            if (!IsPaymentEligible(sourceOperation))
                return await PaymentIdempotencyService.AbortAsync(idempotency, Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.SourceOperationId)] = ["The source operation is not a completed payable sale or confirmed payable change."] }));
        }
        if (request.Amount <= 0) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Amount)] = ["Amount must be greater than zero."] });
        var method = NormalizeMovementMethod(request.PaymentMethod);
        if (method is null) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PaymentMethod)] = ["Payment method must be CashHandToHand, CashTransaction, BankTransfer, or Wallet."] });
        if (MerchantAccountService.RequiresTransactionReference(method) && string.IsNullOrWhiteSpace(request.TransactionReference))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.TransactionReference)] = ["An electronic payment requires a transaction reference."] });
        try
        {
            var now = clock.EgyptNow;
            var actorId = currentUser.UserId ?? Guid.Empty;
            var assignedTo = await SelectLeastLoadedAccountantAsync(identityDbContext, paymentsDbContext, cancellationToken);
            MerchantAccountCollectionDraft? draft = null;
            await MerchantCollectionWorkspacePersistence.RunAsync(paymentsDbContext, async () =>
            {
                var account = await merchantAccountService.GetOrCreateForUpdateAsync(merchantId, actorId, now, cancellationToken);
                draft = new MerchantAccountCollectionDraft
                {
                    Id = Guid.NewGuid(),
                    AccountId = account.Id,
                    Account = account,
                    SourceOperationId = request.SourceOperationId,
                    Amount = request.Amount,
                    PaymentMethod = method,
                    TransactionReference = request.TransactionReference?.Trim(),
                    AllocationsJson = request.Allocations is { Count: > 0 } ? JsonSerializer.Serialize(request.Allocations) : null,
                    Notes = request.Notes?.Trim(),
                    Status = request.SubmitForReview ? PendingAdminReview : Draft,
                    DraftedBy = actorId,
                    DraftedAt = now,
                    AssignedTo = assignedTo,
                    AssignedAt = assignedTo.HasValue ? now : null
                };
                paymentsDbContext.MerchantAccountCollectionDrafts.Add(draft);
                AddPaymentWorkflowAudit(paymentsDbContext, request.SubmitForReview ? "MerchantAccountCollectionSubmitted" : "MerchantAccountCollectionDrafted", null, draft.Status, merchantId, null, null, draft.Id, actorId, now, draft.Amount, method, null, httpContext: null, idempotencyKey, new { assignedTo, request.Allocations });
                await MerchantCollectionWorkspacePersistence.SaveAsync(paymentsDbContext, cancellationToken);
            }, cancellationToken);
            return await paymentIdempotencyService.CompleteAsync(idempotency, ToMerchantAccountCollectionDraftResponse(draft!), StatusCodes.Status201Created, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return await PaymentIdempotencyService.AbortAsync(idempotency, Results.ValidationProblem(new Dictionary<string, string[]> { ["collection"] = [exception.Message] }));
        }
    }

    private static async Task<IResult> ListMerchantAccountCollectionDraftsAsync(
        string? status,
        Guid? merchantId,
        Guid? assignedTo,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        CancellationToken cancellationToken)
    {
        var query = paymentsDbContext.MerchantAccountCollectionDrafts.AsNoTracking()
            .Include(value => value.Account)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(value => value.Status == status.Trim());
        if (merchantId.HasValue) query = query.Where(value => value.Account.MerchantId == merchantId.Value);
        if (assignedTo.HasValue) query = query.Where(value => value.AssignedTo == assignedTo.Value);
        var rows = await query.OrderByDescending(value => value.DraftedAt).Take(200).ToListAsync(cancellationToken);
        var userIds = rows.SelectMany(value => new Guid?[] { value.DraftedBy, value.AssignedTo, value.ConfirmedBy })
            .Where(value => value.HasValue).Select(value => value!.Value).Distinct().ToArray();
        var users = await identityDbContext.Users.AsNoTracking().Where(value => userIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, cancellationToken);
        return Results.Ok(rows.Select(value => ToMerchantAccountCollectionDraftResponse(value, users)));
    }

    private static async Task<IResult> ListCollectionWorkAsync(
        string? scope,
        string? status,
        Guid? merchantId,
        Guid? operationId,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        OperationsDbContext operationsDbContext,
        CancellationToken cancellationToken)
    {
        var drafts = paymentsDbContext.MerchantAccountCollectionDrafts.AsNoTracking().Include(value => value.Account).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) drafts = drafts.Where(value => value.Status == status.Trim());
        if (merchantId.HasValue) drafts = drafts.Where(value => value.Account.MerchantId == merchantId.Value);
        if (operationId.HasValue) drafts = drafts.Where(value => value.SourceOperationId == operationId.Value);
        var draftRows = await drafts.OrderByDescending(value => value.DraftedAt).Take(200).ToListAsync(cancellationToken);
        // Include legacy logs whose denormalized MerchantId is empty. Their
        // source operation remains authoritative for workspace scope.
        var allLogs = await paymentsDbContext.MainPaymentLogs.AsNoTracking().Include(value => value.InstallmentSubLogs)
            .Where(value => !value.IsDeleted && (!operationId.HasValue || value.OperationId == operationId.Value))
            .OrderByDescending(value => value.LastModifiedAt).Take(200).ToListAsync(cancellationToken);
        var operationRefs = await LoadPaymentOperationLookupAsync(
            operationsDbContext,
            allLogs.Select(value => value.OperationId).Concat(draftRows.Where(value => value.SourceOperationId.HasValue).Select(value => value.SourceOperationId!.Value)),
            cancellationToken);
        var scopedLogs = allLogs.Where(value =>
        {
            var effectiveMerchant = value.MerchantId ?? operationRefs.GetValueOrDefault(value.OperationId)?.MerchantId;
            return string.IsNullOrWhiteSpace(scope)
                || (string.Equals(scope, "MerchantAccount", StringComparison.OrdinalIgnoreCase) && effectiveMerchant.HasValue && (!merchantId.HasValue || effectiveMerchant == merchantId))
                || (string.Equals(scope, "DirectOperation", StringComparison.OrdinalIgnoreCase) && !effectiveMerchant.HasValue);
        }).ToList();
        var directLogsForUsers = scopedLogs
            .Where(value => !(value.MerchantId ?? operationRefs.GetValueOrDefault(value.OperationId)?.MerchantId).HasValue)
            .Select(value => new { value.AssignedTo, value.InitializedBy, value.LastModifiedBy })
            .ToList();
        var userIds = draftRows.SelectMany(value => new Guid?[] { value.DraftedBy, value.AssignedTo, value.ConfirmedBy })
            .Concat(directLogsForUsers.SelectMany(value => new Guid?[] { value.AssignedTo, value.InitializedBy, value.LastModifiedBy }))
            .Where(value => value.HasValue).Select(value => value!.Value).Distinct().ToArray();
        var users = await identityDbContext.Users.AsNoTracking().Where(value => userIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, cancellationToken);
        var result = new List<object>();
        if (string.IsNullOrWhiteSpace(scope) || string.Equals(scope, "MerchantAccount", StringComparison.OrdinalIgnoreCase))
        {
            result.AddRange(draftRows.Select(value => new
            {
                id = value.Id, scope = "MerchantAccount", reference = $"COL-{value.Id:N}"[..12].ToUpperInvariant(), merchantId = value.Account.MerchantId,
                operationId = value.SourceOperationId, operationNumber = value.SourceOperationId is { } sourceId
                    ? operationRefs.GetValueOrDefault(sourceId)?.OperationNumber
                    : null,
                amount = value.Amount, movementMethod = value.PaymentMethod, status = value.Status,
                transactionReference = value.TransactionReference, confirmedBy = value.ConfirmedBy, confirmedAt = value.ConfirmedAt,
                rejectionReason = value.RejectionReason, allocations = value.AllocationsJson,
                permittedActions = CollectionPermittedActions(value.Status),
                assignedTo = value.AssignedTo, assignedToName = GetUserDisplayName(value.AssignedTo, users), draftedAt = value.DraftedAt,
                source = "Merchant account"
            }));
            result.AddRange(scopedLogs
                .Where(log => (log.MerchantId ?? operationRefs.GetValueOrDefault(log.OperationId)?.MerchantId).HasValue)
                .SelectMany(log => log.InstallmentSubLogs
                    .Where(sub => string.IsNullOrWhiteSpace(status) || sub.SubLogStatus == status.Trim())
                    .Select(sub => new
                    {
                        id = sub.Id, scope = "MerchantAccount", reference = $"SUB-{sub.Id:N}"[..12].ToUpperInvariant(),
                        merchantId = log.MerchantId ?? operationRefs.GetValueOrDefault(log.OperationId)?.MerchantId,
                        operationId = log.OperationId, operationNumber = operationRefs.GetValueOrDefault(log.OperationId)?.OperationNumber,
                        amount = sub.Amount, movementMethod = sub.PaymentMethod, status = sub.SubLogStatus,
                        assignedTo = log.AssignedTo, assignedToName = GetUserDisplayName(log.AssignedTo, users), draftedAt = sub.DraftedAt,
                        transactionReference = sub.TransactionReference, confirmedBy = sub.ConfirmedBy, confirmedAt = sub.ConfirmedAt,
                        rejectionReason = sub.RejectionReason, allocations = (string?)null, permittedActions = CollectionPermittedActions(sub.SubLogStatus),
                        source = "Merchant account legacy collection"
                    })));
        }
        if (string.IsNullOrWhiteSpace(scope) || string.Equals(scope, "DirectOperation", StringComparison.OrdinalIgnoreCase))
        {
            var logs = scopedLogs.Where(log => !(log.MerchantId ?? operationRefs.GetValueOrDefault(log.OperationId)?.MerchantId).HasValue).ToList();
            result.AddRange(logs.SelectMany(log => log.InstallmentSubLogs.Where(sub => string.IsNullOrWhiteSpace(status) || sub.SubLogStatus == status.Trim()).Select(sub => new
            {
                id = sub.Id, scope = "DirectOperation", reference = $"SUB-{sub.Id:N}"[..12].ToUpperInvariant(), merchantId = (Guid?)null,
                operationId = log.OperationId, operationNumber = operationRefs.GetValueOrDefault(log.OperationId)?.OperationNumber, amount = sub.Amount,
                movementMethod = sub.PaymentMethod, status = sub.SubLogStatus, assignedTo = log.AssignedTo, assignedToName = GetUserDisplayName(log.AssignedTo, users),
                draftedAt = sub.DraftedAt, transactionReference = sub.TransactionReference, confirmedBy = sub.ConfirmedBy, confirmedAt = sub.ConfirmedAt,
                rejectionReason = sub.RejectionReason, allocations = (string?)null, permittedActions = CollectionPermittedActions(sub.SubLogStatus),
                source = "Direct operation"
            })));
            var directOperationIds = logs.Select(log => log.OperationId).ToArray();
            var pendingCash = await paymentsDbContext.CashRecords.AsNoTracking()
                .Where(value => value.OperationId.HasValue && (value.Status == PendingAccountant || value.Status == PendingAdminReview) && directOperationIds.Contains(value.OperationId.Value))
                .Where(value => string.IsNullOrWhiteSpace(status) || value.Status == status.Trim())
                .OrderByDescending(value => value.PaymentDate)
                .Take(200)
                .ToListAsync(cancellationToken);
            result.AddRange(pendingCash.Select(record =>
            {
                var log = allLogs.FirstOrDefault(value => value.OperationId == record.OperationId);
                // A cash receipt is its own review item. Its status must win over
                // the parent payment-log status so approval/rejection targets the
                // exact receipt shown in the inbox.
                var workflowStatus = record.Status;
                return new
                {
                    id = record.Id, paymentLogId = log?.Id, scope = "DirectOperation", reference = $"CASH-{record.Id:N}"[..13].ToUpperInvariant(),
                    merchantId = log?.MerchantId, operationId = record.OperationId,
                    operationNumber = record.OperationId.HasValue ? operationRefs.GetValueOrDefault(record.OperationId.Value)?.OperationNumber : null,
                    amount = record.Amount, movementMethod = record.SubType, transactionReference = record.TransactionReference,
                    status = workflowStatus, assignedTo = log?.AssignedTo, assignedToName = GetUserDisplayName(log?.AssignedTo, users),
                    draftedAt = record.PaymentDate, confirmedBy = (Guid?)null, confirmedAt = (DateTime?)null,
                    rejectionReason = (string?)null, allocations = (string?)null, permittedActions = CollectionPermittedActions(workflowStatus),
                    source = "Direct operation cash receipt"
                };
            }));
        }
        return Results.Ok(result.OrderByDescending(value => value.GetType().GetProperty("draftedAt")?.GetValue(value)));
    }

    private static async Task<IResult> RecordCollectionAsync(
        CollectionWorkspaceRequest request,
        CrmDbContext crmDbContext,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        HttpContext httpContext,
        ICurrentUser currentUser,
        MerchantAccountService merchantAccountService,
        IAppEventPublisher eventPublisher,
        PaymentIdempotencyService paymentIdempotencyService,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.Equals(request.Scope, "MerchantAccount", StringComparison.OrdinalIgnoreCase))
        {
            if (!request.MerchantId.HasValue) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.MerchantId)] = ["MerchantId is required for merchant-account collections."] });
            return await RecordMerchantCollectionAsync(request.MerchantId.Value,
                new MerchantCollectionRequest(request.Amount, request.PaymentMethod, request.TransactionReference, request.Allocations, request.Notes, request.SubmitForReview, request.OperationId),
                crmDbContext,
                paymentsDbContext, operationsDbContext, identityDbContext, merchantAccountService, paymentIdempotencyService, currentUser, clock, idempotencyKey, cancellationToken);
        }
        if (!string.Equals(request.Scope, "DirectOperation", StringComparison.OrdinalIgnoreCase) || !request.OperationId.HasValue)
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Scope)] = ["Scope must be MerchantAccount or DirectOperation."], [nameof(request.OperationId)] = ["OperationId is required for direct-operation collections."] });
        if (request.MerchantId.HasValue)
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.MerchantId)] = ["Direct-operation collections cannot include a merchant account. Submit a separate merchant-account collection."] });
        var directOperation = await operationsDbContext.OperationLogs.AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == request.OperationId.Value && !value.IsDeleted, cancellationToken);
        if (directOperation is null) return Results.NotFound();
        if (directOperation.ClientId.HasValue || directOperation.OperationType != RetailSale || directOperation.Status != Completed)
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.OperationId)] = ["Other payments can only be collected against a completed anonymous retail operation."] });
        var logId = await paymentsDbContext.MainPaymentLogs.AsNoTracking()
            .Where(value => value.OperationId == request.OperationId.Value && !value.MerchantId.HasValue && !value.IsDeleted)
            .Select(value => (Guid?)value.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (!logId.HasValue) return Results.NotFound();
        return await DraftSubLogAsync(logId.Value,
            new PaymentSubLogRequest(request.Amount, request.PaymentMethod, request.DateReceived, request.Notes, request.TransactionReference, request.SubmitForReview),
            paymentsDbContext, operationsDbContext, identityDbContext, sharedDbContext, httpContext, currentUser, merchantAccountService, eventPublisher, paymentIdempotencyService, clock, idempotencyKey, cancellationToken);
    }

    private static async Task<IResult> ReassignCollectionWorkAsync(
        Guid id,
        CollectionReassignmentRequest request,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        PaymentIdempotencyService paymentIdempotencyService,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!IsCollectionReviewer(currentUser)) return Results.Forbid();
        var parentLogId = await paymentsDbContext.InstallmentSubLogs.AsNoTracking()
            .Where(value => value.Id == id).Select(value => (Guid?)value.MainLogId).SingleOrDefaultAsync(cancellationToken);
        if (parentLogId.HasValue)
        {
            return await AssignPaymentLogAsync(parentLogId.Value, new AssignPaymentRequest(request.AccountantUserId), paymentsDbContext, operationsDbContext, identityDbContext, sharedDbContext, httpContext, currentUser, eventPublisher, paymentIdempotencyService, clock, idempotencyKey, cancellationToken);
        }
        MerchantAccountCollectionDraft? draft = null;
        Guid? previous = null;
        IResult? failure = null;
        await MerchantCollectionWorkspacePersistence.RunAsync(paymentsDbContext, async () =>
        {
            draft = await LoadMerchantAccountCollectionDraftForUpdateAsync(id, paymentsDbContext, cancellationToken);
            if (draft is null) { failure = Results.NotFound(); return; }
            if (request.AccountantUserId.HasValue && !await identityDbContext.Users.AnyAsync(value => value.Id == request.AccountantUserId.Value && value.IsActive && value.Role == LenseeRoles.Accountant, cancellationToken))
            { failure = Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.AccountantUserId)] = ["Assigned user must be an active Accountant."] }); return; }
            previous = draft.AssignedTo;
            draft.AssignedTo = request.AccountantUserId;
            draft.AssignedAt = clock.EgyptNow;
            AddPaymentWorkflowAudit(paymentsDbContext, "CollectionReassigned", draft.Status, draft.Status, draft.Account.MerchantId, null, null, draft.Id, currentUser.UserId ?? Guid.Empty, clock.EgyptNow, draft.Amount, draft.PaymentMethod, request.Reason, null, null, new { previous, draft.AssignedTo });
            await MerchantCollectionWorkspacePersistence.SaveAsync(paymentsDbContext, cancellationToken);
        }, cancellationToken, identityDbContext, sharedDbContext);
        if (failure is not null) return failure;
        if (draft is null) return Results.NotFound();
        return Results.Ok(new { id = draft.Id, previousAssignee = previous, assignedTo = draft.AssignedTo, assignedAt = draft.AssignedAt, status = draft.Status });
    }

    private static async Task<IResult> ApproveCollectionAsync(
        Guid id,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        MerchantAccountService merchantAccountService,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        PaymentIdempotencyService paymentIdempotencyService,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (await paymentsDbContext.InstallmentSubLogs.AsNoTracking().AnyAsync(value => value.Id == id, cancellationToken))
            return await ApproveSubLogAsync(id, paymentsDbContext, operationsDbContext, identityDbContext, sharedDbContext, merchantAccountService, httpContext, currentUser, eventPublisher, paymentIdempotencyService, clock, idempotencyKey, cancellationToken);
        if (await paymentsDbContext.CashRecords.AsNoTracking().AnyAsync(value => value.Id == id && value.PaymentType == CashReceived, cancellationToken))
            return await ApproveCashReceiptAsync(id, paymentsDbContext, operationsDbContext, identityDbContext, sharedDbContext, merchantAccountService, httpContext, currentUser, eventPublisher, paymentIdempotencyService, clock, idempotencyKey, cancellationToken, cashRecordId: id);
        if (await paymentsDbContext.MainPaymentLogs.AsNoTracking().AnyAsync(value => value.Id == id && !value.IsDeleted, cancellationToken))
            return await ApproveCashReceiptAsync(id, paymentsDbContext, operationsDbContext, identityDbContext, sharedDbContext, merchantAccountService, httpContext, currentUser, eventPublisher, paymentIdempotencyService, clock, idempotencyKey, cancellationToken);
        return await ApproveMerchantAccountCollectionAsync(id, paymentsDbContext, merchantAccountService, paymentIdempotencyService, currentUser, clock, idempotencyKey, cancellationToken);
    }

    private static async Task<IResult> SubmitCollectionAsync(
        Guid id,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (await paymentsDbContext.InstallmentSubLogs.AsNoTracking().AnyAsync(value => value.Id == id, cancellationToken))
            return await SubmitSubLogAsync(id, paymentsDbContext, identityDbContext, currentUser, clock, cancellationToken);
        return await SubmitMerchantAccountCollectionAsync(id, paymentsDbContext, identityDbContext, currentUser, clock, cancellationToken);
    }

    private static async Task<IResult> RejectCollectionAsync(
        Guid id,
        RejectionRequest request,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        MerchantAccountService merchantAccountService,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        PaymentIdempotencyService paymentIdempotencyService,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (await paymentsDbContext.InstallmentSubLogs.AsNoTracking().AnyAsync(value => value.Id == id, cancellationToken))
            return await RejectSubLogAsync(id, request, paymentsDbContext, operationsDbContext, identityDbContext, sharedDbContext, merchantAccountService, httpContext, currentUser, eventPublisher, paymentIdempotencyService, clock, idempotencyKey, cancellationToken);
        if (await paymentsDbContext.CashRecords.AsNoTracking().AnyAsync(value => value.Id == id && value.PaymentType == CashReceived, cancellationToken))
            return await RejectCashReceiptAsync(id, request, paymentsDbContext, paymentIdempotencyService, currentUser, clock, idempotencyKey, cancellationToken, cashRecordId: id);
        if (await paymentsDbContext.MainPaymentLogs.AsNoTracking().AnyAsync(value => value.Id == id && !value.IsDeleted, cancellationToken))
            return await RejectCashReceiptAsync(id, request, paymentsDbContext, paymentIdempotencyService, currentUser, clock, idempotencyKey, cancellationToken);
        return await RejectMerchantAccountCollectionAsync(id, new CollectionRejectionRequest(request.Reason), paymentsDbContext, paymentIdempotencyService, currentUser, clock, idempotencyKey, cancellationToken);
    }

    private static async Task<IResult> SubmitMerchantAccountCollectionAsync(
        Guid id,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        MerchantAccountCollectionDraft? draft = null;
        IResult? failure = null;
        await MerchantCollectionWorkspacePersistence.RunAsync(paymentsDbContext, async () =>
        {
            draft = await LoadMerchantAccountCollectionDraftForUpdateAsync(id, paymentsDbContext, cancellationToken);
            if (draft is null) { failure = Results.NotFound(); return; }
            if (draft.Status != Draft) { failure = Results.Conflict(new { code = "transition-conflict", detail = "Only saved drafts can be sent for approval." }); return; }
            if (draft.DraftedBy != currentUser.UserId && LenseeRoles.Normalize(currentUser.Role) is not (LenseeRoles.Admin or LenseeRoles.ERPAdmin)) { failure = Results.Forbid(); return; }

            var now = clock.EgyptNow;
            draft.AssignedTo ??= await SelectLeastLoadedAccountantAsync(identityDbContext, paymentsDbContext, cancellationToken);
            draft.AssignedAt ??= draft.AssignedTo.HasValue ? now : null;
            draft.Status = PendingAdminReview;
            AddPaymentWorkflowAudit(paymentsDbContext, "MerchantAccountCollectionSubmitted", Draft, draft.Status, draft.Account.MerchantId, null, null, draft.Id, currentUser.UserId ?? Guid.Empty, now, draft.Amount, draft.PaymentMethod, null, null, null, null);
            await MerchantCollectionWorkspacePersistence.SaveAsync(paymentsDbContext, cancellationToken);
        }, cancellationToken, identityDbContext);
        return failure ?? Results.Ok(ToMerchantAccountCollectionDraftResponse(draft!));
    }

    private static async Task<IResult> ApproveMerchantAccountCollectionAsync(
        Guid id,
        PaymentsDbContext paymentsDbContext,
        MerchantAccountService merchantAccountService,
        PaymentIdempotencyService paymentIdempotencyService,
        ICurrentUser currentUser,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var idempotency = await paymentIdempotencyService.StartAsync(idempotencyKey, $"POST /api/v1/payments/merchant-account-collections/{id}/approve", new { id }, cancellationToken);
        if (idempotency.Result is not null) return idempotency.Result;
        if (!IsCollectionReviewer(currentUser)) return await PaymentIdempotencyService.AbortAsync(idempotency, Results.Forbid());
        try
        {
            MerchantAccountCollectionDraft? draft = null;
            var now = clock.EgyptNow;
            var actorId = currentUser.UserId ?? Guid.Empty;
            await MerchantCollectionWorkspacePersistence.RunAsync(paymentsDbContext, async () =>
            {
                draft = await LoadMerchantAccountCollectionDraftForUpdateAsync(id, paymentsDbContext, cancellationToken);
                if (draft is null) return;
                if (draft.Status != PendingAdminReview) throw new InvalidOperationException("Only submitted account collections can be approved.");
                var allocations = string.IsNullOrWhiteSpace(draft.AllocationsJson)
                    ? null
                    : JsonSerializer.Deserialize<List<MerchantAllocationInput>>(draft.AllocationsJson);
                await merchantAccountService.PostCollectionAsync(draft.Account.MerchantId, draft.Id, draft.Amount, draft.PaymentMethod, draft.TransactionReference, actorId, now, allocations, draft.Notes, cancellationToken);
                if (draft.SourceOperationId is { } sourceOperationId)
                {
                    var sourcePayment = await paymentsDbContext.MainPaymentLogs
                        .SingleOrDefaultAsync(value => value.OperationId == sourceOperationId && !value.IsDeleted, cancellationToken);
                    if (sourcePayment is not null)
                    {
                        sourcePayment.AmountPaid = Math.Min(sourcePayment.TotalAmount, sourcePayment.AmountPaid + draft.Amount);
                        sourcePayment.PendingAmount = Math.Max(sourcePayment.TotalAmount - sourcePayment.AmountPaid, 0m);
                        sourcePayment.Status = sourcePayment.PendingAmount <= 0m ? ConfirmedPayment : PendingAccountant;
                        sourcePayment.LastModifiedBy = actorId;
                        sourcePayment.LastModifiedAt = now;
                    }
                }
                var previous = draft.Status;
                draft.Status = ConfirmedPayment;
                draft.ConfirmedBy = actorId;
                draft.ConfirmedAt = now;
                AddPaymentWorkflowAudit(paymentsDbContext, "MerchantAccountCollectionApproved", previous, draft.Status, draft.Account.MerchantId, null, null, draft.Id, actorId, now, draft.Amount, draft.PaymentMethod, null, httpContext: null, idempotencyKey, null);
                await MerchantCollectionWorkspacePersistence.SaveAsync(paymentsDbContext, cancellationToken);
            }, cancellationToken);
            if (draft is null) return await PaymentIdempotencyService.AbortAsync(idempotency, Results.NotFound());
            return await paymentIdempotencyService.CompleteAsync(idempotency, ToMerchantAccountCollectionDraftResponse(draft), StatusCodes.Status200OK, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return await PaymentIdempotencyService.AbortAsync(idempotency, Results.ValidationProblem(new Dictionary<string, string[]> { ["collection"] = [exception.Message] }));
        }
    }

    private static async Task<IResult> RejectMerchantAccountCollectionAsync(
        Guid id,
        CollectionRejectionRequest request,
        PaymentsDbContext paymentsDbContext,
        PaymentIdempotencyService paymentIdempotencyService,
        ICurrentUser currentUser,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var idempotency = await paymentIdempotencyService.StartAsync(idempotencyKey, $"POST /api/v1/payments/merchant-account-collections/{id}/reject", request, cancellationToken);
        if (idempotency.Result is not null) return idempotency.Result;
        if (!IsCollectionReviewer(currentUser)) return await PaymentIdempotencyService.AbortAsync(idempotency, Results.Forbid());
        if (string.IsNullOrWhiteSpace(request.Reason)) return await PaymentIdempotencyService.AbortAsync(idempotency, Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Reason)] = ["A rejection reason is required."] }));
        MerchantAccountCollectionDraft? draft = null;
        var now = clock.EgyptNow;
        await MerchantCollectionWorkspacePersistence.RunAsync(paymentsDbContext, async () =>
        {
            draft = await LoadMerchantAccountCollectionDraftForUpdateAsync(id, paymentsDbContext, cancellationToken);
            if (draft is null) return;
            if (draft.Status != PendingAdminReview) throw new InvalidOperationException("Only submitted account collections can be rejected.");
            var previous = draft.Status;
            draft.Status = "Rejected";
            draft.RejectionReason = request.Reason.Trim();
            draft.ConfirmedBy = currentUser.UserId;
            draft.ConfirmedAt = now;
            AddPaymentWorkflowAudit(paymentsDbContext, "MerchantAccountCollectionRejected", previous, draft.Status, draft.Account.MerchantId, null, null, draft.Id, currentUser.UserId ?? Guid.Empty, now, draft.Amount, draft.PaymentMethod, draft.RejectionReason, httpContext: null, idempotencyKey, null);
            await MerchantCollectionWorkspacePersistence.SaveAsync(paymentsDbContext, cancellationToken);
        }, cancellationToken);
        if (draft is null) return await PaymentIdempotencyService.AbortAsync(idempotency, Results.NotFound());
        return await paymentIdempotencyService.CompleteAsync(idempotency, ToMerchantAccountCollectionDraftResponse(draft), StatusCodes.Status200OK, cancellationToken);
    }

    private static async Task<IResult> ListPaymentAuditAsync(
        Guid? merchantId,
        Guid? operationId,
        Guid? paymentLogId,
        Guid? actorId,
        string? action,
        string? status,
        DateTime? from,
        DateTime? to,
        int? page,
        int? pageSize,
        PaymentsDbContext paymentsDbContext,
        CrmDbContext crmDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        CancellationToken cancellationToken)
    {
        var query = paymentsDbContext.PaymentAuditEvents.AsNoTracking().AsQueryable();
        if (merchantId.HasValue) query = query.Where(value => value.MerchantId == merchantId.Value);
        if (operationId.HasValue) query = query.Where(value => value.OperationId == operationId.Value);
        if (paymentLogId.HasValue) query = query.Where(value => value.PaymentLogId == paymentLogId.Value);
        if (actorId.HasValue) query = query.Where(value => value.ActorId == actorId.Value);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(value => value.Action == action.Trim());
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(value => value.NewStatus == status.Trim());
        if (from.HasValue) query = query.Where(value => value.OccurredAt >= from.Value);
        if (to.HasValue) query = query.Where(value => value.OccurredAt <= to.Value);
        var safePage = Math.Max(page ?? 1, 1);
        var safePageSize = Math.Clamp(pageSize ?? 50, 1, 200);
        var total = await query.CountAsync(cancellationToken);
        var events = await query.OrderByDescending(value => value.OccurredAt).ThenByDescending(value => value.Id)
            .Skip((safePage - 1) * safePageSize).Take(safePageSize)
            .ToListAsync(cancellationToken);
        var merchantIds = events.Where(value => value.MerchantId.HasValue).Select(value => value.MerchantId!.Value).Distinct().ToArray();
        var operationIds = events.Where(value => value.OperationId.HasValue).Select(value => value.OperationId!.Value).Distinct().ToArray();
        var actorIds = events.Where(value => value.ActorId != Guid.Empty).Select(value => value.ActorId).Distinct().ToArray();
        var draftIds = events.Where(value => value.CollectionDraftId.HasValue).Select(value => value.CollectionDraftId!.Value).Distinct().ToArray();
        var merchantNames = merchantIds.Length == 0 ? new Dictionary<Guid, string>() : await crmDbContext.Merchants.AsNoTracking()
            .Where(value => merchantIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, value => value.BusinessName, cancellationToken);
        var operations = operationIds.Length == 0 ? new Dictionary<Guid, PaymentAuditOperationContext>() : await operationsDbContext.OperationLogs.AsNoTracking()
            .Where(value => operationIds.Contains(value.Id)).Select(value => new PaymentAuditOperationContext(value.Id, value.OperationNumber, value.ClientName)).ToDictionaryAsync(value => value.Id, cancellationToken);
        var actors = actorIds.Length == 0 ? new Dictionary<Guid, PaymentAuditActorContext>() : await identityDbContext.Users.AsNoTracking()
            .Where(value => actorIds.Contains(value.Id)).Select(value => new PaymentAuditActorContext(value.Id, string.IsNullOrWhiteSpace(value.FullName) ? value.Username : value.FullName, value.Role)).ToDictionaryAsync(value => value.Id, cancellationToken);
        var drafts = draftIds.Length == 0 ? new Dictionary<Guid, string?>() : await paymentsDbContext.MerchantAccountCollectionDrafts.AsNoTracking()
            .Where(value => draftIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, value => value.TransactionReference, cancellationToken);
        var items = events.Select(value => new PaymentAuditEventResponse(
            value.Id, value.Action, value.PreviousStatus, value.NewStatus, value.MerchantId, value.OperationId, value.PaymentLogId, value.CollectionDraftId,
            value.ActorId, value.OccurredAt, value.Amount, value.PaymentMethod, value.Reason, value.CorrelationId, value.IdempotencyKey, value.DataJson,
            merchantNames.GetValueOrDefault(value.MerchantId ?? Guid.Empty), operations.GetValueOrDefault(value.OperationId ?? Guid.Empty)?.OperationNumber,
            operations.GetValueOrDefault(value.OperationId ?? Guid.Empty)?.BuyerName, actors.GetValueOrDefault(value.ActorId)?.Name,
            actors.GetValueOrDefault(value.ActorId)?.Role, drafts.GetValueOrDefault(value.CollectionDraftId ?? Guid.Empty),
            value.MerchantId.HasValue ? "MerchantAccount" : "DirectOperation")).ToList();
        return Results.Ok(new PagedResult<PaymentAuditEventResponse>(items, safePage, safePageSize, total));
    }

    private static async Task<IResult> RequestMerchantRefundAsync(
        Guid merchantId,
        MerchantRefundRequest request,
        PaymentsDbContext paymentsDbContext,
        MerchantAccountService merchantAccountService,
        PaymentIdempotencyService paymentIdempotencyService,
        ICurrentUser currentUser,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var idempotency = await paymentIdempotencyService.StartAsync(idempotencyKey, $"POST /api/v1/payments/merchant-accounts/{merchantId}/refund-requests", request, cancellationToken);
        if (idempotency.Result is not null) return idempotency.Result;
        if (request.Amount <= 0) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Amount)] = ["Amount must be greater than zero."] });
        var account = await merchantAccountService.GetOrCreateForUpdateAsync(merchantId, currentUser.UserId ?? Guid.Empty, clock.EgyptNow, cancellationToken);
        var snapshot = await merchantAccountService.GetSnapshotAsync(merchantId, cancellationToken);
        if (snapshot is null || request.Amount > snapshot.CreditAvailable) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Amount)] = ["Refund amount exceeds available merchant credit."] });
        var reservation = new MerchantRefundReservation { Id = Guid.NewGuid(), AccountId = account.Id, Amount = request.Amount, Status = PendingApproval, CreatedBy = currentUser.UserId ?? Guid.Empty, CreatedAt = clock.EgyptNow, Notes = request.Notes?.Trim() };
        paymentsDbContext.MerchantRefundReservations.Add(reservation);
        await MerchantCollectionWorkspacePersistence.SaveAsync(paymentsDbContext, cancellationToken);
        return await paymentIdempotencyService.CompleteAsync(idempotency, reservation, StatusCodes.Status201Created, cancellationToken);
    }

    private static async Task<IResult> ApproveMerchantRefundAsync(Guid id, PaymentsDbContext paymentsDbContext, ICurrentUser currentUser, IClock clock, PaymentIdempotencyService paymentIdempotencyService, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        await using var idempotency = await paymentIdempotencyService.StartAsync(idempotencyKey, $"POST /api/v1/payments/merchant-accounts/refund-requests/{id}/approve", new { id }, cancellationToken);
        if (idempotency.Result is not null) return idempotency.Result;
        var reservation = await paymentsDbContext.MerchantRefundReservations.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (reservation is null) return Results.NotFound();
        if (reservation.Status != PendingApproval) return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Only pending refund requests can be approved."] });
        reservation.Status = "Approved";
        reservation.ApprovedBy = currentUser.UserId;
        reservation.ApprovedAt = clock.EgyptNow;
        await MerchantCollectionWorkspacePersistence.SaveAsync(paymentsDbContext, cancellationToken);
        return await paymentIdempotencyService.CompleteAsync(idempotency, reservation, StatusCodes.Status200OK, cancellationToken);
    }

    private static async Task<IResult> PayoutMerchantRefundAsync(Guid id, MerchantRefundPayoutRequest request, PaymentsDbContext paymentsDbContext, MerchantAccountService merchantAccountService, ICurrentUser currentUser, IClock clock, PaymentIdempotencyService paymentIdempotencyService, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken)
    {
        await using var idempotency = await paymentIdempotencyService.StartAsync(idempotencyKey, $"POST /api/v1/payments/merchant-accounts/refund-requests/{id}/payout", request, cancellationToken);
        if (idempotency.Result is not null) return idempotency.Result;
        var reservation = await paymentsDbContext.MerchantRefundReservations.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (reservation is null) return Results.NotFound();
        var account = await paymentsDbContext.MerchantReceivableAccounts.AsNoTracking().SingleAsync(value => value.Id == reservation.AccountId, cancellationToken);
        var method = NormalizeMovementMethod(request.PaymentMethod);
        if (method is null) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PaymentMethod)] = ["Payment method must be CashHandToHand, CashTransaction, BankTransfer, or Wallet."] });
        if (MerchantAccountService.RequiresTransactionReference(method) && string.IsNullOrWhiteSpace(request.TransactionReference)) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.TransactionReference)] = ["An electronic payment requires a transaction reference."] });
        try
        {
            var entry = await merchantAccountService.PostRefundPayoutAsync(account.MerchantId, id, Guid.NewGuid(), request.Amount, method, request.TransactionReference?.Trim(), currentUser.UserId ?? Guid.Empty, clock.EgyptNow, request.Notes, cancellationToken);
            return await paymentIdempotencyService.CompleteAsync(idempotency, entry, StatusCodes.Status200OK, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return await PaymentIdempotencyService.AbortAsync(idempotency, Results.ValidationProblem(new Dictionary<string, string[]> { ["refund"] = [exception.Message] }));
        }
    }

    private static async Task<IResult> GetMerchantAccountClassificationSettingsAsync(MerchantAccountService merchantAccountService, CancellationToken cancellationToken) =>
        Results.Ok(await merchantAccountService.GetClassificationSettingsAsync(cancellationToken));

    private static async Task<IResult> GetMerchantAccountClassificationHistoryAsync(Guid merchantId, MerchantAccountService merchantAccountService, CancellationToken cancellationToken) =>
        Results.Ok(await merchantAccountService.GetClassificationHistoryAsync(merchantId, cancellationToken));

    private static async Task<IResult> UpdateMerchantAccountClassificationSettingsAsync(MerchantAccountClassificationSettings request, SharedDbContext sharedDbContext, IClock clock, CancellationToken cancellationToken)
    {
        if (!request.IsValid) return Results.ValidationProblem(new Dictionary<string, string[]> { ["settings"] = ["Classification weights, thresholds, and grade bands are invalid."] });
        var setting = await sharedDbContext.SystemSettings.SingleOrDefaultAsync(value => value.Key == MerchantAccountClassificationSettings.SettingKey, cancellationToken);
        if (setting is null)
        {
            setting = new SystemSetting { Key = MerchantAccountClassificationSettings.SettingKey, Description = "Merchant receivable account classification settings." };
            sharedDbContext.SystemSettings.Add(setting);
        }
        setting.Value = JsonSerializer.Serialize(request);
        setting.UpdatedAt = clock.EgyptNow;
        await MerchantCollectionWorkspacePersistence.SaveAsync(sharedDbContext, cancellationToken);
        return Results.Ok(request);
    }

    private static async Task<IResult> ListPaymentLogsAsync(
        string? scope,
        HttpContext httpContext,
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
        scope ??= httpContext.Request.Path.Value?.Contains("/other-payments", StringComparison.OrdinalIgnoreCase) == true
            ? "DirectOperation"
            : httpContext.Request.Path.Value?.Contains("/merchant-account-payments", StringComparison.OrdinalIgnoreCase) == true
                ? "MerchantAccount"
                : null;
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
        // Scope is derived from the source operation as a fallback for legacy
        // payment rows whose denormalized MerchantId was never populated.
        if (string.Equals(scope, "MerchantAccount", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scope, "DirectOperation", StringComparison.OrdinalIgnoreCase) ||
            merchantId.HasValue)
        {
            var sourceOperationIds = await operationsDbContext.OperationLogs.AsNoTracking()
                .Where(operation => !operation.IsDeleted &&
                    (!merchantId.HasValue || operation.ClientId == merchantId.Value))
                .Select(operation => new { operation.Id, operation.ClientId, operation.OperationType })
                .ToListAsync(cancellationToken);
            var merchantOperationIds = sourceOperationIds.Where(value => value.ClientId.HasValue).Select(value => value.Id).ToArray();
            var directOperationIds = sourceOperationIds
                .Where(value => !value.ClientId.HasValue && value.OperationType == RetailSale)
                .Select(value => value.Id)
                .ToArray();
            if (merchantId.HasValue)
            {
                query = query.Where(log => log.MerchantId == merchantId.Value ||
                    (log.MerchantId == null && merchantOperationIds.Contains(log.OperationId)));
            }
            else if (string.Equals(scope, "MerchantAccount", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(log => log.MerchantId.HasValue || merchantOperationIds.Contains(log.OperationId));
            }
            else if (string.Equals(scope, "DirectOperation", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(log => !log.MerchantId.HasValue && directOperationIds.Contains(log.OperationId));
            }
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(log => log.LastModifiedAt)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var operationIds = rows.Select(row => row.OperationId).ToArray();
        var logIds = rows.Select(row => row.Id).ToArray();
        var cashRecords = operationIds.Length == 0 ? [] : await paymentsDbContext.CashRecords
            .Where(record => record.OperationId.HasValue && operationIds.Contains(record.OperationId.Value)).ToListAsync(cancellationToken);
        var adjustments = logIds.Length == 0 ? [] : await paymentsDbContext.FinancialAdjustments
            .Where(adjustment => adjustment.PaymentLogId.HasValue && logIds.Contains(adjustment.PaymentLogId.Value)).ToListAsync(cancellationToken);
        var cashByOperation = cashRecords.GroupBy(record => record.OperationId!.Value).ToDictionary(group => group.Key, group => (IReadOnlyList<CashRecord>)group.ToList());
        var adjustmentsByLog = adjustments.GroupBy(adjustment => adjustment.PaymentLogId!.Value).ToDictionary(group => group.Key, group => (IReadOnlyList<FinancialAdjustment>)group.ToList());
        var userLookup = await LoadUserLookupAsync(identityDbContext, rows, cancellationToken);
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, rows.Select(row => row.OperationId), cancellationToken);
        var responses = rows.Select(log => ToListResponse(log, userLookup, operationLookup,
            cashByOperation.GetValueOrDefault(log.OperationId, []),
            adjustmentsByLog.GetValueOrDefault(log.Id, []))).ToList();

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
        string? scope,
        HttpContext httpContext,
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
        scope ??= httpContext.Request.Path.Value?.Contains("/other-payments", StringComparison.OrdinalIgnoreCase) == true
            ? "DirectOperation"
            : httpContext.Request.Path.Value?.Contains("/merchant-account-payments", StringComparison.OrdinalIgnoreCase) == true
                ? "MerchantAccount"
                : null;
        var request = new PageRequest(page ?? 1, pageSize ?? 100);

        var logsQuery = paymentsDbContext.MainPaymentLogs
            .Include(log => log.InstallmentSubLogs)
            .Where(log => !log.IsDeleted)
            .AsQueryable();
        var cashQuery = paymentsDbContext.CashRecords.AsQueryable();
        var adjustmentsQuery = paymentsDbContext.FinancialAdjustments.AsQueryable();

        if (merchantId.HasValue)
        {
            var merchantOperationIds = await operationsDbContext.OperationLogs.AsNoTracking()
                .Where(operation => !operation.IsDeleted && operation.ClientId == merchantId.Value)
                .Select(operation => operation.Id)
                .ToArrayAsync(cancellationToken);
            logsQuery = logsQuery.Where(log => log.MerchantId == merchantId.Value ||
                (log.MerchantId == null && merchantOperationIds.Contains(log.OperationId)));
            cashQuery = cashQuery.Where(record => record.MerchantId == merchantId.Value ||
                (record.OperationId.HasValue && merchantOperationIds.Contains(record.OperationId.Value)));
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
            .Concat(cashRecords.Where(record => record.OperationId.HasValue).Select(record => record.OperationId!.Value))
            .Concat(adjustments.Where(adjustment => adjustment.OperationId.HasValue).Select(adjustment => adjustment.OperationId!.Value))
            .Distinct()
            .ToArray();
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, operationIds, cancellationToken);

        var merchantIds = logs.Select(log => log.MerchantId)
            .Concat(operationLookup.Values.Select(operation => operation.MerchantId))
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
        if (string.Equals(scope, "MerchantAccount", StringComparison.OrdinalIgnoreCase))
        {
            ordered = ordered.Where(row => row.MerchantId.HasValue).ToList();
        }
        else if (string.Equals(scope, "DirectOperation", StringComparison.OrdinalIgnoreCase))
        {
            ordered = ordered.Where(row => !row.MerchantId.HasValue && string.Equals(row.OperationType, RetailSale, StringComparison.OrdinalIgnoreCase)).ToList();
        }
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
        MerchantAccountService merchantAccountService,
        PaymentIdempotencyService paymentIdempotencyService,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var idempotency = await paymentIdempotencyService.StartAsync(idempotencyKey, "POST /api/v1/payments/initialize", request, cancellationToken);
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
            return await paymentIdempotencyService.CompleteAsync(
                idempotency,
                ToDetailResponse(existing, existingCashRecords, existingAdjustments, existingLookup, existingOperationLookup),
                StatusCodes.Status200OK,
                cancellationToken);
        }

        var paymentMethod = NormalizePaymentMethod(request.PaymentMethod);
        if (paymentMethod is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PaymentMethod)] = ["Payment method is required and must be CashHandToHand, CashTransaction, or MerchantAccount."] });
        }
        if (!IsPaymentEligible(operation))
        {
            return Results.Conflict(new { code = "payment-ineligible", detail = "Payments can only be initialized for finalized sales or a positive finalized exchange." });
        }

        var now = clock.EgyptNow;
        var assignedTo = await SelectLeastLoadedAccountantAsync(identityDbContext, paymentsDbContext, cancellationToken);
        var log = new MainPaymentLog
        {
            Id = Guid.NewGuid(),
            OperationId = operation.Id,
            MerchantId = merchantId,
            Scope = "MerchantAccount",
            TotalAmount = total,
            AmountPaid = 0,
            PendingAmount = 0,
            PaymentMethod = paymentMethod,
            Status = PendingAccountant,
            InitializedBy = currentUser.UserId ?? Guid.Empty,
            InitializedAt = now,
            AssignedTo = assignedTo,
            AssignedAt = assignedTo.HasValue ? now : null,
            LastModifiedBy = currentUser.UserId,
            LastModifiedAt = now,
            Notes = request.Notes
        };

        await SharedDbTransaction.ExecuteAsync(paymentsDbContext, async () =>
        {
            paymentsDbContext.MainPaymentLogs.Add(log);
            await AddPaymentAuditAsync(identityDbContext, currentUser, httpContext, "PaymentLogInitialized", log.Id, new { log.OperationId, log.TotalAmount, log.PaymentMethod }, now, cancellationToken);
            AddPaymentWorkflowAudit(paymentsDbContext, "PaymentLogInitialized", null, log.Status, log.MerchantId, log.OperationId, log.Id, null, currentUser.UserId ?? Guid.Empty, now, log.TotalAmount, log.PaymentMethod, null, httpContext, idempotencyKey, null);
            await paymentsDbContext.SaveChangesAsync(cancellationToken);
            await identityDbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken, identityDbContext, sharedDbContext);
        var userLookup = await LoadUserLookupAsync(identityDbContext, [log], cancellationToken);
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, [log.OperationId], cancellationToken);
        var cashRecords = await LoadCashRecordsForLogAsync(paymentsDbContext, log, cancellationToken);
        var adjustments = await LoadAdjustmentsForLogAsync(paymentsDbContext, log, cancellationToken);
        return await paymentIdempotencyService.CompleteAsync(idempotency, ToDetailResponse(log, cashRecords, adjustments, userLookup, operationLookup), StatusCodes.Status201Created, cancellationToken);
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
        PaymentIdempotencyService paymentIdempotencyService,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var idempotency = await paymentIdempotencyService.StartAsync(idempotencyKey, $"POST /api/v1/payments/{id}/assign", request, cancellationToken);
        if (idempotency.Result is not null)
        {
            return idempotency.Result;
        }
        if (!IsCollectionReviewer(currentUser))
        {
            return await PaymentIdempotencyService.AbortAsync(idempotency, Results.Forbid());
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
            AddPaymentWorkflowAudit(paymentsDbContext, "PaymentReassigned", null, log.Status, log.MerchantId, log.OperationId, log.Id, null, currentUser.UserId ?? Guid.Empty, now, null, log.PaymentMethod, null, httpContext, idempotencyKey, new { log.AssignedTo });
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
            return await PaymentIdempotencyService.AbortAsync(idempotency, transactionResult);
        }

        var userLookup = await LoadUserLookupAsync(identityDbContext, [log!], cancellationToken);
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, [log!.OperationId], cancellationToken);
        var cashRecords = await LoadCashRecordsForLogAsync(paymentsDbContext, log!, cancellationToken);
        var adjustments = await LoadAdjustmentsForLogAsync(paymentsDbContext, log!, cancellationToken);
        return await paymentIdempotencyService.CompleteAsync(idempotency, ToDetailResponse(log!, cashRecords, adjustments, userLookup, operationLookup), StatusCodes.Status200OK, cancellationToken);
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
        MerchantAccountService merchantAccountService,
        IAppEventPublisher eventPublisher,
        PaymentIdempotencyService paymentIdempotencyService,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var idempotency = await paymentIdempotencyService.StartAsync(idempotencyKey, $"POST /api/v1/payments/{id}/sub-logs", request, cancellationToken);
        if (idempotency.Result is not null)
        {
            return idempotency.Result;
        }

        if (request.Amount <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Amount)] = ["Amount must be greater than zero."] });
        }

        var paymentMethod = NormalizeMovementMethod(request.PaymentMethod);
        if (paymentMethod is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PaymentMethod)] = ["Payment method is required and must be CashHandToHand, CashTransaction, BankTransfer, or Wallet."] });
        }
        if (MerchantAccountService.RequiresTransactionReference(paymentMethod) && string.IsNullOrWhiteSpace(request.TransactionReference))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.TransactionReference)] = ["An electronic payment requires a transaction reference."] });
        }

        MainPaymentLog? log = null;
        Guid? effectiveMerchantId = null;
        IResult? transactionResult = null;
        await SharedDbTransaction.ExecuteAsync(paymentsDbContext, async () =>
        {
            log = await LoadPaymentLogForUpdateAsync(id, paymentsDbContext, cancellationToken);
            if (log is null)
            {
                transactionResult = Results.NotFound();
                return;
            }
            effectiveMerchantId = log.MerchantId ?? await operationsDbContext.OperationLogs.AsNoTracking()
                .Where(value => value.Id == log.OperationId && !value.IsDeleted)
                .Select(value => value.ClientId)
                .SingleOrDefaultAsync(cancellationToken);
            if (effectiveMerchantId.HasValue && MerchantAccountService.IsMovementMethod(log.PaymentMethod))
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(id)] = ["Registered-merchant movement collections must use the merchant-account approval workflow."] });
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

            var cashRecords = await paymentsDbContext.CashRecords
                .Where(record => record.OperationId == log.OperationId)
                .ToListAsync(cancellationToken);
            var adjustments = await paymentsDbContext.FinancialAdjustments
                .Where(adjustment => adjustment.PaymentLogId == log.Id)
                .ToListAsync(cancellationToken);
            var paymentBalance = PaymentBalanceCalculator.Calculate(log, adjustments, cashRecords);
            var remaining = Math.Max(paymentBalance.RemainingAmount - paymentBalance.PendingCollections, 0m);
            if (request.Amount > remaining)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Amount)] = [$"Amount exceeds remaining payable amount ({remaining:0.####})."] });
                return;
            }

            var now = clock.EgyptNow;
            if (!log.AssignedTo.HasValue)
            {
                log.AssignedTo = await SelectLeastLoadedAccountantAsync(identityDbContext, paymentsDbContext, cancellationToken);
                log.AssignedAt = log.AssignedTo.HasValue ? now : null;
            }
            var subLog = new InstallmentSubLog
            {
                Id = Guid.NewGuid(),
                MainLogId = log.Id,
                Amount = request.Amount,
                PaymentMethod = paymentMethod,
                TransactionReference = request.TransactionReference?.Trim(),
                DateReceived = request.DateReceived ?? DateOnly.FromDateTime(now),
                SubLogStatus = request.SubmitForReview ? PendingAdminReview : Draft,
                DraftedBy = currentUser.UserId ?? Guid.Empty,
                DraftedAt = now,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? "0" : request.Notes.Trim()
            };
            paymentsDbContext.InstallmentSubLogs.Add(subLog);
            log.PendingAmount += subLog.Amount;
            log.Status = request.SubmitForReview ? PendingAdminReview : PendingAccountant;
            log.LastModifiedBy = currentUser.UserId;
            log.LastModifiedAt = now;

            var auditAction = request.SubmitForReview ? "PaymentSubLogSubmittedForApproval" : "PaymentSubLogDrafted";
            await AddPaymentAuditAsync(identityDbContext, currentUser, httpContext, auditAction, log.Id, new { subLog.Id, request.Amount, paymentMethod }, now, cancellationToken);
            AddPaymentWorkflowAudit(paymentsDbContext, auditAction, null, subLog.SubLogStatus, log.MerchantId, log.OperationId, log.Id, null, currentUser.UserId ?? Guid.Empty, now, subLog.Amount, paymentMethod, null, httpContext, idempotencyKey, new { subLog.Id, log.AssignedTo });
            if (request.SubmitForReview)
            {
                await eventPublisher.PublishAsync(new PaymentWorkflowChangedEvent(
                    log.Id, log.MerchantId, log.OperationId, "PaymentSubLogDrafted",
                    $"A payment sub-log for {request.Amount:0.####} is awaiting Admin review.", null, LenseeRoles.Admin, now), cancellationToken);
            }
            await paymentsDbContext.SaveChangesAsync(cancellationToken);
            await identityDbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken, identityDbContext, sharedDbContext);

        if (transactionResult is not null)
        {
            return await PaymentIdempotencyService.AbortAsync(idempotency, transactionResult);
        }

        var userLookup = await LoadUserLookupAsync(identityDbContext, [log!], cancellationToken);
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, [log!.OperationId], cancellationToken);
        var cashRecords = await LoadCashRecordsForLogAsync(paymentsDbContext, log!, cancellationToken);
        var adjustments = await LoadAdjustmentsForLogAsync(paymentsDbContext, log!, cancellationToken);
        return await paymentIdempotencyService.CompleteAsync(idempotency, ToDetailResponse(log!, cashRecords, adjustments, userLookup, operationLookup), StatusCodes.Status201Created, cancellationToken);
    }

    private static Task<IResult> ApproveSubLogAsync(
        Guid id,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        MerchantAccountService merchantAccountService,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        PaymentIdempotencyService paymentIdempotencyService,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) =>
        SetSubLogStatusAsync(id, ConfirmedPayment, null, paymentsDbContext, operationsDbContext, identityDbContext, sharedDbContext, merchantAccountService, httpContext, currentUser, eventPublisher, paymentIdempotencyService, clock, idempotencyKey, cancellationToken);

    private static Task<IResult> RejectSubLogAsync(
        Guid id,
        RejectionRequest request,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        MerchantAccountService merchantAccountService,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        PaymentIdempotencyService paymentIdempotencyService,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken) =>
        SetSubLogStatusAsync(id, Rejected, request.Reason, paymentsDbContext, operationsDbContext, identityDbContext, sharedDbContext, merchantAccountService, httpContext, currentUser, eventPublisher, paymentIdempotencyService, clock, idempotencyKey, cancellationToken);

    private static async Task<IResult> SubmitSubLogAsync(
        Guid id,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var mainLogId = await paymentsDbContext.InstallmentSubLogs.AsNoTracking()
            .Where(value => value.Id == id).Select(value => (Guid?)value.MainLogId).SingleOrDefaultAsync(cancellationToken);
        if (!mainLogId.HasValue) return Results.NotFound();
        InstallmentSubLog? subLog = null;
        MainPaymentLog? log = null;
        IResult? failure = null;
        await SharedDbTransaction.ExecuteAsync(paymentsDbContext, async () =>
        {
            log = await LoadPaymentLogForUpdateAsync(mainLogId.Value, paymentsDbContext, cancellationToken);
            subLog = log?.InstallmentSubLogs.SingleOrDefault(value => value.Id == id);
            if (subLog is null || log is null) { failure = Results.NotFound(); return; }
            if (subLog.SubLogStatus != Draft) { failure = Results.Conflict(new { code = "transition-conflict", detail = "Only saved drafts can be sent for approval." }); return; }
            if (subLog.DraftedBy != currentUser.UserId && LenseeRoles.Normalize(currentUser.Role) is not (LenseeRoles.Admin or LenseeRoles.ERPAdmin)) { failure = Results.Forbid(); return; }

            var now = clock.EgyptNow;
            log.AssignedTo ??= await SelectLeastLoadedAccountantAsync(identityDbContext, paymentsDbContext, cancellationToken);
            log.AssignedAt ??= log.AssignedTo.HasValue ? now : null;
            subLog.SubLogStatus = PendingAdminReview;
            log.Status = PendingAdminReview;
            log.LastModifiedBy = currentUser.UserId;
            log.LastModifiedAt = now;
            AddPaymentWorkflowAudit(paymentsDbContext, "PaymentSubLogSubmittedForApproval", Draft, PendingAdminReview, log.MerchantId, log.OperationId, log.Id, null, currentUser.UserId ?? Guid.Empty, now, subLog.Amount, subLog.PaymentMethod, null, null, null, new { subLog.Id });
            await paymentsDbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken, identityDbContext);
        return failure ?? Results.Ok(new { Id = subLog!.Id, status = subLog.SubLogStatus, MainLogId = subLog.MainLogId, AssignedTo = log!.AssignedTo });
    }

    private static async Task<IResult> ApproveCashReceiptAsync(
        Guid id,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        MerchantAccountService merchantAccountService,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        PaymentIdempotencyService paymentIdempotencyService,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken,
        Guid? cashRecordId = null)
    {
        await using var idempotency = await paymentIdempotencyService.StartAsync(idempotencyKey, $"POST /api/v1/payments/cash-receipts/{id}/approve", new { id }, cancellationToken);
        if (idempotency.Result is not null)
        {
            return idempotency.Result;
        }
        if (!IsCollectionReviewer(currentUser))
        {
            return await PaymentIdempotencyService.AbortAsync(idempotency, Results.Forbid());
        }

        MainPaymentLog? log = null;
        Guid? effectiveMerchantId = null;
        IResult? transactionResult = null;
        await SharedDbTransaction.ExecuteAsync(paymentsDbContext, async () =>
        {
            var parentLogId = cashRecordId.HasValue
                ? await paymentsDbContext.CashRecords.AsNoTracking()
                    .Where(value => value.Id == cashRecordId.Value && value.PaymentType == CashReceived)
                    .Join(paymentsDbContext.MainPaymentLogs.Where(value => !value.IsDeleted), value => value.OperationId, value => value.OperationId, (_, parent) => (Guid?)parent.Id)
                    .SingleOrDefaultAsync(cancellationToken)
                : id;
            log = parentLogId.HasValue
                ? await LoadPaymentLogForUpdateAsync(parentLogId.Value, paymentsDbContext, cancellationToken)
                : null;
            if (log is null)
            {
                transactionResult = Results.NotFound();
                return;
            }
            effectiveMerchantId = log.MerchantId ?? await operationsDbContext.OperationLogs.AsNoTracking()
                .Where(value => value.Id == log.OperationId && !value.IsDeleted)
                .Select(value => value.ClientId)
                .SingleOrDefaultAsync(cancellationToken);
            if (log.Status == PaymentCompleted)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["This cash receipt is already approved."] });
                return;
            }

            var cashRecord = await paymentsDbContext.CashRecords
                .FirstOrDefaultAsync(value => (cashRecordId.HasValue ? value.Id == cashRecordId.Value : value.OperationId == log.OperationId) &&
                                              value.PaymentType == CashReceived &&
                                              (value.Status == PendingAccountant || value.Status == PendingAdminReview), cancellationToken);
            if (cashRecord is null)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["cashRecord"] = ["The cash receipt record was not found."] });
                return;
            }
            if (cashRecord.Status is not (PendingAccountant or PendingAdminReview))
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["This cash receipt was already changed by another user."] });
                return;
            }

            var now = clock.EgyptNow;
            if (!MerchantAccountService.IsMovementMethod(cashRecord.SubType) ||
                (MerchantAccountService.RequiresTransactionReference(cashRecord.SubType) && string.IsNullOrWhiteSpace(cashRecord.TransactionReference)))
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["paymentMethod"] = ["The cash receipt has no valid movement method or electronic reference and must be reconciled before approval."] });
                return;
            }
            cashRecord.Status = PaymentCompleted;
            cashRecord.ConfirmedBy = currentUser.UserId;
            cashRecord.ConfirmedAt = now;
            var cashRecords = await paymentsDbContext.CashRecords
                .Where(record => record.OperationId == log.OperationId)
                .ToListAsync(cancellationToken);
            var adjustments = await paymentsDbContext.FinancialAdjustments
                .Where(adjustment => adjustment.PaymentLogId == log.Id)
                .ToListAsync(cancellationToken);
            var balance = PaymentBalanceCalculator.Calculate(log, adjustments, cashRecords);
            log.AmountPaid = balance.ConfirmedCollections;
            log.PendingAmount = balance.PendingCollections;
            log.Status = balance.RemainingAmount == 0m && balance.RefundDue == 0m
                ? PaymentCompleted
                : PendingAccountant;
            if (effectiveMerchantId is { } merchantId)
            {
                await merchantAccountService.PostCollectionAsync(merchantId, cashRecord.Id, cashRecord.Amount, cashRecord.SubType!, cashRecord.TransactionReference, currentUser.UserId ?? Guid.Empty, now, null, cashRecord.Notes, cancellationToken);
            }
            log.LastModifiedBy = currentUser.UserId;
            log.LastModifiedAt = now;

            await AddPaymentAuditAsync(identityDbContext, currentUser, httpContext, "CashReceiptApproved", log.Id, new { cashRecord.Id, cashRecord.Amount }, now, cancellationToken);
            AddPaymentWorkflowAudit(paymentsDbContext, "CashReceiptApproved", PendingAccountant, PaymentCompleted, effectiveMerchantId, log.OperationId, log.Id, null, currentUser.UserId ?? Guid.Empty, now, cashRecord.Amount, cashRecord.SubType, null, httpContext, idempotencyKey, new { cashRecord.Id });
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
            return await PaymentIdempotencyService.AbortAsync(idempotency, transactionResult);
        }

        var userLookup = await LoadUserLookupAsync(identityDbContext, [log!], cancellationToken);
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, [log!.OperationId], cancellationToken);
        var cashRecords = await LoadCashRecordsForLogAsync(paymentsDbContext, log!, cancellationToken);
        var adjustments = await LoadAdjustmentsForLogAsync(paymentsDbContext, log!, cancellationToken);
        return await paymentIdempotencyService.CompleteAsync(idempotency, ToDetailResponse(log!, cashRecords, adjustments, userLookup, operationLookup), StatusCodes.Status200OK, cancellationToken);
    }

    private static async Task<IResult> RejectCashReceiptAsync(
        Guid id,
        RejectionRequest request,
        PaymentsDbContext paymentsDbContext,
        PaymentIdempotencyService paymentIdempotencyService,
        ICurrentUser currentUser,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken,
        Guid? cashRecordId = null)
    {
        await using var idempotency = await paymentIdempotencyService.StartAsync(idempotencyKey, $"POST /api/v1/payments/cash-receipts/{id}/reject", request, cancellationToken);
        if (idempotency.Result is not null) return idempotency.Result;
        if (!IsCollectionReviewer(currentUser)) return await PaymentIdempotencyService.AbortAsync(idempotency, Results.Forbid());
        if (string.IsNullOrWhiteSpace(request.Reason)) return await PaymentIdempotencyService.AbortAsync(idempotency, Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Reason)] = ["Rejection reason is required."] }));

        MainPaymentLog? log = null;
        CashRecord? record = null;
        await SharedDbTransaction.ExecuteAsync(paymentsDbContext, async () =>
        {
            var parentLogId = cashRecordId.HasValue
                ? await paymentsDbContext.CashRecords.AsNoTracking()
                    .Where(value => value.Id == cashRecordId.Value && value.PaymentType == CashReceived)
                    .Join(paymentsDbContext.MainPaymentLogs.Where(value => !value.IsDeleted), value => value.OperationId, value => value.OperationId, (_, parent) => (Guid?)parent.Id)
                    .SingleOrDefaultAsync(cancellationToken)
                : id;
            log = parentLogId.HasValue
                ? await LoadPaymentLogForUpdateAsync(parentLogId.Value, paymentsDbContext, cancellationToken)
                : null;
            if (log is null) return;
            record = await paymentsDbContext.CashRecords
                .FirstOrDefaultAsync(value => (cashRecordId.HasValue ? value.Id == cashRecordId.Value : value.OperationId == log.OperationId) && value.PaymentType == CashReceived && (value.Status == PendingAccountant || value.Status == PendingAdminReview), cancellationToken);
            if (record is null) return;

            var now = clock.EgyptNow;
            record.Status = "Cancelled";
            record.ConfirmedBy = currentUser.UserId;
            record.ConfirmedAt = now;
            record.Notes = string.IsNullOrWhiteSpace(record.Notes)
                ? $"Rejected: {request.Reason.Trim()}"
                : $"{record.Notes}\nRejected: {request.Reason.Trim()}";
            var cashRecords = await paymentsDbContext.CashRecords.Where(value => value.OperationId == log.OperationId).ToListAsync(cancellationToken);
            var adjustments = await paymentsDbContext.FinancialAdjustments.Where(value => value.PaymentLogId == log.Id).ToListAsync(cancellationToken);
            var balance = PaymentBalanceCalculator.Calculate(log, adjustments, cashRecords);
            log.AmountPaid = balance.ConfirmedCollections;
            log.PendingAmount = balance.PendingCollections;
            log.Status = PendingAccountant;
            log.LastModifiedBy = currentUser.UserId;
            log.LastModifiedAt = now;
            AddPaymentWorkflowAudit(paymentsDbContext, "CashReceiptRejected", PendingAccountant, Rejected, log.MerchantId, log.OperationId, log.Id, null, currentUser.UserId ?? Guid.Empty, now, record.Amount, record.SubType, request.Reason.Trim(), null, idempotencyKey, new { record.Id });
            await paymentsDbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        if (log is null) return await PaymentIdempotencyService.AbortAsync(idempotency, Results.NotFound());
        if (record is null) return await PaymentIdempotencyService.AbortAsync(idempotency, Results.Conflict(new { code = "transition-conflict", detail = "No cash receipt is waiting for review." }));
        return await paymentIdempotencyService.CompleteAsync(idempotency, new { record.Id, status = Rejected, record.ConfirmedBy, record.ConfirmedAt, rejectionReason = request.Reason.Trim() }, StatusCodes.Status200OK, cancellationToken);
    }

    private static async Task<IResult> SetSubLogStatusAsync(
        Guid id,
        string status,
        string? rejectionReason,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        MerchantAccountService merchantAccountService,
        HttpContext httpContext,
        ICurrentUser currentUser,
        IAppEventPublisher eventPublisher,
        PaymentIdempotencyService paymentIdempotencyService,
        IClock clock,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var idempotency = await paymentIdempotencyService.StartAsync(idempotencyKey, $"POST /api/v1/payments/sub-logs/{id}/{(status == ConfirmedPayment ? "approve" : "reject")}", new { id, status, rejectionReason }, cancellationToken);
        if (idempotency.Result is not null)
        {
            return idempotency.Result;
        }

        if (!IsCollectionReviewer(currentUser))
        {
            return await PaymentIdempotencyService.AbortAsync(idempotency, Results.Forbid());
        }

        if (status == Rejected && string.IsNullOrWhiteSpace(rejectionReason))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(rejectionReason)] = ["Rejection reason is required."] });
        }

        InstallmentSubLog? subLog = null;
        MainPaymentLog? log = null;
        Guid? effectiveMerchantId = null;
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

            if (subLog.MainLog is not null)
            {
                paymentsDbContext.Entry(subLog.MainLog).State = EntityState.Detached;
            }
            paymentsDbContext.Entry(subLog).State = EntityState.Detached;
            log = await LoadPaymentLogForUpdateAsync(subLog.MainLogId, paymentsDbContext, cancellationToken);
            if (log is null)
            {
                transactionResult = Results.NotFound();
                return;
            }
            subLog = log.InstallmentSubLogs.Single(value => value.Id == id);
            effectiveMerchantId = log.MerchantId ?? await operationsDbContext.OperationLogs.AsNoTracking()
                .Where(value => value.Id == log.OperationId && !value.IsDeleted)
                .Select(value => value.ClientId)
                .SingleOrDefaultAsync(cancellationToken);
            if (subLog.SubLogStatus != PendingAdminReview)
            {
                transactionResult = Results.Conflict(new { code = "transition-conflict", detail = "Only submitted sub-logs can be approved or rejected." });
                return;
            }

            RecalculateInstallmentAggregates(log);
            if (status == ConfirmedPayment && log.AmountPaid + log.PendingAmount > log.TotalAmount)
            {
                transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["Approving this entry would overpay the payment log."] });
                return;
            }
            if (status == ConfirmedPayment && effectiveMerchantId.HasValue &&
                await paymentsDbContext.MerchantAccountCollectionDrafts.AnyAsync(value =>
                    value.SourceOperationId == log.OperationId && value.Status == ConfirmedPayment, cancellationToken))
            {
                transactionResult = Results.Conflict(new { code = "collection-already-posted", detail = "A collection for this registered-merchant sale has already been approved." });
                return;
            }

            var now = clock.EgyptNow;
            subLog.SubLogStatus = status;
            subLog.ConfirmedBy = currentUser.UserId;
            subLog.ConfirmedAt = now;
            subLog.RejectionReason = status == Rejected ? rejectionReason?.Trim() : null;

            RecalculateInstallmentAggregates(log);
            if (status == ConfirmedPayment && effectiveMerchantId is { } merchantId)
            {
                await merchantAccountService.PostCollectionAsync(merchantId, subLog.Id, subLog.Amount, subLog.PaymentMethod!, subLog.TransactionReference, currentUser.UserId ?? Guid.Empty, now, null, subLog.Notes, cancellationToken);
            }
            log.LastModifiedBy = currentUser.UserId;
            log.LastModifiedAt = now;

            await AddPaymentAuditAsync(identityDbContext, currentUser, httpContext, status == ConfirmedPayment ? "PaymentSubLogApproved" : "PaymentSubLogRejected", log.Id, new { subLog.Id, subLog.Amount, status }, now, cancellationToken);
            AddPaymentWorkflowAudit(paymentsDbContext, status == ConfirmedPayment ? "PaymentSubLogApproved" : "PaymentSubLogRejected", PendingAdminReview, status, effectiveMerchantId, log.OperationId, log.Id, null, currentUser.UserId ?? Guid.Empty, now, subLog.Amount, subLog.PaymentMethod, subLog.RejectionReason, httpContext, idempotencyKey, new { subLog.Id });
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
            return await PaymentIdempotencyService.AbortAsync(idempotency, transactionResult);
        }

        var userLookup = await LoadUserLookupAsync(identityDbContext, [log!], cancellationToken);
        var operationLookup = await LoadPaymentOperationLookupAsync(operationsDbContext, [log!.OperationId], cancellationToken);
        var cashRecords = await LoadCashRecordsForLogAsync(paymentsDbContext, log!, cancellationToken);
        var adjustments = await LoadAdjustmentsForLogAsync(paymentsDbContext, log!, cancellationToken);
        return await paymentIdempotencyService.CompleteAsync(idempotency, ToDetailResponse(log!, cashRecords, adjustments, userLookup, operationLookup), StatusCodes.Status200OK, cancellationToken);
    }

    private static async Task<IResult> CreateCashRecordAsync(
        CashRecordRequest request,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        MerchantAccountService merchantAccountService,
        HttpContext httpContext,
        ICurrentUser currentUser,
        PaymentIdempotencyService paymentIdempotencyService,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var idempotency = await paymentIdempotencyService.StartAsync(idempotencyKey, "POST /api/v1/payments/cash-records", request, cancellationToken);
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
        var paymentMethod = NormalizeMovementMethod(request.PaymentMethod ?? request.SubType);
        if (paymentMethod is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PaymentMethod)] = ["Payment method is required and must be CashHandToHand, CashTransaction, BankTransfer, or Wallet."] });
        }
        if (MerchantAccountService.RequiresTransactionReference(paymentMethod) && string.IsNullOrWhiteSpace(request.TransactionReference))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.TransactionReference)] = ["An electronic payment requires a transaction reference."] });
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
        if (!IsPaymentEligible(operation))
        {
            return Results.Conflict(new { code = "payment-ineligible", detail = "Cash receipts require a finalized eligible operation." });
        }

        var now = clock.EgyptNow;
        var record = new CashRecord
        {
            Id = Guid.NewGuid(),
            OperationId = operation.Id,
            PaymentType = paymentType,
            SubType = paymentMethod,
            TransactionReference = paymentMethod == "CashHandToHand" ? $"CASH-{Guid.NewGuid():N}" : request.TransactionReference!.Trim(),
            Amount = request.Amount,
            Status = PendingAccountant,
            PaymentDate = now,
            CreatedBy = currentUser.UserId ?? Guid.Empty,
            Notes = request.Notes
        };
        IResult? cashResult = null;
        await SharedDbTransaction.ExecuteAsync(paymentsDbContext, async () =>
        {
            var lockedOperation = await LoadOperationForUpdateAsync(operation.Id, operationsDbContext, cancellationToken);
            if (lockedOperation is null || !IsPaymentEligible(lockedOperation))
            {
                cashResult = Results.Conflict(new { code = "payment-ineligible", detail = "Cash receipts require a finalized eligible operation." });
                return;
            }

            var mainLogId = await paymentsDbContext.MainPaymentLogs
                .Where(value => value.OperationId == lockedOperation.Id && !value.IsDeleted)
                .Select(value => (Guid?)value.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (mainLogId is null)
            {
                cashResult = Results.Conflict(new { code = "payment-ineligible", detail = "Initialize the operation payment log before recording cash." });
                return;
            }
            var mainLog = await LoadPaymentLogForUpdateAsync(mainLogId.Value, paymentsDbContext, cancellationToken);
            if (mainLog is null)
            {
                cashResult = Results.Conflict(new { code = "payment-ineligible", detail = "The operation payment log is no longer active." });
                return;
            }
            // Scope is determined by the registered merchant on the source payment log,
            // never by the movement/settlement label. Registered-merchant cash receipts
            // must use the shared merchant-account collection workflow as well.
            var effectiveMerchantId = mainLog.MerchantId ?? lockedOperation.ClientId;
            if (effectiveMerchantId.HasValue)
            {
                cashResult = Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.OperationId)] = ["Registered-merchant collections must be submitted through the merchant-account approval workflow."] });
                return;
            }
            if (!mainLog.AssignedTo.HasValue)
            {
                mainLog.AssignedTo = await SelectLeastLoadedAccountantAsync(identityDbContext, paymentsDbContext, cancellationToken);
                mainLog.AssignedAt = mainLog.AssignedTo.HasValue ? now : null;
            }
            var completedReceived = await PaymentFinancialCapacity.CompletedCashReceivedAsync(paymentsDbContext, mainLog, cancellationToken);
            var completedRefunded = await PaymentFinancialCapacity.CompletedCashRefundedAsync(paymentsDbContext, mainLog, cancellationToken);
            var confirmedInstallments = await PaymentFinancialCapacity.CompletedInstallmentsAsync(paymentsDbContext, mainLog, cancellationToken);
            var pendingInstallments = await PaymentFinancialCapacity.PendingInstallmentsAsync(paymentsDbContext, mainLog, cancellationToken);
            var pendingCash = await PaymentFinancialCapacity.PendingCashReceivedAsync(paymentsDbContext, mainLog, cancellationToken);
            if (confirmedInstallments + completedReceived + pendingInstallments + pendingCash - completedRefunded + record.Amount > CalculatePaymentTotal(lockedOperation))
            {
                cashResult = Results.Conflict(new { code = "payment-cap-exceeded", detail = "The receipt exceeds the finalized operation liability." });
                return;
            }
            paymentsDbContext.CashRecords.Add(record);
            mainLog.PendingAmount += record.Amount;
            mainLog.Status = PendingAdminReview;
            mainLog.LastModifiedBy = currentUser.UserId;
            mainLog.LastModifiedAt = now;
            await AddPaymentAuditAsync(identityDbContext, currentUser, httpContext, "CashReceiptSubmittedForApproval", record.Id, new { record.OperationId, record.Amount, record.Status }, record.PaymentDate, cancellationToken);
            AddPaymentWorkflowAudit(paymentsDbContext, "CashReceiptSubmittedForApproval", null, PendingAdminReview, mainLog.MerchantId, mainLog.OperationId, mainLog.Id, null, currentUser.UserId ?? Guid.Empty, now, record.Amount, paymentMethod, null, httpContext, idempotencyKey, new { record.Id, record.TransactionReference, mainLog.AssignedTo });
            await paymentsDbContext.SaveChangesAsync(cancellationToken);
            await identityDbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken, operationsDbContext, identityDbContext, sharedDbContext);
        if (cashResult is not null) return await PaymentIdempotencyService.AbortAsync(idempotency, cashResult);

        var userLookup = await LoadUserLookupAsync(identityDbContext, [record], cancellationToken);
        return await paymentIdempotencyService.CompleteAsync(idempotency, ToCashResponse(record, userLookup), StatusCodes.Status201Created, cancellationToken);
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

    private static async Task<IResult> ResolvePaymentOperationAsync(
        string? reference,
        OperationsDbContext operationsDbContext,
        CancellationToken cancellationToken)
    {
        var operation = await ResolveOperationReferenceAsync(operationsDbContext, reference, cancellationToken);
        if (operation is null)
        {
            return Results.NotFound(new { code = "operation-not-found", detail = "Operation must exist. Use the full operation ID or operation code." });
        }

        return Results.Ok(new PaymentOperationResolutionResponse(
            operation.Id,
            operation.OperationNumber,
            operation.ClientId,
            operation.ClientName,
            operation.OperationType));
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
        MerchantAccountService merchantAccountService,
        PaymentIdempotencyService paymentIdempotencyService,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var idempotency = await paymentIdempotencyService.StartAsync(idempotencyKey, "POST /api/v1/payments/adjustments", request, cancellationToken);
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
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.AdjustmentType)] = ["Adjustment type must be AdditionalCharge, BalanceReduction, or CashRefund."] });
        }
        if (adjustmentType == AdditionalCharge && string.IsNullOrWhiteSpace(request.Notes))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Notes)] = ["An additional charge requires a reason."] });
        }
        if (!await crmDbContext.Merchants.AnyAsync(merchant => merchant.Id == request.MerchantId && !merchant.IsDeleted, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.MerchantId)] = ["Merchant must exist."] });
        }

        var operationReference = request.OperationId?.Trim();
        OperationLog? operation = null;
        if (!string.IsNullOrWhiteSpace(operationReference))
        {
            operation = await ResolveOperationReferenceAsync(operationsDbContext, operationReference, cancellationToken);
            if (operation is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.OperationId)] = ["Operation must exist. Use the full operation ID or operation code."] });
            if (operation.ClientId != request.MerchantId)
                return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.OperationId)] = ["Operation must belong to the selected merchant."] });
        }
        else
        {
            var snapshot = await merchantAccountService.GetSnapshotAsync(request.MerchantId, cancellationToken);
            if (adjustmentType == BalanceReduction && (snapshot is null || request.Amount > snapshot.AmountDue))
                return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Amount)] = ["A merchant-level reduction cannot exceed the remaining amount owed."] });
        }

        MainPaymentLog? paymentLog = null;
        IResult? transactionResult = null;
        FinancialAdjustment? adjustment = null;
        var now = clock.EgyptNow;
        var userId = currentUser.UserId ?? Guid.Empty;
        await SharedDbTransaction.ExecuteAsync(paymentsDbContext, async () =>
        {
            if (operation is not null)
            {
                paymentLog = await paymentsDbContext.MainPaymentLogs
                    .Include(log => log.InstallmentSubLogs)
                    .FirstOrDefaultAsync(log => log.OperationId == operation.Id && log.MerchantId == request.MerchantId && !log.IsDeleted, cancellationToken);
                if (paymentLog is null)
                {
                    transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.OperationId)] = ["The linked order does not have a payment record yet."] });
                    return;
                }
                paymentLog = await LoadPaymentLogForUpdateAsync(paymentLog.Id, paymentsDbContext, cancellationToken);
                var cap = await CalculateAdjustmentCapAsync(paymentsDbContext, paymentLog!, adjustmentType, null, cancellationToken);
                if (cap.HasValue && request.Amount > cap.Value)
                {
                    transactionResult = Results.Conflict(new { code = "payment-cap-exceeded", detail = $"Adjustment exceeds the remaining source cap ({cap.Value:0.####})." });
                    return;
                }
            }

            adjustment = new FinancialAdjustment
            {
                Id = Guid.NewGuid(),
                MerchantId = request.MerchantId,
                OperationId = operation?.Id,
                PaymentLogId = paymentLog?.Id,
                AdjustmentType = adjustmentType,
                Amount = request.Amount,
                Status = PendingApproval,
                Notes = request.Notes,
                CreatedBy = userId,
                CreatedAt = now,
                LineageKind = operation is null ? "MerchantAccount" : "SourceLinked"
            };
            paymentsDbContext.FinancialAdjustments.Add(adjustment);
            await AddPaymentAuditAsync(identityDbContext, currentUser, httpContext, "FinancialAdjustmentRequested", paymentLog?.Id ?? adjustment.Id, new { adjustment.Id, adjustmentType, request.Amount, adjustment.LineageKind }, now, cancellationToken);
            AddPaymentWorkflowAudit(paymentsDbContext, "FinancialAdjustmentRequested", null, adjustment.Status, adjustment.MerchantId, adjustment.OperationId, paymentLog?.Id, null, userId, now, adjustment.Amount, null, null, httpContext, idempotencyKey, new { adjustment.Id, adjustment.AdjustmentType, adjustment.LineageKind });
            await paymentsDbContext.SaveChangesAsync(cancellationToken);
            await identityDbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken, identityDbContext, sharedDbContext);

        if (transactionResult is not null)
        {
            return await PaymentIdempotencyService.AbortAsync(idempotency, transactionResult);
        }

        var userLookup = await LoadUserLookupAsync(identityDbContext, [adjustment!], cancellationToken);
        return await paymentIdempotencyService.CompleteAsync(idempotency, ToAdjustmentResponse(adjustment!, userLookup), StatusCodes.Status201Created, cancellationToken);
    }

    private static async Task<IResult> ApproveFinancialAdjustmentAsync(
        Guid id,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        HttpContext httpContext,
        ICurrentUser currentUser,
        MerchantAccountService merchantAccountService,
        PaymentIdempotencyService paymentIdempotencyService,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var idempotency = await paymentIdempotencyService.StartAsync(idempotencyKey, $"POST /api/v1/payments/adjustments/{id}/approve", new { id }, cancellationToken);
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
            adjustment = await LoadFinancialAdjustmentForUpdateAsync(id, paymentsDbContext, cancellationToken);
            if (adjustment is null)
            {
                transactionResult = Results.NotFound();
                return;
            }
            if (adjustment.Status != PendingApproval)
            {
                transactionResult = Results.Conflict(new { code = "transition-conflict", detail = "Only pending adjustment requests can be approved." });
                return;
            }
            MainPaymentLog? paymentLog = null;
            if (adjustment.PaymentLogId is { } paymentLogId)
            {
                paymentLog = await LoadPaymentLogForUpdateAsync(paymentLogId, paymentsDbContext, cancellationToken);
                if (paymentLog is null)
                {
                    transactionResult = Results.ValidationProblem(new Dictionary<string, string[]> { ["paymentLog"] = ["The source payment log no longer exists."] });
                    return;
                }
                var cap = await CalculateAdjustmentCapAsync(paymentsDbContext, paymentLog, adjustment.AdjustmentType, adjustment.Id, cancellationToken);
                if (cap.HasValue && adjustment.Amount > cap.Value)
                {
                    transactionResult = Results.Conflict(new { code = "payment-cap-exceeded", detail = $"Adjustment exceeds the remaining source cap ({cap.Value:0.####})." });
                    return;
                }
            }

            var now = clock.EgyptNow;
            adjustment.Status = adjustment.AdjustmentType == CashRefund ? "Approved" : PaymentCompleted;
            adjustment.ReviewedBy = currentUser.UserId;
            adjustment.ReviewedAt = now;

            if (adjustment.AdjustmentType == AdditionalCharge)
            {
                await merchantAccountService.PostDebitForSourceAsync(adjustment.MerchantId, adjustment.Id, adjustment.OperationId, adjustment.Amount, "AdditionalCharge", currentUser.UserId ?? Guid.Empty, now, adjustment.Notes, cancellationToken);
            }
            else if (adjustment.AdjustmentType == BalanceReduction)
            {
                await merchantAccountService.PostCreditForSourceAsync(adjustment.MerchantId, adjustment.Id, adjustment.OperationId, adjustment.Amount, "BalanceReduction", currentUser.UserId ?? Guid.Empty, now, adjustment.Notes, cancellationToken);
            }

            await AddPaymentAuditAsync(identityDbContext, currentUser, httpContext, "FinancialAdjustmentApproved", paymentLog?.Id ?? adjustment.Id, new { adjustment.Id, adjustment.AdjustmentType, adjustment.Amount, adjustment.LineageKind }, now, cancellationToken);
            AddPaymentWorkflowAudit(paymentsDbContext, "FinancialAdjustmentApproved", PendingApproval, adjustment.Status, adjustment.MerchantId, adjustment.OperationId, paymentLog?.Id, null, currentUser.UserId ?? Guid.Empty, now, adjustment.Amount, null, null, httpContext, idempotencyKey, new { adjustment.Id, adjustment.AdjustmentType, adjustment.LineageKind });
            await paymentsDbContext.SaveChangesAsync(cancellationToken);
            await identityDbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken, identityDbContext, sharedDbContext);

        if (transactionResult is not null)
        {
            return await PaymentIdempotencyService.AbortAsync(idempotency, transactionResult);
        }

        var userLookup = await LoadUserLookupAsync(identityDbContext, [adjustment!], cancellationToken);
        return await paymentIdempotencyService.CompleteAsync(idempotency, ToAdjustmentResponse(adjustment!, userLookup), StatusCodes.Status200OK, cancellationToken);
    }

    private static async Task<IResult> PayoutCashRefundAsync(
        Guid id,
        CashRefundPayoutRequest request,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        HttpContext httpContext,
        ICurrentUser currentUser,
        MerchantAccountService merchantAccountService,
        PaymentIdempotencyService paymentIdempotencyService,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var idempotency = await paymentIdempotencyService.StartAsync(idempotencyKey, $"POST /api/v1/payments/adjustments/{id}/payout", request, cancellationToken);
        if (idempotency.Result is not null) return idempotency.Result;
        if (!IsAdjustmentReviewer(currentUser)) return Results.Forbid();
        if (request.Amount <= 0m)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Amount)] = ["Payout amount must be greater than zero."] });
        }
        var paymentMethod = NormalizeMovementMethod(request.PaymentMethod);
        if (paymentMethod is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.PaymentMethod)] = ["Payment method is required and must be CashHandToHand, CashTransaction, BankTransfer, or Wallet."] });
        }
        if (MerchantAccountService.RequiresTransactionReference(paymentMethod) && string.IsNullOrWhiteSpace(request.TransactionReference))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.TransactionReference)] = ["An electronic payment requires a transaction reference."] });
        }

        FinancialAdjustment? adjustment = null;
        IResult? transactionResult = null;
        await SharedDbTransaction.ExecuteAsync(paymentsDbContext, async () =>
        {
            adjustment = await LoadFinancialAdjustmentForUpdateAsync(id, paymentsDbContext, cancellationToken);
            if (adjustment is null)
            {
                transactionResult = Results.NotFound();
                return;
            }
            if (adjustment.AdjustmentType != CashRefund || adjustment.Status != "Approved")
            {
                transactionResult = Results.Conflict(new { code = "transition-conflict", detail = "Only an approved cash refund can be paid out." });
                return;
            }

            var alreadyPaid = await paymentsDbContext.CashRecords
                .Where(record => record.FinancialAdjustmentId == adjustment.Id && record.Status == PaymentCompleted)
                .SumAsync(record => record.Amount, cancellationToken);
            var due = adjustment.Amount - alreadyPaid;
            if (request.Amount > due)
            {
                transactionResult = Results.Conflict(new { code = "refund-cap-exceeded", detail = $"Payout exceeds the remaining approved refund ({due:0.####})." });
                return;
            }

            var operationId = adjustment.OperationId;
            var paymentLog = adjustment.PaymentLogId is { } paymentLogId
                ? await LoadPaymentLogForUpdateAsync(paymentLogId, paymentsDbContext, cancellationToken)
                : null;

            var now = clock.EgyptNow;
            var payout = new CashRecord
            {
                Id = Guid.NewGuid(),
                OperationId = operationId,
                MerchantId = adjustment.MerchantId,
                PaymentType = CashRefund,
                SubType = paymentMethod,
                TransactionReference = paymentMethod == "CashHandToHand" ? $"CASH-{Guid.NewGuid():N}" : request.TransactionReference!.Trim(),
                Amount = request.Amount,
                Status = PaymentCompleted,
                PaymentDate = now,
                CreatedBy = currentUser.UserId ?? Guid.Empty,
                ConfirmedBy = currentUser.UserId ?? Guid.Empty,
                ConfirmedAt = now,
                Notes = string.IsNullOrWhiteSpace(request.Notes) ? adjustment.Notes : request.Notes.Trim(),
                FinancialAdjustmentId = adjustment.Id
            };
            paymentsDbContext.CashRecords.Add(payout);
            await merchantAccountService.PostDebitForSourceAsync(adjustment.MerchantId, payout.Id, operationId, payout.Amount, "RefundPayout", currentUser.UserId ?? Guid.Empty, now, payout.Notes, cancellationToken);
            if (request.Amount == due)
            {
                adjustment.Status = PaymentCompleted;
                adjustment.ReviewedAt = now;
            }
            if (paymentLog is not null) { paymentLog.LastModifiedBy = currentUser.UserId; paymentLog.LastModifiedAt = now; }
            await AddPaymentAuditAsync(identityDbContext, currentUser, httpContext, "CashRefundPaidOut", paymentLog?.Id ?? adjustment.Id, new { adjustment.Id, request.Amount, remaining = due - request.Amount, merchantLevel = operationId is null }, now, cancellationToken);
            AddPaymentWorkflowAudit(paymentsDbContext, "CashRefundPaidOut", "Approved", adjustment.Status, adjustment.MerchantId, operationId, paymentLog?.Id, null, currentUser.UserId ?? Guid.Empty, now, request.Amount, paymentMethod, null, httpContext, idempotencyKey, new { AdjustmentId = adjustment.Id, PayoutId = payout.Id, remaining = due - request.Amount, merchantLevel = operationId is null });
            await PaymentPersistence.PersistAsync(paymentsDbContext, identityDbContext, cancellationToken);
        }, cancellationToken, identityDbContext, sharedDbContext);

        if (transactionResult is not null) return await PaymentIdempotencyService.AbortAsync(idempotency, transactionResult);
        var userLookup = await LoadUserLookupAsync(identityDbContext, [adjustment!], cancellationToken);
        return await paymentIdempotencyService.CompleteAsync(idempotency, ToAdjustmentResponse(adjustment!, userLookup), StatusCodes.Status200OK, cancellationToken);
    }

    private static async Task<IResult> RejectFinancialAdjustmentAsync(
        Guid id,
        RejectionRequest request,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        SharedDbContext sharedDbContext,
        HttpContext httpContext,
        ICurrentUser currentUser,
        PaymentIdempotencyService paymentIdempotencyService,
        IClock clock,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var idempotency = await paymentIdempotencyService.StartAsync(idempotencyKey, $"POST /api/v1/payments/adjustments/{id}/reject", request, cancellationToken);
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
            adjustment = await LoadFinancialAdjustmentForUpdateAsync(id, paymentsDbContext, cancellationToken);
            if (adjustment is null)
            {
                transactionResult = Results.NotFound();
                return;
            }
            if (adjustment.Status != PendingApproval)
            {
                transactionResult = Results.Conflict(new { code = "transition-conflict", detail = "Only pending adjustment requests can be rejected." });
                return;
            }
            var now = clock.EgyptNow;
            adjustment.Status = Rejected;
            adjustment.ReviewedBy = currentUser.UserId;
            adjustment.ReviewedAt = now;
            adjustment.RejectionReason = request.Reason.Trim();
            await AddPaymentAuditAsync(identityDbContext, currentUser, httpContext, "FinancialAdjustmentRejected", adjustment.PaymentLogId ?? adjustment.Id, new { adjustment.Id, adjustment.AdjustmentType, adjustment.Amount, adjustment.RejectionReason }, now, cancellationToken);
            AddPaymentWorkflowAudit(paymentsDbContext, "FinancialAdjustmentRejected", PendingApproval, Rejected, adjustment.MerchantId, adjustment.OperationId, adjustment.PaymentLogId, null, currentUser.UserId ?? Guid.Empty, now, adjustment.Amount, null, adjustment.RejectionReason, httpContext, idempotencyKey, new { adjustment.Id, adjustment.AdjustmentType });
            await paymentsDbContext.SaveChangesAsync(cancellationToken);
            await identityDbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken, identityDbContext, sharedDbContext);

        if (transactionResult is not null)
        {
            return await PaymentIdempotencyService.AbortAsync(idempotency, transactionResult);
        }

        var userLookup = await LoadUserLookupAsync(identityDbContext, [adjustment!], cancellationToken);
        return await paymentIdempotencyService.CompleteAsync(idempotency, ToAdjustmentResponse(adjustment!, userLookup), StatusCodes.Status200OK, cancellationToken);
    }

    private static async Task<IResult> GetMerchantBalanceAsync(
        Guid merchantId,
        MerchantBalanceService merchantBalanceService,
        MerchantAccountService merchantAccountService,
        CancellationToken cancellationToken)
    {
        var accountBreakdown = await merchantAccountService.GetFinancialBreakdownAsync(merchantId, cancellationToken);
        if (accountBreakdown is not null)
        {
            return Results.Ok(new MerchantBalanceSnapshot(
                accountBreakdown.MerchantId,
                accountBreakdown.SaleTotal,
                accountBreakdown.ReturnTotal,
                accountBreakdown.ChangeNet,
                accountBreakdown.PaymentsReceived,
                accountBreakdown.CashRefunded,
                accountBreakdown.AdditionalCharges,
                accountBreakdown.BalanceReductions,
                accountBreakdown.Balance));
        }
        var balance = await merchantBalanceService.CalculateAsync(merchantId, cancellationToken);
        return Results.Ok(balance);
    }

    private static async Task<decimal?> CalculateAdjustmentCapAsync(
        PaymentsDbContext paymentsDbContext,
        MainPaymentLog paymentLog,
        string adjustmentType,
        Guid? excludingAdjustmentId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(adjustmentType, CashRefund, StringComparison.OrdinalIgnoreCase))
        {
            // Merchant-account collections are posted as ledger credits rather
            // than cash-record rows. Their approved amount is already reflected
            // on the source log, so use that confirmed amount when reserving a
            // source-linked cash refund.
            if (paymentLog.MerchantId.HasValue)
            {
                var alreadyRequested = await paymentsDbContext.FinancialAdjustments.AsNoTracking()
                    .Where(value => value.PaymentLogId == paymentLog.Id && value.AdjustmentType == CashRefund && value.Status != Rejected &&
                                    (!excludingAdjustmentId.HasValue || value.Id != excludingAdjustmentId.Value))
                    .SumAsync(value => (decimal?)value.Amount, cancellationToken) ?? 0m;
                return Math.Max(paymentLog.AmountPaid - alreadyRequested, 0m);
            }
            return await PaymentFinancialCapacity.CashRefundCapacityAsync(paymentsDbContext, paymentLog, excludingAdjustmentId, cancellationToken);
        }

        if (string.Equals(adjustmentType, AdditionalCharge, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return await PaymentFinancialCapacity.BalanceReductionCapacityAsync(paymentsDbContext, paymentLog, excludingAdjustmentId, cancellationToken);
    }

    private static bool IsPaymentEligible(OperationLog operation)
    {
        if (operation.IsDeleted || string.Equals(operation.RecordKind, "Reversal", StringComparison.OrdinalIgnoreCase)) return false;
        if (operation.OperationType is WholesaleSale or RetailSale) return operation.Status == Completed;
        return operation.OperationType == Change && operation.Status == Confirmed && CalculatePaymentTotal(operation) > 0;
    }

    private static bool IsAdjustmentReviewer(ICurrentUser currentUser) =>
        LenseeRoles.Normalize(currentUser.Role) is LenseeRoles.Admin or LenseeRoles.ERPAdmin or LenseeRoles.CLevel;

    // Collection review is deliberately narrower than adjustment review. C-Level retains
    // payment read access and adjustment authority, but does not approve collected cash.
    private static bool IsCollectionReviewer(ICurrentUser currentUser) =>
        LenseeRoles.Normalize(currentUser.Role) is LenseeRoles.Admin or LenseeRoles.ERPAdmin;

    private static IReadOnlyList<string> CollectionPermittedActions(string? status) =>
        status switch
        {
            Draft => ["submit", "reassign"],
            PendingAdminReview => ["approve", "reject", "reassign"],
            _ => []
        };

    private static bool IsMerchantAccountSettlement(string? paymentMethod) =>
        string.Equals(paymentMethod, "MerchantAccount", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(paymentMethod, "Installment", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(paymentMethod, "Installlaugment", StringComparison.OrdinalIgnoreCase);

    public static async Task CreatePaymentArtifactsForCompletedSaleAsync(
        OperationLog operation,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext? identityDbContext,
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

        if (await paymentsDbContext.MainPaymentLogs.AnyAsync(log => log.OperationId == operation.Id && !log.IsDeleted, cancellationToken))
        {
            return;
        }

        var assignedTo = operation.ClientId.HasValue
            ? identityDbContext is null
                ? null
                : await SelectLeastLoadedAccountantAsync(identityDbContext, paymentsDbContext, cancellationToken)
            : null;

        paymentsDbContext.MainPaymentLogs.Add(new MainPaymentLog
        {
            Id = Guid.NewGuid(),
                OperationId = operation.Id,
                MerchantId = operation.ClientId,
                Scope = operation.ClientId.HasValue ? "MerchantAccount" : "DirectOperation",
            TotalAmount = total,
            AmountPaid = 0,
            PaymentMethod = operation.PaymentMethod ?? throw new InvalidOperationException("A completed financial sale must have an explicit settlement method."),
            Status = PendingAccountant,
            InitializedBy = userId,
            InitializedAt = now,
            AssignedTo = assignedTo,
            AssignedAt = assignedTo.HasValue ? now : null,
            LastModifiedBy = userId,
            LastModifiedAt = now,
            Notes = "Created from completed sale. Record the collection separately for approval."
        });
        if (operation.ClientId is { } merchantId &&
            identityDbContext is not null &&
            MerchantAccountService.IsMovementMethod(operation.PaymentMethod))
        {
            var account = await paymentsDbContext.MerchantReceivableAccounts
                .SingleOrDefaultAsync(value => value.MerchantId == merchantId, cancellationToken);
            if (account is not null && !await paymentsDbContext.MerchantAccountCollectionDrafts
                    .AnyAsync(value => value.SourceOperationId == operation.Id && value.Status != "Rejected", cancellationToken))
            {
                paymentsDbContext.MerchantAccountCollectionDrafts.Add(new MerchantAccountCollectionDraft
                {
                    Id = Guid.NewGuid(),
                    AccountId = account.Id,
                    Account = account,
                    SourceOperationId = operation.Id,
                    Amount = total,
                    PaymentMethod = operation.PaymentMethod!,
                    Status = Draft,
                    DraftedBy = userId,
                    DraftedAt = now,
                    AssignedTo = assignedTo,
                    AssignedAt = assignedTo.HasValue ? now : null,
                    Notes = "Prefilled from completed registered-merchant sale; submit after money is received."
                });
            }
        }
        await paymentsDbContext.SaveChangesAsync(cancellationToken);
    }

    public static Task CreatePaymentArtifactsForCompletedSaleAsync(
        OperationLog operation,
        PaymentsDbContext paymentsDbContext,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken) =>
        CreatePaymentArtifactsForCompletedSaleAsync(operation, paymentsDbContext, null, userId, now, cancellationToken);

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
            .Select(operation => new PaymentOperationContext(operation.Id, operation.OperationNumber, operation.OperationType, operation.ClientId, operation.ClientName))
            .ToDictionaryAsync(operation => operation.OperationId, cancellationToken);
    }

    private static PaymentLogListResponse ToListResponse(
        MainPaymentLog log,
        IReadOnlyDictionary<Guid, User> userLookup,
        IReadOnlyDictionary<Guid, PaymentOperationContext> operationLookup,
        IReadOnlyList<CashRecord>? cashRecords = null,
        IReadOnlyList<FinancialAdjustment>? adjustments = null)
    {
        operationLookup.TryGetValue(log.OperationId, out var operation);
        var effectiveMerchantId = log.MerchantId ?? operation?.MerchantId;
        var balance = PaymentBalanceCalculator.Calculate(log, adjustments ?? [], cashRecords ?? []);
        return
        new(
            log.Id,
            log.OperationId,
            effectiveMerchantId,
            operation?.OperationNumber,
            operation?.OperationType,
            operation?.BuyerName,
            log.TotalAmount,
            balance.ConfirmedCollections,
            balance.RemainingAmount,
            log.PaymentMethod,
            log.Status,
            log.AssignedTo,
            log.LastModifiedAt,
            GetUserDisplayName(log.InitializedBy, userLookup),
            GetUserDisplayName(log.AssignedTo, userLookup),
            GetUserDisplayName(log.LastModifiedBy, userLookup),
            balance.RefundDue,
            balance.AdjustedAmount,
            balance.AdditionalCharges,
            balance.BalanceReductions,
            balance.PendingCollections,
            balance.CompletedRefunds);
    }

    private static PaymentLogDetailResponse ToDetailResponse(
        MainPaymentLog log,
        IReadOnlyList<CashRecord> cashRecords,
        IReadOnlyList<FinancialAdjustment> adjustments,
        IReadOnlyDictionary<Guid, User> userLookup,
        IReadOnlyDictionary<Guid, PaymentOperationContext> operationLookup)
    {
        // Resolve legacy rows from their source operation before projecting
        // nested records so detail, list, and history agree on payment scope.
        var effectiveMerchantId = log.MerchantId ?? operationLookup.GetValueOrDefault(log.OperationId)?.MerchantId;
        var stages = BuildPaymentStages(log, cashRecords, adjustments, userLookup, operationLookup)
            .OrderBy(stage => stage.HappenedAt)
            .ToList();

        return
        new(
            ToListResponse(log, userLookup, operationLookup, cashRecords, adjustments),
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
                GetUserDisplayName(sub.ConfirmedBy, userLookup))
            {
                OperationId = log.OperationId,
                MerchantId = effectiveMerchantId,
                OperationNumber = operationLookup.GetValueOrDefault(log.OperationId)?.OperationNumber,
                Reference = $"SUB-{sub.Id:N}"[..12].ToUpperInvariant()
            }).ToList(),
            cashRecords.OrderByDescending(record => record.PaymentDate).Select(record => ToCashResponse(record, userLookup, record.MerchantId ?? effectiveMerchantId, record.OperationId.HasValue ? operationLookup.GetValueOrDefault(record.OperationId.Value)?.OperationNumber : null)).ToList(),
            adjustments.OrderByDescending(adjustment => adjustment.CreatedAt).Select(adjustment => ToAdjustmentResponse(adjustment, userLookup)).ToList(),
            stages,
            log.Notes);
    }

    private static CashRecordResponse ToCashResponse(CashRecord record, IReadOnlyDictionary<Guid, User> userLookup, Guid? merchantId = null, string? operationNumber = null) =>
        new(record.Id, record.OperationId, record.PaymentType, record.SubType, record.TransactionReference, record.Amount, record.Status, record.PaymentDate, record.CreatedBy, GetUserDisplayName(record.CreatedBy, userLookup), record.ConfirmedBy, record.ConfirmedAt, record.Notes)
        {
            MerchantId = merchantId,
            OperationNumber = operationNumber,
            Reference = $"CASH-{record.Id:N}"[..13].ToUpperInvariant()
        };

    private static string? NormalizePaymentMethod(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, "Installlaugment", StringComparison.OrdinalIgnoreCase)) return "MerchantAccount";
        return PaymentMethods.FirstOrDefault(method => string.Equals(method, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeMovementMethod(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return MerchantAccountService.IsMovementMethod(trimmed) ? trimmed : null;
    }

    private static bool IsTrustedFinancialActor(ICurrentUser currentUser) =>
        LenseeRoles.Normalize(currentUser.Role) is LenseeRoles.Accountant or LenseeRoles.Admin or LenseeRoles.ERPAdmin or LenseeRoles.CLevel;

    private static string? NormalizeCashType(string? value)
    {
        if (string.Equals(value, CashReceived, StringComparison.OrdinalIgnoreCase))
        {
            return CashReceived;
        }
        if (string.Equals(value, CashRefund, StringComparison.OrdinalIgnoreCase))
        {
            return CashRefund;
        }

        return null;
    }

    private static string? NormalizeAdjustmentType(string? value)
    {
        if (string.Equals(value, AdditionalCharge, StringComparison.OrdinalIgnoreCase))
        {
            return AdditionalCharge;
        }
        if (string.Equals(value, BalanceReduction, StringComparison.OrdinalIgnoreCase))
        {
            return BalanceReduction;
        }
        if (string.Equals(value, CashRefund, StringComparison.OrdinalIgnoreCase))
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
            var effectiveMerchantId = log.MerchantId ?? operation?.MerchantId;
            var merchant = effectiveMerchantId.HasValue && merchantLookup.TryGetValue(effectiveMerchantId.Value, out var merchantValue)
                ? merchantValue
                : null;

            rows.Add(new PaymentHistoryResponse(
                log.Id,
                "PaymentLogOpened",
                log.OperationId,
                operation?.OperationNumber,
                operation?.OperationType,
                effectiveMerchantId,
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
                    effectiveMerchantId,
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
                        effectiveMerchantId,
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
                    effectiveMerchantId,
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
            var operation = record.OperationId.HasValue && operationLookup.TryGetValue(record.OperationId.Value, out var operationValue) ? operationValue : null;

            rows.Add(new PaymentHistoryResponse(
                record.Id,
                record.PaymentType == CashRefund ? "CashRefundRecorded" : "CashReceiptRecorded",
                record.OperationId,
                operation?.OperationNumber,
                operation?.OperationType,
                record.MerchantId ?? operation?.MerchantId,
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

    private static async Task<OperationLog?> LoadOperationForUpdateAsync(
        Guid id,
        OperationsDbContext operationsDbContext,
        CancellationToken cancellationToken)
    {
        if (operationsDbContext.Database.IsRelational())
        {
            await operationsDbContext.Database.ExecuteSqlInterpolatedAsync(
                $"select 1 from operations.operation_logs where id = {id} and is_deleted = false for update",
                cancellationToken);
        }

        return await operationsDbContext.OperationLogs
            .Include(operation => operation.OperationLines)
            .FirstOrDefaultAsync(operation => operation.Id == id && !operation.IsDeleted, cancellationToken);
    }

    private static async Task<FinancialAdjustment?> LoadFinancialAdjustmentForUpdateAsync(
        Guid id,
        PaymentsDbContext paymentsDbContext,
        CancellationToken cancellationToken)
    {
        if (paymentsDbContext.Database.IsRelational())
        {
            await paymentsDbContext.Database.ExecuteSqlInterpolatedAsync(
                $"select 1 from payments.financial_adjustments where id = {id} for update",
                cancellationToken);
        }

        return await paymentsDbContext.FinancialAdjustments.FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
    }

    private static async Task<MerchantAccountCollectionDraft?> LoadMerchantAccountCollectionDraftForUpdateAsync(
        Guid id,
        PaymentsDbContext paymentsDbContext,
        CancellationToken cancellationToken)
    {
        if (paymentsDbContext.Database.IsRelational())
        {
            await paymentsDbContext.Database.ExecuteSqlInterpolatedAsync(
                $"select 1 from payments.merchant_account_collection_drafts where \"Id\" = {id} for update",
                cancellationToken);
        }

        return await paymentsDbContext.MerchantAccountCollectionDrafts
            .Include(value => value.Account)
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
    }

    private static async Task<Guid?> SelectLeastLoadedAccountantAsync(
        IdentityDbContext identityDbContext,
        PaymentsDbContext paymentsDbContext,
        CancellationToken cancellationToken)
    {
        var accountants = await identityDbContext.Users.AsNoTracking()
            .Where(user => user.IsActive && user.Role == LenseeRoles.Accountant)
            .Select(user => user.Id)
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);
        if (accountants.Count == 0) return null;

        var paymentAssignments = await paymentsDbContext.MainPaymentLogs.AsNoTracking()
            .Where(log => log.AssignedTo.HasValue && accountants.Contains(log.AssignedTo.Value) &&
                log.Status != PaymentCompleted && log.Status != Rejected && !log.IsDeleted)
            .GroupBy(log => log.AssignedTo!.Value)
            .Select(group => new { UserId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(value => value.UserId, value => value.Count, cancellationToken);
        var collectionAssignments = await paymentsDbContext.MerchantAccountCollectionDrafts.AsNoTracking()
            .Where(draft => draft.AssignedTo.HasValue && accountants.Contains(draft.AssignedTo.Value) &&
                (draft.Status == Draft || draft.Status == PendingAdminReview))
            .GroupBy(draft => draft.AssignedTo!.Value)
            .Select(group => new { UserId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(value => value.UserId, value => value.Count, cancellationToken);

        return accountants
            .OrderBy(id => paymentAssignments.GetValueOrDefault(id) + collectionAssignments.GetValueOrDefault(id))
            .ThenBy(id => id)
            .First();
    }

    private static void RecalculateInstallmentAggregates(MainPaymentLog log)
    {
        log.AmountPaid = log.InstallmentSubLogs
            .Where(value => value.SubLogStatus == ConfirmedPayment)
            .Sum(value => value.Amount);
        log.PendingAmount = log.InstallmentSubLogs
            .Where(value => value.SubLogStatus is Draft or PendingAdminReview)
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

    private static MerchantAccountCollectionDraftResponse ToMerchantAccountCollectionDraftResponse(
        MerchantAccountCollectionDraft draft,
        IReadOnlyDictionary<Guid, User>? users = null) =>
        new(
            draft.Id,
            $"COL-{draft.Id:N}"[..16].ToUpperInvariant(),
            draft.Account.MerchantId,
            draft.Amount,
            draft.PaymentMethod,
            draft.TransactionReference,
            draft.Status,
            draft.DraftedBy,
            draft.DraftedAt,
            draft.AssignedTo,
            draft.AssignedAt,
            draft.ConfirmedBy,
            draft.ConfirmedAt,
            draft.RejectionReason,
            draft.Notes,
            draft.AllocationsJson,
            GetUserDisplayName(draft.DraftedBy, users ?? new Dictionary<Guid, User>()),
            GetUserDisplayName(draft.AssignedTo, users ?? new Dictionary<Guid, User>()),
            GetUserDisplayName(draft.ConfirmedBy, users ?? new Dictionary<Guid, User>()),
            draft.SourceOperationId);

    private static void AddPaymentWorkflowAudit(
        PaymentsDbContext paymentsDbContext,
        string action,
        string? previousStatus,
        string? newStatus,
        Guid? merchantId,
        Guid? operationId,
        Guid? paymentLogId,
        Guid? collectionDraftId,
        Guid actorId,
        DateTime occurredAt,
        decimal? amount,
        string? paymentMethod,
        string? reason,
        HttpContext? httpContext,
        string? idempotencyKey,
        object? data)
    {
        paymentsDbContext.PaymentAuditEvents.Add(new PaymentAuditEvent
        {
            Id = Guid.NewGuid(),
            Action = action,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            MerchantId = merchantId,
            OperationId = operationId,
            PaymentLogId = paymentLogId,
            CollectionDraftId = collectionDraftId,
            ActorId = actorId,
            OccurredAt = occurredAt,
            Amount = amount,
            PaymentMethod = paymentMethod,
            Reason = reason,
            CorrelationId = httpContext?.TraceIdentifier,
            IdempotencyKey = idempotencyKey,
            DataJson = data is null ? null : JsonSerializer.Serialize(data, JsonOptions)
        });
    }

    private static string DescribeMerchantEntry(string entryType) => entryType switch
    {
        "SaleCharge" => "Sale added",
        "ReturnCredit" => "Return accepted",
        "ExchangeSurcharge" => "Exchange amount added",
        "ExchangeCredit" => "Exchange credit added",
        "Collection" => "Money received",
        "RefundPayout" => "Money refunded",
        "AdditionalCharge" => "Additional charge added",
        "BalanceReduction" => "Amount reduced",
        _ => "Account activity"
    };

    private static string DescribeMovementMethod(string? method) => method switch
    {
        "CashHandToHand" => "Cash in hand",
        "CashTransaction" => "Cash transaction",
        "BankTransfer" => "Bank transfer",
        "Wallet" => "Wallet",
        _ => "-"
    };
}

public sealed record InitializePaymentRequest(Guid OperationId, string? PaymentMethod, string? Notes);

public sealed record AssignPaymentRequest(Guid? AccountantUserId);

public sealed record PaymentSubLogRequest(decimal Amount, string? PaymentMethod, DateOnly? DateReceived, string? Notes, string? TransactionReference = null, bool SubmitForReview = true);

public sealed record RejectionRequest(string? Reason);

public sealed record CollectionRejectionRequest(string? Reason);

public sealed record CashRecordRequest(string? OperationId, string? PaymentType, string? SubType, decimal Amount, string? Notes, string? PaymentMethod = null, string? TransactionReference = null);

public sealed record FinancialAdjustmentRequest(Guid MerchantId, string? OperationId, string AdjustmentType, decimal Amount, string? Notes);

public sealed record PaymentOperationResolutionResponse(Guid OperationId, string OperationNumber, Guid? MerchantId, string? MerchantName, string OperationType);

public sealed record CashRefundPayoutRequest(decimal Amount, string? Notes, string? PaymentMethod = null, string? TransactionReference = null);

public sealed record PaymentLogListResponse(Guid Id, Guid OperationId, Guid? MerchantId, string? OperationNumber, string? OperationType, string? BuyerName, decimal TotalAmount, decimal AmountPaid, decimal RemainingAmount, string PaymentMethod, string Status, Guid? AssignedTo, DateTime LastModifiedAt, string? InitializedByName, string? AssignedToName, string? LastModifiedByName, decimal RefundDue, decimal AdjustedAmount, decimal AdditionalCharges, decimal BalanceReductions, decimal PendingCollections, decimal CompletedRefunds)
{
    public string Scope => MerchantId.HasValue ? "MerchantAccount" : "DirectOperation";
}

public sealed record PaymentLogDetailResponse(PaymentLogListResponse Log, IReadOnlyList<PaymentSubLogResponse> SubLogs, IReadOnlyList<CashRecordResponse> CashRecords, IReadOnlyList<FinancialAdjustmentResponse> Adjustments, IReadOnlyList<PaymentStageResponse> Stages, string? Notes);

public sealed record PaymentSubLogResponse(Guid Id, decimal Amount, string? PaymentMethod, DateOnly DateReceived, string Status, Guid DraftedBy, DateTime DraftedAt, Guid? ConfirmedBy, DateTime? ConfirmedAt, string? RejectionReason, string? Notes, string? DraftedByName, string? ConfirmedByName)
{
    public string Scope => MerchantId.HasValue ? "MerchantAccount" : "DirectOperation";
    public Guid? OperationId { get; init; }
    public Guid? MerchantId { get; init; }
    public string? OperationNumber { get; init; }
    public string? Reference { get; init; }
    public string MovementMethod => PaymentMethod ?? "";
    public IReadOnlyList<string> PermittedActions => Status switch
    {
        "Draft" => ["submit"],
        "PendingAdminReview" => ["approve", "reject"],
        _ => []
    };
}

public sealed record CashRecordResponse(Guid Id, Guid? OperationId, string PaymentType, string? SubType, string? TransactionReference, decimal Amount, string Status, DateTime PaymentDate, Guid CreatedBy, string? CreatedByName, Guid? ConfirmedBy, DateTime? ConfirmedAt, string? Notes)
{
    public string Scope => MerchantId.HasValue ? "MerchantAccount" : "DirectOperation";
    public Guid? MerchantId { get; init; }
    public string? OperationNumber { get; init; }
    public string? Reference { get; init; }
    public string MovementMethod => SubType ?? "";
    public IReadOnlyList<string> PermittedActions => Status switch
    {
        "PendingAccountant" or "PendingAdminReview" => ["approve", "reject"],
        _ => []
    };
}

public sealed record FinancialAdjustmentResponse(Guid Id, Guid MerchantId, Guid? OperationId, string AdjustmentType, decimal Amount, string Status, string? Notes, Guid CreatedBy, string? CreatedByName, DateTime CreatedAt);

public sealed record PaymentHistoryResponse(Guid Id, string RecordType, Guid? OperationId, string? OperationNumber, string? OperationType, Guid? MerchantId, string? MerchantName, string? BuyerName, string? PaymentMethod, decimal Amount, string Status, DateTime HappenedAt, string? ActorName, string? Notes)
{
    public string Scope => MerchantId.HasValue ? "MerchantAccount" : "DirectOperation";
}

public sealed record PaymentStageResponse(string StageType, DateTime HappenedAt, string? ActorName, decimal Amount, string? PaymentMethod, string Status, string? Notes, string? OperationNumber);

public sealed record MerchantAccountStatementResponse(
    Guid Id,
    string EntryType,
    string EventLabel,
    string SourceReference,
    string? PaymentMethod,
    string MethodLabel,
    decimal DebitAmount,
    decimal CreditAmount,
    decimal RunningBalance,
    DateTime PostedAt,
    Guid PostedBy);

internal sealed record PaymentOperationContext(Guid OperationId, string OperationNumber, string OperationType, Guid? MerchantId, string? BuyerName);

public sealed record MerchantCollectionRequest(decimal Amount, string? PaymentMethod, string? TransactionReference, IReadOnlyList<MerchantAllocationInput>? Allocations, string? Notes, bool SubmitForReview = true, Guid? SourceOperationId = null);
public sealed record CollectionWorkspaceRequest(string Scope, decimal Amount, string? PaymentMethod, string? TransactionReference, Guid? MerchantId = null, Guid? OperationId = null, DateOnly? DateReceived = null, IReadOnlyList<MerchantAllocationInput>? Allocations = null, string? Notes = null, bool SubmitForReview = true);
public sealed record CollectionReassignmentRequest(Guid? AccountantUserId, string? Reason = null);
public sealed record MerchantAccountCollectionDraftResponse(Guid Id, string Reference, Guid MerchantId, decimal Amount, string PaymentMethod, string? TransactionReference, string Status, Guid DraftedBy, DateTime DraftedAt, Guid? AssignedTo, DateTime? AssignedAt, Guid? ConfirmedBy, DateTime? ConfirmedAt, string? RejectionReason, string? Notes, string? AllocationsJson, string? DraftedByName, string? AssignedToName, string? ConfirmedByName, Guid? SourceOperationId = null)
{
    public string Scope => "MerchantAccount";
    public string MovementMethod => PaymentMethod;
    public IReadOnlyList<string> PermittedActions => Status switch
    {
        "Draft" => ["submit", "reassign"],
        "PendingAdminReview" => ["approve", "reject", "reassign"],
        _ => []
    };
}
public sealed record PaymentAuditEventResponse(Guid Id, string Action, string? PreviousStatus, string? NewStatus, Guid? MerchantId, Guid? OperationId, Guid? PaymentLogId, Guid? CollectionDraftId, Guid ActorId, DateTime OccurredAt, decimal? Amount, string? PaymentMethod, string? Reason, string? CorrelationId, string? IdempotencyKey, string? DataJson, string? MerchantName = null, string? OperationNumber = null, string? BuyerName = null, string? ActorName = null, string? ActorRole = null, string? TransactionReference = null, string? Scope = null);
internal sealed record PaymentAuditOperationContext(Guid Id, string OperationNumber, string? BuyerName);
internal sealed record PaymentAuditActorContext(Guid Id, string Name, string Role);
public sealed record MerchantRefundRequest(decimal Amount, string? Notes);
public sealed record MerchantRefundPayoutRequest(decimal Amount, string? PaymentMethod, string? TransactionReference, string? Notes);
public sealed record MerchantAccountListResponse(Guid MerchantId, string BusinessName, decimal AmountDue, decimal CreditAvailable, decimal ReservedRefunds, decimal ConfirmedCollections, decimal NetCollected, DateTime OpenedAt, MerchantAccountClassification Classification);
public sealed record MerchantAccountDetailResponse(Guid MerchantId, string BusinessName, MerchantAccountSnapshot Balance, MerchantAccountFinancialBreakdown? Breakdown, decimal NetCollected, MerchantAccountClassification Classification, MerchantAccountProfileResponse? Profile = null);
public sealed record MerchantAccountProfileResponse(string ContactPersonName, IReadOnlyList<string> PhoneNumbers, string? Email, string? Address, string BusinessType, string Status);
public sealed record MerchantStatementDisplayRow(Guid Id, long Sequence, DateTime PostedAt, string EntryType, string EventLabel, Guid? OperationId, Guid? PaymentId, string SourceReference, decimal DebitAmount, decimal CreditAmount, decimal RunningBalance, string? PaymentMethod, string MethodLabel, string? TransactionReference, string Status, string? Notes, string ActorName);
public sealed record MerchantOrderLineResponse(string ProductName, string SkuCode, int Quantity);
public sealed record MerchantOrderResponse(Guid OperationId, string OperationNumber, string OperationType, string Status, DateTime Date, IReadOnlyList<MerchantOrderLineResponse> Lines, decimal SaleTotal, decimal CollectionsAllocated, decimal AcceptedReturns, decimal AdditionalCharges, decimal AmountReductions, decimal Refunds, decimal Remaining, string FinancialClosureStatus = "Open");
public sealed record FinancialClosureProposalRequest(Guid[] OperationIds, string? Notes, string? IdempotencyKey);
public sealed record FinancialClosureReviewRequest(Guid[]? ApprovedOperationIds, string? RejectionReason, string? ReviewReason);
