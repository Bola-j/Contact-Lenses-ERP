using Lensee.Modules.Payments.Data;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

/// <summary>
/// The single source of financial capacity while a main payment log is locked.
/// Callers must lock the payment log before invoking these queries.
/// </summary>
public static class PaymentFinancialCapacity
{
    private const string Completed = "Completed";
    private const string Confirmed = "Confirmed";
    private const string CashReceived = "CashReceived";
    private const string CashRefund = "CashRefund";
    private const string AdditionalCharge = "AdditionalCharge";
    private const string PendingApproval = "PendingApproval";

    public static Task<decimal> CompletedInstallmentsAsync(
        PaymentsDbContext paymentsDbContext,
        MainPaymentLog paymentLog,
        CancellationToken cancellationToken) =>
        paymentsDbContext.InstallmentSubLogs
            .Where(value => value.MainLogId == paymentLog.Id && value.SubLogStatus == Confirmed)
            .SumAsync(value => value.Amount, cancellationToken);

    public static Task<decimal> CompletedCashReceivedAsync(
        PaymentsDbContext paymentsDbContext,
        MainPaymentLog paymentLog,
        CancellationToken cancellationToken) =>
        paymentsDbContext.CashRecords
            .Where(value => value.OperationId == paymentLog.OperationId && value.Status == Completed && value.PaymentType == CashReceived)
            .SumAsync(value => value.Amount, cancellationToken);

    public static Task<decimal> CompletedCashRefundedAsync(
        PaymentsDbContext paymentsDbContext,
        MainPaymentLog paymentLog,
        CancellationToken cancellationToken) =>
        paymentsDbContext.CashRecords
            .Where(value => value.OperationId == paymentLog.OperationId && value.Status == Completed && value.PaymentType == CashRefund)
            .SumAsync(value => value.Amount, cancellationToken);

    public static async Task<decimal> FinalizedPaidValueAsync(
        PaymentsDbContext paymentsDbContext,
        MainPaymentLog paymentLog,
        CancellationToken cancellationToken) =>
        await CompletedInstallmentsAsync(paymentsDbContext, paymentLog, cancellationToken) +
        await CompletedCashReceivedAsync(paymentsDbContext, paymentLog, cancellationToken);

    public static async Task<decimal> CashRefundCapacityAsync(
        PaymentsDbContext paymentsDbContext,
        MainPaymentLog paymentLog,
        Guid? excludingAdjustmentId,
        CancellationToken cancellationToken)
    {
        var pendingRefunds = await PendingCashRefundsAsync(
            paymentsDbContext,
            paymentLog,
            excludingAdjustmentId,
            cancellationToken);
        var completedRefunds = await CompletedCashRefundedAsync(paymentsDbContext, paymentLog, cancellationToken);
        var cashCapacity = await CompletedCashReceivedAsync(paymentsDbContext, paymentLog, cancellationToken) -
            completedRefunds - pendingRefunds;
        var totalSettlementCapacity = await FinalizedPaidValueAsync(paymentsDbContext, paymentLog, cancellationToken) -
            completedRefunds - pendingRefunds;

        return Math.Max(Math.Min(cashCapacity, totalSettlementCapacity), 0);
    }

    public static async Task<decimal> BalanceReductionCapacityAsync(
        PaymentsDbContext paymentsDbContext,
        MainPaymentLog paymentLog,
        Guid? excludingAdjustmentId,
        CancellationToken cancellationToken)
    {
        var pendingReductions = await PendingBalanceReductionsAsync(
            paymentsDbContext,
            paymentLog,
            excludingAdjustmentId,
            cancellationToken);
        var completedRefunds = await CompletedCashRefundedAsync(paymentsDbContext, paymentLog, cancellationToken);
        var pendingRefunds = await PendingCashRefundsAsync(
            paymentsDbContext,
            paymentLog,
            excludingAdjustmentId,
            cancellationToken);

        var additionalCharges = await CompletedAdditionalChargesAsync(paymentsDbContext, paymentLog, excludingAdjustmentId, cancellationToken);
        return Math.Max(paymentLog.TotalAmount + additionalCharges -
            await FinalizedPaidValueAsync(paymentsDbContext, paymentLog, cancellationToken) +
            completedRefunds - pendingReductions - pendingRefunds, 0);
    }

    private static Task<decimal> PendingCashRefundsAsync(
        PaymentsDbContext paymentsDbContext,
        MainPaymentLog paymentLog,
        Guid? excludingAdjustmentId,
        CancellationToken cancellationToken) =>
        paymentsDbContext.FinancialAdjustments
            .Where(adjustment => adjustment.PaymentLogId == paymentLog.Id &&
                adjustment.Id != excludingAdjustmentId &&
                adjustment.AdjustmentType == CashRefund &&
                (adjustment.Status == PendingApproval || adjustment.Status == "Approved"))
            .SumAsync(adjustment => adjustment.Amount, cancellationToken);

    private static Task<decimal> CompletedAdditionalChargesAsync(
        PaymentsDbContext paymentsDbContext,
        MainPaymentLog paymentLog,
        Guid? excludingAdjustmentId,
        CancellationToken cancellationToken) =>
        paymentsDbContext.FinancialAdjustments
            .Where(adjustment => adjustment.PaymentLogId == paymentLog.Id &&
                adjustment.Id != excludingAdjustmentId &&
                (adjustment.AdjustmentType == AdditionalCharge || adjustment.AdjustmentType == "MerchantCredit") &&
                adjustment.Status == Completed)
            .SumAsync(adjustment => adjustment.Amount, cancellationToken);

    private static Task<decimal> PendingBalanceReductionsAsync(
        PaymentsDbContext paymentsDbContext,
        MainPaymentLog paymentLog,
        Guid? excludingAdjustmentId,
        CancellationToken cancellationToken) =>
        paymentsDbContext.FinancialAdjustments
            .Where(adjustment => adjustment.PaymentLogId == paymentLog.Id &&
                adjustment.Id != excludingAdjustmentId &&
                adjustment.AdjustmentType == "BalanceReduction" &&
                adjustment.Status == PendingApproval)
            .SumAsync(adjustment => adjustment.Amount, cancellationToken);
}
