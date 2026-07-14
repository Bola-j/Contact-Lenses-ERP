using Lensee.Modules.Catalog.Domain.Events;

namespace Lensee.Modules.Catalog.Services;

public interface ICatalogEventPublisher
{
    Task PublishAsync(CatalogEvent catalogEvent, CancellationToken cancellationToken = default);
}

public sealed class NoOpCatalogEventPublisher : ICatalogEventPublisher
{
    public Task PublishAsync(CatalogEvent catalogEvent, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
