namespace Lensee.Modules.Operations.Data;

public sealed class MerchantExpiryRecall
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public Guid SkuId { get; set; }
    public string LotNumber { get; set; } = string.Empty;
    public DateOnly ExpiryDate { get; set; }
    public string Status { get; set; } = "Active";
    public int SoldQuantity { get; set; }
    public int ReturnedQuantity { get; set; }
    public int? ResolvedSoldQuantity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
    public string? ResolutionNote { get; set; }
}
