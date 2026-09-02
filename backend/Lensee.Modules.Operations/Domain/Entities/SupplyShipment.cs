using System;
using System.Collections.Generic;

namespace Lensee.Modules.Operations.Data;

public partial class SupplyShipment
{
    public Guid Id { get; set; }

    public uint ConcurrencyVersion { get; private set; }

    public string ShipmentNumber { get; set; } = null!;

    public string SupplierName { get; set; } = null!;

    public string? InvoiceNumber { get; set; }

    public DateTime ShipmentDate { get; set; }

    public Guid DestinationLocationId { get; set; }

    public string Status { get; set; } = null!;

    public string? Notes { get; set; }

    public decimal ProductSubtotal { get; set; }

    public decimal CostSubtotal { get; set; }

    public decimal LandedTotal { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public Guid? ConfirmedBy { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    public Guid? CancelledBy { get; set; }

    public DateTime? CancelledAt { get; set; }

    public Guid? InventoryReceiptOperationId { get; set; }

    public virtual OperationLog? InventoryReceiptOperation { get; set; }

    public virtual ICollection<SupplyShipmentCost> Costs { get; set; } = new List<SupplyShipmentCost>();

    public virtual ICollection<SupplyShipmentHistory> HistoryLogs { get; set; } = new List<SupplyShipmentHistory>();

    public virtual ICollection<SupplyShipmentLine> Lines { get; set; } = new List<SupplyShipmentLine>();
}
