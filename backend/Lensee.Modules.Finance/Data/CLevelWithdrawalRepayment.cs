namespace Lensee.Modules.Finance.Data;

public sealed class CLevelWithdrawalRepayment
{
    public Guid Id { get; set; }
    public Guid ClientRequestId { get; set; }
    public Guid WithdrawalId { get; set; }
    public Guid FinanceAccountId { get; set; }
    public decimal Amount { get; set; }
    public string MovementMethod { get; set; } = null!;
    public DateOnly BusinessDate { get; set; }
    public string? Notes { get; set; }
    public string? ExternalReference { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid PostedEntryId { get; set; }
    public Guid? ReversesRepaymentId { get; set; }
    public Guid? ReplacedByRepaymentId { get; set; }
    public string? CorrectionNote { get; set; }
}
