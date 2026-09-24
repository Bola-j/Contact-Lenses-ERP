namespace Lensee.Modules.Finance.Data;

/// <summary>
/// Immutable registration record for an externally signed historical-reconciliation package.
/// The source CSV is never rewritten: row decisions and application evidence are append-only.
/// </summary>
public sealed class ReconciliationImportPackage
{
    public Guid Id { get; set; }
    public string SchemaVersion { get; set; } = null!;
    public string Signer { get; set; } = null!;
    public string Signature { get; set; } = null!;
    public string PayloadSha256 { get; set; } = null!;
    public DateOnly SourcePeriodStart { get; set; }
    public DateOnly SourcePeriodEnd { get; set; }
    public DateTime ExportedAtUtc { get; set; }
    public string Status { get; set; } = "Registered";
    public Guid RegisteredByUserId { get; set; }
    public DateTime RegisteredAt { get; set; }
    public Guid? AppliedByUserId { get; set; }
    public DateTime? AppliedAt { get; set; }
    public string? ApplyIdempotencyKey { get; set; }
    public ICollection<ReconciliationImportRow> Rows { get; set; } = new List<ReconciliationImportRow>();
}
