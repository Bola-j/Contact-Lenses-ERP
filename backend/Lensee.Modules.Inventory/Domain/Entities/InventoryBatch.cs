using System;
using System.Collections.Generic;

namespace Lensee.Modules.Inventory.Data;

public partial class InventoryBatch
{
    public Guid Id { get; set; }

    public Guid SkuId { get; set; }

    public Guid LocationId { get; set; }

    public string? LotNumber { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public int Quantity { get; set; }

    public Guid? CreatedFrom { get; set; }

    public Guid? CreatedBy { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Location Location { get; set; } = null!;
}
