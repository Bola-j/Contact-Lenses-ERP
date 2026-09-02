using System.Security.Claims;
using System.Text.Encodings.Web;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.Modules.Reporting.Data;
using Lensee.SharedKernel.Data;
using Lensee.SharedKernel.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Lensee.PostgresIntegrationTests;

/// <summary>
/// Real PostgreSQL HTTP host used by concurrency tests.  Test authentication is
/// intentionally limited to the test environment; every application DbContext
/// still uses the same Testcontainers connection and real EF migrations.
/// </summary>
internal sealed class PostgresApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public PostgresApplicationFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Database:AutoMigrate"] = "false",
                ["Jwt:Secret"] = "PostgresApplicationFactorySecret123!",
                ["Jwt:Issuer"] = "Lensee",
                ["Jwt:Audience"] = "Lensee.App",
                ["Shopify:Enabled"] = "false"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<NpgsqlConnection>();
            services.AddScoped(_ => new NpgsqlConnection(_connectionString));
            services.RemoveAll<IHostedService>();
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = PostgresTestAuthHandler.TestScheme;
                options.DefaultChallengeScheme = PostgresTestAuthHandler.TestScheme;
                options.DefaultForbidScheme = PostgresTestAuthHandler.TestScheme;
            }).AddScheme<AuthenticationSchemeOptions, PostgresTestAuthHandler>(PostgresTestAuthHandler.TestScheme, _ => { });
        });
    }

    public async Task MigrateAllAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        await provider.GetRequiredService<SharedDbContext>().Database.MigrateAsync();
        await provider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
        await provider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
        await provider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
        await provider.GetRequiredService<CrmDbContext>().Database.MigrateAsync();
        await provider.GetRequiredService<OperationsDbContext>().Database.MigrateAsync();
        await provider.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync();
        await provider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync();
        await provider.GetRequiredService<ReportingDbContext>().Database.MigrateAsync();
    }

    public async Task<PostgresOperationSeed> SeedOperationReferencesAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var mainLocationId = Guid.NewGuid();
        var onlineLocationId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var skuId = Guid.NewGuid();

        inventory.Locations.AddRange(
            new Location { Id = mainLocationId, Name = $"Main {mainLocationId:N}", LocationType = "MainWarehouse", IsActive = true },
            new Location { Id = onlineLocationId, Name = $"Online {onlineLocationId:N}", LocationType = "Online", IsActive = true });
        catalog.Categories.Add(new Category { Id = categoryId, Name = $"Category {categoryId:N}" });
        catalog.Brands.Add(new Brand { Id = brandId, Name = $"Brand {brandId:N}" });
        catalog.Products.Add(new Product
        {
            Id = productId,
            CategoryId = categoryId,
            BrandId = brandId,
            Name = $"Product {productId:N}",
            ProductType = "Lens",
            ExpiryType = "Batch",
            PiecesPerPack = 2,
            SellMode = "Both",
            ClinicalParams = "{}",
            ExtendedAttributes = "{}",
            IsActive = true,
            CreatedAt = DatabaseNow
        });
        catalog.Skus.Add(new Sku { Id = skuId, ProductId = productId, SkuCode = $"SKU-{skuId:N}", ColorName = "Blue", IsActive = true });
        await catalog.SaveChangesAsync();
        await inventory.SaveChangesAsync();
        return new PostgresOperationSeed(mainLocationId, onlineLocationId, skuId);
    }

    public async Task<Guid> SeedFinalizedCorrectionOperationAsync(PostgresOperationSeed seed)
    {
        await using var scope = Services.CreateAsyncScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var id = Guid.NewGuid();
        operations.OperationLogs.Add(new OperationLog
        {
            Id = id,
            OperationNumber = $"PG-CORRECTION-{id:N}",
            OperationType = "RetailSale",
            Status = "Completed",
            RecordKind = "Standard",
            SourceLocationId = seed.MainLocationId,
            CreatedBy = Guid.NewGuid(),
            CreatedAt = DatabaseNow,
            ConfirmedAt = DatabaseNow
        });
        await operations.SaveChangesAsync();
        return id;
    }

    public async Task SeedUserAsync(Guid userId, string role = "Admin")
    {
        await using var scope = Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        identity.Users.Add(new User
        {
            Id = userId,
            Username = $"pg-user-{userId:N}",
            FullName = "PostgreSQL test user",
            PasswordHash = "test-only",
            Role = role,
            IsActive = true,
            CreatedAt = DatabaseNow
        });
        await identity.SaveChangesAsync();
    }

    private static DateTime DatabaseNow => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
}

internal sealed class PostgresTestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string TestScheme = "PostgresTest";

    public PostgresTestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
        var claims = new List<Claim>
        {
            new(LenseeClaims.UserId, Request.Headers.TryGetValue("X-Test-UserId", out var userId) ? userId.ToString() : Guid.NewGuid().ToString()),
            new(LenseeClaims.Role, role.ToString()),
            new(ClaimTypes.Role, role.ToString())
        };
        if (Request.Headers.TryGetValue("X-Test-Permissions", out var permissions))
        {
            claims.AddRange(permissions.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(permission => new Claim("permission", permission)));
        }
        var identity = new ClaimsIdentity(claims, TestScheme);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
    }
}

internal static class PostgresTestClientExtensions
{
    public static void AuthorizeAs(this HttpClient client, string role, params string[] permissions)
    {
        client.DefaultRequestHeaders.Remove("X-Test-Role");
        client.DefaultRequestHeaders.Remove("X-Test-Permissions");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", string.Join(',', permissions));
    }

    public static void AuthorizeAs(this HttpClient client, string role, Guid userId, params string[] permissions)
    {
        client.AuthorizeAs(role, permissions);
        client.DefaultRequestHeaders.Remove("X-Test-UserId");
        client.DefaultRequestHeaders.Add("X-Test-UserId", userId.ToString());
    }
}

internal sealed record PostgresOperationSeed(Guid MainLocationId, Guid OnlineLocationId, Guid SkuId);
