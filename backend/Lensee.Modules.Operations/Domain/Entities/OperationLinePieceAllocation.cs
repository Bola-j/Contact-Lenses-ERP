namespace Lensee.Modules.Operations.Data;

/// <summary>
/// Immutable physical lineage for a piece-mode issue.  This intentionally permits
/// multiple opened lots for one operation line.
/// </summary>
public sealed class OperationLinePieceAllocation
{
    public Guid Id { get; set; }
    public Guid OperationLineId { get; set; }
    public Guid OpenedPieceLotId { get; set; }
    public Guid? SourceBatchId { get; set; }
    public int IssuedPieceQuantity { get; set; }
    public int PiecesPerPackSnapshot { get; set; }
    public Guid PieceIssueTransactionId { get; set; }
    public Guid? PackOpenTransactionId { get; set; }
    public string? LotNumber { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public OperationLine OperationLine { get; set; } = null!;
}
