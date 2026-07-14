using System;
using System.Collections.Generic;

namespace Lensee.Modules.Inventory.Data;

public partial class StockBalance
{
    public Guid Id { get; set; }

    public Guid LocationId { get; set; }

    public Guid SkuId { get; set; }

    public int AvailableQty { get; set; }

    public int ReservedInWarehouseQty { get; set; }

    public int ReservedWithRepQty { get; set; }

    public int? TargetQty { get; set; }

    public int RowVersion { get; set; }

    public DateTime LastUpdated { get; set; }

    public virtual Location Location { get; set; } = null!;
}
