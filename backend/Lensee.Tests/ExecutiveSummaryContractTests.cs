using System.Net;
using System.Net.Http.Json;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
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
    public async Task DailySummaryCountsPaidPieces_UsesHistoricalRatioEstimate_AndExcludesErpAdmin()
    {
        var seed = await _factory.SeedAsync();
        var actor = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
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
        payments.MainPaymentLogs.AddRange(
            new MainPaymentLog
            {
                Id = Guid.NewGuid(),
                OperationId = firstId,
                TotalAmount = 30m,
                AmountPaid = 30m,
                Status = "Completed",
                InitializedBy = actor,
                InitializedAt = now,
                LastModifiedAt = now
            },
            new MainPaymentLog
            {
                Id = Guid.NewGuid(),
                OperationId = secondId,
                TotalAmount = 20m,
                AmountPaid = 20m,
                Status = "Completed",
                InitializedBy = actor,
                InitializedAt = now,
                LastModifiedAt = now
            });
        await operations.SaveChangesAsync();
        await payments.SaveChangesAsync();

        using var admin = _factory.CreateClient();
        admin.AuthorizeAs(LenseeRoles.Admin, actor, LenseePermissions.ExecutiveSummaryRead);
        var day = DateOnly.FromDateTime(now);
        var summary = await admin.GetFromJsonAsync<ExecutiveSummaryContract>($"/api/v1/reports/executive-summary?period=daily&date={day:yyyy-MM-dd}");
        Assert.NotNull(summary);
        Assert.Equal(40m, summary!.Flows.NetSales);
        Assert.Equal(4, summary.Units.PaidPiecesSold);
        Assert.True(summary.Units.Estimated);
        Assert.Equal(0, summary.Units.UnresolvedPackLines);

        using var erp = _factory.CreateClient();
        erp.AuthorizeAs(LenseeRoles.ERPAdmin, actor, LenseePermissions.ReportsRead, LenseePermissions.ExecutiveSummaryRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await erp.GetAsync("/api/v1/reports/executive-summary?period=daily")).StatusCode);
    }

    private sealed record ExecutiveSummaryContract(ExecutiveFlows Flows, ExecutiveUnits Units);
    private sealed record ExecutiveFlows(decimal NetSales);
    private sealed record ExecutiveUnits(int PaidPiecesSold, bool Estimated, int UnresolvedPackLines);
}
