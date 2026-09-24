namespace Lensee.Modules.Finance.Data;

/// <summary>Immutable protected treasury withdrawal assigned to a persisted C-Level user.</summary>
public sealed class CLevelWithdrawal
{
    public Guid Id { get; set; }
    public Guid AssignedToCLevelUserId { get; set; }
    public Guid AssignedByUserId { get; set; }
    public Guid FinanceAccountId { get; set; }
    public decimal Amount { get; set; }
    public string MovementMethod { get; set; } = null!;
    public DateOnly BusinessDate { get; set; }
    public string Reason { get; set; } = null!;
    public Guid? CategoryId { get; set; }
    public string? CorrectionNote { get; set; }
    public string Status { get; set; } = "Draft";
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public Guid? PostedFinanceLedgerEntryId { get; set; }
    public Guid? ReversesWithdrawalId { get; set; }
    public Guid? ReplacedByWithdrawalId { get; set; }
    public string? ExternalReference { get; set; }
    public string? CorrelationId { get; set; }
    public FinanceAccount FinanceAccount { get; set; } = null!;
}
