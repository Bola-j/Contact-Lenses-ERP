namespace Lensee.Modules.Finance.Data;

public sealed class FinanceAccount
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Type { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public string? Reference { get; set; }
    public string? Details { get; set; }
    // A treasury account may be assigned to one sales location.  This is an
    // attribution key only; it is intentionally not a cross-module FK.
    public Guid? LocationId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public ICollection<FinanceLedgerEntry> Entries { get; set; } = new List<FinanceLedgerEntry>();
}
