namespace Lensee.Modules.Operations.Data;

/// <summary>A received quantity is always tied to one immutable shipment line.</summary>
public sealed class SupplyReceivingLine
{
    public Guid Id { get; set; }
    public Guid ReceivingSessionId { get; set; }
    public Guid ShipmentLineId { get; set; }
    public int ReceivedQuantity { get; set; }
    public string? LotNumber { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string? Notes { get; set; }
    public SupplyReceivingSession ReceivingSession { get; set; } = null!;
    public SupplyShipmentLine ShipmentLine { get; set; } = null!;
}
