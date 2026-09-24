namespace Lensee.Modules.Operations.Data;

/// <summary>
/// Immutable evidence that a return or change-out consumes a specific sold line.
/// The allocation is intentionally independent of the mutable editor payload.
/// </summary>
public sealed class OperationLineSourceAllocation
{
    public Guid Id { get; set; }
    public Guid TargetOperationLineId { get; set; }
    public Guid SourceOperationId { get; set; }
    public Guid SourceOperationLineId { get; set; }
    public Guid SkuId { get; set; }
    public Guid? SourceBatchId { get; set; }
    public Guid? SourceOpenedPieceLotId { get; set; }
    public string EntryMode { get; set; } = null!;
    public string? LotNumber { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; }

    public OperationLine TargetOperationLine { get; set; } = null!;
}
