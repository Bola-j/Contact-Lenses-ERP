using System.Net;
using System.Net.Http.Json;
using Lensee.Host.Endpoints;
using Lensee.Host.Infrastructure;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
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
    public async Task AdminCanCreateEmployeeAccountWithCredentials()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.UsersWrite);

        var response = await client.PostAsJsonAsync("/api/v1/users", new
        {
            username = "warehouse.ahmed",
            password = "SecurePass123!",
            fullName = "Ahmed Hassan",
            role = LenseeRoles.WarehouseClerk,
            locationId = Guid.NewGuid()
        });
        var created = await response.Content.ReadFromJsonAsync<UserResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(created);
        Assert.Equal("warehouse.ahmed", created!.Username);
        Assert.Equal(LenseeRoles.WarehouseClerk, created.Role);

        using var scope = _factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var stored = await identity.Users.SingleAsync(user => user.Id == created.Id);
        Assert.True(hasher.Verify("SecurePass123!", stored.PasswordHash));
    }

    [Fact]
    public async Task ErpAdminCannotCreateEmployeeAccounts()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.ERPAdmin, LenseePermissions.UsersWrite);

        var response = await client.PostAsJsonAsync("/api/v1/users", new
        {
            username = "blocked.user",
            password = "SecurePass123!",
            fullName = "Blocked User",
            role = LenseeRoles.Accountant,
            locationId = (Guid?)null
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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

    [Fact]
    public async Task CreateUser_RejectsCaseInsensitiveDuplicateUsernameWithClearMessage()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.UsersWrite);

        var first = await client.PostAsJsonAsync("/api/v1/users", new { username = "Ahmed", password = "Password123!", fullName = "Ahmed One", role = LenseeRoles.Accountant, locationId = (Guid?)null });
        var duplicate = await client.PostAsJsonAsync("/api/v1/users", new { username = " ahmed ", password = "Password123!", fullName = "Ahmed Two", role = LenseeRoles.Accountant, locationId = (Guid?)null });
        var problem = await duplicate.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("This username is already in use. Choose a different username.", problem!.Detail);
    }

    [Fact]
    public async Task CreateUser_RejectsWhitespaceInsideUsername()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.UsersWrite);

        var response = await client.PostAsJsonAsync("/api/v1/users", new { username = "ahmed hassan", password = "Password123!", fullName = "Ahmed Hassan", role = LenseeRoles.Accountant, locationId = (Guid?)null });
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemContract>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Username cannot contain spaces.", problem!.Errors["Username"].Single());
    }

    [Fact]
    public async Task DeleteUser_OnlyPrimaryAdminCanDeleteAnotherAdmin()
    {
        await _factory.ResetAsync();
        var primaryAdminId = Guid.NewGuid();
        var otherAdminId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identity.Users.AddRange(
                new User { Id = primaryAdminId, Username = "bola", FullName = "Lansee Admin", PasswordHash = "hash", Role = LenseeRoles.Admin, IsPrimaryAdmin = true, IsActive = true, CreatedAt = DateTime.UtcNow },
                new User { Id = otherAdminId, Username = "bola1", FullName = "Bola", PasswordHash = "hash", Role = LenseeRoles.Admin, IsActive = true, CreatedAt = DateTime.UtcNow },
                new User { Id = employeeId, Username = "employee", FullName = "Employee", PasswordHash = "hash", Role = LenseeRoles.Accountant, IsActive = true, CreatedAt = DateTime.UtcNow, CreatedByAdminId = primaryAdminId });
            await identity.SaveChangesAsync();
        }

        using var otherAdmin = _factory.CreateClient();
        otherAdmin.AuthorizeAs(LenseeRoles.Admin, otherAdminId, LenseePermissions.UsersDelete);
        var restrictedAdminDelete = await otherAdmin.DeleteAsync($"/api/v1/users/{primaryAdminId}");
        var employeeDelete = await otherAdmin.DeleteAsync($"/api/v1/users/{employeeId}");

        using var primaryAdmin = _factory.CreateClient();
        primaryAdmin.AuthorizeAs(LenseeRoles.Admin, primaryAdminId, LenseePermissions.UsersDelete);
        var allowedAdminDelete = await primaryAdmin.DeleteAsync($"/api/v1/users/{otherAdminId}");

        Assert.Equal(HttpStatusCode.Forbidden, restrictedAdminDelete.StatusCode);
        Assert.True(employeeDelete.StatusCode == HttpStatusCode.NoContent, await employeeDelete.Content.ReadAsStringAsync());
        Assert.True(allowedAdminDelete.StatusCode == HttpStatusCode.NoContent, await allowedAdminDelete.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PrimaryAdminCanTransferPrimaryAuthorityToAnotherActiveAdmin()
    {
        await _factory.ResetAsync();
        var primaryAdminId = Guid.NewGuid();
        var targetAdminId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identity.Users.AddRange(
                new User { Id = primaryAdminId, Username = "primary", FullName = "Primary Admin", PasswordHash = "hash", Role = LenseeRoles.Admin, IsPrimaryAdmin = true, IsActive = true, CreatedAt = DateTime.UtcNow },
                new User { Id = targetAdminId, Username = "target", FullName = "Target Admin", PasswordHash = "hash", Role = LenseeRoles.Admin, IsActive = true, CreatedAt = DateTime.UtcNow });
            await identity.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, primaryAdminId, LenseePermissions.UsersDelete);
        var response = await client.PostAsync($"/api/v1/users/{targetAdminId}/transfer-primary", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var verificationScope = _factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False((await verificationDb.Users.SingleAsync(user => user.Id == primaryAdminId)).IsPrimaryAdmin);
        Assert.True((await verificationDb.Users.SingleAsync(user => user.Id == targetAdminId)).IsPrimaryAdmin);
    }

    [Fact]
    public async Task TransferPrimaryAuthorityRejectsNonPrimaryAndInvalidTargets()
    {
        await _factory.ResetAsync();
        var primaryAdminId = Guid.NewGuid();
        var secondaryAdminId = Guid.NewGuid();
        var inactiveAdminId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identity.Users.AddRange(
                new User { Id = primaryAdminId, Username = "primary", FullName = "Primary Admin", PasswordHash = "hash", Role = LenseeRoles.Admin, IsPrimaryAdmin = true, IsActive = true, CreatedAt = DateTime.UtcNow },
                new User { Id = secondaryAdminId, Username = "secondary", FullName = "Secondary Admin", PasswordHash = "hash", Role = LenseeRoles.Admin, IsActive = true, CreatedAt = DateTime.UtcNow },
                new User { Id = inactiveAdminId, Username = "inactive", FullName = "Inactive Admin", PasswordHash = "hash", Role = LenseeRoles.Admin, IsActive = false, CreatedAt = DateTime.UtcNow },
                new User { Id = employeeId, Username = "employee", FullName = "Employee", PasswordHash = "hash", Role = LenseeRoles.Accountant, IsActive = true, CreatedAt = DateTime.UtcNow });
            await identity.SaveChangesAsync();
        }

        using var secondaryClient = _factory.CreateClient();
        secondaryClient.AuthorizeAs(LenseeRoles.Admin, secondaryAdminId, LenseePermissions.UsersDelete);
        var nonPrimary = await secondaryClient.PostAsync($"/api/v1/users/{primaryAdminId}/transfer-primary", null);

        using var primaryClient = _factory.CreateClient();
        primaryClient.AuthorizeAs(LenseeRoles.Admin, primaryAdminId, LenseePermissions.UsersDelete);
        var self = await primaryClient.PostAsync($"/api/v1/users/{primaryAdminId}/transfer-primary", null);
        var inactive = await primaryClient.PostAsync($"/api/v1/users/{inactiveAdminId}/transfer-primary", null);
        var nonAdmin = await primaryClient.PostAsync($"/api/v1/users/{employeeId}/transfer-primary", null);

        Assert.Equal(HttpStatusCode.Forbidden, nonPrimary.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, inactive.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, nonAdmin.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_RejectsDeletionOfTheCurrentAdminAccount()
    {
        await _factory.ResetAsync();
        var adminId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identity.Users.Add(new User { Id = adminId, Username = "admin", FullName = "Admin", PasswordHash = "hash", Role = LenseeRoles.Admin, IsActive = true, CreatedAt = DateTime.UtcNow });
            await identity.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, adminId, LenseePermissions.UsersDelete);
        var response = await client.DeleteAsync($"/api/v1/users/{adminId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var verificationScope = _factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True(await verificationDb.Users.AnyAsync(user => user.Id == adminId));
    }

    [Fact]
    public async Task OnlyPrimaryAdminCanDeactivateOrReactivateAnotherUser()
    {
        await _factory.ResetAsync();
        var primaryAdminId = Guid.NewGuid();
        var secondaryAdminId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identity.Users.AddRange(
                new User { Id = primaryAdminId, Username = "primary", FullName = "Primary Admin", PasswordHash = "hash", Role = LenseeRoles.Admin, IsPrimaryAdmin = true, IsActive = true, CreatedAt = DateTime.UtcNow },
                new User { Id = secondaryAdminId, Username = "secondary", FullName = "Secondary Admin", PasswordHash = "hash", Role = LenseeRoles.Admin, IsActive = true, CreatedAt = DateTime.UtcNow },
                new User { Id = employeeId, Username = "employee", FullName = "Employee", PasswordHash = "hash", Role = LenseeRoles.Accountant, IsActive = true, CreatedAt = DateTime.UtcNow });
            await identity.SaveChangesAsync();
        }

        using var secondary = _factory.CreateClient();
        secondary.AuthorizeAs(LenseeRoles.Admin, secondaryAdminId, LenseePermissions.UsersWrite);
        var denied = await secondary.PatchAsync($"/api/v1/users/{employeeId}/deactivate", null);

        using var primary = _factory.CreateClient();
        primary.AuthorizeAs(LenseeRoles.Admin, primaryAdminId, LenseePermissions.UsersWrite);
        var deactivated = await primary.PatchAsync($"/api/v1/users/{employeeId}/deactivate", null);
        var reactivated = await primary.PatchAsync($"/api/v1/users/{employeeId}/activate", null);
        var self = await primary.PatchAsync($"/api/v1/users/{primaryAdminId}/deactivate", null);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, reactivated.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        using var verificationScope = _factory.Services.CreateScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.True((await verificationDb.Users.SingleAsync(user => user.Id == employeeId)).IsActive);
        Assert.True((await verificationDb.Users.SingleAsync(user => user.Id == primaryAdminId)).IsActive);
    }

    [Fact]
    public async Task AuditHistory_ReturnsPagedImmutableAuditEvents()
    {
        await _factory.ResetAsync();
        var eventId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            identity.AuditLogs.Add(new AuditLog { Id = eventId, EntityType = "Operation", EntityId = Guid.NewGuid(), Action = "Confirm", ActorType = "Admin", ActorName = "Amina Hassan", ChangedFields = "{\"operationNumber\":\"OP-100\",\"status\":\"Completed\"}", CreatedAt = DateTime.UtcNow });
            await identity.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.AuditRead);
        var response = await client.GetAsync("/api/v1/audit?page=1&pageSize=25&entityType=Operation");
        var body = await response.Content.ReadFromJsonAsync<PagedContract<AuditEventContract>>();

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Equal(1, body!.TotalCount);
        Assert.Equal("Amina Hassan", body.Items.Single().ActorName);
        Assert.Equal("Admin", body.Items.Single().ActorType);
        Assert.Equal("operations", body.Items.Single().Section);
        Assert.Equal("Confirmed OP-100.", body.Items.Single().Summary);
        Assert.Equal("OP-100", body.Items.Single().RecordName);
        Assert.Contains(body.Items.Single().Changes, change => change.Field == "Status" && change.After == "Completed");
    }

    [Fact]
    public async Task AuditHistory_ReplacesLegacyUuidFragmentsWithFriendlyReferences()
    {
        await _factory.ResetAsync();
        var operationId = Guid.Parse("b804ad50-1337-4d12-8cb4-112233445566");
        var legacyShortId = operationId.ToString("N")[..8];
        using (var scope = _factory.Services.CreateScope())
        {
            var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            operations.OperationLogs.Add(new OperationLog
            {
                Id = operationId,
                OperationNumber = "OP-20260816171751-398",
                OperationType = "InventoryReceipt",
                Status = "Received",
                CreatedBy = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow
            });
            identity.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                EntityType = "Operation",
                EntityId = operationId,
                Action = "Create",
                ActorType = "Admin",
                ActorName = "Amina Hassan",
                ChangedFields = $"{{\"summary\":\"Created operation {legacyShortId}.\",\"recordName\":\"operation {legacyShortId}\",\"changes\":[{{\"field\":\"Operation\",\"after\":\"{legacyShortId}\"}}]}}",
                CreatedAt = DateTime.UtcNow
            });
            await operations.SaveChangesAsync();
            await identity.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.AuditRead);
        var response = await client.GetAsync("/api/v1/audit?page=1&pageSize=25&entityType=Operation");
        var body = await response.Content.ReadFromJsonAsync<PagedContract<AuditEventContract>>();

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var audit = Assert.Single(body!.Items);
        Assert.DoesNotContain(legacyShortId, audit.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(legacyShortId, audit.RecordName, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OP-20260816171751-398", audit.Summary);
        Assert.Contains("OP-20260816171751-398", audit.RecordName);
        Assert.DoesNotContain(legacyShortId, audit.Changes.Single().After, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NavigationReference_ResolvesOnlyForIssuingUserWithCurrentPermission()
    {
        await _factory.ResetAsync();
        var userId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var navigationReferences = _factory.Services.GetRequiredService<NavigationReferenceService>();
        var reference = navigationReferences.Issue(userId, new NavigationDestination("#/operations", "operation", LenseePermissions.OperationsRead, "Open operation"), recordId);

        using var owner = _factory.CreateClient();
        owner.AuthorizeAs(LenseeRoles.Admin, userId, LenseePermissions.OperationsRead);
        var resolved = await owner.GetAsync($"/api/v1/navigation-references/{Uri.EscapeDataString(reference)}/resolve");
        var resolvedBody = await resolved.Content.ReadFromJsonAsync<NavigationReferenceContract>();

        using var anotherUser = _factory.CreateClient();
        anotherUser.AuthorizeAs(LenseeRoles.Admin, Guid.NewGuid(), LenseePermissions.OperationsRead);
        var foreign = await anotherUser.GetAsync($"/api/v1/navigation-references/{Uri.EscapeDataString(reference)}/resolve");

        using var revoked = _factory.CreateClient();
        revoked.AuthorizeAs(LenseeRoles.Admin, userId, LenseePermissions.AuditRead);
        var revokedAccess = await revoked.GetAsync($"/api/v1/navigation-references/{Uri.EscapeDataString(reference)}/resolve");

        var replacementCharacter = reference[^1] == 'A' ? 'B' : 'A';
        var tamperedReference = reference[..^1] + replacementCharacter;
        var tampered = await owner.GetAsync($"/api/v1/navigation-references/{Uri.EscapeDataString(tamperedReference)}/resolve");

        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        Assert.Equal("#/operations", resolvedBody!.Route);
        Assert.Equal("operation", resolvedBody.Focus);
        Assert.Equal(recordId, resolvedBody.RecordId);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, revokedAccess.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, tampered.StatusCode);
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
            services.RemoveAll<DbContextOptions<OperationsDbContext>>();
            services.RemoveAll<IAuditLogWriter>();
            services.AddDbContext<IdentityDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<OperationsDbContext>(options => options.UseInMemoryDatabase(_databaseName));
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
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        operations.OperationLogs.RemoveRange(operations.OperationLogs);
        await operations.SaveChangesAsync();
        identity.AuditLogs.RemoveRange(identity.AuditLogs);
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

internal sealed record AuditEventContract(Guid Id, string ActorName, string ActorType, string Section, string Summary, string RecordName, IReadOnlyList<AuditChangeContract> Changes);
internal sealed record AuditChangeContract(string Field, string? Before, string? After);
internal sealed record NavigationReferenceContract(string Route, string Focus, Guid RecordId);
