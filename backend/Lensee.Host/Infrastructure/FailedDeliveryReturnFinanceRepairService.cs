using System.Text.Json;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

/// <summary>
/// Keeps a physical return of an undelivered online order separate from a
/// refund.  A failed delivery is not evidence that the company collected any
/// money; only completed payment events may make its return refundable.
/// </summary>
public static class FailedDeliveryReturnFinanceRepairService
{
    public const string RefundReviewPaymentTrack = "FailedDeliveryRefundReview";
    public const string GatewayRefundReviewPaymentTrack = "FailedDeliveryGatewayRefundReview";
    public const string ReconciliationRequiredPaymentTrack = "FailedDeliveryReconciliationRequired";
    private const string FailedDelivery = "FailedDelivery";
    private const string Completed = "Completed";
    private const string Confirmed = "Confirmed";
    private const string CashReceived = "CashReceived";

    public static async Task<decimal> GetConfirmedCollectionAsync(
        PaymentsDbContext paymentsDbContext,
        Guid sourceOperationId,
        CancellationToken cancellationToken)
    {
        var paymentLogIds = await paymentsDbContext.MainPaymentLogs.AsNoTracking()
            .Where(value => value.OperationId == sourceOperationId && !value.IsDeleted)
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);

        var confirmedCash = await paymentsDbContext.CashRecords.AsNoTracking()
            .Where(value => value.OperationId == sourceOperationId &&
                value.PaymentType == CashReceived && value.Status == Completed)
            .SumAsync(value => (decimal?)value.Amount, cancellationToken) ?? 0m;
        var confirmedInstallments = paymentLogIds.Length == 0
            ? 0m
            : await paymentsDbContext.InstallmentSubLogs.AsNoTracking()
                .Where(value => paymentLogIds.Contains(value.MainLogId) && value.SubLogStatus == Confirmed)
                .SumAsync(value => (decimal?)value.Amount, cancellationToken) ?? 0m;

        return confirmedCash + confirmedInstallments;
    }

    /// <summary>
    /// Idempotently closes historic claims created by the former unconditional
    /// entitlement writer. Rows with any settlement evidence stay untouched so
    /// no financial movement is silently reversed.
    /// </summary>
    public static async Task<int> RepairHistoricalUnpaidClaimsAsync(
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var candidates = await paymentsDbContext.ReturnEntitlements
            .Where(value => value.OriginalSaleOperationId.HasValue && value.Status != "Cancelled" &&
                value.ReservedAmount == 0m && value.SettledAmount == 0m)
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0) return 0;

        var sourceIds = candidates.Select(value => value.OriginalSaleOperationId!.Value).Distinct().ToArray();
        var failedDeliveryIds = (await operationsDbContext.OperationLogs.AsNoTracking()
            .Where(value => sourceIds.Contains(value.Id) && !value.IsDeleted && value.Status == FailedDelivery)
            .Select(value => value.Id)
            .ToListAsync(cancellationToken)).ToHashSet();
        if (failedDeliveryIds.Count == 0) return 0;

        var candidateIds = candidates.Where(value => failedDeliveryIds.Contains(value.OriginalSaleOperationId!.Value)).Select(value => value.Id).ToArray();
        var protectedIds = new HashSet<Guid>();
        if (candidateIds.Length > 0)
        {
            protectedIds.UnionWith(await paymentsDbContext.ReturnEntitlementWaivers.AsNoTracking()
                .Where(value => candidateIds.Contains(value.ReturnEntitlementId))
                .Select(value => value.ReturnEntitlementId).ToArrayAsync(cancellationToken));
            protectedIds.UnionWith(await paymentsDbContext.FinancialAdjustments.AsNoTracking()
                .Where(value => value.ReturnEntitlementId.HasValue && candidateIds.Contains(value.ReturnEntitlementId.Value))
                .Select(value => value.ReturnEntitlementId!.Value).ToArrayAsync(cancellationToken));
            protectedIds.UnionWith(await paymentsDbContext.CashRecords.AsNoTracking()
                .Where(value => value.ReturnEntitlementId.HasValue && candidateIds.Contains(value.ReturnEntitlementId.Value))
                .Select(value => value.ReturnEntitlementId!.Value).ToArrayAsync(cancellationToken));
        }

        var now = clock.EgyptNow;
        var repaired = 0;
        foreach (var entitlement in candidates.Where(value =>
                     failedDeliveryIds.Contains(value.OriginalSaleOperationId!.Value)))
        {
            if (await GetConfirmedCollectionAsync(paymentsDbContext, entitlement.OriginalSaleOperationId!.Value, cancellationToken) > 0m)
                continue;

            // Existing adjustment, cash, or waiver evidence may need a human
            // reconciliation. Do not cancel or reverse that history here.
            if (protectedIds.Contains(entitlement.Id))
            {
                if (entitlement.PaymentTrack != ReconciliationRequiredPaymentTrack)
                {
                    entitlement.PaymentTrack = ReconciliationRequiredPaymentTrack;
                    entitlement.UpdatedAt = now;
                    entitlement.UpdatedBy = Guid.Empty;
                    entitlement.Notes = AppendNote(entitlement.Notes,
                        "Manual Finance reconciliation required: linked failed delivery has no confirmed collection, but this claim already has financial workflow evidence.");
                    paymentsDbContext.PaymentAuditEvents.Add(new PaymentAuditEvent
                    {
                        Id = Guid.NewGuid(),
                        Action = "FailedDeliveryReturnEntitlementReconciliationRequired",
                        PreviousStatus = entitlement.Status,
                        NewStatus = entitlement.Status,
                        OperationId = entitlement.ReturnOperationId,
                        ActorId = Guid.Empty,
                        OccurredAt = now,
                        Amount = entitlement.NormalAmount + entitlement.ExceptionAmount,
                        Reason = "No confirmed collection exists; existing financial workflow evidence requires manual reconciliation.",
                        DataJson = JsonSerializer.Serialize(new { entitlementId = entitlement.Id, sourceOperationId = entitlement.OriginalSaleOperationId })
                    });
                    repaired++;
                }
                continue;
            }

            var previousStatus = entitlement.Status;
            entitlement.Status = "Cancelled";
            entitlement.ReservedAmount = 0m;
            entitlement.ReservedScope = null;
            entitlement.UpdatedAt = now;
            entitlement.UpdatedBy = Guid.Empty;
            entitlement.Notes = AppendNote(entitlement.Notes, "Automatically cancelled: linked failed delivery has no confirmed collection or capture.");
            paymentsDbContext.PaymentAuditEvents.Add(new PaymentAuditEvent
            {
                Id = Guid.NewGuid(),
                Action = "FailedDeliveryReturnEntitlementAutoCancelled",
                PreviousStatus = previousStatus,
                NewStatus = "Cancelled",
                OperationId = entitlement.ReturnOperationId,
                ActorId = Guid.Empty,
                OccurredAt = now,
                Amount = entitlement.NormalAmount + entitlement.ExceptionAmount,
                Reason = "Linked failed delivery has no confirmed collection or capture.",
                DataJson = JsonSerializer.Serialize(new { entitlementId = entitlement.Id, sourceOperationId = entitlement.OriginalSaleOperationId })
            });
            repaired++;
        }

        if (repaired > 0) await paymentsDbContext.SaveChangesAsync(cancellationToken);
        return repaired;
    }

    private static string AppendNote(string? existing, string note) =>
        string.IsNullOrWhiteSpace(existing) ? note : $"{existing.Trim()}\n{note}";
}
