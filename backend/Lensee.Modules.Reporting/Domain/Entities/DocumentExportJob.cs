namespace Lensee.Modules.Reporting.Data;

/// <summary>Immutable report snapshot plus its asynchronously rendered artifact.</summary>
public sealed class DocumentExportJob
{
    public Guid Id { get; set; }
    public string DocumentKey { get; set; } = null!;
    public string Format { get; set; } = null!;
    public string Language { get; set; } = null!;
    public string Status { get; set; } = "Queued";
    public Guid? RequestedBy { get; set; }
    public string? RequestedByRole { get; set; }
    public string? IdempotencyKey { get; set; }
    public int RowCount { get; set; }
    public string SnapshotJson { get; set; } = null!;
    public byte[]? Content { get; set; }
    public string? ContentType { get; set; }
    public string? FileName { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
