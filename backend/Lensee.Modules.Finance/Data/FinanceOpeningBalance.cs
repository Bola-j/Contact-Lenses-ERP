namespace Lensee.Modules.Finance.Data;

public sealed class FinanceOpeningBalance
{
    public Guid Id { get; set; }
    public Guid FinanceAccountId { get; set; }
    public decimal Amount { get; set; }
    public string Direction { get; set; } = "Credit";
    public DateOnly AsOfDate { get; set; }
    public string Description { get; set; } = null!;
    public string Status { get; set; } = "Draft";
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public Guid? PostedEntryId { get; set; }
    public Guid? ReversesOpeningBalanceId { get; set; }
    public Guid? ReplacedByOpeningBalanceId { get; set; }
    public string? CorrelationId { get; set; }
    public FinanceAccount FinanceAccount { get; set; } = null!;
}
