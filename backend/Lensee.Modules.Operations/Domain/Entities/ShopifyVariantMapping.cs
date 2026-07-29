namespace Lensee.Modules.Operations.Data;

public partial class ShopifyVariantMapping
{
    public Guid Id { get; set; }
    public string ShopifyVariantId { get; set; } = null!;
    public Guid SkuId { get; set; }
    public string EntryMode { get; set; } = "Packs";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
