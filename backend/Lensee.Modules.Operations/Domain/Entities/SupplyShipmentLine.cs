using System;

namespace Lensee.Modules.Operations.Data;

public partial class SupplyShipmentLine
{
    public Guid Id { get; set; }

    public Guid ShipmentId { get; set; }

    public Guid SkuId { get; set; }

    public string ProductNameSnapshot { get; set; } = null!;

    public string SkuCodeSnapshot { get; set; } = null!;

    public int Quantity { get; set; }

    /// <summary>Supply manifests are sealed-pack only. Kept explicitly so API and operation history cannot disagree.</summary>
    public string QuantityUnit { get; set; } = "Pack";

    public decimal? UnitPrice { get; set; }

    public decimal LineSubtotal { get; set; }

    public decimal AllocatedCost { get; set; }

    public decimal LandedUnitCost { get; set; }

    public string? LotNumber { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public string? Notes { get; set; }

    public virtual SupplyShipment Shipment { get; set; } = null!;
}
