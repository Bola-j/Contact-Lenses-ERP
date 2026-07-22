using System.Net;
using System.Net.Http.Json;
using Lensee.Modules.Identity.Data;
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

public sealed class UserEndpointContractTests : IClassFixture<UserEndpointFactory>
{
    private readonly UserEndpointFactory _factory;

    public UserEndpointContractTests(UserEndpointFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateUser_RejectsInvalidRequiredFieldsAndRole()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.UsersWrite, LenseePermissions.UsersPasswordWrite);

        var response = await client.PostAsJsonAsync("/api/v1/users", new
        {
            username = "",
            password = "short",
            fullName = "",
            role = "Owner",
            locationId = (Guid?)null
        });
        var body = await response.Content.ReadFromJsonAsync<ValidationProblemContract>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Username", body!.Errors.Keys);
        Assert.Contains("Password", body.Errors.Keys);
        Assert.Contains("FullName", body.Errors.Keys);
        Assert.Contains("Role", body.Errors.Keys);
    }

    [Fact]
    public async Task CreateUser_RejectsWarehouseClerkWithoutLocation()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.UsersWrite, LenseePermissions.UsersPasswordWrite);

        var response = await client.PostAsJsonAsync("/api/v1/users", new
        {
            username = "clerk",
            password = "Password123!",
            fullName = "Warehouse Clerk",
            role = LenseeRoles.WarehouseClerk,
            locationId = (Guid?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_RejectsLocationForNonWarehouseClerk()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.UsersWrite);

        var response = await client.PostAsJsonAsync("/api/v1/users", new
        {
            username = "accountant",
            password = "Password123!",
            fullName = "Accountant",
            role = LenseeRoles.Accountant,
            locationId = Guid.NewGuid()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_RejectsShortPassword()
    {
        var userId = await _factory.SeedUserAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.UsersWrite, LenseePermissions.UsersPasswordWrite);

        var response = await client.PatchAsJsonAsync($"/api/v1/users/{userId}/password", new
        {
            newPassword = "short"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

public sealed class UserEndpointFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"user-contracts-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=lensee_user_contract_tests;Username=test;Password=test",
                ["Jwt:Secret"] = "UserContractTestsNeedASecret123!",
                ["Jwt:Issuer"] = "Lensee",
                ["Jwt:Audience"] = "Lensee.App"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.RemoveAll<IAuditLogWriter>();
            services.AddDbContext<IdentityDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddSingleton<IAuditLogWriter, NoOpAuditLogWriter>();

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.TestScheme;
                options.DefaultChallengeScheme = TestAuthHandler.TestScheme;
                options.DefaultForbidScheme = TestAuthHandler.TestScheme;
            }).AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.TestScheme, _ => { });
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

    public async Task<Guid> SeedUserAsync()
    {
        await ResetAsync();
        using var scope = Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var id = Guid.NewGuid();
        identity.Users.Add(new User
        {
            Id = id,
            Username = "existing",
            FullName = "Existing User",
            PasswordHash = "hash",
            Role = LenseeRoles.Admin,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await identity.SaveChangesAsync();
        return id;
    }
}
