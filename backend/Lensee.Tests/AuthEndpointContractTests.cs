using System.Net;
using System.Net.Http.Json;
using Lensee.Modules.Identity.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Lensee.Tests;

public sealed class AuthEndpointContractTests : IClassFixture<AuthEndpointFactory>
{
    private readonly AuthEndpointFactory _factory;

    public AuthEndpointContractTests(AuthEndpointFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_RejectsBlankCredentials()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "",
            password = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_RejectsBlankRefreshToken()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            refreshToken = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

public sealed class AuthEndpointFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"auth-contracts-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=lensee_auth_contract_tests;Username=test;Password=test",
                ["Jwt:Secret"] = "AuthContractTestsNeedASecret123!",
                ["Jwt:Issuer"] = "Lensee",
                ["Jwt:Audience"] = "Lensee.App"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.AddDbContext<IdentityDbContext>(options => options.UseInMemoryDatabase(_databaseName));
        });
    }

    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        identity.RefreshTokens.RemoveRange(identity.RefreshTokens);
        identity.Users.RemoveRange(identity.Users);
        await identity.SaveChangesAsync();
    }
}
