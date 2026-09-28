using System;
using System.Collections.Generic;

namespace Lensee.Modules.Operations.Data;

public partial class StocktakeSession
{
    public Guid Id { get; set; }

    public uint ConcurrencyVersion { get; private set; }

    public Guid LocationId { get; set; }

    public DateTime SessionDate { get; set; }

    public Guid PerformedBy { get; set; }

    public Guid? ConfirmedBy { get; set; }

    public int? ProductsCounted { get; set; }

    public int? TotalDiscrepancyUnits { get; set; }

    public string? Notes { get; set; }

    public string Status { get; set; } = null!;

    /// <summary>CycleCount is the historical workflow; SupplyReceiving creates an inventory receipt only.</summary>
    public string Purpose { get; set; } = "CycleCount";
    public Guid? SupplyShipmentId { get; set; }
    public Guid? SupplyFinanceLogId { get; set; }
    public Guid? InventoryReceiptOperationId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    public virtual ICollection<StocktakeAdjustmentLine> StocktakeAdjustmentLines { get; set; } = new List<StocktakeAdjustmentLine>();
}
