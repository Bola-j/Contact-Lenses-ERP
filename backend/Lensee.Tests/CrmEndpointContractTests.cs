using System.Net;
using System.Net.Http.Json;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
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

public sealed class CrmEndpointContractTests : IClassFixture<CrmEndpointFactory>
{
    private readonly CrmEndpointFactory _factory;

    public CrmEndpointContractTests(CrmEndpointFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Merchant_RequiresContactPerson()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite);

        var response = await client.PostAsJsonAsync("/api/v1/crm/merchants", new
        {
            businessName = "Lens Partner",
            contactPersonName = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Merchant_RequiresBusinessName()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite);

        var response = await client.PostAsJsonAsync("/api/v1/crm/merchants", new
        {
            businessName = "",
            contactPersonName = "Mina"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanCreateUpdateDeactivateReactivateAndNoteMerchant()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite);

        var createdResponse = await client.PostAsJsonAsync("/api/v1/crm/merchants", new
        {
            businessName = "Lens Partner",
            contactPersonName = "Mina",
            phoneNumbers = new[] { "01000000000" },
            businessType = "Merchant"
        });
        var created = await createdResponse.Content.ReadFromJsonAsync<MerchantContract>();
        var update = await client.PutAsJsonAsync($"/api/v1/crm/merchants/{created!.Id}", new
        {
            businessName = "Lens Partner Updated",
            contactPersonName = "Mina",
            phoneNumbers = new[] { "01000000000" },
            businessType = "Pharmacy"
        });
        var deactivate = await client.PatchAsync($"/api/v1/crm/merchants/{created.Id}/deactivate", null);
        var reactivate = await client.PatchAsync($"/api/v1/crm/merchants/{created.Id}/reactivate", null);
        var note = await client.PostAsJsonAsync($"/api/v1/crm/merchants/{created.Id}/notes", new { note = "Prefers monthly lens orders." });
        var detail = await client.GetFromJsonAsync<MerchantDetailContract>($"/api/v1/crm/merchants/{created.Id}");

        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, note.StatusCode);
        Assert.Equal("Lens Partner Updated", detail!.Merchant.BusinessName);
        Assert.Equal("Active", detail.Merchant.Status);
        Assert.Single(detail.Notes);
    }

    [Fact]
    public async Task Merchant_RejectsBusinessTypeOutsideDatabaseConstraint()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite);

        var response = await client.PostAsJsonAsync("/api/v1/crm/merchants", new
        {
            businessName = "Lens Partner",
            contactPersonName = "Mina",
            businessType = "Wholesale"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MerchantNote_RejectsBlankNote()
    {
        await _factory.ResetAsync();
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite);

        var response = await client.PostAsJsonAsync($"/api/v1/crm/merchants/{merchantId}/notes", new { note = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Representative_RejectsTypeOutsideSelectList()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite);

        var response = await client.PostAsJsonAsync("/api/v1/crm/representatives", new
        {
            name = "Ramy Rep",
            type = "Partner"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanCreateUpdateDeactivateReactivateRepresentative()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite);

        var createdResponse = await client.PostAsJsonAsync("/api/v1/crm/representatives", new
        {
            name = "Ramy Rep",
            phoneNumbers = new[] { "01111111111" },
            type = "External"
        });
        var created = await createdResponse.Content.ReadFromJsonAsync<RepresentativeContract>();
        var update = await client.PutAsJsonAsync($"/api/v1/crm/representatives/{created!.Id}", new
        {
            name = "Ramy Rep Updated",
            phoneNumbers = new[] { "01111111111" },
            type = "External"
        });
        var deactivate = await client.PatchAsync($"/api/v1/crm/representatives/{created.Id}/deactivate", null);
        var reactivate = await client.PatchAsync($"/api/v1/crm/representatives/{created.Id}/reactivate", null);
        var rows = await client.GetFromJsonAsync<IReadOnlyList<RepresentativeContract>>("/api/v1/crm/representatives?includeInactive=true");

        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, reactivate.StatusCode);
        Assert.Contains(rows!, rep => rep.Id == created.Id && rep.Name == "Ramy Rep Updated" && rep.Status == "Active");
    }
}

public sealed class CrmEndpointFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"crm-contracts-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=lensee_crm_contract_tests;Username=test;Password=test",
                ["Jwt:Secret"] = "CrmContractTestsNeedASecret123!",
                ["Jwt:Issuer"] = "Lensee",
                ["Jwt:Audience"] = "Lensee.App"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<CrmDbContext>>();
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.RemoveAll<DbContextOptions<OperationsDbContext>>();
            services.RemoveAll<DbContextOptions<PaymentsDbContext>>();
            services.AddDbContext<CrmDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<IdentityDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<OperationsDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<PaymentsDbContext>(options => options.UseInMemoryDatabase(_databaseName));

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
        var crm = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        payments.CashRecords.RemoveRange(payments.CashRecords);
        payments.FinancialAdjustments.RemoveRange(payments.FinancialAdjustments);
        payments.InstallmentSubLogs.RemoveRange(payments.InstallmentSubLogs);
        payments.MainPaymentLogs.RemoveRange(payments.MainPaymentLogs);
        operations.OperationLines.RemoveRange(operations.OperationLines);
        operations.OperationLogs.RemoveRange(operations.OperationLogs);
        identity.RefreshTokens.RemoveRange(identity.RefreshTokens);
        identity.Users.RemoveRange(identity.Users);
        crm.MerchantNotes.RemoveRange(crm.MerchantNotes);
        crm.Merchants.RemoveRange(crm.Merchants);
        crm.Representatives.RemoveRange(crm.Representatives);
        await operations.SaveChangesAsync();
        await identity.SaveChangesAsync();
        await payments.SaveChangesAsync();
        await crm.SaveChangesAsync();
    }

    public async Task<Guid> CreateMerchantAsync()
    {
        await ResetAsync();
        using var scope = Services.CreateScope();
        var crm = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var id = Guid.NewGuid();
        crm.Merchants.Add(new Merchant
        {
            Id = id,
            BusinessName = "Lens Partner",
            ContactPersonName = "Mina",
            PhoneNumbers = [],
            BusinessType = "Merchant",
            Status = "Active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await crm.SaveChangesAsync();
        return id;
    }
}

public sealed record MerchantContract(Guid Id, string BusinessName, string ContactPersonName, string Status);

public sealed record MerchantDetailContract(MerchantContract Merchant, IReadOnlyList<MerchantOperationContract> RecentOperations, IReadOnlyList<MerchantNoteContract> Notes);

public sealed record MerchantOperationContract(Guid Id, string OperationNumber, string OperationType, string Status, int Quantity, int BonusQuantity, decimal Total);

public sealed record MerchantNoteContract(Guid Id, string Note);

public sealed record RepresentativeContract(Guid Id, string Name, string Status);
