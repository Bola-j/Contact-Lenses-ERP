using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Data;
using Lensee.Host.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Services;

/// <summary>Operational, payload-safe access to messages that require manual recovery.</summary>
public sealed class OutboxOperationsService
{
    private readonly SharedDbContext _shared;
    private readonly IClock _clock;

    public OutboxOperationsService(SharedDbContext shared, IClock clock)
    {
        _shared = shared;
        _clock = clock;
    }

    public async Task<IReadOnlyList<OutboxMessageSummary>> ListDeadLettersAsync(CancellationToken cancellationToken)
    {
        var messages = await _shared.OutboxMessages.AsNoTracking()
            .Where(message => message.Status == "DeadLetter")
            .OrderBy(message => message.OccurredAt)
            .Take(200)
            .ToListAsync(cancellationToken);
        return messages.Select(message => new OutboxMessageSummary(
                message.Id,
                message.EventType,
                message.EventVersion,
                message.Status,
                message.Attempts,
                message.OccurredAt,
                message.NextAttemptAt,
                message.ProcessedAt,
                message.LastError is null ? null : message.LastError.Length <= 500 ? message.LastError : message.LastError[..500],
                message.CorrelationId))
            .ToList();
    }

    public async Task<bool> RetryAsync(Guid id, CancellationToken cancellationToken)
    {
        var message = await _shared.OutboxMessages.FirstOrDefaultAsync(value => value.Id == id && value.Status == "DeadLetter", cancellationToken);
        if (message is null) return false;

        message.Status = "Pending";
        message.Attempts = 0;
        message.NextAttemptAt = _clock.EgyptNow;
        message.LastError = null;
        message.ProcessedAt = null;
        await _shared.SaveChangesAsync(cancellationToken);
        LenseeTelemetry.OutboxReplays.Add(1, new KeyValuePair<string, object?>("event_type", message.EventType));
        return true;
    }
}

public sealed record OutboxMessageSummary(
    Guid Id,
    string EventType,
    int EventVersion,
    string Status,
    int Attempts,
    DateTime OccurredAt,
    DateTime NextAttemptAt,
    DateTime? ProcessedAt,
    string? LastError,
    string? CorrelationId);
