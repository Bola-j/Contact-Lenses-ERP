namespace Lensee.Modules.Inventory.Data;

/// <summary>
/// Durable logical hold for a piece-mode operation line. Loose pieces remain in
/// their source lot and sealed packs remain unopened while this reservation is active.
/// </summary>
public sealed class StockPieceReservation
{
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public Guid OperationLineId { get; set; }
    public Guid LocationId { get; set; }
    public Guid SkuId { get; set; }
    public int RequestedPieceQuantity { get; set; }
    public int PiecesPerPackSnapshot { get; set; }
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }

    public ICollection<StockPieceReservationAllocation> Allocations { get; set; } = new List<StockPieceReservationAllocation>();
}
