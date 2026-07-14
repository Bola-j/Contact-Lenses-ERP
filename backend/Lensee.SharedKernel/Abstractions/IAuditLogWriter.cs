namespace Lensee.SharedKernel.Abstractions;

public interface IAuditLogWriter
{
    Task WriteAsync(
        string entityType,
        Guid entityId,
        string action,
        object? changedFields = null,
        int? stockDeltaApplied = null,
        CancellationToken cancellationToken = default);
}
