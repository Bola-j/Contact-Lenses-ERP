using System.Security.Claims;
using Lensee.Host.Infrastructure;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Inventory.Services;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lensee.PostgresIntegrationTests;

public sealed class InventoryReceiptCommandPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("lensee")
        .WithUsername("lensee_user")
        .WithPassword("SomeStrongPassword123!")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var catalog = CreateCatalogContext(connection);
        await using var inventory = CreateInventoryContext(connection);
        await catalog.Database.MigrateAsync();
        await inventory.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [PostgreSqlIntegrationFact]
    public async Task ReceiptExecution_RechecksSkuAfterConcurrentDeactivation_WithoutPartialWrites()
    {
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        await SeedActiveReferencesAsync(locationId, skuId);

        await using var deactivationConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await deactivationConnection.OpenAsync();
        await using var deactivationTransaction = await deactivationConnection.BeginTransactionAsync();
        await using (var lockSku = deactivationConnection.CreateCommand())
        {
            lockSku.Transaction = deactivationTransaction;
            lockSku.CommandText = "select 1 from catalog.skus where id = @sku_id for update;";
            lockSku.Parameters.AddWithValue("sku_id", skuId);
            await lockSku.ExecuteScalarAsync();
        }

        await using var receiptConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await receiptConnection.OpenAsync();
        await using var inventory = CreateInventoryContext(receiptConnection);
        await using var catalog = CreateCatalogContext(receiptConnection);
        await using var identity = CreateIdentityContext(receiptConnection);
        var receiptService = new InventoryReceiptCommandService(
            inventory,
            catalog,
            identity,
            new StockLedgerService(inventory, new IntegrationClock()),
            new FixedCurrentUser(Guid.NewGuid()),
            new NoOpAuditLogWriter());

        var execution = receiptService.ExecuteAsync(
            Guid.NewGuid(),
            "receipt-deactivation-race",
            locationId,
            skuId,
            3,
            "deactivation-race",
            null,
            null,
            CancellationToken.None);

        await WaitForLockWaiterAsync();
        await using (var deactivateSku = deactivationConnection.CreateCommand())
        {
            deactivateSku.Transaction = deactivationTransaction;
            deactivateSku.CommandText = "update catalog.skus set is_active = false where id = @sku_id;";
            deactivateSku.Parameters.AddWithValue("sku_id", skuId);
            await deactivateSku.ExecuteNonQueryAsync();
        }
        await deactivationTransaction.CommitAsync();

        var result = await execution;
        Assert.Equal("SkuId", result.InvalidReference);

        await using var verification = CreateInventoryContext();
        Assert.Empty(await verification.InventoryReceiptCommands.ToListAsync());
        Assert.Empty(await verification.InventoryBatches.ToListAsync());
        Assert.Empty(await verification.StockTransactions.ToListAsync());
    }

    private async Task SeedActiveReferencesAsync(Guid locationId, Guid skuId)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var catalog = CreateCatalogContext(connection);
        await using var inventory = CreateInventoryContext(connection);
        var categoryId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        catalog.Categories.Add(new Category { Id = categoryId, Name = $"Receipt race category {categoryId:N}" });
        catalog.Brands.Add(new Brand { Id = brandId, Name = $"Receipt race brand {brandId:N}", CreatedAt = now });
        catalog.Products.Add(new Product
        {
            Id = productId,
            CategoryId = categoryId,
            BrandId = brandId,
            Name = $"Receipt race product {productId:N}",
            ProductType = "Lens",
            ExpiryType = "Batch",
            OpenedExpiryDuration = "6 months",
            PiecesPerPack = 2,
            SellMode = "Both",
            ClinicalParams = "{}",
            ExtendedAttributes = "{}",
            IsActive = true,
            CreatedAt = now
        });
        catalog.Skus.Add(new Sku { Id = skuId, ProductId = productId, SkuCode = $"RR-{skuId:N}", IsActive = true });
        inventory.Locations.Add(new Location
        {
            Id = locationId,
            Name = "Receipt race location",
            LocationType = "MainWarehouse",
            IsActive = true
        });
        await catalog.SaveChangesAsync();
        await inventory.SaveChangesAsync();
    }

    private async Task WaitForLockWaiterAsync()
    {
        await using var inspection = new NpgsqlConnection(_postgres.GetConnectionString());
        await inspection.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var command = inspection.CreateCommand();
            command.CommandText = "select count(*) from pg_stat_activity where wait_event_type = 'Lock' and state = 'active';";
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) >= 1)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Expected receipt execution to wait for the SKU row lock.");
    }

    private InventoryDbContext CreateInventoryContext(NpgsqlConnection? connection = null) =>
        new(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connection ?? new NpgsqlConnection(_postgres.GetConnectionString()))
            .Options);

    private CatalogDbContext CreateCatalogContext(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connection).Options);

    private IdentityDbContext CreateIdentityContext(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connection).Options);

    private sealed class IntegrationClock : IClock
    {
        public DateTime UtcNow { get; } = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

        public DateTime EgyptNow { get; } = new(2026, 9, 2, 15, 0, 0, DateTimeKind.Unspecified);
    }

    private sealed class FixedCurrentUser(Guid userId) : ICurrentUser
    {
        public Guid? UserId { get; } = userId;
        public string? Role => "Admin";
        public Guid? LocationId => null;
        public bool IsAuthenticated => true;
        public ClaimsPrincipal Principal { get; } = new(new ClaimsIdentity());
    }

    private sealed class NoOpAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(string entityType, Guid entityId, string action, object? changedFields = null, int? stockDeltaApplied = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteSystemAsync(string actorName, string entityType, Guid entityId, string action, object? changedFields = null, int? stockDeltaApplied = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteForUserAsync(Guid actorUserId, string actorName, string entityType, Guid entityId, string action, object? changedFields = null, int? stockDeltaApplied = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
