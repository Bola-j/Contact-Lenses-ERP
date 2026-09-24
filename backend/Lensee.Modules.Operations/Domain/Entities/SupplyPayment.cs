namespace Lensee.Modules.Operations.Data;

/// <summary>
/// A real payment made for a supply shipment. Valuation costs deliberately do
/// not create this aggregate: only a posted payment may affect treasury.
/// </summary>
public sealed class SupplyPayment
{
    public Guid Id { get; set; }
    public Guid ShipmentId { get; set; }
    public string Category { get; set; } = null!;
    public decimal Amount { get; set; }
    public string MovementMethod { get; set; } = null!;
    public Guid FinanceAccountId { get; set; }
    public string? ExternalReference { get; set; }
    public string Status { get; set; } = null!;
    public string? Notes { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? SubmittedBy { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
    public Guid? PostedFinanceLedgerEntryId { get; set; }
    public Guid? ReversesPaymentId { get; set; }
    public Guid? ReplacedByPaymentId { get; set; }
    public string CorrelationId { get; set; } = null!;

    public SupplyShipment Shipment { get; set; } = null!;
}
