using System;
using System.Collections.Generic;

namespace Lensee.Modules.Inventory.Data;

public partial class Location
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string LocationType { get; set; } = null!;

    public bool IsActive { get; set; }

    public virtual ICollection<InventoryBatch> InventoryBatches { get; set; } = new List<InventoryBatch>();

    public virtual ICollection<StockBalance> StockBalances { get; set; } = new List<StockBalance>();

    public virtual ICollection<StockTransaction> StockTransactions { get; set; } = new List<StockTransaction>();
}
