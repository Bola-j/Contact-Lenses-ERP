namespace Lensee.Modules.Operations.Data;

public sealed class ShopifyVariantMapping
{
    public Guid Id { get; set; }
    public string ShopDomain { get; set; } = null!;
    public string ShopifyVariantId { get; set; } = null!;
    public string? ShopifySku { get; set; }
    public Guid? SkuId { get; set; }
    public string EntryMode { get; set; } = "Pieces";
    public string Status { get; set; } = "Unresolved";
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public long Revision { get; set; } = 1;
}
