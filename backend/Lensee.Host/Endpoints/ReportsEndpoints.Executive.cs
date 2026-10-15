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
        DateOnly? date,
        DateOnly? from,
        DateOnly? to,
        int? page,
        int? pageSize,
        OperationsDbContext operations,
        PaymentsDbContext paymentsDb,
        FinanceDbContext finance,
        CatalogDbContext catalog,
        IClock clock,
        CancellationToken ct)
    {
        var requested = date ?? DateOnly.FromDateTime(clock.EgyptNow);
        var start = from ?? requested;
        var end = to ?? start;
        if (start > end) return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = ["From must be earlier than or equal to To."] });
        var normalizedPage = Math.Max(1, page ?? 1);
        var normalizedPageSize = Math.Clamp(pageSize ?? 25, 1, 100);
        var endExclusive = end.AddDays(1);
        var startTime = start.ToDateTime(TimeOnly.MinValue);
        var endTime = endExclusive.ToDateTime(TimeOnly.MinValue);

        var ledger = await finance.FinanceLedgerEntries.AsNoTracking()
            .Where(value => value.Status == "Posted" && value.BusinessDate >= start && value.BusinessDate < endExclusive)
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
        var cLevelLedger = await finance.FinanceLedgerEntries.AsNoTracking()
            .Where(value => value.Status == "Posted" &&
                (value.SourceType == "CLevelWithdrawal" || value.SourceType == "CLevelWithdrawalReversal" ||
                 value.SourceType == "CLevelWithdrawalRepayment" || value.SourceType == "CLevelWithdrawalRepaymentReversal"))
            .Select(value => new { value.SourceType, value.Direction, value.Amount })
            .ToListAsync(ct);
        var cLevelWithdrawn = cLevelLedger.Where(value => value.Direction == "Debit" && value.SourceType == "CLevelWithdrawal").Sum(value => value.Amount)
            - cLevelLedger.Where(value => value.Direction == "Credit" && value.SourceType == "CLevelWithdrawalReversal").Sum(value => value.Amount);
        var cLevelRepaid = cLevelLedger.Where(value => value.Direction == "Credit" && value.SourceType == "CLevelWithdrawalRepayment").Sum(value => value.Amount)
            - cLevelLedger.Where(value => value.Direction == "Debit" && value.SourceType == "CLevelWithdrawalRepaymentReversal").Sum(value => value.Amount);
        var cLevelWithdrawalOutstanding = cLevelWithdrawn - cLevelRepaid;

        var merchantObligations = await paymentsDb.MerchantOperationObligations.AsNoTracking()
            .Where(value => value.Status != "Settled" && value.Status != "Reversed")
            .Select(value => new { value.Id, value.OriginalAmount })
            .ToListAsync(ct);
        var obligationIds = merchantObligations.Select(value => value.Id).ToArray();
        var effectiveAllocations = await paymentsDb.EffectiveAllocations().Where(value => obligationIds.Contains(value.ObligationId))
            .GroupBy(value => value.ObligationId).Select(group => new { Id = group.Key, Amount = group.Sum(value => value.Amount) }).ToListAsync(ct);
        var allocated = effectiveAllocations.ToDictionary(value => value.Id, value => value.Amount);
        var merchantReceivables = merchantObligations.Sum(value => Math.Max(0m, value.OriginalAmount - allocated.GetValueOrDefault(value.Id)));

        var operationRows = await operations.OperationLogs.AsNoTracking().Include(value => value.OperationLines)
            .Where(value => !value.IsDeleted && value.ConfirmedAt >= startTime && value.ConfirmedAt < endTime &&
                 ((value.OperationType == "WholesaleSale" && value.Status == "Completed") ||
                 (value.OperationType == "RetailSale" &&
                    (value.Status == "Completed" ||
                     ((value.SalesChannel == "Online" || value.SalesChannel == "Shopify") && value.Status == "Delivered"))) ||
                 (value.OperationType == "Return" && value.Status == "Confirmed")))
            .ToListAsync(ct);
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
        var nonBonusUnits = 0;
        var bonusUnits = 0;
        decimal netSales = 0m;
        foreach (var operation in operationRows)
        {
            var sign = operation.RecordKind == "Reversal" ? -1 : 1;
            if (operation.OperationType is "WholesaleSale" or "RetailSale")
            {
                netSales += sign * (operation.OperationLines.Sum(value => value.LineTotal) +
  (operation.OperationType == "RetailSale" && (operation.SalesChannel == "Online" || operation.SalesChannel == "Shopify") ? operation.CodCharge : 0m));
                nonBonusUnits += sign * operation.OperationLines.Where(value => value.BonusQuantity == 0).Sum(value => Pieces(value, value.Quantity));
                bonusUnits += sign * operation.OperationLines.Where(value => value.BonusQuantity > 0).Sum(value => Pieces(value, value.BonusQuantity));
            }
            else if (operation.OperationType == "Return")
            {
                netSales -= sign * operation.OperationLines.Sum(value => value.LineTotal);
                nonBonusUnits -= sign * operation.OperationLines.Where(value => value.BonusQuantity == 0).Sum(value => Pieces(value, value.Quantity));
                bonusUnits -= sign * operation.OperationLines.Where(value => value.BonusQuantity > 0).Sum(value => Pieces(value, value.BonusQuantity));
            }
        }

        var collections = ledger.Where(value => value.Direction == "Credit" && value.SourceType is ("MerchantCollection" or "CashRecord" or "InstallmentSubLog")).Sum(value => value.Amount);
        var expenses = ledger.Where(value => value.Direction == "Debit" && value.SourceType == "FinanceExpense").Sum(value => value.Amount)
            - ledger.Where(value => value.Direction == "Credit" && value.SourceType == "FinanceExpenseReversal").Sum(value => value.Amount);
        var withdrawals = ledger.Where(value => value.Direction == "Debit" && value.SourceType == "CLevelWithdrawal").Sum(value => value.Amount)
            - ledger.Where(value => value.Direction == "Credit" && value.SourceType == "CLevelWithdrawalReversal").Sum(value => value.Amount);
        var repayments = ledger.Where(value => value.Direction == "Credit" && value.SourceType == "CLevelWithdrawalRepayment").Sum(value => value.Amount)
            - ledger.Where(value => value.Direction == "Debit" && value.SourceType == "CLevelWithdrawalRepaymentReversal").Sum(value => value.Amount);
        var supplyPayments = ledger.Where(value => value.Direction == "Debit" &&
                (value.SourceType == "SupplyPayment" || value.SourceType == "SupplySupplierInstallment"))
            .Sum(value => value.Amount)
            - ledger.Where(value => value.Direction == "Credit" &&
                (value.SourceType == "SupplyPaymentReversal" || value.SourceType == "SupplySupplierInstallmentReversal"))
            .Sum(value => value.Amount);
        var externalMovement = ledger.Where(value => value.SourceType is not ("FinanceTransferIn" or "FinanceTransferOut" or
            "FinanceOpeningBalance" or "FinanceOpeningBalanceReversal"))
            .Sum(value => value.Direction == "Credit" ? value.Amount : -value.Amount);
        var operationTotal = operationRows.Count;
        var pagedOperations = operationRows.OrderByDescending(value => value.ConfirmedAt).ThenByDescending(value => value.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize).Take(normalizedPageSize)
            .Select(value => new { value.Id, value.OperationNumber, value.OperationType, value.Status, value.ConfirmedAt, total = value.OperationLines.Sum(line => line.LineTotal) })
            .ToArray();
        var paymentRows = await paymentsDb.MainPaymentLogs.AsNoTracking()
            .Where(value => !value.IsDeleted && value.InitializedAt >= startTime && value.InitializedAt < endTime)
            .OrderByDescending(value => value.InitializedAt).ThenByDescending(value => value.Id)
            .Skip((normalizedPage - 1) * normalizedPageSize).Take(normalizedPageSize)
            .Select(value => new { value.Id, value.OperationId, value.TotalAmount, value.AmountPaid, value.Status, value.InitializedAt })
            .ToArrayAsync(ct);
        return Results.Ok(new
        {
            start,
            end,
            endExclusive,
            page = normalizedPage,
            pageSize = normalizedPageSize,
            operationTotal,
            operationTotalPages = (int)Math.Ceiling(operationTotal / (double)normalizedPageSize),
            operations = pagedOperations,
            payments = paymentRows,
            currentBalances = new { asOf = DateOnly.FromDateTime(clock.EgyptNow), cash, bank, wallet, availableFunds = cash + bank + wallet, merchantReceivables, cLevelWithdrawalOutstanding },
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
            units = new { nonBonusPiecesSold = nonBonusUnits, paidPiecesSold = nonBonusUnits, bonusPieces = bonusUnits, estimated, unresolvedPackLines }
        });
    }
}
