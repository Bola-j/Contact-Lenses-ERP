namespace Lensee.SharedKernel.Abstractions;

public interface IAppEvent
{
    DateTime OccurredAt { get; }
}

public interface IAppEventPublisher
{
    Task PublishAsync<TEvent>(TEvent appEvent, CancellationToken cancellationToken = default)
        where TEvent : IAppEvent;
}

public interface IAppEventHandler<in TEvent>
    where TEvent : IAppEvent
{
    Task HandleAsync(TEvent appEvent, CancellationToken cancellationToken = default);
}
