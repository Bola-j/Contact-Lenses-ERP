using System;

namespace Lensee.Modules.Payments.Data;

public partial class FinancialAdjustment
{
    public Guid Id { get; set; }

    public Guid MerchantId { get; set; }

    public Guid? OperationId { get; set; }

    public string AdjustmentType { get; set; } = null!;

    public decimal Amount { get; set; }

    public string Status { get; set; } = null!;

    public string? Notes { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string? RejectionReason { get; set; }

    public Guid? PaymentLogId { get; set; }

    /// <summary>Return entitlements may be settled only through this linked workflow.</summary>
    public Guid? ReturnEntitlementId { get; set; }

    /// <summary>Separates an original-sale reduction from a merchant-account-only settlement.</summary>
    public string? ReturnSettlementScope { get; set; }

    // The payment instructions belong to the return refund itself.  Keeping
    // them here means approval and payout do not depend on a separate page's
    // temporary form state.
    public Guid? RefundFinanceAccountId { get; set; }
    public string? RefundPaymentMethod { get; set; }
    public string? RefundTransactionReference { get; set; }
    public decimal? RefundTransferFeeAmount { get; set; }

    public Guid? ReversesAdjustmentId { get; set; }

    public string LineageKind { get; set; } = null!;
}
