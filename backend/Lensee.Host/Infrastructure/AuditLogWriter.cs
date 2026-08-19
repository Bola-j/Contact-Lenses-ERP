using System.Text.Json;
using Lensee.Modules.Identity.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

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

        var actor = await _identityDbContext.Users.AsNoTracking()
            .Where(value => value.Id == userId)
            .Select(value => new { value.FullName, value.Role })
            .SingleOrDefaultAsync(cancellationToken);

        await WriteUserAuditAsync(
            userId,
            actor?.FullName ?? "Full name unavailable",
            actor?.Role ?? "Role unavailable",
            entityType,
            entityId,
            action,
            changedFields,
            stockDeltaApplied,
            cancellationToken);
    }

    public async Task WriteSystemAsync(
        string actorName,
        string entityType,
        Guid entityId,
        string action,
        object? changedFields = null,
        int? stockDeltaApplied = null,
        CancellationToken cancellationToken = default)
    {
        _identityDbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            ChangedFields = changedFields is null ? null : JsonSerializer.Serialize(changedFields),
            StockDeltaApplied = stockDeltaApplied,
            ActorType = "Integration",
            ActorName = actorName,
            IpAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = _clock.EgyptNow
        });

        _httpContextAccessor.HttpContext?.Items.TryAdd(AuditMutationMiddleware.AuditWrittenItemKey, true);
        await _identityDbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task WriteForUserAsync(
        Guid actorUserId,
        string _,
        string entityType,
        Guid entityId,
        string action,
        object? changedFields = null,
        int? stockDeltaApplied = null,
        CancellationToken cancellationToken = default)
    {
        var actor = await _identityDbContext.Users.AsNoTracking()
            .Where(value => value.Id == actorUserId)
            .Select(value => new { value.FullName, value.Role })
            .SingleOrDefaultAsync(cancellationToken);

        await WriteUserAuditAsync(
            actorUserId,
            actor?.FullName ?? "Full name unavailable",
            actor?.Role ?? "Role unavailable",
            entityType,
            entityId,
            action,
            changedFields,
            stockDeltaApplied,
            cancellationToken);
    }

    private async Task WriteUserAuditAsync(
        Guid actorUserId,
        string actorName,
        string actorRole,
        string entityType,
        Guid entityId,
        string action,
        object? changedFields,
        int? stockDeltaApplied,
        CancellationToken cancellationToken)
    {
        _identityDbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            ChangedFields = changedFields is null ? null : JsonSerializer.Serialize(changedFields),
            StockDeltaApplied = stockDeltaApplied,
            UserId = actorUserId,
            ActorType = actorRole,
            ActorName = actorName,
            IpAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = _clock.EgyptNow
        });

        _httpContextAccessor.HttpContext?.Items.TryAdd(AuditMutationMiddleware.AuditWrittenItemKey, true);
        await _identityDbContext.SaveChangesAsync(cancellationToken);
    }
}
