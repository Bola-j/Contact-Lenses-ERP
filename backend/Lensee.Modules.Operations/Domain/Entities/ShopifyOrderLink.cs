namespace Lensee.Modules.Operations.Data;

public partial class ShopifyOrderLink
{
    public Guid OperationId { get; set; }
    public string ShopifyOrderId { get; set; } = null!;
    public string? ShopifyOrderNumber { get; set; }
    public string? PaymentReference { get; set; }
    public string ShopDomain { get; set; } = null!;
    public string? FinancialStatus { get; set; }
    public bool FinancialStatusTrusted { get; set; }
    public string FinanceReconciliationStatus { get; set; } = "PendingEvidence";
    public string? ReportedCurrency { get; set; }
    public string? PresentmentCurrency { get; set; }
    public decimal? ReportedSubtotal { get; set; }
    public decimal? ReportedDiscounts { get; set; }
    public decimal? ReportedShipping { get; set; }
    public decimal? ReportedTax { get; set; }
    public decimal? ReportedTotal { get; set; }
    public DateTime? SourceUpdatedAt { get; set; }
    public bool IsCancellationTombstone { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CommercialSnapshot { get; set; }
    public string? CommercialSnapshotHash { get; set; }
    public bool RequiresReview { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public virtual OperationLog Operation { get; set; } = null!;
}
