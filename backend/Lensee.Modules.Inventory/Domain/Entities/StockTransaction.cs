using System;
using System.Collections.Generic;

namespace Lensee.Modules.Inventory.Data;

public partial class StockTransaction
{
    public Guid Id { get; set; }

    public Guid SkuId { get; set; }

    public Guid LocationId { get; set; }

    public string TransactionType { get; set; } = null!;

    public int QuantityChange { get; set; }

    public Guid? ReferenceOperationId { get; set; }

    public Guid UserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Location Location { get; set; } = null!;
}
