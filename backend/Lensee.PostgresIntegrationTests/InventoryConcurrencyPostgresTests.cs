using Lensee.Modules.Inventory.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lensee.PostgresIntegrationTests;

public sealed class InventoryConcurrencyPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("lensee")
        .WithUsername("lensee_user")
        .WithPassword("SomeStrongPassword123!")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var database = CreateContext();
        await database.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [PostgreSqlIntegrationFact]
    public async Task StockBalanceXmin_RejectsLostUpdate()
    {
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        await using (var setup = CreateContext())
        {
            setup.Locations.Add(new Location
            {
                Id = locationId,
                Name = "Concurrency test warehouse",
                LocationType = "MainWarehouse",
                IsActive = true
            });
            setup.StockBalances.Add(new StockBalance
            {
                Id = Guid.NewGuid(),
                LocationId = locationId,
                SkuId = skuId,
                AvailableQty = 10,
                LastUpdated = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            });
            await setup.SaveChangesAsync();
        }

        await using var first = CreateContext();
        await using var second = CreateContext();
        var firstBalance = await first.StockBalances.SingleAsync(balance => balance.LocationId == locationId && balance.SkuId == skuId);
        var secondBalance = await second.StockBalances.SingleAsync(balance => balance.LocationId == locationId && balance.SkuId == skuId);

        firstBalance.AvailableQty = 9;
        await first.SaveChangesAsync();

        secondBalance.AvailableQty = 8;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());

        await using var verification = CreateContext();
        var persisted = await verification.StockBalances.SingleAsync(balance => balance.LocationId == locationId && balance.SkuId == skuId);
        Assert.Equal(9, persisted.AvailableQty);
    }

    private InventoryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);
}
