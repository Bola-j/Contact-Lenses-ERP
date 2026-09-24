using System.Text.Json;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.SharedKernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

/// <summary>
/// The only service allowed to append merchant receivable entries. Balances are
/// derived from immutable entries; they are never stored as a mutable total.
/// </summary>
public sealed class MerchantAccountService
{
    private const string Posted = "Posted";
    private const string Approved = "Approved";
    private const string Paid = "Paid";
    private readonly PaymentsDbContext _payments;
    private readonly SharedDbContext _shared;
    private readonly OperationsDbContext _operations;

    public MerchantAccountService(PaymentsDbContext payments, SharedDbContext shared, OperationsDbContext operations)
    {
        _payments = payments;
        _shared = shared;
        _operations = operations;
    }

    public static bool IsMovementMethod(string? method) => method is "CashHandToHand" or "CashTransaction" or "BankTransfer" or "Wallet";

    public static bool RequiresTransactionReference(string? method) => method is "CashTransaction" or "BankTransfer" or "Wallet";

    public async Task<MerchantReceivableAccount> GetOrCreateForUpdateAsync(Guid merchantId, Guid actorId, DateTime now, CancellationToken cancellationToken)
    {
        var account = _payments.MerchantReceivableAccounts.Local.FirstOrDefault(value => value.MerchantId == merchantId)
            ?? await _payments.MerchantReceivableAccounts.FirstOrDefaultAsync(value => value.MerchantId == merchantId, cancellationToken);
        if (account is not null)
        {
            if (_payments.Database.IsRelational())
            {
                account = await _payments.MerchantReceivableAccounts
                    .FromSqlInterpolated($"select * from payments.merchant_receivable_accounts where merchant_id = {merchantId} for update")
                    .SingleAsync(cancellationToken);
            }
            return account;
        }

        account = new MerchantReceivableAccount
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            Status = "Open",
            NextSequence = 1,
            OpenedAt = now,
            UpdatedAt = now,
            OpenedBy = actorId
        };
        _payments.MerchantReceivableAccounts.Add(account);
        await _payments.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<MerchantOperationObligation> PostSaleAsync(Guid merchantId, Guid operationId, decimal amount, Guid actorId, DateTime now, string? notes, CancellationToken cancellationToken)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var existing = await _payments.MerchantOperationObligations.SingleOrDefaultAsync(value => value.OperationId == operationId, cancellationToken);
        if (existing is not null) return existing;

