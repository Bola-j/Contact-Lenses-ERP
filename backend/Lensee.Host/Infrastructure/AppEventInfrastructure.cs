using System.Text.Json;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Catalog.Domain.Events;
using Lensee.Modules.Catalog.Services;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Data;
using Lensee.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

public sealed class OutboxAppEventPublisher : IAppEventPublisher
{
    private readonly SharedDbContext _sharedDbContext;

    public OutboxAppEventPublisher(SharedDbContext sharedDbContext)
    {
        _sharedDbContext = sharedDbContext;
    }

    public async Task PublishAsync<TEvent>(TEvent appEvent, CancellationToken cancellationToken = default)
        where TEvent : IAppEvent
    {
        _sharedDbContext.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(TEvent).AssemblyQualifiedName ?? typeof(TEvent).FullName ?? typeof(TEvent).Name,
            EventVersion = 1,
            Payload = JsonSerializer.Serialize(appEvent, appEvent.GetType()),
            Status = "Pending",
            Attempts = 0,
            OccurredAt = appEvent.OccurredAt,
            NextAttemptAt = appEvent.OccurredAt
        });

        await _sharedDbContext.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Replaces the catalog event sink with a durable, payload-minimised outbox envelope.</summary>
public sealed class TransactionalCatalogEventPublisher : ICatalogEventPublisher
{
    private readonly SharedDbContext _sharedDbContext;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TransactionalCatalogEventPublisher(SharedDbContext sharedDbContext, IHttpContextAccessor httpContextAccessor)
    {
        _sharedDbContext = sharedDbContext;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task PublishAsync(CatalogEvent catalogEvent, CancellationToken cancellationToken = default)
    {
        var trace = _httpContextAccessor.HttpContext?.TraceIdentifier;
        _sharedDbContext.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = typeof(CatalogEventEnvelope).AssemblyQualifiedName ?? nameof(CatalogEventEnvelope),
            EventVersion = 1,
            CorrelationId = trace,
            CausationId = trace,
            Payload = JsonSerializer.Serialize(new CatalogEventEnvelope(
                catalogEvent.EntityId,
                catalogEvent.EntityType,
                catalogEvent.GetType().Name,
                catalogEvent.OccurredAt)),
            Status = "Pending",
            Attempts = 0,
            OccurredAt = catalogEvent.OccurredAt,
            NextAttemptAt = catalogEvent.OccurredAt
        });
        await _sharedDbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed record PaymentWorkflowChangedEvent(
    Guid PaymentLogId,
    Guid? MerchantId,
    Guid? OperationId,
    string EventType,
    string Message,
    Guid? TargetUserId,
    string? TargetRole,
    DateTime OccurredAt) : IAppEvent;

public sealed record OperationCorrectionChangedEvent(
    Guid CorrectionProposalId,
    Guid OperationId,
    string Action,
    Guid ActorId,
    DateTime OccurredAt) : IAppEvent;

public sealed record CatalogEventEnvelope(
    Guid EntityId,
    string EntityType,
    string Action,
    DateTime OccurredAt) : IAppEvent;

public sealed class PaymentWorkflowNotificationHandler : IAppEventHandler<PaymentWorkflowChangedEvent>
{
    private readonly NotificationsDbContext _notificationsDbContext;

    public PaymentWorkflowNotificationHandler(NotificationsDbContext notificationsDbContext)
    {
        _notificationsDbContext = notificationsDbContext;
    }

    public async Task HandleAsync(PaymentWorkflowChangedEvent appEvent, CancellationToken cancellationToken = default)
    {
        var targets = new List<(Guid? UserId, string? Role)>();
        if (appEvent.TargetUserId.HasValue || !string.IsNullOrWhiteSpace(appEvent.TargetRole))
        {
            targets.Add((appEvent.TargetUserId, appEvent.TargetRole));
        }
        else
        {
            targets.Add((null, LenseeRoles.Admin));
        }

        foreach (var target in targets)
        {
            _notificationsDbContext.NotificationLogs.Add(new NotificationLog
            {
                Id = Guid.NewGuid(),
                AlertType = appEvent.EventType,
                Message = BuildPaymentMessage(appEvent),
                ReferenceId = appEvent.PaymentLogId,
                ReferenceType = "PaymentLog",
                TargetUserId = target.UserId,
                TargetRole = target.Role,
                Channel = "InApp",
                IsRead = false,
                CreatedAt = appEvent.OccurredAt
            });
        }

        await _notificationsDbContext.SaveChangesAsync(cancellationToken);
    }

    private static string BuildPaymentMessage(PaymentWorkflowChangedEvent appEvent)
    {
        var context = appEvent.OperationId.HasValue
            ? $" Operation {AuditEventPayload.FriendlyReference("Operation", appEvent.OperationId.Value)} is linked."
            : string.Empty;
        var merchant = appEvent.MerchantId.HasValue
            ? $" Merchant {AuditEventPayload.FriendlyReference("Merchant", appEvent.MerchantId.Value)} is affected."
            : string.Empty;

        return $"{appEvent.Message}{merchant}{context} Open Payments to review assignment, approval state, paid amount, and remaining effect.";
    }
}

public sealed class OperationCorrectionNotificationHandler : IAppEventHandler<OperationCorrectionChangedEvent>
{
    private readonly NotificationsDbContext _notificationsDbContext;

    public OperationCorrectionNotificationHandler(NotificationsDbContext notificationsDbContext)
    {
        _notificationsDbContext = notificationsDbContext;
    }

