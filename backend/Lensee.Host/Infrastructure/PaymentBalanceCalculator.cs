using Lensee.Modules.Payments.Data;

namespace Lensee.Host.Infrastructure;

/// <summary>
/// The canonical balance for one operation payment.  Persisted payment totals are
/// collection aggregates; this projection deliberately derives the financial
/// position from immutable payment events and approved adjustments.
/// </summary>
public static class PaymentBalanceCalculator
{
    public const string Completed = "Completed";
    public const string Confirmed = "Confirmed";
    public const string Draft = "Draft";
    public const string PendingAccountant = "PendingAccountant";
    public const string CashReceived = "CashReceived";
    public const string CashRefund = "CashRefund";
    public const string AdditionalCharge = "AdditionalCharge";
    public const string LegacyMerchantCredit = "MerchantCredit";
    public const string BalanceReduction = "BalanceReduction";

    public static PaymentBalanceSnapshot Calculate(
        MainPaymentLog log,
        IEnumerable<FinancialAdjustment> adjustments,
        IEnumerable<CashRecord> cashRecords)
    {
        var linkedAdjustments = adjustments
            .Where(value => value.PaymentLogId == log.Id || value.OperationId == log.OperationId)
            .ToArray();
        var completedAdjustments = linkedAdjustments
            .Where(value => value.Status == Completed)
            .ToArray();
        var operationCash = cashRecords.Where(value => value.OperationId == log.OperationId).ToArray();

        var additionalCharges = completedAdjustments
            .Where(value => value.AdjustmentType is AdditionalCharge or LegacyMerchantCredit)
            .Sum(value => value.Amount);
        var balanceReductions = completedAdjustments
            .Where(value => value.AdjustmentType == BalanceReduction)
            .Sum(value => value.Amount);
        var confirmedInstallments = log.InstallmentSubLogs
            .Where(value => value.SubLogStatus == Confirmed)
            .Sum(value => value.Amount);
        var pendingInstallments = log.InstallmentSubLogs
            .Where(value => value.SubLogStatus == Draft)
            .Sum(value => value.Amount);
        var confirmedCash = operationCash
            .Where(value => value.Status == Completed && value.PaymentType == CashReceived)
            .Sum(value => value.Amount);
        var pendingCash = operationCash
            .Where(value => value.Status == PendingAccountant && value.PaymentType == CashReceived)
            .Sum(value => value.Amount);
        var completedRefunds = operationCash
            .Where(value => value.Status == Completed && value.PaymentType == CashRefund)
            .Sum(value => value.Amount);

        var adjustedAmount = Math.Max(log.TotalAmount + additionalCharges - balanceReductions, 0m);
        var confirmedCollections = confirmedInstallments + confirmedCash;
        var rawPosition = adjustedAmount - confirmedCollections + completedRefunds;

        return new PaymentBalanceSnapshot(
            log.TotalAmount,
            additionalCharges,
            balanceReductions,
            adjustedAmount,
            confirmedCollections,
            pendingInstallments + pendingCash,
            completedRefunds,
            Math.Max(rawPosition, 0m),
            Math.Max(-rawPosition, 0m));
    }
}

public sealed record PaymentBalanceSnapshot(
    decimal OriginalAmount,
    decimal AdditionalCharges,
    decimal BalanceReductions,
    decimal AdjustedAmount,
    decimal ConfirmedCollections,
    decimal PendingCollections,
    decimal CompletedRefunds,
    decimal RemainingAmount,
    decimal RefundDue);
