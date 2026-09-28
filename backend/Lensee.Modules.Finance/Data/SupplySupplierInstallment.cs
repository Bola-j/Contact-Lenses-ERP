namespace Lensee.Modules.Finance.Data;

/// <summary>A supplier payment. Only Posted installments create a Finance ledger debit.</summary>
public sealed class SupplySupplierInstallment
{
    public Guid Id { get; set; }
    public Guid SupplyFinanceLogId { get; set; }
    public Guid FinanceAccountId { get; set; }
    public decimal Amount { get; set; }
    public string MovementMethod { get; set; } = null!;
    public DateOnly BusinessDate { get; set; }
    public string? ExternalReference { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = "Draft";
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? PaidByUserId { get; set; }
    public DateTime? PaidAt { get; set; }
    public Guid? PostedFinanceLedgerEntryId { get; set; }
    public Guid? ReversesInstallmentId { get; set; }
    public Guid? ReplacedByInstallmentId { get; set; }
    public string? CorrectionNote { get; set; }
    public string? CorrelationId { get; set; }
    public SupplyFinanceLog SupplyFinanceLog { get; set; } = null!;
    public FinanceAccount FinanceAccount { get; set; } = null!;
}
