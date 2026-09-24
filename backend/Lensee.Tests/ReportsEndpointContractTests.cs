using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.IO.Compression;
using System.Text;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
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

    [Fact]
    public async Task Catalog_IsAuthorizedAndDeclaresBilingualTypedExports()
    {
        using var anonymous = _factory.CreateClient();
        using var denied = await anonymous.GetAsync("/api/v1/reports/catalog");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.ReportsRead);
        using var response = await client.GetAsync("/api/v1/reports/catalog");
        await using var content = await response.Content.ReadAsStreamAsync();
        using var payload = await JsonDocument.ParseAsync(content);
        var stock = payload.RootElement.EnumerateArray().Single(item => item.GetProperty("key").GetString() == "stock");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(stock.GetProperty("formats").EnumerateArray(), value => value.GetString() == "xlsx");
        Assert.Contains(stock.GetProperty("languages").EnumerateArray(), value => value.GetString() == "bi");
    }

    [Fact]
    public async Task ReportingAuthorization_EnforcesPermissionRoleAndLocationScope()
    {
        await _factory.ResetAsync();
        var seed = await _factory.SeedPrintableDocumentsAsync();
        using var client = _factory.CreateClient();

        client.AuthorizeAs(LenseeRoles.Admin);
        using var missingPermission = await client.GetAsync("/api/v1/reports/catalog");
        Assert.Equal(HttpStatusCode.Forbidden, missingPermission.StatusCode);

        client.AuthorizeAs(LenseeRoles.Accountant, LenseePermissions.ReportsRead);
        using var accountantStock = await client.GetAsync("/api/v1/reports/stock/export?format=xlsx&language=en");
        Assert.Equal(HttpStatusCode.Forbidden, accountantStock.StatusCode);

        client.AuthorizeAsAtLocation(LenseeRoles.WarehouseClerk, seed.LocationId, LenseePermissions.ReportsRead);
        using var foreignLocation = await client.GetAsync($"/api/v1/reports/stock/export?format=csv&language=en&locationId={Guid.NewGuid()}");
        using var scopedLocation = await client.GetAsync("/api/v1/reports/stock/export?format=csv&language=en");
        Assert.Equal(HttpStatusCode.Forbidden, foreignLocation.StatusCode);
        Assert.Equal(HttpStatusCode.OK, scopedLocation.StatusCode);
    }

    [Fact]
    public async Task StandardRoutes_ReturnAllMimeTypesServerFilenamesAndProblemDetails()
    {
        await _factory.ResetAsync();
        var seed = await _factory.SeedPrintableDocumentsAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.ReportsRead);

        var expectations = new[]
        {
            (Path: "/api/v1/reports/operations/export?format=pdf&language=bi", Mime: "application/pdf", Extension: ".pdf"),
            (Path: "/api/v1/reports/operations/export?format=xlsx&language=ar", Mime: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Extension: ".xlsx"),
            (Path: "/api/v1/reports/operations/export?format=csv&language=en", Mime: "text/csv", Extension: ".csv"),
            (Path: $"/api/v1/documents/supply-landed-cost/{seed.ShipmentId}?format=xlsx&language=bi", Mime: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Extension: ".xlsx")
        };

        foreach (var expectation in expectations)
        {
            using var response = await client.GetAsync(expectation.Path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(expectation.Mime, response.Content.Headers.ContentType?.MediaType);
            Assert.EndsWith(expectation.Extension, response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName, StringComparison.OrdinalIgnoreCase);
            Assert.True((await response.Content.ReadAsByteArrayAsync()).Length > 20);
        }

        using var badLanguage = await client.GetAsync("/api/v1/reports/operations/export?format=pdf&language=fr");
        using var badFormat = await client.GetAsync($"/api/v1/documents/operation-bill/{seed.OperationId}?format=csv&language=en");
        using var missingKey = await client.GetAsync($"/api/v1/documents/not-a-document/{seed.OperationId}?format=pdf&language=en");
        using var badOperationType = await client.GetAsync("/api/v1/reports/operations/export?format=csv&operationType=Invented");
        using var badSupplyStatus = await client.GetAsync("/api/v1/reports/supply/export?format=xlsx&status=Invented");

        Assert.Equal(HttpStatusCode.BadRequest, badLanguage.StatusCode);
        Assert.Equal("application/problem+json", badLanguage.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.BadRequest, badFormat.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingKey.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badOperationType.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badSupplyStatus.StatusCode);
    }

    [Fact]
    public async Task StandardExport_RejectsRowsBeyondConfiguredLimitWithoutTruncating()
    {
        await using var factory = new LimitedReportsEndpointFactory();
        await factory.SeedPrintableDocumentsAsync();
        await factory.SeedPrintableDocumentsAsync();
        using var client = factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.ReportsRead);

        using var response = await client.GetAsync("/api/v1/reports/operations/export?format=xlsx&language=en");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ListReports_ReturnCanonicalPagedContract_WhileExportUsesAllFilteredRows()
    {
        await _factory.ResetAsync();
        await _factory.SeedPrintableDocumentsAsync();
        await _factory.SeedPrintableDocumentsAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.ReportsRead);

        using var pageResponse = await client.GetAsync("/api/v1/reports/operations?page=1&pageSize=1");
        await using var pageStream = await pageResponse.Content.ReadAsStreamAsync();
        using var page = await JsonDocument.ParseAsync(pageStream);
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        Assert.Equal(1, page.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(1, page.RootElement.GetProperty("pageSize").GetInt32());
        Assert.True(page.RootElement.GetProperty("totalCount").GetInt32() >= 2);
        Assert.True(page.RootElement.GetProperty("totalPages").GetInt32() >= 2);
        Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());

        using var sorted = await client.GetAsync("/api/v1/reports/operations?page=1&pageSize=1&sortBy=total&sortDirection=asc");
        Assert.Equal(HttpStatusCode.OK, sorted.StatusCode);

        using var invalidSort = await client.GetAsync("/api/v1/reports/operations?sortBy=not-a-field");
        Assert.Equal(HttpStatusCode.BadRequest, invalidSort.StatusCode);

        using var export = await client.GetAsync("/api/v1/reports/operations/export?format=csv&language=en");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var csv = await export.Content.ReadAsStringAsync();
        Assert.True(csv.Split('\n').Length >= 3);
    }

    [Fact]
    public async Task PrintableDocuments_RenderInEnglishAndArabic_AndLogExports()
    {
        await _factory.ResetAsync();
        var seed = await _factory.SeedPrintableDocumentsAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.ReportsRead);

        var paths = new[]
        {
            $"/api/v1/reports/operations/{seed.OperationId}/bill.pdf",
            $"/api/v1/reports/payments/{seed.PaymentId}/receipt.pdf",
            $"/api/v1/reports/payments/{seed.CashPaymentId}/cash-receipt.pdf",
            $"/api/v1/reports/supply/{seed.ShipmentId}/landed-cost.pdf",
            $"/api/v1/reports/merchants/{seed.MerchantId}/statement.pdf",
            $"/api/v1/reports/stocktakes/{seed.StocktakeId}/summary.pdf"
        };

        foreach (var path in paths)
        {
            foreach (var language in new[] { "en", "ar" })
            {
                using var response = await client.GetAsync($"{path}?language={language}");
                var pdf = await response.Content.ReadAsByteArrayAsync();

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
                Assert.True(pdf.Length > 1_000);
                Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
            }
        }

        Assert.Equal(12, await _factory.CountExportLogsAsync());
    }

    [Fact]
    public async Task MerchantStatement_UsesLedgerRowsWithFriendlyBusinessReferences()
    {
        await _factory.ResetAsync();
        var seed = await _factory.SeedPrintableDocumentsAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.PaymentsRead, LenseePermissions.ReportsRead);

        var rows = await client.GetFromJsonAsync<JsonElement>($"/api/v1/payments/merchant-accounts/{seed.MerchantId}/statement?from=2026-08-01&to=2026-08-31");

        Assert.Equal(JsonValueKind.Array, rows.ValueKind);
        var sale = rows.EnumerateArray().Single(row => row.GetProperty("entryType").GetString() == "SaleCharge");
        Assert.Equal("Sale added", sale.GetProperty("eventLabel").GetString());
        Assert.Equal("OP-2026-000001", sale.GetProperty("sourceReference").GetString());
        Assert.False(sale.GetProperty("sourceReference").GetString()!.Contains(seed.OperationId.ToString("N"), StringComparison.OrdinalIgnoreCase));

        var collection = rows.EnumerateArray().Single(row => row.GetProperty("entryType").GetString() == "Collection");
        Assert.Equal("Money received", collection.GetProperty("eventLabel").GetString());
        Assert.Contains("OP-2026-000001", collection.GetProperty("sourceReference").GetString());
        Assert.Contains("PAY-", collection.GetProperty("sourceReference").GetString());
        Assert.Equal("Bank transfer", collection.GetProperty("methodLabel").GetString());

        using var arabicWorkbookResponse = await client.GetAsync($"/api/v1/documents/merchant-statement/{seed.MerchantId}?format=xlsx&language=ar");
        Assert.Equal(HttpStatusCode.OK, arabicWorkbookResponse.StatusCode);
        await using var workbookStream = await arabicWorkbookResponse.Content.ReadAsStreamAsync();
        using var workbook = new ZipArchive(workbookStream, ZipArchiveMode.Read);
        var workbookText = string.Join("\n", workbook.Entries
            .Where(entry => entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(entry =>
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                return reader.ReadToEnd();
            }));
        Assert.Contains("صافي التحصيل", workbookText);
        Assert.DoesNotContain(">Net collected<", workbookText, StringComparison.OrdinalIgnoreCase);
    }
}

