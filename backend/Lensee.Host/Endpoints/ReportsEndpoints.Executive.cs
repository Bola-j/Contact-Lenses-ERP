using Lensee.Host.Infrastructure;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Finance.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static partial class ReportsEndpoints
{
    private static async Task<IResult> GetExecutiveSummaryAsync(
        string? period,
        DateOnly? date,
        OperationsDbContext operations,
        PaymentsDbContext payments,
        FinanceDbContext finance,
        CatalogDbContext catalog,
        IClock clock,
        CancellationToken ct)
    {
        if (period is not ("daily" or "monthly")) return Results.BadRequest(new { code = "period-must-be-daily-or-monthly" });
        var requested = date ?? DateOnly.FromDateTime(clock.EgyptNow);
        var start = period == "monthly" ? new DateOnly(requested.Year, requested.Month, 1) : requested;
        var end = period == "monthly" ? start.AddMonths(1) : start.AddDays(1);
        var startTime = start.ToDateTime(TimeOnly.MinValue);
        var endTime = end.ToDateTime(TimeOnly.MinValue);

        var ledger = await finance.FinanceLedgerEntries.AsNoTracking()
            .Where(value => value.Status == "Posted" && value.BusinessDate >= start && value.BusinessDate < end)
            .Select(value => new { value.SourceType, value.Category, value.Direction, value.Amount })
            .ToListAsync(ct);
        var balances = await finance.FinanceAccounts.AsNoTracking().Select(account => new
        {
            account.Type,
            Balance = finance.FinanceLedgerEntries.Where(entry => entry.FinanceAccountId == account.Id && entry.Status == "Posted")
                .Sum(entry => (decimal?)(entry.Direction == FinanceLedgerService.Credit ? entry.Amount : -entry.Amount)) ?? 0m
        }).ToListAsync(ct);
        var cash = balances.Where(value => value.Type == FinanceLedgerService.CashOnHand).Sum(value => value.Balance);
        var bank = balances.Where(value => value.Type == FinanceLedgerService.BankAccount).Sum(value => value.Balance);
        var wallet = balances.Where(value => value.Type == FinanceLedgerService.Wallet).Sum(value => value.Balance);

        var merchantObligations = await payments.MerchantOperationObligations.AsNoTracking()
            .Where(value => value.Status != "Settled" && value.Status != "Reversed")
            .Select(value => new { value.Id, value.OriginalAmount })
            .ToListAsync(ct);
        var obligationIds = merchantObligations.Select(value => value.Id).ToArray();
        var effectiveAllocations = await payments.EffectiveAllocations().Where(value => obligationIds.Contains(value.ObligationId))
            .GroupBy(value => value.ObligationId).Select(group => new { Id = group.Key, Amount = group.Sum(value => value.Amount) }).ToListAsync(ct);
        var allocated = effectiveAllocations.ToDictionary(value => value.Id, value => value.Amount);
        var merchantReceivables = merchantObligations.Sum(value => Math.Max(0m, value.OriginalAmount - allocated.GetValueOrDefault(value.Id)));

        var operationRows = await operations.OperationLogs.AsNoTracking().Include(value => value.OperationLines)
            .Where(value => !value.IsDeleted && value.ConfirmedAt >= startTime && value.ConfirmedAt < endTime &&
                (((value.OperationType == "WholesaleSale" || value.OperationType == "RetailSale") && value.Status == "Completed") ||
                 ((value.OperationType == "Return" || value.OperationType == "Change") && value.Status == "Confirmed")))
            .ToListAsync(ct);
        var operationIds = operationRows.Select(operation => operation.Id).ToArray();
        var paidSaleIds = await payments.MainPaymentLogs.AsNoTracking()
            .Where(value => !value.IsDeleted && value.TotalAmount > 0 && value.AmountPaid >= value.TotalAmount &&
                operationIds.Contains(value.OperationId))
            .Select(value => value.OperationId).Distinct().ToListAsync(ct);
        var paidSales = paidSaleIds.ToHashSet();
        var skuIds = operationRows.SelectMany(value => value.OperationLines).Where(value => value.EntryMode == "Packs" && value.PiecesPerPackSnapshot == null)
            .Select(value => value.SkuId).Distinct().ToArray();
        var currentRatios = await catalog.Skus.AsNoTracking().Where(value => skuIds.Contains(value.Id))
            .Select(value => new { value.Id, value.Product.PiecesPerPack }).ToDictionaryAsync(value => value.Id, value => value.PiecesPerPack, ct);
        var estimated = false;
        var unresolvedPackLines = 0;
        int Pieces(OperationLine line, int quantity)
        {
            if (line.EntryMode != "Packs") return quantity;
            if (line.PiecesPerPackSnapshot is int saved && saved > 0) return checked(quantity * saved);
            estimated = true;
            var ratio = currentRatios.GetValueOrDefault(line.SkuId);
            if (ratio is not > 0) { unresolvedPackLines++; return 0; }
            return checked(quantity * ratio.Value);
        }
        var paidUnits = 0;
        var bonusUnits = 0;
        decimal netSales = 0m;
        foreach (var operation in operationRows)
        {
            var sign = operation.RecordKind == "Reversal" ? -1 : 1;
            if (operation.OperationType is "WholesaleSale" or "RetailSale")
            {
                netSales += sign * operation.OperationLines.Sum(value => value.LineTotal);
                if (!paidSales.Contains(operation.Id)) continue;
                paidUnits += sign * operation.OperationLines.Where(value => value.BonusQuantity == 0).Sum(value => Pieces(value, value.Quantity));
                bonusUnits += sign * operation.OperationLines.Where(value => value.BonusQuantity > 0).Sum(value => Pieces(value, value.BonusQuantity));
            }
            else if (operation.OperationType == "Return")
            {
                netSales -= sign * operation.OperationLines.Sum(value => value.LineTotal);
                paidUnits -= sign * operation.OperationLines.Where(value => value.BonusQuantity == 0).Sum(value => Pieces(value, value.Quantity));
                bonusUnits -= sign * operation.OperationLines.Where(value => value.BonusQuantity > 0).Sum(value => Pieces(value, value.BonusQuantity));
            }
            else
            {
                var incoming = operation.OperationLines.Where(value => value.Section == "ChangeIn").ToArray();
                var outgoing = operation.OperationLines.Where(value => value.Section == "ChangeOut").ToArray();
                netSales += sign * (incoming.Sum(value => value.LineTotal) - outgoing.Sum(value => value.LineTotal));
                paidUnits += sign * (incoming.Where(value => value.BonusQuantity == 0).Sum(value => Pieces(value, value.Quantity)) -
                                     outgoing.Where(value => value.BonusQuantity == 0).Sum(value => Pieces(value, value.Quantity)));
            }
        }

        var collections = ledger.Where(value => value.Direction == "Credit" && value.SourceType is ("MerchantCollection" or "CashRecord" or "InstallmentSubLog")).Sum(value => value.Amount);
        var expenses = ledger.Where(value => value.Direction == "Debit" && value.SourceType == "FinanceExpense").Sum(value => value.Amount)
            - ledger.Where(value => value.Direction == "Credit" && value.SourceType == "FinanceExpenseReversal").Sum(value => value.Amount);
        var withdrawals = ledger.Where(value => value.Direction == "Debit" && value.SourceType == "CLevelWithdrawal").Sum(value => value.Amount)
            - ledger.Where(value => value.Direction == "Credit" && value.SourceType == "CLevelWithdrawalReversal").Sum(value => value.Amount);
        var repayments = ledger.Where(value => value.Direction == "Credit" && value.SourceType == "CLevelWithdrawalRepayment").Sum(value => value.Amount)
            - ledger.Where(value => value.Direction == "Debit" && value.SourceType == "CLevelWithdrawalRepaymentReversal").Sum(value => value.Amount);
        var supplyPayments = ledger.Where(value => value.Direction == "Debit" && value.SourceType == "SupplyPayment").Sum(value => value.Amount)
            - ledger.Where(value => value.Direction == "Credit" && value.SourceType == "SupplyPaymentReversal").Sum(value => value.Amount);
        var externalMovement = ledger.Where(value => value.SourceType is not ("FinanceTransferIn" or "FinanceTransferOut" or
            "FinanceOpeningBalance" or "FinanceOpeningBalanceReversal"))
            .Sum(value => value.Direction == "Credit" ? value.Amount : -value.Amount);
        return Results.Ok(new
        {
            period,
            start,
            endExclusive = end,
            currentBalances = new { asOf = DateOnly.FromDateTime(clock.EgyptNow), cash, bank, wallet, availableFunds = cash + bank + wallet, merchantReceivables },
            flows = new
            {
                netSales,
                actualCollections = collections,
                paidExpenses = expenses,
                withdrawals,
                repayments,
                postedSupplyPayments = supplyPayments,
                netExternalCashMovement = externalMovement
            },
            units = new { paidPiecesSold = paidUnits, bonusPieces = bonusUnits, estimated, unresolvedPackLines }
        });
    }
}
