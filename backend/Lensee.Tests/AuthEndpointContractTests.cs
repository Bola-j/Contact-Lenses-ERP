using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Lensee.Host.Infrastructure;
using Lensee.Modules.Identity.Data;
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
    public async Task Refresh_WithoutCookieOrBodyToken_ReturnsNoContentForAnonymousSessionRestore()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new
        {
            refreshToken = ""
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Login_SetsHttpOnlyRefreshCookieAndDoesNotReturnRefreshToken()
    {
        await _factory.SeedUserAsync("admin", "Password123!", LenseeRoles.Admin);
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "admin",
            password = "Password123!"
        });
        var body = await response.Content.ReadFromJsonAsync<AuthBodyContract>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
        Assert.NotNull(body.User);
        Assert.DoesNotContain("refreshToken", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        Assert.Contains("lensee.refresh=", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_CanReadRefreshTokenFromCookieAndRotatesCookie()
    {
        await _factory.SeedUserAsync("admin", "Password123!", LenseeRoles.Admin);
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "admin",
            password = "Password123!"
        });
        var cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("Cookie", cookie);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<AuthBodyContract>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(body!.AccessToken));
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.Contains("lensee.refresh=", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Refresh_ReuseOfRotatedToken_RevokesTheReplacementSession()
    {
        await _factory.SeedUserAsync("admin", "Password123!", LenseeRoles.Admin);
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "admin",
            password = "Password123!"
        });
        var originalCookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

        var firstRefresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = JsonContent.Create(new { })
        };
        firstRefresh.Headers.Add("Cookie", originalCookie);
        var rotated = await client.SendAsync(firstRefresh);
        var replacementCookie = rotated.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);

        var replay = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = JsonContent.Create(new { })
        };
        replay.Headers.Add("Cookie", originalCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(replay)).StatusCode);

        var replacementRefresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = JsonContent.Create(new { })
        };
        replacementRefresh.Headers.Add("Cookie", replacementCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(replacementRefresh)).StatusCode);
    }

    [Fact]
    public async Task Logout_ClearsRefreshCookie()
    {
        await _factory.SeedUserAsync("admin", "Password123!", LenseeRoles.Admin);
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "admin",
            password = "Password123!"
        });
        var loginBody = await login.Content.ReadFromJsonAsync<AuthBodyContract>();
        var cookie = login.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginBody!.AccessToken);
        request.Headers.Add("Cookie", cookie);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value =>
            value.Contains("lensee.refresh=", StringComparison.OrdinalIgnoreCase) &&
            value.Contains("expires=", StringComparison.OrdinalIgnoreCase));
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

    public async Task<Guid> SeedUserAsync(string username, string password, string role)
    {
        await ResetAsync();
        using var scope = Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var id = Guid.NewGuid();
        var hasher = new PasswordHasher();
        identity.Users.Add(new User
        {
            Id = id,
            Username = username,
            FullName = username,
            PasswordHash = hasher.Hash(password),
            Role = role,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await identity.SaveChangesAsync();
        return id;
    }
}

public sealed record AuthBodyContract(string AccessToken, SessionResponseContract User);

public sealed record SessionResponseContract(Guid UserId, string Role, Guid? LocationId);
