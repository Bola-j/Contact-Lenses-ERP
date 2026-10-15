using System;
using System.Collections.Generic;

namespace Lensee.Modules.Payments.Data;

public partial class CashRecord
{
    public Guid Id { get; set; }

    public Guid? OperationId { get; set; }

    public Guid? MerchantId { get; set; }

    public Guid? FinanceAccountId { get; set; }

    public Guid? FinancialAdjustmentId { get; set; }

    /// <summary>Links an anonymous return payout task to its confirmed return entitlement.</summary>
    public Guid? ReturnEntitlementId { get; set; }

    /// <summary>Separates source-sale refunds from valuation-pool payouts.</summary>
    public string? ReturnSettlementScope { get; set; }

    public string PaymentType { get; set; } = null!;

    public string? SubType { get; set; }

    public string? TransactionReference { get; set; }

    public decimal Amount { get; set; }

    public decimal? TransferFeeAmount { get; set; }

    public string Status { get; set; } = null!;

    public DateTime PaymentDate { get; set; }

    public Guid CreatedBy { get; set; }

    public Guid? ConfirmedBy { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    public string? Notes { get; set; }
}
