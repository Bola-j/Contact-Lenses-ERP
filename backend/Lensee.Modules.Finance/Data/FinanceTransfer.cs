namespace Lensee.Modules.Finance.Data;

public sealed class FinanceTransfer
{
    public Guid Id { get; set; }
    public Guid ClientRequestId { get; set; }
    public Guid SourceAccountId { get; set; }
    public Guid DestinationAccountId { get; set; }
    public decimal Amount { get; set; }
    public decimal FeeAmount { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string? Notes { get; set; }
    public string? ExternalReference { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid SourceEntryId { get; set; }
    public Guid DestinationEntryId { get; set; }
    public Guid? FeeExpenseId { get; set; }
}
