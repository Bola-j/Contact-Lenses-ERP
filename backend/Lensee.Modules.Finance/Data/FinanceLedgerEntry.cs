namespace Lensee.Modules.Finance.Data;

public sealed class FinanceLedgerEntry
{
    public Guid Id { get; set; }
    public Guid FinanceAccountId { get; set; }
    public string Direction { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Category { get; set; } = null!;
    public string? MovementMethod { get; set; }
    public string SourceType { get; set; } = null!;
    public Guid SourceId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string Status { get; set; } = "Posted";
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ReversesEntryId { get; set; }
    public string? CorrelationId { get; set; }
    public string? ExternalReference { get; set; }
    public FinanceAccount FinanceAccount { get; set; } = null!;
}
