namespace Lensee.Modules.Finance.Data;

/// <summary>One component of a supply shipment's landed cost.  It is not a cash movement.</summary>
public sealed class SupplyFinanceCostEntry
{
    public Guid Id { get; set; }
    public Guid SupplyFinanceLogId { get; set; }
    public string Category { get; set; } = null!;
    /// <summary>Whether this component originated from the legacy supply manifest or Finance.</summary>
    public string Origin { get; set; } = "Finance";
    public decimal Amount { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = "Active";
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? ReversesCostEntryId { get; set; }
    public Guid? ReplacedByCostEntryId { get; set; }
    public string? CorrectionNote { get; set; }
    public SupplyFinanceLog SupplyFinanceLog { get; set; } = null!;
}
