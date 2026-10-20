namespace Lensee.Modules.Operations.Data;

public sealed class ShopifyReconciliationCheckpoint
{
    public Guid Id { get; set; }
    public string ShopDomain { get; set; } = null!;
    // All checkpoint timestamps use UTC with the database's timestamp-without-time-zone convention.
    public DateTime ActivatedAtUtc { get; set; }
    public DateTime? UpdatedThroughUtc { get; set; }
    public DateTime? WindowStartedUtc { get; set; }
    public DateTime? WindowEndedUtc { get; set; }
    public string? Cursor { get; set; }
    public DateTime NextRunAt { get; set; }
    public DateTime? RequestedAt { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public long Revision { get; set; }
}