        var account = await GetOrCreateForUpdateAsync(merchantId, actorId, now, cancellationToken);
        await AppendAsync(account, "SaleCharge", amount, 0m, null, null, "OperationSale", operationId, operationId, null, actorId, now, notes, cancellationToken);
        var obligation = new MerchantOperationObligation
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            OperationId = operationId,
            SourceType = "Operation",
            SourceId = operationId,
            OriginalAmount = amount,
            PostedAt = now,
            Status = "Open"
        };
        _payments.MerchantOperationObligations.Add(obligation);
        await _payments.SaveChangesAsync(cancellationToken);
        return obligation;
    }

    public async Task<MerchantOperationObligation> PostOpeningBalanceAsync(
        MerchantOpeningBalanceCharge charge,
        Guid actorId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (charge.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(charge.Amount));
        var existing = await _payments.MerchantOperationObligations
            .SingleOrDefaultAsync(value => value.SourceType == "OpeningBalanceCharge" && value.SourceId == charge.Id, cancellationToken);
        if (existing is not null) return existing;

        var account = await GetOrCreateForUpdateAsync(charge.MerchantId, actorId, now, cancellationToken);
        var entry = await AppendAsync(account, "OpeningBalanceCharge", charge.Amount, 0m, null, null, "OpeningBalanceCharge", charge.Id, null, null, actorId, now, charge.Description, cancellationToken);
        var obligation = new MerchantOperationObligation
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            OperationId = null,
            SourceType = "OpeningBalanceCharge",
            SourceId = charge.Id,
            OriginalAmount = charge.Amount,
            PostedAt = charge.AsOfDate.ToDateTime(TimeOnly.MinValue),
            Status = "Open"
        };
        _payments.MerchantOperationObligations.Add(obligation);
        charge.PostedEntryId = entry.Id;
        charge.Status = "Posted";
        charge.ReviewedBy = actorId;
        charge.ReviewedAt = now;
        await _payments.SaveChangesAsync(cancellationToken);
        return obligation;
    }

    public async Task<MerchantOpeningBalanceCharge> CorrectOpeningBalanceAsync(
        MerchantOpeningBalanceCharge original,
        decimal amount,
        DateOnly asOfDate,
        string description,
        Guid actorId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        // Reload under the same transaction that owns the idempotency key.  This
        // serializes correction with collection posting because both paths then
        // take the merchant-account row lock before changing allocations.
        original = await LockOpeningBalanceAsync(original.Id, cancellationToken);
        if (original.Status != "Posted") throw new InvalidOperationException("Only a posted opening balance can be corrected.");
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var account = await GetOrCreateForUpdateAsync(original.MerchantId, actorId, now, cancellationToken);
        // Keep lock order consistent with collections: account, then all open
        // obligations, then the historical allocation rows being superseded.
        await LockOpenObligationsAsync(account.Id, cancellationToken);
        var obligation = await LockOpeningBalanceObligationAsync(original.Id, cancellationToken);
        if (obligation is null) throw new InvalidOperationException("The posted opening balance has no obligation.");
        var historicalAllocations = await LockEffectiveAllocationsAsync(obligation.Id, cancellationToken);
        await AppendAsync(account, "OpeningBalanceReversal", 0m, original.Amount, null, null, "OpeningBalanceReversal", original.Id, null, null, actorId, now, "Reversal for opening balance correction.", cancellationToken);
        obligation.Status = "Reversed";
        original.Status = "Corrected";
        original.ReviewedBy = actorId;
        original.ReviewedAt = now;
        var replacement = new MerchantOpeningBalanceCharge
        {
            Id = Guid.NewGuid(),
            MerchantId = original.MerchantId,
            Amount = amount,
            AsOfDate = asOfDate,
            Description = description,
            Status = "Posted",
            CreatedBy = actorId,
            CreatedAt = now,
            ReversesChargeId = original.Id,
            CorrelationId = Guid.NewGuid().ToString("N")
        };
        original.ReplacedByChargeId = replacement.Id;
        _payments.MerchantOpeningBalanceCharges.Add(replacement);
        await _payments.SaveChangesAsync(cancellationToken);

        var replacementObligation = await PostOpeningBalanceAsync(replacement, actorId, now, cancellationToken);
        var remaining = amount;
        foreach (var source in historicalAllocations)
        {
            if (remaining <= 0m) break;
            var transferred = Math.Min(source.Amount, remaining);
            if (transferred <= 0m) continue;
            var replacementAllocation = new MerchantEntryAllocation
            {
                Id = Guid.NewGuid(),
                EntryId = source.EntryId,
                ObligationId = replacementObligation.Id,
                Amount = transferred,
                AllocatedAt = now,
                AllocatedBy = actorId
            };
            _payments.MerchantEntryAllocations.Add(replacementAllocation);
            _payments.MerchantAllocationReconciliations.Add(new MerchantAllocationReconciliation
            {
                Id = Guid.NewGuid(),
                SourceAllocationId = source.Id,
                ReplacementAllocationId = replacementAllocation.Id,
                CorrectionChargeId = replacement.Id,
                CreatedBy = actorId,
                CreatedAt = now,
                CorrelationId = replacement.CorrelationId!
            });
            remaining -= transferred;
        }
        if (remaining <= 0m) replacementObligation.Status = "Settled";
        await _payments.SaveChangesAsync(cancellationToken);
        return replacement;
    }

    public async Task<MerchantAccountEntry?> PostCreditForOperationAsync(Guid merchantId, Guid operationId, decimal amount, string entryType, Guid actorId, DateTime now, string? notes, CancellationToken cancellationToken)
        => await PostCreditForSourceAsync(merchantId, operationId, operationId, amount, entryType, actorId, now, notes, cancellationToken);

    public async Task<MerchantAccountEntry?> PostCreditForSourceAsync(Guid merchantId, Guid sourceId, Guid? operationId, decimal amount, string entryType, Guid actorId, DateTime now, string? notes, CancellationToken cancellationToken)
    {
        if (amount <= 0) return null;
        var account = await GetOrCreateForUpdateAsync(merchantId, actorId, now, cancellationToken);
        var entry = await AppendAsync(account, entryType, 0m, amount, null, null, entryType, sourceId, operationId, null, actorId, now, notes, cancellationToken);
        await _payments.SaveChangesAsync(cancellationToken);
        return entry;
    }

    public async Task<MerchantAccountEntry?> PostDebitForOperationAsync(Guid merchantId, Guid operationId, decimal amount, string entryType, Guid actorId, DateTime now, string? notes, CancellationToken cancellationToken)
        => await PostDebitForSourceAsync(merchantId, operationId, operationId, amount, entryType, actorId, now, notes, cancellationToken);

    public async Task<MerchantAccountEntry?> PostDebitForSourceAsync(Guid merchantId, Guid sourceId, Guid? operationId, decimal amount, string entryType, Guid actorId, DateTime now, string? notes, CancellationToken cancellationToken)
    {
        if (amount <= 0) return null;
        var account = await GetOrCreateForUpdateAsync(merchantId, actorId, now, cancellationToken);
        var entry = await AppendAsync(account, entryType, amount, 0m, null, null, entryType, sourceId, operationId, null, actorId, now, notes, cancellationToken);
        await _payments.SaveChangesAsync(cancellationToken);
        return entry;
    }

    public async Task<MerchantAccountEntry> PostCollectionAsync(
        Guid merchantId,
        Guid paymentId,
        decimal amount,
        string method,
        string? transactionReference,
        Guid actorId,
        DateTime now,
        IReadOnlyList<MerchantAllocationInput>? requestedAllocations,
        string? notes,
        CancellationToken cancellationToken)
    {
        ValidateMovement(method, transactionReference);
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var account = await GetOrCreateForUpdateAsync(merchantId, actorId, now, cancellationToken);
        var existing = await _payments.MerchantAccountEntries.SingleOrDefaultAsync(value =>
            value.SourceType == "PaymentCollection" && value.SourceId == paymentId && value.EntryType == "Collection", cancellationToken);
        if (existing is not null) return existing;
        await LockOpenObligationsAsync(account.Id, cancellationToken);
        var entry = await AppendAsync(account, "Collection", 0m, amount, method, transactionReference, "PaymentCollection", paymentId, null, paymentId, actorId, now, notes, cancellationToken);
        await AllocateCreditAsync(account.Id, entry, requestedAllocations, actorId, now, cancellationToken);
        await _payments.SaveChangesAsync(cancellationToken);
        return entry;
    }

    public async Task<IReadOnlyList<MerchantAllocationPreview>> PreviewAllocationsAsync(Guid merchantId, decimal amount, CancellationToken cancellationToken)
    {
        if (amount <= 0) return [];
        var account = await _payments.MerchantReceivableAccounts.AsNoTracking().SingleOrDefaultAsync(value => value.MerchantId == merchantId, cancellationToken);
        if (account is null) return [];
        var obligations = await _payments.MerchantOperationObligations.AsNoTracking()
            .Where(value => value.AccountId == account.Id && value.Status == "Open")
            .OrderBy(value => value.PostedAt).ThenBy(value => value.Id).ToListAsync(cancellationToken);
        var allocated = await _payments.EffectiveCreditAllocations().AsNoTracking()
            .Where(value => obligations.Select(obligation => obligation.Id).Contains(value.ObligationId) && value.Entry.CreditAmount > 0)
            .GroupBy(value => value.ObligationId).Select(group => new { group.Key, Amount = group.Sum(value => value.Amount) })
            .ToDictionaryAsync(value => value.Key, value => value.Amount, cancellationToken);
        return BuildOldestFirst(obligations, allocated, amount)
            .Select(value => new MerchantAllocationPreview(value.ObligationId, value.Amount, obligations.Single(obligation => obligation.Id == value.ObligationId).OperationId))
            .ToList();
    }

    public async Task<MerchantAccountEntry> PostRefundPayoutAsync(Guid merchantId, Guid reservationId, Guid payoutId, decimal amount, string method, string? transactionReference, Guid actorId, DateTime now, string? notes, CancellationToken cancellationToken)
    {
        ValidateMovement(method, transactionReference);
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var account = await GetOrCreateForUpdateAsync(merchantId, actorId, now, cancellationToken);
        var reservation = _payments.Database.IsRelational()
            ? await _payments.MerchantRefundReservations
                .FromSqlInterpolated($"select * from payments.merchant_refund_reservations where \"Id\" = {reservationId} and account_id = {account.Id} for update")
                .SingleAsync(cancellationToken)
            : await _payments.MerchantRefundReservations.SingleAsync(value => value.Id == reservationId && value.AccountId == account.Id, cancellationToken);
        if (reservation.Status is not (Approved or "PartiallyPaid")) throw new InvalidOperationException("The refund reservation is not approved for this payout amount.");
        var outstanding = reservation.Amount - reservation.PaidAmount;
        if (amount > outstanding) throw new InvalidOperationException("The refund payout exceeds the reservation's remaining amount.");
        var snapshot = await GetSnapshotAsync(account.MerchantId, cancellationToken);
        var availableAfterOtherReservations = (snapshot?.CreditAvailable ?? 0m) + outstanding;
        if (snapshot is null || availableAfterOtherReservations < amount) throw new InvalidOperationException("The account does not have enough available credit for this refund.");
        var entry = await AppendAsync(account, "RefundPayout", amount, 0m, method, transactionReference, "RefundPayout", payoutId, null, payoutId, actorId, now, notes, cancellationToken);
        reservation.PaidAmount += amount;
        reservation.Status = reservation.PaidAmount >= reservation.Amount ? Paid : "PartiallyPaid";
        reservation.PayoutEntryId = entry.Id;
        await _payments.SaveChangesAsync(cancellationToken);
        return entry;
    }

    public async Task<MerchantAccountSnapshot?> GetSnapshotAsync(Guid merchantId, CancellationToken cancellationToken)
    {
        var account = await _payments.MerchantReceivableAccounts.AsNoTracking().SingleOrDefaultAsync(value => value.MerchantId == merchantId, cancellationToken);
        if (account is null) return null;
        var totals = await _payments.MerchantAccountEntries.AsNoTracking()
            .Where(value => value.AccountId == account.Id && value.Status == Posted)
            .GroupBy(_ => 1)
            .Select(group => new { Debit = group.Sum(value => value.DebitAmount), Credit = group.Sum(value => value.CreditAmount) })
            .SingleOrDefaultAsync(cancellationToken);
        var reserved = await _payments.MerchantRefundReservations.AsNoTracking()
            .Where(value => value.AccountId == account.Id && (value.Status == Approved || value.Status == "PartiallyPaid"))
            .SumAsync(value => (decimal?)(value.Amount - value.PaidAmount), cancellationToken) ?? 0m;
        var pendingCollections = await _payments.InstallmentSubLogs.AsNoTracking()
            .Where(value => value.MainLog.MerchantId == merchantId &&
                (value.MainLog.PaymentMethod == "MerchantAccount" || value.MainLog.PaymentMethod == "Installment" || value.MainLog.PaymentMethod == "Installlaugment") &&
                (value.SubLogStatus == "Draft" || value.SubLogStatus == "PendingAdminReview"))
            .SumAsync(value => (decimal?)value.Amount, cancellationToken) ?? 0m;
        pendingCollections += await _payments.MerchantAccountCollectionDrafts.AsNoTracking()
            .Where(value => value.Account.MerchantId == merchantId && (value.Status == "Draft" || value.Status == "PendingAdminReview"))
            .SumAsync(value => (decimal?)value.Amount, cancellationToken) ?? 0m;
        pendingCollections += await _payments.CashRecords.AsNoTracking()
            .Where(value => value.OperationId != Guid.Empty && value.PaymentType == "CashReceived" &&
                (value.Status == "PendingAccountant" || value.Status == "PendingAdminReview") &&
                _payments.MainPaymentLogs.Any(log => log.OperationId == value.OperationId && log.MerchantId == merchantId && !log.IsDeleted &&
                    (log.PaymentMethod == "MerchantAccount" || log.PaymentMethod == "Installment" || log.PaymentMethod == "Installlaugment")))
            .SumAsync(value => (decimal?)value.Amount, cancellationToken) ?? 0m;
        var completedRefunds = await _payments.MerchantAccountEntries.AsNoTracking()
            .Where(value => value.AccountId == account.Id && value.Status == Posted && value.EntryType == "RefundPayout")
            .SumAsync(value => (decimal?)value.DebitAmount, cancellationToken) ?? 0m;
        var net = (totals?.Debit ?? 0m) - (totals?.Credit ?? 0m);
        var grossCredit = Math.Max(-net, 0m);
        return new MerchantAccountSnapshot(account.Id, account.MerchantId, Math.Max(net, 0m), grossCredit, Math.Max(grossCredit - reserved, 0m), reserved, totals?.Debit ?? 0m, totals?.Credit ?? 0m, pendingCollections, completedRefunds, account.OpenedAt);
    }

    public async Task<MerchantAccountFinancialBreakdown?> GetFinancialBreakdownAsync(Guid merchantId, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(merchantId, cancellationToken);
        if (snapshot is null) return null;
        var entries = await _payments.MerchantAccountEntries.AsNoTracking()
            .Where(value => value.AccountId == snapshot.AccountId && value.Status == Posted)
            .ToListAsync(cancellationToken);
        return BuildFinancialBreakdown(merchantId, entries, snapshot.AmountDue);
    }

    public async Task<MerchantOpeningBalanceSummary> GetOpeningBalanceSummaryAsync(Guid merchantId, CancellationToken cancellationToken)
    {
        var chargeRows = await _payments.MerchantOpeningBalanceCharges.AsNoTracking()
            .Where(value => value.MerchantId == merchantId && new[] { "Posted", "Reversed", "Corrected" }.Contains(value.Status))
            .OrderBy(value => value.AsOfDate).ThenBy(value => value.CreatedAt)
            .ToListAsync(cancellationToken);
        var postedIds = chargeRows.Where(value => value.Status == "Posted").Select(value => value.Id).ToArray();
        if (postedIds.Length == 0)
            return new MerchantOpeningBalanceSummary(0m, 0m, 0m, null);

        var account = await _payments.MerchantReceivableAccounts.AsNoTracking().SingleOrDefaultAsync(value => value.MerchantId == merchantId, cancellationToken);
        if (account is null)
            return new MerchantOpeningBalanceSummary(chargeRows.Where(value => value.Status == "Posted").Sum(value => value.Amount), 0m, chargeRows.Where(value => value.Status == "Posted").Sum(value => value.Amount), chargeRows.Min(value => value.AsOfDate));

        var obligations = await _payments.MerchantOperationObligations.AsNoTracking()
            .Where(value => value.AccountId == account.Id && value.SourceType == "OpeningBalanceCharge" && postedIds.Contains(value.SourceId))
            .ToListAsync(cancellationToken);
        var obligationIds = obligations.Select(value => value.Id).ToArray();
        var allocated = obligationIds.Length == 0
            ? 0m
            : await _payments.EffectiveAllocations().AsNoTracking()
                .Where(value => obligationIds.Contains(value.ObligationId))
                .SumAsync(value => (decimal?)value.Amount, cancellationToken) ?? 0m;
        var original = chargeRows.Where(value => value.Status == "Posted").Sum(value => value.Amount);
        return new MerchantOpeningBalanceSummary(original, allocated, Math.Max(0m, original - allocated), chargeRows.Min(value => value.AsOfDate));
    }

    public async Task<IReadOnlyDictionary<Guid, MerchantAccountFinancialBreakdown>> GetFinancialBreakdownsAsync(IReadOnlyCollection<Guid> merchantIds, CancellationToken cancellationToken)
    {
        if (merchantIds.Count == 0) return new Dictionary<Guid, MerchantAccountFinancialBreakdown>();
        var idSet = merchantIds.ToHashSet();
        var entries = await _payments.MerchantAccountEntries.AsNoTracking()
            .Where(value => idSet.Contains(value.Account.MerchantId) && value.Status == Posted)
            .Select(value => new { MerchantId = value.Account.MerchantId, value.EntryType, value.DebitAmount, value.CreditAmount })
            .ToListAsync(cancellationToken);
        return entries.GroupBy(value => value.MerchantId).ToDictionary(
            group => group.Key,
            group => BuildFinancialBreakdown(group.Key, group.Select(value => new MerchantAccountEntry
            {
                EntryType = value.EntryType,
                DebitAmount = value.DebitAmount,
                CreditAmount = value.CreditAmount
            }), group.Sum(value => value.DebitAmount - value.CreditAmount)));
    }

    public async Task<IReadOnlyList<MerchantStatementRow>> GetStatementAsync(Guid merchantId, int take, CancellationToken cancellationToken, DateTime? from = null, DateTime? to = null)
    {
        var account = await _payments.MerchantReceivableAccounts.AsNoTracking().SingleOrDefaultAsync(value => value.MerchantId == merchantId, cancellationToken);
        if (account is null) return [];
        var start = from?.Date;
        var endExclusive = to?.Date.AddDays(1);
        if (start.HasValue && endExclusive.HasValue && endExclusive <= start) return [];
        var opening = start.HasValue
            ? await _payments.MerchantAccountEntries.AsNoTracking()
                .Where(value => value.AccountId == account.Id && value.PostedAt < start.Value && value.Status == Posted)
                .SumAsync(value => (decimal?)(value.DebitAmount - value.CreditAmount), cancellationToken) ?? 0m
            : 0m;
        var entries = await _payments.MerchantAccountEntries.AsNoTracking()
            .Where(value => value.AccountId == account.Id &&
                value.Status == Posted &&
                (!start.HasValue || value.PostedAt >= start.Value) &&
                (!endExclusive.HasValue || value.PostedAt < endExclusive.Value))
            .OrderBy(value => value.Sequence)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);
        decimal running = opening;
        return entries.Select(value =>
        {
            running += value.DebitAmount - value.CreditAmount;
            return new MerchantStatementRow(value.Id, value.Sequence, value.PostedAt, value.EntryType, value.OperationId, value.PaymentId, value.DebitAmount, value.CreditAmount, running, value.PaymentMethod, value.TransactionReference, value.Status, value.Notes, value.PostedBy);
        }).ToList();
    }

    private static MerchantAccountFinancialBreakdown BuildFinancialBreakdown(Guid merchantId, IEnumerable<MerchantAccountEntry> entries, decimal amountDue)
    {
        var rows = entries.ToList();
        var totalSales = rows.Where(value => value.EntryType == "SaleCharge").Sum(value => value.DebitAmount);
        // A cheaper approved replacement is an accepted return credit; a more
        // expensive replacement is an additional charge. This keeps all
        // customer-facing account math within the seven approved categories.
        var acceptedReturnValue = rows.Where(value => value.EntryType is "ReturnCredit" or "ExchangeCredit").Sum(value => value.CreditAmount);
        var collections = rows.Where(value => value.EntryType == "Collection").Sum(value => value.CreditAmount);
        var refunds = rows.Where(value => value.EntryType == "RefundPayout").Sum(value => value.DebitAmount);
        var additionalCharges = rows.Where(value => value.EntryType is "AdditionalCharge" or "ExchangeSurcharge").Sum(value => value.DebitAmount);
        var amountReductions = rows.Where(value => value.EntryType == "BalanceReduction").Sum(value => value.CreditAmount);
        return new MerchantAccountFinancialBreakdown(merchantId, totalSales, acceptedReturnValue, collections, refunds, additionalCharges, amountReductions, amountDue);
    }

    public async Task<decimal> GetOpeningBalanceAsync(Guid merchantId, DateTime? from, CancellationToken cancellationToken)
    {
        if (!from.HasValue) return 0m;
        var accountId = await _payments.MerchantReceivableAccounts.AsNoTracking()
            .Where(value => value.MerchantId == merchantId).Select(value => (Guid?)value.Id).SingleOrDefaultAsync(cancellationToken);
        if (!accountId.HasValue) return 0m;
        return await _payments.MerchantAccountEntries.AsNoTracking()
            .Where(value => value.AccountId == accountId.Value && value.PostedAt < from.Value.Date && value.Status == Posted)
            .SumAsync(value => (decimal?)(value.DebitAmount - value.CreditAmount), cancellationToken) ?? 0m;
    }

    public async Task<decimal> GetClosingBalanceAsync(Guid merchantId, DateTime? to, CancellationToken cancellationToken)
    {
        var accountId = await _payments.MerchantReceivableAccounts.AsNoTracking()
            .Where(value => value.MerchantId == merchantId)
            .Select(value => (Guid?)value.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (!accountId.HasValue) return 0m;
        var endExclusive = to?.Date.AddDays(1);
        return await _payments.MerchantAccountEntries.AsNoTracking()
            .Where(value => value.AccountId == accountId.Value &&
                (!endExclusive.HasValue || value.PostedAt < endExclusive.Value) &&
                value.Status == Posted)
            .SumAsync(value => (decimal?)(value.DebitAmount - value.CreditAmount), cancellationToken) ?? 0m;
    }

    public async Task<MerchantAccountClassification> GetClassificationAsync(Guid merchantId, DateTime now, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(merchantId, cancellationToken);
        if (snapshot is null) return MerchantAccountClassification.Empty;
        var start = snapshot.OpenedAt > now.AddMonths(-12) ? snapshot.OpenedAt : now.AddMonths(-12);
        var obligations = await _payments.MerchantOperationObligations.AsNoTracking()
            .Where(value => value.AccountId == snapshot.AccountId && value.PostedAt >= start)
            .ToListAsync(cancellationToken);
        var obligationOperationIds = obligations.Where(value => value.OperationId.HasValue).Select(value => value.OperationId!.Value).ToArray();
        var finalizedInstallmentSaleIds = (await _operations.OperationLogs.AsNoTracking()
            .Where(value => obligationOperationIds.Contains(value.Id) && !value.IsDeleted && value.Status == "Completed" &&
                (value.OperationType == "WholesaleSale" || value.OperationType == "RetailSale") &&
                (value.PaymentMethod == "MerchantAccount" || value.PaymentMethod == "Installment"))
            .Select(value => value.Id)
            .ToListAsync(cancellationToken)).ToHashSet();
        obligations = obligations.Where(value => value.OperationId.HasValue && finalizedInstallmentSaleIds.Contains(value.OperationId.Value)).ToList();
        var allocationRows = await _payments.EffectiveCreditAllocations().AsNoTracking()
            .Where(value => obligations.Select(obligation => obligation.Id).Contains(value.ObligationId) && value.Entry.CreditAmount > 0)
            .GroupBy(value => value.ObligationId)
            .Select(group => new { ObligationId = group.Key, Amount = group.Sum(value => value.Amount) })
            .ToDictionaryAsync(value => value.ObligationId, value => value.Amount, cancellationToken);
        var salePercentages = obligations.Where(value => value.OriginalAmount > 0).Select(value => Math.Min((allocationRows.GetValueOrDefault(value.Id) / value.OriginalAmount) * 100m, 100m)).ToList();
        var discipline = salePercentages.Count == 0 ? 0m : salePercentages.Average();
        var obligationTotal = obligations.Sum(value => value.OriginalAmount);
        var coverage = obligationTotal == 0 ? 0m : Math.Min((allocationRows.Values.Sum() / obligationTotal) * 100m, 100m);
        var settings = await GetClassificationSettingsAsync(cancellationToken);
        var exposureScore = Math.Max(0m, 100m - (snapshot.AmountDue / settings.HighExposureThreshold * 100m));
        var score = Math.Round(discipline * settings.DisciplineWeight + coverage * settings.CollectionCoverageWeight + exposureScore * settings.ExposureWeight, 2);
        var grade = score >= settings.GradeA ? "A" : score >= settings.GradeB ? "B" : score >= settings.GradeC ? "C" : score >= settings.GradeD ? "D" : "E";
        var flags = new List<string>();
        if (snapshot.AmountDue >= settings.HighExposureThreshold) flags.Add("HighExposure");
        if (snapshot.CreditAvailable > 0) flags.Add("CreditAvailable");
        if (snapshot.ReservedRefunds > 0) flags.Add("ReservedRefund");
        if (coverage < settings.LowCollectionCoveragePercent && obligations.Count > 0) flags.Add("LowCollectionCoverage");
        if (discipline < settings.WeakDisciplinePercent && obligations.Count > 0) flags.Add("WeakPaymentDiscipline");
        if (obligations.Count < settings.MinimumSaleCount || now - snapshot.OpenedAt < TimeSpan.FromDays(settings.MinimumHistoryDays)) flags.Add("Provisional");
        return new MerchantAccountClassification(grade, score, discipline, coverage, flags, start, now);
    }

    public async Task<MerchantAccountClassificationSettings> GetClassificationSettingsAsync(CancellationToken cancellationToken)
    {
        var raw = await _shared.SystemSettings.AsNoTracking()
            .Where(value => value.Key == MerchantAccountClassificationSettings.SettingKey)
            .Select(value => value.Value).SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(raw)) return MerchantAccountClassificationSettings.Default;
        try
        {
            return JsonSerializer.Deserialize<MerchantAccountClassificationSettings>(raw) is { } value && value.IsValid
                ? value
                : MerchantAccountClassificationSettings.Default;
        }
        catch (JsonException)
        {
            return MerchantAccountClassificationSettings.Default;
        }
    }

    public async Task<IReadOnlyList<MerchantAccountClassificationSnapshot>> GetClassificationHistoryAsync(Guid merchantId, CancellationToken cancellationToken)
    {
        var accountId = await _payments.MerchantReceivableAccounts.AsNoTracking()
            .Where(value => value.MerchantId == merchantId)
            .Select(value => (Guid?)value.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (accountId is null) return [];
        return await _payments.MerchantAccountClassificationSnapshots.AsNoTracking()
            .Where(value => value.AccountId == accountId.Value)
            .OrderByDescending(value => value.CalendarYear)
            .ToListAsync(cancellationToken);
    }

    private async Task<MerchantOpeningBalanceCharge> LockOpeningBalanceAsync(Guid chargeId, CancellationToken cancellationToken)
    {
        if (!_payments.Database.IsRelational())
            return await _payments.MerchantOpeningBalanceCharges.SingleAsync(value => value.Id == chargeId, cancellationToken);

        return await _payments.MerchantOpeningBalanceCharges
            .FromSqlInterpolated($"select * from payments.merchant_opening_balance_charges where id = {chargeId} for update")
            .SingleAsync(cancellationToken);
    }

    private async Task<MerchantOperationObligation?> LockOpeningBalanceObligationAsync(Guid chargeId, CancellationToken cancellationToken)
    {
        if (!_payments.Database.IsRelational())
            return await _payments.MerchantOperationObligations.SingleOrDefaultAsync(value => value.SourceType == "OpeningBalanceCharge" && value.SourceId == chargeId, cancellationToken);

        return await _payments.MerchantOperationObligations
            .FromSqlInterpolated($"select * from payments.merchant_operation_obligations where source_type = {"OpeningBalanceCharge"} and source_id = {chargeId} for update")
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<List<MerchantEntryAllocation>> LockEffectiveAllocationsAsync(Guid obligationId, CancellationToken cancellationToken)
    {
        if (!_payments.Database.IsRelational())
        {
            return await _payments.EffectiveAllocations()
                .Where(value => value.ObligationId == obligationId)
                .OrderBy(value => value.AllocatedAt).ThenBy(value => value.Id)
                .ToListAsync(cancellationToken);
        }

        return await _payments.MerchantEntryAllocations.FromSqlInterpolated($"""
            select allocation.*
            from payments.merchant_entry_allocations allocation
            where allocation.obligation_id = {obligationId}
              and not exists (
                  select 1 from payments.merchant_allocation_reconciliations reconciliation
                  where reconciliation.source_allocation_id = allocation."Id")
            order by allocation.allocated_at, allocation."Id"
            for update
            """).ToListAsync(cancellationToken);
    }

    private async Task<MerchantAccountEntry> AppendAsync(MerchantReceivableAccount account, string entryType, decimal debit, decimal credit, string? method, string? transactionReference, string sourceType, Guid sourceId, Guid? operationId, Guid? paymentId, Guid actorId, DateTime now, string? notes, CancellationToken cancellationToken)
    {
        var existing = await _payments.MerchantAccountEntries.SingleOrDefaultAsync(value => value.SourceType == sourceType && value.SourceId == sourceId && value.EntryType == entryType, cancellationToken);
        if (existing is not null) return existing;
        var entry = new MerchantAccountEntry
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Sequence = account.NextSequence++,
            EntryType = entryType,
            DebitAmount = debit,
            CreditAmount = credit,
            PaymentMethod = method,
            TransactionReference = transactionReference,
            SourceType = sourceType,
            SourceId = sourceId,
            OperationId = operationId,
            PaymentId = paymentId,
            Status = Posted,
            PostedBy = actorId,
            PostedAt = now,
            Notes = notes
        };
        account.UpdatedAt = now;
        _payments.MerchantAccountEntries.Add(entry);
        return entry;
    }

    private async Task AllocateCreditAsync(Guid accountId, MerchantAccountEntry entry, IReadOnlyList<MerchantAllocationInput>? requested, Guid actorId, DateTime now, CancellationToken cancellationToken)
    {
        var obligations = await LockOpenObligationsAsync(accountId, cancellationToken);
        var allocatedByObligation = await _payments.EffectiveCreditAllocations()
            .Where(value => obligations.Select(obligation => obligation.Id).Contains(value.ObligationId) && value.Entry.CreditAmount > 0)
            .GroupBy(value => value.ObligationId).Select(group => new { group.Key, Amount = group.Sum(value => value.Amount) })
            .ToDictionaryAsync(value => value.Key, value => value.Amount, cancellationToken);
        var allocations = requested?.Count > 0 ? requested : BuildOldestFirst(obligations, allocatedByObligation, entry.CreditAmount);
        if (allocations.Sum(value => value.Amount) > entry.CreditAmount) throw new InvalidOperationException("Allocations exceed the collection amount.");
        foreach (var allocation in allocations)
        {
            var obligation = obligations.SingleOrDefault(value => value.Id == allocation.ObligationId) ?? throw new InvalidOperationException("Allocation obligation does not belong to this merchant account.");
            var remaining = Math.Max(obligation.OriginalAmount - allocatedByObligation.GetValueOrDefault(obligation.Id), 0m);
            if (allocation.Amount <= 0 || allocation.Amount > remaining) throw new InvalidOperationException("Allocation exceeds the remaining obligation amount.");
            _payments.MerchantEntryAllocations.Add(new MerchantEntryAllocation { Id = Guid.NewGuid(), EntryId = entry.Id, ObligationId = obligation.Id, Amount = allocation.Amount, AllocatedAt = now, AllocatedBy = actorId });
            allocatedByObligation[obligation.Id] = allocatedByObligation.GetValueOrDefault(obligation.Id) + allocation.Amount;
            if (allocatedByObligation[obligation.Id] >= obligation.OriginalAmount) obligation.Status = "Settled";
        }
    }

    private async Task<List<MerchantOperationObligation>> LockOpenObligationsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        if (!_payments.Database.IsRelational())
        {
            return await _payments.MerchantOperationObligations
                .Where(value => value.AccountId == accountId && value.Status == "Open")
                .OrderBy(value => value.PostedAt).ThenBy(value => value.Id)
                .ToListAsync(cancellationToken);
        }

        const string openStatus = "Open";
        return await _payments.MerchantOperationObligations
            .FromSqlInterpolated($"select * from payments.merchant_operation_obligations where account_id = {accountId} and status = {openStatus} order by posted_at, \"Id\" for update")
            .ToListAsync(cancellationToken);
    }

    private static IReadOnlyList<MerchantAllocationInput> BuildOldestFirst(IEnumerable<MerchantOperationObligation> obligations, IReadOnlyDictionary<Guid, decimal> allocated, decimal amount)
    {
        var remainingAmount = amount;
        var result = new List<MerchantAllocationInput>();
        foreach (var obligation in obligations)
        {
            var remaining = Math.Max(obligation.OriginalAmount - allocated.GetValueOrDefault(obligation.Id), 0m);
            var allocatedAmount = Math.Min(remaining, remainingAmount);
            if (allocatedAmount > 0) result.Add(new MerchantAllocationInput(obligation.Id, allocatedAmount));
            remainingAmount -= allocatedAmount;
            if (remainingAmount == 0) break;
        }
        return result;
    }

    private static void ValidateMovement(string method, string? transactionReference)
    {
        if (!IsMovementMethod(method)) throw new InvalidOperationException("Payment method must be CashHandToHand, CashTransaction, BankTransfer, or Wallet.");
        if (RequiresTransactionReference(method) && string.IsNullOrWhiteSpace(transactionReference)) throw new InvalidOperationException("An electronic payment requires a transaction reference.");
    }
}

