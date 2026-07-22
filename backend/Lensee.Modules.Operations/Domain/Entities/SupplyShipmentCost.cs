using System;

namespace Lensee.Modules.Operations.Data;

public partial class SupplyShipmentCost
{
    public Guid Id { get; set; }

    public Guid ShipmentId { get; set; }

    public string CostType { get; set; } = null!;

    public string? Description { get; set; }

    public decimal Amount { get; set; }

    public virtual SupplyShipment Shipment { get; set; } = null!;
}
