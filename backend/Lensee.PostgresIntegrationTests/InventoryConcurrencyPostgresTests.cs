using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Inventory.Services;
using Lensee.SharedKernel.Abstractions;
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

    [PostgreSqlIntegrationFact]
    public async Task ConcurrentWarehouseReservations_ReturnOneConflictWithoutPartialLedger()
    {
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        await using (var setup = CreateContext())
        {
            setup.Locations.Add(new Location
            {
                Id = locationId,
                Name = "Reservation concurrency warehouse",
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

        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var clock = new IntegrationClock();
        var firstService = new StockLedgerService(firstContext, clock);
        var secondService = new StockLedgerService(secondContext, clock);
        var userId = Guid.NewGuid();

        var results = await Task.WhenAll(
            CaptureAsync(() => firstService.ReserveInWarehouseAsync(locationId, skuId, 6, userId)),
            CaptureAsync(() => secondService.ReserveInWarehouseAsync(locationId, skuId, 6, userId)));

        Assert.Single(results, exception => exception is null);
        Assert.Single(results, exception => exception is StockWriteConflictException);

        await using var verification = CreateContext();
        var balance = await verification.StockBalances.SingleAsync(balance => balance.LocationId == locationId && balance.SkuId == skuId);
        Assert.Equal(4, balance.AvailableQty);
        Assert.Equal(6, balance.ReservedInWarehouseQty);
        Assert.Single(await verification.StockTransactions.ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task ReceiptCommand_UniqueKeyAndLedgerLinkPreventDuplicateCompletion()
    {
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var key = Guid.NewGuid();
        Guid batchId;
        Guid transactionId;
        await using (var setup = CreateContext())
        {
            setup.Locations.Add(new Location
            {
                Id = locationId,
                Name = "Receipt idempotency warehouse",
                LocationType = "MainWarehouse",
                IsActive = true
            });
            await setup.SaveChangesAsync();

            var receipt = await new StockLedgerService(setup, new IntegrationClock())
                .ReceiveReceiptWithLedgerAsync(locationId, skuId, 5, userId, "receipt-key");
            batchId = receipt.Batch.Id;
            transactionId = receipt.StockTransactionId;
            setup.InventoryReceiptCommands.Add(new InventoryReceiptCommand
            {
                Id = Guid.NewGuid(),
                Key = key,
                RequestHash = "A",
                Status = "Completed",
                BatchId = batchId,
                StockTransactionId = transactionId,
                ResponseBatchQuantity = 5,
                ResponseStatusCode = 201,
                ResponseBody = "{}",
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                LastSeenAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            });
            await setup.SaveChangesAsync();
        }

        await using (var duplicateKey = CreateContext())
        {
            duplicateKey.InventoryReceiptCommands.Add(new InventoryReceiptCommand
            {
                Id = Guid.NewGuid(),
                Key = key,
                RequestHash = "B",
                Status = "Pending",
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                LastSeenAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateKey.SaveChangesAsync());
        }

        await using (var duplicateLedgerLink = CreateContext())
        {
            duplicateLedgerLink.InventoryReceiptCommands.Add(new InventoryReceiptCommand
            {
                Id = Guid.NewGuid(),
                Key = Guid.NewGuid(),
                RequestHash = "C",
                Status = "Completed",
                BatchId = batchId,
                StockTransactionId = transactionId,
                ResponseBatchQuantity = 5,
                ResponseStatusCode = 201,
                ResponseBody = "{}",
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                LastSeenAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateLedgerLink.SaveChangesAsync());
        }

        await using var verification = CreateContext();
        Assert.Single(await verification.InventoryReceiptCommands.ToListAsync());
        Assert.Equal(5, await verification.InventoryBatches.Where(batch => batch.Id == batchId).Select(batch => batch.Quantity).SingleAsync());
        Assert.Single(await verification.StockTransactions.Where(transaction => transaction.Id == transactionId).ToListAsync());
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private InventoryDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);

    private sealed class IntegrationClock : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

        public DateTime EgyptNow { get; } = new(2026, 8, 25, 15, 0, 0, DateTimeKind.Unspecified);
    }
}
