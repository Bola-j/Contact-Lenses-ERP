namespace Lensee.Modules.Finance.Data;

public sealed class ReconciliationImportRow
{
    public Guid Id { get; set; }
    public Guid PackageId { get; set; }
    public int RowNumber { get; set; }
    public string RowType { get; set; } = null!;
    public string SourceReference { get; set; } = null!;
    public string OriginalPayload { get; set; } = null!;
    public string Decision { get; set; } = "PendingReview";
    public string? CanonicalTrack { get; set; }
    public string? DecisionReason { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNote { get; set; }
    public ReconciliationImportPackage Package { get; set; } = null!;
}
