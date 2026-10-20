namespace Lensee.Modules.Operations.Data;

public sealed class ShopifyRefundReview
{
    public Guid Id { get; set; }
    public string ShopDomain { get; set; } = null!;
    public string ShopifyRefundId { get; set; } = null!;
    public string ShopifyOrderId { get; set; } = null!;
    public Guid? OperationId { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string Status { get; set; } = "RequiresAttention";
    public DateTime ReceivedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
