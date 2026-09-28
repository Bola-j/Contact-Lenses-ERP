namespace Lensee.Modules.Operations.Data;

/// <summary>One physical receiving event for an arrived supply shipment.</summary>
public sealed class SupplyReceivingSession
{
    public Guid Id { get; set; }
    public Guid ShipmentId { get; set; }
    public string Status { get; set; } = "Draft";
    public string? Notes { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public Guid? InventoryReceiptOperationId { get; set; }
    public SupplyShipment Shipment { get; set; } = null!;
    public ICollection<SupplyReceivingLine> Lines { get; set; } = new List<SupplyReceivingLine>();
}