public class ReportsEndpointFactory : WebApplicationFactory<Program>
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
            services.RemoveAll<DbContextOptions<CatalogDbContext>>();
            services.RemoveAll<DbContextOptions<CrmDbContext>>();
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.RemoveAll<DbContextOptions<InventoryDbContext>>();
            services.RemoveAll<DbContextOptions<OperationsDbContext>>();
            services.RemoveAll<DbContextOptions<PaymentsDbContext>>();
            services.RemoveAll<DbContextOptions<ReportingDbContext>>();

            services.AddDbContext<CatalogDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<CrmDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<IdentityDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<InventoryDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<OperationsDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<PaymentsDbContext>(options => options.UseInMemoryDatabase(_databaseName));
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

    public async Task<PrintableDocumentSeed> SeedPrintableDocumentsAsync()
    {
        using var scope = Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var crm = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var now = new DateTime(2026, 8, 4, 10, 30, 0, DateTimeKind.Utc);
        var userId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var cashPaymentId = Guid.NewGuid();
        var shipmentId = Guid.NewGuid();
        var stocktakeId = Guid.NewGuid();

        identity.Users.Add(new User
        {
            Id = userId,
            Username = "report.admin",
            PasswordHash = "test",
            FullName = "Report Admin",
            Role = LenseeRoles.Admin,
            IsActive = true,
            CreatedAt = now
        });
        crm.Merchants.Add(new Merchant
        {
            Id = merchantId,
            BusinessName = "Prototype Optical Center",
            ContactPersonName = "Mina Adel",
            PhoneNumbers = ["+20 100 555 0182"],
            Address = "Nasr City, Cairo",
            BusinessType = "Optical",
            Status = "Active",
            CreatedAt = now,
            UpdatedAt = now
        });
        inventory.Locations.Add(new Location
        {
            Id = locationId,
            Name = "Roxy Main Warehouse",
            LocationType = "Warehouse",
            IsActive = true
        });
        catalog.Products.Add(new Product
        {
            Id = productId,
            CategoryId = Guid.NewGuid(),
            BrandId = Guid.NewGuid(),
            Name = "Horizon Clear -0.75",
            ProductType = "Lens",
            IsActive = true,
            CreatedAt = now
        });
        catalog.Skus.Add(new Sku
        {
            Id = skuId,
            ProductId = productId,
            SkuCode = "CLR-HZ-075",
            IsActive = true
        });
        operations.OperationLogs.Add(new OperationLog
        {
            Id = operationId,
            OperationNumber = "OP-2026-000001",
            OperationType = "WholesaleSale",
            Status = "Completed",
            ClientId = merchantId,
            ClientName = "Prototype Optical Center",
            DestinationLocationId = locationId,
            PaymentMethod = "BankTransfer",
            CreatedBy = userId,
            ConfirmedBy = userId,
            CreatedAt = now,
            ConfirmedAt = now
        });
        operations.OperationLines.Add(new OperationLine
        {
            Id = Guid.NewGuid(),
            OperationId = operationId,
            SkuId = skuId,
            ProductNameSnapshot = "Horizon Clear -0.75",
            SkuCodeSnapshot = "CLR-HZ-075",
            RepresentativeNameSnapshot = "Mina Adel",
            Section = "Main",
            EntryMode = "Manual",
            Quantity = 6,
            BonusQuantity = 1,
            UnitPrice = 625m,
            LineTotal = 3750m
        });
        payments.MainPaymentLogs.AddRange(
            new MainPaymentLog
            {
                Id = paymentId,
                OperationId = operationId,
                MerchantId = merchantId,
                TotalAmount = 3750m,
                AmountPaid = 2500m,
                PaymentMethod = "BankTransfer",
                Status = "Approved",
                InitializedBy = userId,
                InitializedAt = now,
                LastModifiedBy = userId,
                LastModifiedAt = now,
                Notes = "Bank transfer received."
            },
            new MainPaymentLog
            {
                Id = cashPaymentId,
                OperationId = operationId,
                MerchantId = merchantId,
                TotalAmount = 3750m,
                AmountPaid = 1250m,
                PaymentMethod = "CashHandToHand",
                Status = "Approved",
                InitializedBy = userId,
                InitializedAt = now,
                LastModifiedBy = userId,
                LastModifiedAt = now,
                Notes = "Cash counted and received."
            });
        payments.CashRecords.Add(new CashRecord
        {
            Id = Guid.NewGuid(),
            OperationId = operationId,
            PaymentType = "CashReceived",
            Amount = 1250m,
            Status = "Approved",
            PaymentDate = now,
            CreatedBy = userId,
            Notes = "Cash handover"
        });
        var accountId = Guid.NewGuid();
        payments.MerchantReceivableAccounts.Add(new MerchantReceivableAccount
        {
            Id = accountId,
            MerchantId = merchantId,
            Status = "Open",
            NextSequence = 3,
            OpenedAt = now,
            UpdatedAt = now,
            OpenedBy = userId
        });
        payments.MerchantAccountEntries.AddRange(
            new MerchantAccountEntry
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Sequence = 1,
                EntryType = "SaleCharge",
                DebitAmount = 3750m,
                CreditAmount = 0m,
                SourceType = "OperationSale",
                SourceId = operationId,
                OperationId = operationId,
                Status = "Posted",
                PostedBy = userId,
                PostedAt = now,
                Notes = "Completed sale."
            },
            new MerchantAccountEntry
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Sequence = 2,
                EntryType = "Collection",
                DebitAmount = 0m,
                CreditAmount = 2500m,
                PaymentMethod = "BankTransfer",
                TransactionReference = "BANK-001",
                SourceType = "PaymentCollection",
                SourceId = paymentId,
                OperationId = operationId,
                PaymentId = paymentId,
                Status = "Posted",
                PostedBy = userId,
                PostedAt = now.AddMinutes(5),
                Notes = "Bank transfer received."
            });
        operations.SupplyShipments.Add(new SupplyShipment
        {
            Id = shipmentId,
            ShipmentNumber = "SUP-2026-000001",
            SupplierName = "VisionTech Manufacturing",
            InvoiceNumber = "VT-INV-1",
            ShipmentDate = now,
            DestinationLocationId = locationId,
            Status = "InventoryPosted",
            ProductSubtotal = 3750m,
            CostSubtotal = 250m,
            LandedTotal = 4000m,
            CreatedBy = userId,
            CreatedAt = now,
            ConfirmedBy = userId,
            ConfirmedAt = now,
            InventoryReceiptOperationId = operationId
        });
        operations.SupplyShipmentLines.Add(new SupplyShipmentLine
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentId,
            SkuId = skuId,
            ProductNameSnapshot = "Horizon Clear -0.75",
            SkuCodeSnapshot = "CLR-HZ-075",
            Quantity = 6,
            UnitPrice = 625m,
            LineSubtotal = 3750m,
            AllocatedCost = 250m,
            LandedUnitCost = 666.67m,
            LotNumber = "HZ24071",
            ExpiryDate = new DateOnly(2029, 7, 1)
        });
        operations.SupplyShipmentCosts.Add(new SupplyShipmentCost
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentId,
            CostType = "Freight",
            Description = "Freight charge",
            Amount = 250m
        });
        operations.SupplyShipmentHistoryLogs.Add(new SupplyShipmentHistory
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipmentId,
            Action = "InventoryPosted",
            ActorUserId = userId,
            CreatedAt = now,
            Summary = "Inventory posted"
        });
        operations.StocktakeSessions.Add(new StocktakeSession
        {
            Id = stocktakeId,
            LocationId = locationId,
            SessionDate = now,
            PerformedBy = userId,
            ConfirmedBy = userId,
            ProductsCounted = 1,
            TotalDiscrepancyUnits = -1,
            Status = "Confirmed",
            CreatedAt = now,
            ConfirmedAt = now,
            Notes = "Count completed."
        });
        operations.StocktakeAdjustmentLines.Add(new StocktakeAdjustmentLine
        {
            Id = Guid.NewGuid(),
            SessionId = stocktakeId,
            SkuId = skuId,
            LotNumber = "HZ24071",
            ExpiryDate = new DateOnly(2029, 7, 1),
            SystemQtyBefore = 6,
            PhysicalCount = 5,
            Delta = -1,
            LineNote = "Recount confirmed."
        });

        await identity.SaveChangesAsync();
        await crm.SaveChangesAsync();
        await inventory.SaveChangesAsync();
        await catalog.SaveChangesAsync();
        await operations.SaveChangesAsync();
        await payments.SaveChangesAsync();

        return new PrintableDocumentSeed(operationId, paymentId, cashPaymentId, shipmentId, merchantId, stocktakeId, locationId);
    }
}

public sealed class LimitedReportsEndpointFactory : ReportsEndpointFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Reporting:MaxExportRows"] = "1" }));
    }
}

public sealed record PrintableDocumentSeed(Guid OperationId, Guid PaymentId, Guid CashPaymentId, Guid ShipmentId, Guid MerchantId, Guid StocktakeId, Guid LocationId);
