using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;

namespace Lensee.Host.Infrastructure;

/// <summary>
/// Canonical receivable projection shared by merchant balances and aggregate
/// financial reporting.  This is deliberately an in-memory projection: callers
/// load the records using the appropriate tenant/merchant scope first, then all
/// consumers apply exactly the same sign and status rules.
/// </summary>
public static class FinancialProjection
{
    private const string WholesaleSale = "WholesaleSale";
    private const string RetailSale = "RetailSale";
    private const string Return = "Return";
    private const string Change = "Change";
    private const string ChangeOut = "ChangeOut";
    private const string ChangeIn = "ChangeIn";
    private const string Reversal = "Reversal";
    private const string Completed = "Completed";
    private const string Confirmed = "Confirmed";
    private const string CashReceived = "CashReceived";
    private const string CashRefund = "CashRefund";
    private const string MerchantCredit = "MerchantCredit";
    private const string BalanceReduction = "BalanceReduction";

    public static FinancialProjectionSnapshot Calculate(
        IEnumerable<OperationLog> operations,
        IEnumerable<InstallmentSubLog> installmentSubLogs,
        IEnumerable<CashRecord> cashRecords,
        IEnumerable<FinancialAdjustment> adjustments)
    {
        var operationList = operations.Where(operation => !operation.IsDeleted).ToArray();
        var operationEffects = operationList
            .Select(operation => new
            {
                Operation = operation,
                Effect = OperationEffect(operation)
            })
            .ToArray();
        var effectiveOperationIds = operationEffects
            .Where(value => value.Effect != 0m)
            .Select(value => value.Operation.Id)
            .ToHashSet();

        var saleTotal = operationEffects
            .Where(value => value.Operation.OperationType is WholesaleSale or RetailSale)
            .Sum(value => value.Effect);
        var returnTotal = operationEffects
            .Where(value => value.Operation.OperationType == Return)
            // ReturnTotal is exposed as a positive category amount for normal
            // returns. A return reversal therefore reduces the category total.
            .Sum(value => -value.Effect);
        var changeNet = operationEffects
            .Where(value => value.Operation.OperationType == Change)
            .Sum(value => value.Effect);

        var confirmedSubLogTotal = installmentSubLogs
            .Where(subLog => subLog.SubLogStatus == Confirmed && effectiveOperationIds.Contains(subLog.MainLog.OperationId))
            .Sum(subLog => subLog.Amount);
        var completedCashRecords = cashRecords
            .Where(record => record.Status == Completed && effectiveOperationIds.Contains(record.OperationId))
            .ToArray();
        var cashReceived = completedCashRecords
            .Where(record => record.PaymentType == CashReceived)
            .Sum(record => record.Amount);
        var cashRefunded = completedCashRecords
            .Where(record => record.PaymentType == CashRefund)
            .Sum(record => record.Amount);
        var completedAdjustments = adjustments
            .Where(adjustment => adjustment.Status == Completed)
            .ToArray();
        var merchantCredits = completedAdjustments
            .Where(adjustment => adjustment.AdjustmentType == MerchantCredit)
            .Sum(adjustment => adjustment.Amount);
        var balanceReductions = completedAdjustments
            .Where(adjustment => adjustment.AdjustmentType == BalanceReduction)
            .Sum(adjustment => adjustment.Amount);

        var paymentsReceived = confirmedSubLogTotal + cashReceived;
        // A refund reverses a previously collected receipt, so it increases the
        // amount still owed even though PaymentsReceived remains gross receipts.
        var balance = saleTotal + changeNet - returnTotal
            - paymentsReceived + cashRefunded - merchantCredits - balanceReductions;

        return new FinancialProjectionSnapshot(
            saleTotal,
            returnTotal,
            changeNet,
            paymentsReceived,
            cashRefunded,
            merchantCredits,
            balanceReductions,
            balance);
    }

    public static decimal OperationEffect(OperationLog operation)
    {
        if (operation.IsDeleted || !IsFinanciallyEffective(operation))
        {
            return 0m;
        }

        var normalEffect = operation.OperationType switch
        {
            WholesaleSale or RetailSale => operation.OperationLines.Sum(line => line.LineTotal),
            Return => -operation.OperationLines.Sum(line => line.LineTotal),
            Change => operation.OperationLines.Where(line => line.Section == ChangeIn).Sum(line => line.LineTotal)
                - operation.OperationLines.Where(line => line.Section == ChangeOut).Sum(line => line.LineTotal),
            _ => 0m
        };

        return string.Equals(operation.RecordKind, Reversal, StringComparison.OrdinalIgnoreCase)
            ? -normalEffect
            : normalEffect;
    }

    private static bool IsFinanciallyEffective(OperationLog operation) =>
        operation.OperationType switch
        {
            WholesaleSale or RetailSale => operation.Status == Completed,
            Return or Change => operation.Status == Confirmed,
            _ => false
        };
}

public sealed record FinancialProjectionSnapshot(
    decimal SaleTotal,
    decimal ReturnTotal,
    decimal ChangeNet,
    decimal PaymentsReceived,
    decimal CashRefunded,
    decimal MerchantCredits,
    decimal BalanceReductions,
    decimal Balance)
{
    public decimal OperationNet => SaleTotal + ChangeNet - ReturnTotal;
}
