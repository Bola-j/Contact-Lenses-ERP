namespace Lensee.Modules.Inventory.Data;

/// <summary>
/// Pins an exact quantity of pieces to an opened lot or unopened source batch.
/// PhysicalPacksHeld is nonzero only for a sealed-batch allocation.
/// </summary>
public sealed class StockPieceReservationAllocation
{
    public Guid Id { get; set; }
    public Guid ReservationId { get; set; }
    public string SourceKind { get; set; } = null!;
    public Guid? SourceOpenedPieceLotId { get; set; }
    public Guid SourceBatchId { get; set; }
    public int PieceQuantity { get; set; }
    public int PhysicalPacksHeld { get; set; }
    public string? LotNumber { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public DateTime CreatedAt { get; set; }

    public StockPieceReservation Reservation { get; set; } = null!;
}
