namespace Lensee.Modules.Operations.Data;

public sealed class ShopifyOrderCancellationTombstone
{
    public Guid Id { get; set; }
    public string ShopDomain { get; set; } = null!;
    public string ShopifyOrderId { get; set; } = null!;
    public DateTime CancelledAt { get; set; }
}
