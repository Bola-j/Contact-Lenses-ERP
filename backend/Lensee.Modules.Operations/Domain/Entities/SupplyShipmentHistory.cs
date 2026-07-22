using System;

namespace Lensee.Modules.Operations.Data;

public partial class SupplyShipmentHistory
{
    public Guid Id { get; set; }

    public Guid ShipmentId { get; set; }

    public string Action { get; set; } = null!;

    public Guid ActorUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? Summary { get; set; }

    public string? SnapshotData { get; set; }

    public virtual SupplyShipment Shipment { get; set; } = null!;
}
