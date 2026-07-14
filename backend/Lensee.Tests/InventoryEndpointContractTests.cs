using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Lensee.Tests;

public sealed class InventoryEndpointContractTests : IClassFixture<InventoryEndpointFactory>
{
    private readonly InventoryEndpointFactory _factory;

    public InventoryEndpointContractTests(InventoryEndpointFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Admin_CanReadInventoryAndPostReceipt()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.InventoryRead, LenseePermissions.InventoryWrite);

        var receipt = await client.PostAsJsonAsync("/api/v1/inventory/receipts", new
        {
            locationId = seed.LocationA,
            skuId = seed.SkuId,
            packQuantity = 7,
            lotNumber = "A-1",
            expiryDate = "2028-06-01",
            notes = "Contract test"
        });
        var balances = await client.GetFromJsonAsync<PagedContract<StockBalanceContract>>("/api/v1/inventory/stock-balances");

        Assert.Equal(HttpStatusCode.Created, receipt.StatusCode);
        Assert.NotNull(balances);
        Assert.Contains(balances!.Items, balance => balance.LocationId == seed.LocationA && balance.SkuId == seed.SkuId && balance.AvailablePacks == 7);
        Assert.Contains(balances.Items, balance => balance.LocationId == seed.LocationA && balance.SkuId == seed.SkuId && balance.AvailablePieces is null);
    }

    [Fact]
    public async Task CLevel_CanReadButCannotWriteInventory()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.CLevel, LenseePermissions.InventoryRead);

        var read = await client.GetAsync("/api/v1/inventory/locations");
        var write = await client.PostAsJsonAsync("/api/v1/inventory/receipts", new { locationId = seed.LocationA, skuId = seed.SkuId, packQuantity = 1 });

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    [Fact]
    public async Task OnlineLocation_ReturnsDerivedAvailablePieces()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.InventoryRead, LenseePermissions.InventoryWrite);

        await client.PostAsJsonAsync("/api/v1/inventory/receipts", new
        {
            locationId = seed.LocationB,
            skuId = seed.SkuId,
            packQuantity = 7
        });
        var balances = await client.GetFromJsonAsync<PagedContract<StockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.LocationB}");

        Assert.Contains(balances!.Items, balance => balance.LocationId == seed.LocationB && balance.AvailablePacks == 7 && balance.AvailablePieces == 14);
    }

    [Fact]
    public async Task IncludeZeroStock_ShowsActiveSkusWithoutStockBalance()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.InventoryRead);

        var normal = await client.GetFromJsonAsync<PagedContract<StockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.LocationA}");
        var withZero = await client.GetFromJsonAsync<PagedContract<StockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.LocationA}&includeZeroStock=true");

        Assert.Empty(normal!.Items);
        Assert.Contains(withZero!.Items, balance =>
            balance.LocationId == seed.LocationA &&
            balance.SkuId == seed.SkuId &&
            balance.AvailablePacks == 0 &&
            balance.AvailablePieces is null);
    }

    [Fact]
    public async Task WarehouseClerk_ReadsOnlyAssignedLocation()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAsAtLocation(LenseeRoles.WarehouseClerk, seed.LocationA, LenseePermissions.InventoryRead);

        var ownLocations = await client.GetFromJsonAsync<IReadOnlyList<LocationContract>>("/api/v1/inventory/locations");
        var otherLocation = await client.GetAsync($"/api/v1/inventory/stock-balances?locationId={seed.LocationB}");

        Assert.Single(ownLocations!);
        Assert.Equal(seed.LocationA, ownLocations![0].Id);
        Assert.Equal(HttpStatusCode.Forbidden, otherLocation.StatusCode);
    }

    [Fact]
    public async Task Accountant_CannotReadInventory()
    {
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Accountant, LenseePermissions.PaymentsRead);

        var response = await client.GetAsync("/api/v1/inventory/locations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminCanEditTargetQuantity_ButNonAdminCannot()
    {
        var seed = await _factory.SeedAsync(withBalance: true);
        using var admin = _factory.CreateClient();
        admin.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.InventoryRead, LenseePermissions.InventoryWrite);
        using var clerk = _factory.CreateClient();
        clerk.AuthorizeAsAtLocation(LenseeRoles.WarehouseClerk, seed.LocationA, LenseePermissions.InventoryRead);

        var adminResponse = await admin.PutAsJsonAsync($"/api/v1/inventory/stock-balances/{seed.LocationA}/{seed.SkuId}/target", new { targetPacks = 10 });
        var clerkResponse = await clerk.PutAsJsonAsync($"/api/v1/inventory/stock-balances/{seed.LocationA}/{seed.SkuId}/target", new { targetPacks = 11 });

        Assert.Equal(HttpStatusCode.NoContent, adminResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, clerkResponse.StatusCode);
    }

    [Fact]
    public async Task GenericStockTransactionPost_IsNotExposed()
    {
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.InventoryRead, LenseePermissions.InventoryWrite);

        var response = await client.PostAsJsonAsync("/api/v1/inventory/transactions", new { });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}

