using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lensee.Tests;

public sealed class SupplyReceivingRegressionTests : IClassFixture<OperationsEndpointFactory>
{
    private readonly OperationsEndpointFactory _factory;

    public SupplyReceivingRegressionTests(OperationsEndpointFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SupplyReceiving_CreatesAnOperationLineThatSatisfiesTheReturnSplitInvariant()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.SupplyRead, LenseePermissions.SupplyWrite, LenseePermissions.SupplyReceive);

        var created = await client.PostAsJsonAsync("/api/v1/supply/shipments", new
        {
            supplierName = "Return split regression supplier",
            destinationLocationId = seed.MainLocationId,
            lines = new[] { new { skuId = seed.SkuId, quantity = 2 } }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var shipment = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var shipmentId = shipment.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/confirm", null)).StatusCode);

        using var sessionResponse = await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions", null);
        Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);
        using var session = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync());
        var sessionId = session.RootElement.GetProperty("id").GetGuid();
        var shipmentLineId = session.RootElement.GetProperty("lines")[0].GetProperty("shipmentLineId").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync(
            $"/api/v1/supply/shipments/{shipmentId}/receiving-sessions/{sessionId}/lines",
            new[] { new { shipmentLineId, receivedQuantity = 2, lotNumber = "SPLIT-REGRESSION", expiryDate = "2028-06-01" } })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions/{sessionId}/confirm", null)).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var receiptOperationId = await operations.SupplyReceivingSessions
            .Where(value => value.Id == sessionId)
            .Select(value => value.InventoryReceiptOperationId)
            .SingleAsync();
        var line = await operations.OperationLines.SingleAsync(value => value.OperationId == receiptOperationId);
        Assert.Equal(line.Quantity, line.NormalQuantity + line.ExceptionQuantity);
        Assert.Equal(line.Quantity, line.NormalQuantity);
        Assert.Equal(0, line.ExceptionQuantity);
    }
}
