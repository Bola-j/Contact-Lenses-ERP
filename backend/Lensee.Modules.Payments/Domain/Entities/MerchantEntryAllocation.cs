namespace Lensee.Modules.Payments.Data;

public sealed class MerchantEntryAllocation
{
    public Guid Id { get; set; }
    public Guid EntryId { get; set; }
    public Guid ObligationId { get; set; }
    public decimal Amount { get; set; }
    public DateTime AllocatedAt { get; set; }
    public Guid AllocatedBy { get; set; }
    public MerchantAccountEntry Entry { get; set; } = null!;
    public MerchantOperationObligation Obligation { get; set; } = null!;
}
