using System.Net;
using System.Net.Http.Json;
using Lensee.Modules.Reporting.Data;
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

public sealed class ReportsEndpointContractTests : IClassFixture<ReportsEndpointFactory>
{
    private readonly ReportsEndpointFactory _factory;

    public ReportsEndpointContractTests(ReportsEndpointFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ExportLog_RejectsUnsupportedReportType()
    {
        await _factory.ResetAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.ReportsRead);

        var response = await client.PostAsJsonAsync("/api/v1/reports/exports", new
        {
            reportType = "custom-script",
            generatedUrl = "demo://reports/custom-script"
        });
        var count = await _factory.CountExportLogsAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, count);
    }
}

public sealed class ReportsEndpointFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"reports-contracts-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=lensee_reports_contract_tests;Username=test;Password=test",
                ["Jwt:Secret"] = "ReportsContractTestsNeedASecret123!",
                ["Jwt:Issuer"] = "Lensee",
                ["Jwt:Audience"] = "Lensee.App"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<ReportingDbContext>>();
            services.AddDbContext<ReportingDbContext>(options => options.UseInMemoryDatabase(_databaseName));

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
        var reporting = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        reporting.ExportLogs.RemoveRange(reporting.ExportLogs);
        await reporting.SaveChangesAsync();
    }

    public async Task<int> CountExportLogsAsync()
    {
        using var scope = Services.CreateScope();
        var reporting = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();
        return await reporting.ExportLogs.CountAsync();
    }
}
