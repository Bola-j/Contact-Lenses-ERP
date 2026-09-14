using Lensee.Modules.Payments.Data;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

public sealed class MerchantAccountService
{
    private readonly PaymentsDbContext _paymentsDbContext;

    public MerchantAccountService(PaymentsDbContext paymentsDbContext)
    {
        _paymentsDbContext = paymentsDbContext;
    }

    public async Task<MerchantAccountSnapshot> GetSnapshotAsync(Guid merchantId, CancellationToken cancellationToken)
    {
        var breakdown = await GetFinancialBreakdownAsync(merchantId, cancellationToken);
        return new MerchantAccountSnapshot(
            Math.Max(breakdown.Balance, 0m),
            Math.Max(-breakdown.Balance, 0m),
            PendingCollections: 0m,
            ReservedRefunds: 0m);
    }

    public async Task<MerchantAccountFinancialBreakdown> GetFinancialBreakdownAsync(Guid merchantId, CancellationToken cancellationToken)
    {
        var entries = await QueryPostedEntries(merchantId)
            .Select(entry => new
            {
                entry.EntryType,
                entry.DebitAmount,
                entry.CreditAmount
            })
            .ToListAsync(cancellationToken);

        return BuildBreakdown(entries.Select(entry => new AccountAmount(entry.EntryType, entry.DebitAmount, entry.CreditAmount)));
    }

    public async Task<IReadOnlyDictionary<Guid, MerchantAccountFinancialBreakdown>> GetFinancialBreakdownsAsync(IReadOnlyCollection<Guid> merchantIds, CancellationToken cancellationToken)
    {
        if (merchantIds.Count == 0)
        {
            return new Dictionary<Guid, MerchantAccountFinancialBreakdown>();
        }

        var idSet = merchantIds.ToHashSet();
        var entries = await _paymentsDbContext.MerchantAccountEntries
            .AsNoTracking()
            .Where(entry => entry.Status == "Posted" && idSet.Contains(entry.Account.MerchantId))
            .Select(entry => new
            {
                entry.Account.MerchantId,
                entry.EntryType,
                entry.DebitAmount,
                entry.CreditAmount
            })
            .ToListAsync(cancellationToken);

        return entries
            .GroupBy(entry => entry.MerchantId)
            .ToDictionary(
                group => group.Key,
                group => BuildBreakdown(group.Select(entry => new AccountAmount(entry.EntryType, entry.DebitAmount, entry.CreditAmount))));
    }

    public async Task<IReadOnlyList<MerchantAccountStatementEntry>> GetStatementAsync(
        Guid merchantId,
        int limit,
        CancellationToken cancellationToken,
        DateTime? from = null,
        DateTime? to = null)
    {
        var upperBound = NormalizeExclusiveUpperBound(to);
        var entries = await QueryPostedEntries(merchantId)
            .Where(entry => (!from.HasValue || entry.PostedAt >= from.Value) && (!upperBound.HasValue || entry.PostedAt < upperBound.Value))
            .OrderBy(entry => entry.PostedAt)
            .ThenBy(entry => entry.Sequence)
            .Take(limit)
            .Select(entry => new
            {
                entry.Id,
                entry.EntryType,
                entry.DebitAmount,
                entry.CreditAmount,
                entry.PaymentMethod,
                entry.OperationId,
                entry.PaymentId,
                entry.PostedBy,
                entry.PostedAt
            })
            .ToListAsync(cancellationToken);

        var openingBalance = await GetOpeningBalanceAsync(merchantId, from, cancellationToken);
        var runningBalance = openingBalance;
        return entries.Select(entry =>
        {
            runningBalance += entry.DebitAmount - entry.CreditAmount;
            return new MerchantAccountStatementEntry(
                entry.Id,
                entry.EntryType,
                entry.DebitAmount,
                entry.CreditAmount,
                entry.PaymentMethod,
                entry.OperationId,
                entry.PaymentId,
                entry.PostedBy,
                entry.PostedAt,
                runningBalance);
        }).ToList();
    }

    public Task<decimal> GetOpeningBalanceAsync(Guid merchantId, DateTime? from, CancellationToken cancellationToken)
    {
        if (!from.HasValue)
        {
            return Task.FromResult(0m);
        }

        return QueryPostedEntries(merchantId)
            .Where(entry => entry.PostedAt < from.Value)
            .SumAsync(entry => entry.DebitAmount - entry.CreditAmount, cancellationToken);
    }

    public Task<decimal> GetClosingBalanceAsync(Guid merchantId, DateTime? to, CancellationToken cancellationToken)
    {
        var upperBound = NormalizeExclusiveUpperBound(to);
        var query = QueryPostedEntries(merchantId);
        if (upperBound.HasValue)
        {
            query = query.Where(entry => entry.PostedAt < upperBound.Value);
        }

        return query.SumAsync(entry => entry.DebitAmount - entry.CreditAmount, cancellationToken);
    }

    public Task PostSaleAsync(Guid merchantId, Guid operationId, decimal amount, Guid postedBy, DateTime postedAt, string? notes, CancellationToken cancellationToken)
    {
        return PostEntryAsync(merchantId, operationId, paymentId: null, "Sale", debitAmount: amount, creditAmount: 0m, paymentMethod: "MerchantAccount", postedBy, postedAt, notes, cancellationToken);
    }

    public Task PostDebitForOperationAsync(Guid merchantId, Guid operationId, decimal amount, string entryType, Guid postedBy, DateTime postedAt, string? notes, CancellationToken cancellationToken)
    {
        return PostEntryAsync(merchantId, operationId, paymentId: null, entryType, debitAmount: amount, creditAmount: 0m, paymentMethod: "MerchantAccount", postedBy, postedAt, notes, cancellationToken);
    }

