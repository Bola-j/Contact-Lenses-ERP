namespace Lensee.Modules.Operations.Data;

public partial class ShopifyOrderLink
{
    public Guid OperationId { get; set; }
    public string ShopifyOrderId { get; set; } = null!;
    public string? ShopifyOrderNumber { get; set; }
    public string? PaymentReference { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public virtual OperationLog Operation { get; set; } = null!;
}
