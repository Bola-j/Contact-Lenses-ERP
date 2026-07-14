using System;

namespace Lensee.Modules.Inventory.Data;

public partial class OpenedPieceLot
{
    public Guid Id { get; set; }

    public Guid LocationId { get; set; }

    public Guid SkuId { get; set; }

    public Guid SourceBatchId { get; set; }

    public string? LotNumber { get; set; }

    public DateOnly? BatchExpiryDate { get; set; }

    public DateOnly OpenedDate { get; set; }

    public DateOnly? PieceExpiryDate { get; set; }

    public int LoosePieceQuantity { get; set; }

    public Guid? CreatedFrom { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Location Location { get; set; } = null!;
}
