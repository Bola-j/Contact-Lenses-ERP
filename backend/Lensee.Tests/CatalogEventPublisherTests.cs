using Lensee.Modules.Catalog.Domain.Events;
using Lensee.Modules.Catalog.Services;
using Xunit;

namespace Lensee.Tests;

public sealed class CatalogEventPublisherTests
{
    [Fact]
    public async Task NoOpCatalogEventPublisher_AcceptsCatalogEvents()
    {
        ICatalogEventPublisher publisher = new NoOpCatalogEventPublisher();

        await publisher.PublishAsync(new ProductCreated(Guid.NewGuid(), DateTime.UtcNow));
    }
}