public sealed class InventoryEndpointFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"inventory-contracts-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=lensee_inventory_contract_tests;Username=test;Password=test",
                ["Jwt:Secret"] = "InventoryContractTestsNeedASecret123!",
                ["Jwt:Issuer"] = "Lensee",
                ["Jwt:Audience"] = "Lensee.App"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<CatalogDbContext>>();
            services.RemoveAll<DbContextOptions<InventoryDbContext>>();
            services.RemoveAll<IAuditLogWriter>();
            services.AddDbContext<CatalogDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<InventoryDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddSingleton<IAuditLogWriter, NoOpAuditLogWriter>();

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.TestScheme;
                options.DefaultChallengeScheme = TestAuthHandler.TestScheme;
                options.DefaultForbidScheme = TestAuthHandler.TestScheme;
            }).AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.TestScheme, _ => { });
        });
    }

    public async Task<InventorySeed> SeedAsync(bool withBalance = false)
    {
        using var scope = Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var locationA = Guid.NewGuid();
        var locationB = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var skuId = Guid.NewGuid();

        inventory.Locations.AddRange(
            new Location { Id = locationA, Name = $"Roxy {locationA:N}", LocationType = "MainWarehouse", IsActive = true },
            new Location { Id = locationB, Name = $"Online {locationB:N}", LocationType = "Online", IsActive = true });
        catalog.Categories.Add(new Category { Id = categoryId, Name = $"Lenses {categoryId:N}" });
        catalog.Brands.Add(new Brand { Id = brandId, Name = $"Lansee {brandId:N}" });
        catalog.Products.Add(new Product
        {
            Id = productId,
            CategoryId = categoryId,
            BrandId = brandId,
            Name = $"Monthly Lens {productId:N}",
            ProductType = "Lens",
            ExpiryType = "Batch",
            OpenedExpiryDuration = "6 months",
            PiecesPerPack = 2,
            SellMode = "Both",
            ClinicalParams = "{}",
            ExtendedAttributes = "{}",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        catalog.Skus.Add(new Sku { Id = skuId, ProductId = productId, SkuCode = $"LEN-{skuId:N}", ColorName = "Hazel", IsActive = true });
        if (withBalance)
        {
            inventory.StockBalances.Add(new StockBalance
            {
                Id = Guid.NewGuid(),
                LocationId = locationA,
                SkuId = skuId,
                AvailableQty = 2,
                LastUpdated = DateTime.UtcNow
            });
        }

        await catalog.SaveChangesAsync();
        await inventory.SaveChangesAsync();
        return new InventorySeed(locationA, locationB, skuId);
    }
}

internal sealed class NoOpAuditLogWriter : IAuditLogWriter
{
    public Task WriteAsync(string entityType, Guid entityId, string action, object? changedFields = null, int? stockDeltaApplied = null, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

public sealed record InventorySeed(Guid LocationA, Guid LocationB, Guid SkuId);

public sealed record LocationContract(Guid Id, string Name, string LocationType, bool IsActive);

public sealed record StockBalanceContract(Guid LocationId, string LocationType, Guid SkuId, int AvailablePacks, int? AvailablePieces);