    public async Task HandleAsync(OperationCorrectionChangedEvent appEvent, CancellationToken cancellationToken = default)
    {
        _notificationsDbContext.NotificationLogs.Add(new NotificationLog
        {
            Id = Guid.NewGuid(),
            AlertType = $"OperationCorrection{appEvent.Action}",
            Message = $"Operation correction {appEvent.Action}. Review the correction workflow and immutable operation lineage.",
            ReferenceId = appEvent.CorrectionProposalId,
            ReferenceType = "OperationCorrection",
            TargetRole = LenseeRoles.Admin,
            Channel = "InApp",
            IsRead = false,
            CreatedAt = appEvent.OccurredAt
        });
        await _notificationsDbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed class OutboxWorker : BackgroundService
{
    private const int BatchSize = 25;
    private const int MaxAttempts = 10;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxWorker> _logger;

    public OutboxWorker(IServiceScopeFactory scopeFactory, ILogger<OutboxWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessBatchAsync(stoppingToken);
                var delay = processed > 0
                    ? TimeSpan.FromMilliseconds(250)
                    : TimeSpan.FromSeconds(5);
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Outbox worker failed while processing a batch.");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }

    private async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sharedDbContext = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
        var notificationsDbContext = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var processed = 0;
        await SharedDbTransaction.ExecuteAsync(sharedDbContext, async () =>
        {
            var messages = sharedDbContext.Database.IsRelational()
                ? await sharedDbContext.OutboxMessages
                    .FromSqlRaw("""
                        select *
                        from shared.outbox_messages
                        where status in ('Pending','Failed')
                          and next_attempt_at <= now()
                        order by occurred_at
                        limit {0}
                        for update skip locked
                        """, BatchSize)
                    .ToListAsync(cancellationToken)
                : await sharedDbContext.OutboxMessages
                    .Where(message => (message.Status == "Pending" || message.Status == "Failed") && message.NextAttemptAt <= clock.EgyptNow)
                    .OrderBy(message => message.OccurredAt)
                    .Take(BatchSize)
                    .ToListAsync(cancellationToken);

            foreach (var message in messages)
            {
                await ProcessMessageAsync(message, scope.ServiceProvider, sharedDbContext, clock.EgyptNow, cancellationToken);
                processed++;
            }

            await notificationsDbContext.SaveChangesAsync(cancellationToken);
            await sharedDbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken, notificationsDbContext);

        return processed;
    }

    private async Task ProcessMessageAsync(
        OutboxMessage message,
        IServiceProvider serviceProvider,
        SharedDbContext sharedDbContext,
        DateTime now,
        CancellationToken cancellationToken)
    {
        try
        {
            if (message.EventType == typeof(PaymentWorkflowChangedEvent).AssemblyQualifiedName)
            {
                var appEvent = JsonSerializer.Deserialize<PaymentWorkflowChangedEvent>(message.Payload)
                    ?? throw new InvalidOperationException("Payment workflow event payload could not be deserialized.");
                await DispatchAsync(message.Id, appEvent, serviceProvider, sharedDbContext, now, cancellationToken);
            }
            else if (message.EventType == typeof(OperationCorrectionChangedEvent).AssemblyQualifiedName)
            {
                var appEvent = JsonSerializer.Deserialize<OperationCorrectionChangedEvent>(message.Payload)
                    ?? throw new InvalidOperationException("Operation correction event payload could not be deserialized.");
                await DispatchAsync(message.Id, appEvent, serviceProvider, sharedDbContext, now, cancellationToken);
            }
            else if (message.EventType == typeof(CatalogEventEnvelope).AssemblyQualifiedName)
            {
                var appEvent = JsonSerializer.Deserialize<CatalogEventEnvelope>(message.Payload)
                    ?? throw new InvalidOperationException("Catalog event payload could not be deserialized.");
                await DispatchAsync(message.Id, appEvent, serviceProvider, sharedDbContext, now, cancellationToken);
            }
            else
            {
                throw new InvalidOperationException($"No outbox dispatcher is registered for event type {message.EventType}.");
            }

            message.Status = "Processed";
            message.ProcessedAt = now;
            message.LastError = null;
        }
        catch (Exception exception)
        {
            message.Attempts++;
            message.Status = message.Attempts >= MaxAttempts ? "DeadLetter" : "Failed";
            if (message.Status == "DeadLetter")
            {
                LenseeTelemetry.OutboxDeadLetters.Add(1, new KeyValuePair<string, object?>("event_type", message.EventType));
            }
            message.NextAttemptAt = now.AddSeconds(Math.Min(Math.Pow(2, message.Attempts), 3600));
            message.LastError = exception.Message;
            _logger.LogWarning(exception, "Outbox message {OutboxMessageId} failed on attempt {Attempt}.", message.Id, message.Attempts);
        }
    }

    private static async Task DispatchAsync<TEvent>(
        Guid outboxMessageId,
        TEvent appEvent,
        IServiceProvider serviceProvider,
        SharedDbContext sharedDbContext,
        DateTime now,
        CancellationToken cancellationToken)
        where TEvent : IAppEvent
    {
        foreach (var handler in serviceProvider.GetServices<IAppEventHandler<TEvent>>())
        {
            var handlerName = handler.GetType().FullName ?? handler.GetType().Name;
            var alreadyProcessed = await sharedDbContext.OutboxDeliveryReceipts
                .AnyAsync(receipt => receipt.OutboxMessageId == outboxMessageId && receipt.HandlerName == handlerName, cancellationToken);
            if (alreadyProcessed)
            {
                continue;
            }

            await handler.HandleAsync(appEvent, cancellationToken);
            sharedDbContext.OutboxDeliveryReceipts.Add(new OutboxDeliveryReceipt
            {
                Id = Guid.NewGuid(),
                OutboxMessageId = outboxMessageId,
                HandlerName = handlerName,
                ProcessedAt = now
            });
        }
    }
}
