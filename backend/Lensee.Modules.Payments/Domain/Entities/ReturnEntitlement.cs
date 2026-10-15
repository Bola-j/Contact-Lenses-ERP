namespace Lensee.Modules.Payments.Data;

/// <summary>
/// The durable financial claim created only after Operations has physically
/// confirmed a return. It is intentionally not a cash record or a merchant
/// ledger entry: Finance must choose and record settlement separately.
/// </summary>
public sealed class ReturnEntitlement
{
    public Guid Id { get; set; }
    public Guid ReturnOperationId { get; set; }
    public Guid? OriginalSaleOperationId { get; set; }
    public Guid? MerchantId { get; set; }
    public string BeneficiaryKind { get; set; } = null!;
    public string? BuyerName { get; set; }
    public string? PaymentTrack { get; set; }
    public string? OriginalPaymentReference { get; set; }
    public decimal NormalAmount { get; set; }
    public decimal ExceptionAmount { get; set; }
    public decimal ReservedAmount { get; set; }
    public decimal SettledAmount { get; set; }
    /// <summary>Approved value that reduced the linked original-sale obligation.</summary>
    public decimal SourceSettledAmount { get; set; }
    /// <summary>Approved value that affected only the merchant account.</summary>
    public decimal AccountLevelSettledAmount { get; set; }
    public decimal MerchantAccountSettledAmount { get; set; }
    public decimal OtherPaymentsSettledAmount { get; set; }
    /// <summary>The scope of the single active settlement reservation, if any.</summary>
    public string? ReservedScope { get; set; }
    public string Status { get; set; } = null!;
    public string? IdempotencyKey { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? Notes { get; set; }
}
