namespace Lensee.Modules.Payments.Data;

public sealed class MerchantReceivableAccount
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public string Status { get; set; } = "Open";
    public long NextSequence { get; set; } = 1;
    public DateTime OpenedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Guid OpenedBy { get; set; }
    public ICollection<MerchantAccountEntry> Entries { get; set; } = new List<MerchantAccountEntry>();
}
