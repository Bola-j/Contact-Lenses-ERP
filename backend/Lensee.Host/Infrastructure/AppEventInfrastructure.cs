using Lensee.Modules.Notifications.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Security;

namespace Lensee.Host.Infrastructure;

public sealed class InProcessAppEventPublisher : IAppEventPublisher
{
    private readonly IServiceProvider _serviceProvider;

    public InProcessAppEventPublisher(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task PublishAsync<TEvent>(TEvent appEvent, CancellationToken cancellationToken = default)
        where TEvent : IAppEvent
    {
        foreach (var handler in _serviceProvider.GetServices<IAppEventHandler<TEvent>>())
        {
            await handler.HandleAsync(appEvent, cancellationToken);
        }
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
            ? $" Operation {appEvent.OperationId.Value.ToString()[..8]} is linked."
            : string.Empty;
        var merchant = appEvent.MerchantId.HasValue
            ? $" Merchant {appEvent.MerchantId.Value.ToString()[..8]} is affected."
            : string.Empty;

        return $"{appEvent.Message}{merchant}{context} Open Payments to review assignment, approval state, paid amount, and remaining effect.";
    }
}
