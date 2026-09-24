namespace Lensee.Modules.Finance.Data;

/// <summary>Immutable approval workflow for a real operating-expense payment.</summary>
public sealed class FinanceExpense
{
    public Guid Id { get; set; }
    public Guid FinanceAccountId { get; set; }
    public decimal Amount { get; set; }
    public string Category { get; set; } = null!;
    public Guid? CategoryId { get; set; }
    public string MovementMethod { get; set; } = null!;
    public DateOnly BusinessDate { get; set; }
    public string? Description { get; set; }
    public string Status { get; set; } = "Draft";
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public Guid? PostedFinanceLedgerEntryId { get; set; }
    public Guid? ReversesExpenseId { get; set; }
    public Guid? ReplacedByExpenseId { get; set; }
    public string? ExternalReference { get; set; }
    public string? CorrelationId { get; set; }
    public string? CorrectionNote { get; set; }
    public Guid? TransferId { get; set; }
    public FinanceAccount FinanceAccount { get; set; } = null!;
}
