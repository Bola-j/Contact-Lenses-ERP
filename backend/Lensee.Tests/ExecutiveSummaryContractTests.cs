using System.Net;
using System.Net.Http.Json;
using Lensee.Modules.Finance.Data;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Security;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lensee.Tests;

public sealed class ExecutiveSummaryContractTests : IClassFixture<OperationsEndpointFactory>
{
    private readonly OperationsEndpointFactory _factory;
    public ExecutiveSummaryContractTests(OperationsEndpointFactory factory) => _factory = factory;

    [Fact]
    public async Task DailySummaryCountsNonBonusPieces_UsesHistoricalRatioEstimateWithoutRequiringCollection_AndExcludesErpAdmin()
    {
        var seed = await _factory.SeedAsync();
        var actor = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().EgyptNow;
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var returnId = Guid.NewGuid();
        OperationLog Sale(Guid id, string code, int? ratio, int amount) => new()
        {
            Id = id,
            OperationNumber = code,
            OperationType = "WholesaleSale",
            Status = "Completed",
            CreatedBy = actor,
            CreatedAt = now,
            ConfirmedAt = now,
            PaymentMethod = "CashHandToHand",
            OperationLines = [new OperationLine { Id = Guid.NewGuid(), OperationId = id, SkuId = seed.SkuId,
                ProductNameSnapshot = "Lens", SkuCodeSnapshot = "TEST", Section = "Standard", Quantity = 1,
                EntryMode = "Packs", PiecesPerPackSnapshot = ratio, UnitPrice = amount, LineTotal = amount }]
        };
        operations.OperationLogs.AddRange(
            Sale(firstId, $"SALE-{firstId:N}", 3, 30),
            Sale(secondId, $"SALE-{secondId:N}", null, 20),
            new OperationLog
            {
                Id = returnId,
                OperationNumber = $"RETURN-{returnId:N}",
                OperationType = "Return",
                Status = "Confirmed",
                CreatedBy = actor,
                CreatedAt = now,
                ConfirmedAt = now,
                OperationLines = [new OperationLine { Id = Guid.NewGuid(), OperationId = returnId, SkuId = seed.SkuId,
                    ProductNameSnapshot = "Lens", SkuCodeSnapshot = "TEST", Section = "Standard", Quantity = 1,
                    EntryMode = "Pieces", UnitPrice = 10m, LineTotal = 10m }]
            });
        await operations.SaveChangesAsync();

        using var admin = _factory.CreateClient();
        admin.AuthorizeAs(LenseeRoles.Admin, actor, LenseePermissions.ExecutiveSummaryRead);
        var day = DateOnly.FromDateTime(now);
        var summary = await admin.GetFromJsonAsync<ExecutiveSummaryContract>($"/api/v1/reports/executive-summary?date={day:yyyy-MM-dd}");
        Assert.NotNull(summary);
        Assert.Equal(40m, summary!.Flows.NetSales);
        Assert.Equal(4, summary.Units.NonBonusPiecesSold);
        Assert.True(summary.Units.Estimated);
        Assert.Equal(0, summary.Units.UnresolvedPackLines);

        using var erp = _factory.CreateClient();
        erp.AuthorizeAs(LenseeRoles.ERPAdmin, actor, LenseePermissions.ReportsRead, LenseePermissions.ExecutiveSummaryRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await erp.GetAsync("/api/v1/reports/executive-summary?date=2040-02-03")).StatusCode);
    }

    [Fact]
    public async Task Summary_UsesDeliveredOnlineSalesAndIncludesTheSelectedEndDateLedgerRows()
    {
        var seed = await _factory.SeedAsync();
        var actor = Guid.NewGuid();
        var financeAccountId = await _factory.GetFinanceAccountIdAsync();
        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().EgyptNow;
        var day = DateOnly.FromDateTime(now);
        var saleId = Guid.NewGuid();

        operations.OperationLogs.Add(new OperationLog
        {
            Id = saleId,
            OperationNumber = $"ONLINE-{saleId:N}",
            OperationType = "RetailSale",
            Status = "Delivered",
            SalesChannel = "Online",
            CreatedBy = actor,
            CreatedAt = now,
            ConfirmedAt = now,
            PaymentMethod = "CashHandToHand",
            CodCharge = 10m,
            OperationLines = [new OperationLine
            {
                Id = Guid.NewGuid(), OperationId = saleId, SkuId = seed.SkuId,
                ProductNameSnapshot = "Lens", SkuCodeSnapshot = "ONLINE-TEST", Section = "Standard",
                Quantity = 1, EntryMode = "Packs", PiecesPerPackSnapshot = 1,
                UnitPrice = 125m, LineTotal = 125m
            }]
        });
        finance.FinanceLedgerEntries.Add(new FinanceLedgerEntry
        {
            Id = Guid.NewGuid(), FinanceAccountId = financeAccountId,
            Direction = "Credit", Amount = 135m, Category = "SaleCollection",
            MovementMethod = "CashHandToHand", SourceType = "CashRecord", SourceId = saleId,
            BusinessDate = day, Status = "Posted", CreatedBy = actor, CreatedAt = now
        });
        await operations.SaveChangesAsync();
        await finance.SaveChangesAsync();

        using var admin = _factory.CreateClient();
        admin.AuthorizeAs(LenseeRoles.Admin, actor, LenseePermissions.ExecutiveSummaryRead);
        var summary = await admin.GetFromJsonAsync<ExecutiveSummaryContract>($"/api/v1/reports/executive-summary?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}");

        Assert.NotNull(summary);
        Assert.Equal(135m, summary!.Flows.NetSales);
        Assert.Equal(135m, summary.Flows.ActualCollections);
    }

    [Fact]
    public async Task Summary_NetsPostedPaymentsFromBothSupplyPaymentWorkflowsAndTheirReversals()
    {
        var actor = Guid.NewGuid();
        var financeAccountId = await _factory.GetFinanceAccountIdAsync();
        using var scope = _factory.Services.CreateScope();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var day = DateOnly.FromDateTime(scope.ServiceProvider.GetRequiredService<IClock>().EgyptNow);

        FinanceLedgerEntry Entry(string sourceType, string direction, decimal amount, string status = "Posted") => new()
        {
            Id = Guid.NewGuid(), FinanceAccountId = financeAccountId, SourceType = sourceType,
            SourceId = Guid.NewGuid(), Direction = direction, Amount = amount, Category = "SupplierPurchase",
            MovementMethod = "CashHandToHand", BusinessDate = day, Status = status, CreatedBy = actor,
            CreatedAt = day.ToDateTime(TimeOnly.MinValue)
        };

        finance.FinanceLedgerEntries.AddRange(
            Entry("SupplyPayment", "Debit", 100m),
            Entry("SupplyPaymentReversal", "Credit", 30m),
            Entry("SupplySupplierInstallment", "Debit", 80m),
            Entry("SupplySupplierInstallmentReversal", "Credit", 20m),
            Entry("SupplyPayment", "Debit", 999m, "Draft"));
        await finance.SaveChangesAsync();

        using var admin = _factory.CreateClient();
        admin.AuthorizeAs(LenseeRoles.Admin, actor, LenseePermissions.ExecutiveSummaryRead);
        var summary = await admin.GetFromJsonAsync<ExecutiveSummaryContract>($"/api/v1/reports/executive-summary?date={day:yyyy-MM-dd}");

        Assert.NotNull(summary);
        Assert.Equal(130m, summary!.Flows.PostedSupplyPayments);
    }

    [Fact]
    public async Task Summary_ShowsOutstandingCLevelWithdrawalsRegardlessOfTheSelectedPeriod()
    {
        await _factory.SeedAsync();
        var actor = Guid.NewGuid();
        var financeAccountId = await _factory.GetFinanceAccountIdAsync();
        using var scope = _factory.Services.CreateScope();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var selectedDay = DateOnly.FromDateTime(scope.ServiceProvider.GetRequiredService<IClock>().EgyptNow);
        var historicDay = selectedDay.AddDays(-10);

        FinanceLedgerEntry Entry(string sourceType, string direction, decimal amount) => new()
        {
            Id = Guid.NewGuid(), FinanceAccountId = financeAccountId, SourceType = sourceType,
            SourceId = Guid.NewGuid(), Direction = direction, Amount = amount, Category = "CLevelWithdrawal",
            MovementMethod = "CashHandToHand", BusinessDate = historicDay, Status = "Posted", CreatedBy = actor,
            CreatedAt = historicDay.ToDateTime(TimeOnly.MinValue)
        };

        finance.FinanceLedgerEntries.AddRange(
            Entry("CLevelWithdrawal", "Debit", 220m),
            Entry("CLevelWithdrawalReversal", "Credit", 20m),
            Entry("CLevelWithdrawalRepayment", "Credit", 50m),
            Entry("CLevelWithdrawalRepaymentReversal", "Debit", 10m));
        await finance.SaveChangesAsync();

        using var admin = _factory.CreateClient();
        admin.AuthorizeAs(LenseeRoles.Admin, actor, LenseePermissions.ExecutiveSummaryRead);
        var summary = await admin.GetFromJsonAsync<ExecutiveSummaryContract>($"/api/v1/reports/executive-summary?date={selectedDay:yyyy-MM-dd}");

        Assert.NotNull(summary);
        Assert.Equal(160m, summary!.CurrentBalances.CLevelWithdrawalOutstanding);
        Assert.Equal(0m, summary.Flows.Withdrawals);
    }

    [Fact]
    public async Task DailyMetrics_UsesTheOnlineDeliveredFinalStateAndCodCharge()
    {
        var seed = await _factory.SeedAsync();
        var actor = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().EgyptNow;
        var day = DateOnly.FromDateTime(now);

        OperationLog Sale(string salesChannel, decimal amount, decimal codCharge) => new()
        {
            Id = Guid.NewGuid(), OperationNumber = $"SALE-{Guid.NewGuid():N}", OperationType = "RetailSale",
            Status = "Delivered", SalesChannel = salesChannel, SourceLocationId = seed.SubLocationId,
            CreatedBy = actor, CreatedAt = now, ConfirmedAt = now, CodCharge = codCharge,
            OperationLines = [new OperationLine
            {
                Id = Guid.NewGuid(), SkuId = seed.SkuId, ProductNameSnapshot = "Lens", SkuCodeSnapshot = "METRIC-TEST",
                Section = "Standard", Quantity = 1, EntryMode = "Packs", PiecesPerPackSnapshot = 1,
                UnitPrice = amount, LineTotal = amount
            }]
        };

        operations.OperationLogs.AddRange(Sale("Online", 125m, 10m), Sale("Manual", 500m, 0m));
        await operations.SaveChangesAsync();

        using var admin = _factory.CreateClient();
        admin.AuthorizeAs(LenseeRoles.Admin, actor, LenseePermissions.FinanceRead);
        var metrics = await admin.GetFromJsonAsync<DailyMetricsContract>($"/api/v1/finance/daily-metrics?date={day:yyyy-MM-dd}");

        Assert.NotNull(metrics);
        Assert.Equal(135m, metrics!.Sales.NetSales);
        Assert.Equal(1, metrics.Sales.SaleCount);
    }

    private sealed record ExecutiveSummaryContract(ExecutiveFlows Flows, ExecutiveUnits Units, ExecutiveBalances CurrentBalances);
    private sealed record ExecutiveFlows(decimal NetSales, decimal ActualCollections = 0m, decimal PostedSupplyPayments = 0m, decimal Withdrawals = 0m);
    private sealed record ExecutiveBalances(decimal CLevelWithdrawalOutstanding);
    private sealed record ExecutiveUnits(int NonBonusPiecesSold, bool Estimated, int UnresolvedPackLines);
    private sealed record DailyMetricsContract(DailySalesContract Sales);
    private sealed record DailySalesContract(decimal NetSales, int SaleCount);
}
