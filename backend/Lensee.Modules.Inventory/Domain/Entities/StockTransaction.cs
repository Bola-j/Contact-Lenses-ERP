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

    // Null is reserved for pre-unit historical rows. All new rows must set Pack or Piece.
    public string? QuantityUnit { get; set; }

    // Links a pack-opening conversion to the actual piece issue it enabled.
    public Guid? RelatedTransactionId { get; set; }

    // Snapshot captured only for PackOpen conversions; never derive history from current SKU settings.
    public int? ConversionPiecesPerPack { get; set; }

    public Guid? ReferenceOperationId { get; set; }

    public Guid UserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Location Location { get; set; } = null!;
}