public sealed record MerchantAllocationInput(Guid ObligationId, decimal Amount);
public sealed record MerchantAllocationPreview(Guid ObligationId, decimal Amount, Guid? OperationId);
public sealed record MerchantAccountSnapshot(Guid AccountId, Guid MerchantId, decimal AmountDue, decimal GrossCredit, decimal CreditAvailable, decimal ReservedRefunds, decimal TotalDebits, decimal TotalCredits, decimal PendingCollections, decimal CompletedRefunds, DateTime OpenedAt)
{
    public decimal RefundDue => GrossCredit;
}
/// <summary>
/// The merchant account has exactly seven financial aspects. Exchange values
/// are folded into accepted returns or additional charges, rather than being a
/// separate balance category.
/// </summary>
public sealed record MerchantAccountFinancialBreakdown(Guid MerchantId, decimal TotalSales, decimal AcceptedReturnValue, decimal ConfirmedCollections, decimal Refunds, decimal AdditionalCharges, decimal AmountReductions, decimal RemainingOwed)
{
    public decimal NetCollected => ConfirmedCollections - Refunds;

    // Compatibility aliases keep existing API consumers compiling while the
    // SPA, reports, and documents use the canonical seven-aspect names.
    public decimal SaleTotal => TotalSales;
    public decimal ReturnTotal => AcceptedReturnValue;
    public decimal PaymentsReceived => ConfirmedCollections;
    public decimal CashRefunded => Refunds;
    public decimal BalanceReductions => AmountReductions;
    public decimal Balance => RemainingOwed;
    public decimal ChangeNet => 0m;
}
public sealed record MerchantOpeningBalanceSummary(decimal OriginalAmount, decimal AllocatedAmount, decimal RemainingAmount, DateOnly? AsOfDate);
public sealed record MerchantStatementRow(Guid Id, long Sequence, DateTime PostedAt, string EntryType, Guid? OperationId, Guid? PaymentId, decimal DebitAmount, decimal CreditAmount, decimal RunningBalance, string? PaymentMethod, string? TransactionReference, string Status, string? Notes, Guid PostedBy);
public sealed record MerchantAccountClassification(string Grade, decimal Score, decimal PaymentDisciplinePercent, decimal CollectionCoveragePercent, IReadOnlyList<string> Flags, DateTime WindowStart, DateTime WindowEnd)
{
    public static MerchantAccountClassification Empty { get; } = new("-", 0m, 0m, 0m, [], DateTime.MinValue, DateTime.MinValue);
    public string GradeLabel => Grade switch
    {
        "A" => "Excellent",
        "B" => "Good",
        "C" => "Fair",
        "D" => "Weak",
        "E" => "Critical",
        _ => "Not rated"
    };
}

public sealed record MerchantAccountClassificationSettings(
    decimal HighExposureThreshold,
    decimal DisciplineWeight,
    decimal CollectionCoverageWeight,
    decimal ExposureWeight,
    decimal GradeA,
    decimal GradeB,
    decimal GradeC,
    decimal GradeD,
    decimal LowCollectionCoveragePercent,
    decimal WeakDisciplinePercent,
    int MinimumSaleCount,
    int MinimumHistoryDays)
{
    public const string SettingKey = "merchant_account_classification";
    public static MerchantAccountClassificationSettings Default { get; } = new(1_000_000m, .50m, .30m, .20m, 85m, 70m, 55m, 40m, 50m, 50m, 3, 90);
    public bool IsValid => HighExposureThreshold > 0 && DisciplineWeight >= 0 && CollectionCoverageWeight >= 0 && ExposureWeight >= 0 && DisciplineWeight + CollectionCoverageWeight + ExposureWeight == 1m && GradeA >= GradeB && GradeB >= GradeC && GradeC >= GradeD && GradeD >= 0 && MinimumSaleCount > 0 && MinimumHistoryDays > 0;
}
