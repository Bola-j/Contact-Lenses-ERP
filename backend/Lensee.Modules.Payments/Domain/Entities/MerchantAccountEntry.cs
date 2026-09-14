namespace Lensee.Modules.Payments.Data;

public sealed class MerchantAccountEntry
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public long Sequence { get; set; }
    public string EntryType { get; set; } = null!;
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string? PaymentMethod { get; set; }
    public string? TransactionReference { get; set; }
    public string SourceType { get; set; } = null!;
    public Guid SourceId { get; set; }
    public Guid? OperationId { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? ReversesEntryId { get; set; }
    public string Status { get; set; } = "Posted";
    public Guid PostedBy { get; set; }
    public DateTime PostedAt { get; set; }
    public string? Notes { get; set; }
    public MerchantReceivableAccount Account { get; set; } = null!;
    public ICollection<MerchantEntryAllocation> Allocations { get; set; } = new List<MerchantEntryAllocation>();
}
