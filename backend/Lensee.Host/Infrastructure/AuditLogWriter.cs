using System.Text.Json;
using Lensee.Modules.Identity.Data;
using Lensee.SharedKernel.Abstractions;

namespace Lensee.Host.Infrastructure;

public sealed class AuditLogWriter : IAuditLogWriter
{
    private readonly IdentityDbContext _identityDbContext;
    private readonly ICurrentUser _currentUser;
    private readonly IClock _clock;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditLogWriter(
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IHttpContextAccessor httpContextAccessor)
    {
        _identityDbContext = identityDbContext;
        _currentUser = currentUser;
        _clock = clock;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task WriteAsync(
        string entityType,
        Guid entityId,
        string action,
        object? changedFields = null,
        int? stockDeltaApplied = null,
        CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return;
        }

        _identityDbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            ChangedFields = changedFields is null ? null : JsonSerializer.Serialize(changedFields),
            StockDeltaApplied = stockDeltaApplied,
            UserId = userId,
            IpAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = _clock.EgyptNow
        });

        await _identityDbContext.SaveChangesAsync(cancellationToken);
    }
}
