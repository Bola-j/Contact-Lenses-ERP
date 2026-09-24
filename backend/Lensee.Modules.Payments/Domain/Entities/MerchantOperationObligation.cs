namespace Lensee.Modules.Payments.Data;

public sealed class MerchantOperationObligation
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid? OperationId { get; set; }
    public string SourceType { get; set; } = "Operation";
    public Guid SourceId { get; set; }
    public decimal OriginalAmount { get; set; }
    public DateTime PostedAt { get; set; }
    public string Status { get; set; } = "Open";
    public MerchantReceivableAccount Account { get; set; } = null!;
    public ICollection<MerchantEntryAllocation> Allocations { get; set; } = new List<MerchantEntryAllocation>();
}
