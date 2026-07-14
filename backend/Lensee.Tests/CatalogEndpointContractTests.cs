using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Lensee.Modules.Catalog.Data;
using Lensee.SharedKernel.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class CatalogEndpointContractTests : IClassFixture<CatalogEndpointFactory>
{
    private readonly CatalogEndpointFactory _factory;

    public CatalogEndpointContractTests(CatalogEndpointFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CatalogRead_RequiresAuthentication()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/catalog/products");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Accountant_CannotReadCatalog()
    {
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Accountant, LenseePermissions.PaymentsRead);

        var response = await client.GetAsync("/api/v1/catalog/products");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CLevel_CanReadCatalogProducts()
    {
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.CLevel, LenseePermissions.CatalogRead);

        var response = await client.GetAsync("/api/v1/catalog/products");
        var body = await response.Content.ReadFromJsonAsync<PagedContract<ProductListContract>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal(25, body!.PageSize);
    }

    [Fact]
    public async Task WarehouseClerk_CannotWriteCatalog()
    {
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.WarehouseClerk, LenseePermissions.CatalogRead);

        var response = await client.PostAsJsonAsync("/api/v1/catalog/categories", new { parentId = (Guid?)null, name = "Test" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminWrite_ReturnsValidationProblemShape()
    {
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.CatalogRead, LenseePermissions.CatalogWrite);

        var response = await client.PostAsJsonAsync("/api/v1/catalog/products", new
        {
            categoryId = Guid.Empty,
            brandId = Guid.Empty,
            name = "",
            productType = "Lens",
            expiryType = "Batch",
            sealedExpiryDuration = (string?)null,
            sealedExpiryRate = (string?)null,
            openedExpiryDuration = "6 months",
            piecesPerPack = 1,
            sellMode = "SinglePiece",
            clinicalParams = "",
            extendedAttributes = "{}"
        });
        var body = await response.Content.ReadFromJsonAsync<ValidationProblemContract>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(body?.Errors);
        Assert.NotEmpty(body!.Errors);
    }

}

public sealed class CatalogEndpointFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"catalog-contracts-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=lensee_contract_tests;Username=test;Password=test",
                ["Jwt:Secret"] = "ContractTestsNeedASecretWithEnoughLength123!",
                ["Jwt:Issuer"] = "Lensee",
                ["Jwt:Audience"] = "Lensee.App"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<CatalogDbContext>>();
            services.AddDbContext<CatalogDbContext>(options => options.UseInMemoryDatabase(_databaseName));

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.TestScheme;
                options.DefaultChallengeScheme = TestAuthHandler.TestScheme;
                options.DefaultForbidScheme = TestAuthHandler.TestScheme;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.TestScheme, _ => { });
        });
    }
}

internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string TestScheme = "Test";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Role", out var roleValues))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(LenseeClaims.UserId, Guid.NewGuid().ToString()),
            new(LenseeClaims.Role, roleValues.ToString()),
            new(ClaimTypes.Role, roleValues.ToString())
        };

        if (Request.Headers.TryGetValue("X-Test-Permissions", out var permissionValues))
        {
            claims.AddRange(permissionValues
                .ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(permission => new Claim("permission", permission)));
        }

        if (Request.Headers.TryGetValue("X-Test-LocationId", out var locationIdValues))
        {
            claims.Add(new Claim(LenseeClaims.LocationId, locationIdValues.ToString()));
        }

        var identity = new ClaimsIdentity(claims, TestScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

internal static class CatalogEndpointClientExtensions
{
    public static void AuthorizeAs(this HttpClient client, string role, params string[] permissions)
    {
        client.DefaultRequestHeaders.Remove("X-Test-Role");
        client.DefaultRequestHeaders.Remove("X-Test-Permissions");
        client.DefaultRequestHeaders.Remove("X-Test-LocationId");
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Permissions", string.Join(",", permissions));
    }

    public static void AuthorizeAsAtLocation(this HttpClient client, string role, Guid locationId, params string[] permissions)
    {
        client.AuthorizeAs(role, permissions);
        client.DefaultRequestHeaders.Add("X-Test-LocationId", locationId.ToString());
    }
}

internal sealed record PagedContract<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

internal sealed record ProductListContract(Guid Id, string Name, string ProductType, bool IsActive);

internal sealed record ValidationProblemContract(Dictionary<string, string[]> Errors);