    public Task PostCreditForOperationAsync(Guid merchantId, Guid operationId, decimal amount, string entryType, Guid postedBy, DateTime postedAt, string? notes, CancellationToken cancellationToken)
    {
        return PostEntryAsync(merchantId, operationId, paymentId: null, entryType, debitAmount: 0m, creditAmount: amount, paymentMethod: "MerchantAccount", postedBy, postedAt, notes, cancellationToken);
    }

    private async Task PostEntryAsync(
        Guid merchantId,
        Guid operationId,
        Guid? paymentId,
        string entryType,
        decimal debitAmount,
        decimal creditAmount,
        string? paymentMethod,
        Guid postedBy,
        DateTime postedAt,
        string? notes,
        CancellationToken cancellationToken)
    {
        if (debitAmount <= 0m && creditAmount <= 0m)
        {
            return;
        }

        var account = await _paymentsDbContext.MerchantReceivableAccounts
            .FirstOrDefaultAsync(value => value.MerchantId == merchantId && value.Status == "Open", cancellationToken);
        if (account is null)
        {
            account = new MerchantReceivableAccount
            {
                Id = Guid.NewGuid(),
                MerchantId = merchantId,
                Status = "Open",
                OpenedAt = postedAt,
                UpdatedAt = postedAt,
                OpenedBy = postedBy
            };
            _paymentsDbContext.MerchantReceivableAccounts.Add(account);
        }

        _paymentsDbContext.MerchantAccountEntries.Add(new MerchantAccountEntry
        {
            Id = Guid.NewGuid(),
            Account = account,
            AccountId = account.Id,
            Sequence = account.NextSequence++,
            EntryType = entryType,
            DebitAmount = Math.Max(debitAmount, 0m),
            CreditAmount = Math.Max(creditAmount, 0m),
            PaymentMethod = paymentMethod,
            SourceType = paymentId.HasValue ? "Payment" : "Operation",
            SourceId = paymentId ?? operationId,
            OperationId = operationId,
            PaymentId = paymentId,
            Status = "Posted",
            PostedBy = postedBy,
            PostedAt = postedAt,
            Notes = notes
        });
        account.UpdatedAt = postedAt;
        await _paymentsDbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<MerchantAccountEntry> QueryPostedEntries(Guid merchantId)
    {
        return _paymentsDbContext.MerchantAccountEntries
            .AsNoTracking()
            .Where(entry => entry.Status == "Posted" && entry.Account.MerchantId == merchantId);
    }

    private static DateTime? NormalizeExclusiveUpperBound(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return value.Value.TimeOfDay == TimeSpan.Zero ? value.Value.Date.AddDays(1) : value.Value;
    }

    private static MerchantAccountFinancialBreakdown BuildBreakdown(IEnumerable<AccountAmount> entries)
    {
        decimal saleTotal = 0m;
        decimal returnTotal = 0m;
        decimal changeNet = 0m;
        decimal paymentsReceived = 0m;
        decimal cashRefunded = 0m;
        decimal additionalCharges = 0m;
        decimal balanceReductions = 0m;

        foreach (var entry in entries)
        {
            var net = entry.DebitAmount - entry.CreditAmount;
            switch (entry.EntryType)
            {
                case "Sale":
                    saleTotal += entry.DebitAmount;
                    break;
                case "ReturnCredit":
                    returnTotal += entry.CreditAmount;
                    break;
                case "ExchangeSurcharge":
                    changeNet += entry.DebitAmount;
                    additionalCharges += entry.DebitAmount;
                    break;
                case "ExchangeCredit":
                    changeNet -= entry.CreditAmount;
                    balanceReductions += entry.CreditAmount;
                    break;
                case "Collection":
                    paymentsReceived += entry.CreditAmount;
                    balanceReductions += entry.CreditAmount;
                    break;
                case "CashRefund":
                    cashRefunded += entry.DebitAmount;
                    break;
                default:
                    if (net > 0m)
                    {
                        additionalCharges += net;
                    }
                    else
                    {
                        balanceReductions += Math.Abs(net);
                    }
                    break;
            }
        }

        return new MerchantAccountFinancialBreakdown(
            saleTotal,
            returnTotal,
            changeNet,
            paymentsReceived,
            cashRefunded,
            additionalCharges,
            balanceReductions,
            saleTotal + additionalCharges + cashRefunded - returnTotal - paymentsReceived - balanceReductions);
    }

    private sealed record AccountAmount(string EntryType, decimal DebitAmount, decimal CreditAmount);
}

public sealed record MerchantAccountSnapshot(decimal AmountDue, decimal CreditAvailable, decimal PendingCollections, decimal ReservedRefunds);

public sealed record MerchantAccountFinancialBreakdown(
    decimal SaleTotal,
    decimal ReturnTotal,
    decimal ChangeNet,
    decimal PaymentsReceived,
    decimal CashRefunded,
    decimal AdditionalCharges,
    decimal BalanceReductions,
    decimal Balance);

public sealed record MerchantAccountStatementEntry(
    Guid Id,
    string EntryType,
    decimal DebitAmount,
    decimal CreditAmount,
    string? PaymentMethod,
    Guid? OperationId,
    Guid? PaymentId,
    Guid PostedBy,
    DateTime PostedAt,
    decimal RunningBalance);

public sealed class MerchantAccountReconciliationService
{
}
