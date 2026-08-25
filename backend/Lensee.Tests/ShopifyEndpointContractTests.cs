using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lensee.Tests;

public sealed class ShopifyEndpointContractTests : IClassFixture<OperationsEndpointFactory>
{
    private readonly OperationsEndpointFactory _factory;

    public ShopifyEndpointContractTests(OperationsEndpointFactory factory) => _factory = factory;

    [Fact]
    public async Task Webhook_RejectsInvalidHmac()
    {
        await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        using var request = CreateWebhookRequest("orders/create", "webhook-invalid", OrderPayload("1001", "UNKNOWN"), "not-a-valid-signature");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task OrdinaryWebhook_UsesDirectSkuAndIsExplicitlyMarked()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        using var request = CreateLegacyWebhookRequest("orders/create", "webhook-legacy", OrderPayload("legacy", SkuCode(seed.SkuId)));
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(request)).StatusCode);
        await ProcessQueuedEventsAsync();

        using var scope = _factory.Services.CreateScope();
        var eventRecord = await scope.ServiceProvider.GetRequiredService<OperationsDbContext>().ShopifyWebhookEvents.SingleAsync();
        Assert.Equal("LegacyPath", eventRecord.VerificationMode);
        Assert.Equal("Imported", eventRecord.Status);
    }

    [Fact]
    public async Task CreateWebhook_IsIdempotent_MatchesSkuWithoutCase_AndPreservesLineSnapshot()
    {
        var seed = await _factory.SeedAsync();
        var payload = OrderPayload("1002", $"  {SkuCode(seed.SkuId).ToLowerInvariant()}  ", "Cash on Delivery");
        using var client = _factory.CreateClient();
        using var firstRequest = CreateWebhookRequest("orders/create", "webhook-1002", payload);
        using var replayRequest = CreateWebhookRequest("orders/create", "webhook-1002", payload);
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(firstRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(replayRequest)).StatusCode);
        await ProcessQueuedEventsAsync();

        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var operation = await operations.OperationLogs.Include(value => value.ShopifyOrderLink).Include(value => value.OperationLines).SingleAsync();
        var line = Assert.Single(operation.OperationLines);
        Assert.Equal("Shopify", operation.SalesChannel);
        Assert.Equal("Draft", operation.Status);
        Assert.Equal("CashHandToHand", operation.PaymentMethod);
        Assert.Equal("customer-1002", operation.ShopifyOrderLink!.ShopifyOrderId);
        Assert.Equal("Pieces", line.EntryMode);
        Assert.Equal(2, line.Quantity);
        Assert.Equal(SkuCode(seed.SkuId), line.SkuCodeSnapshot);
        Assert.Equal($"  {SkuCode(seed.SkuId).ToLowerInvariant()}  ", line.ShopifySkuSnapshot);
        Assert.Equal("line-1002", line.ShopifyLineItemId);
        Assert.Contains("Eye", line.ShopifyPropertiesSnapshot);
        Assert.Equal(1, await operations.ShopifyWebhookEvents.CountAsync());
    }

    [Theory]
    [InlineData("Daily", "1 day")]
    [InlineData("Monthly", "1 month")]
    [InlineData("Annual", "1 year")]
    public async Task SkuReadiness_ReportsCurrentLensWearCycle(string wearCycle, string duration)
    {
        var seed = await _factory.SeedAsync();
        await SetSeedProductAsync(seed.SkuId, "Lens", wearCycle, duration);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.IntegrationsShopifyRead);
        var result = await client.GetFromJsonAsync<PagedContract<ShopifySkuReadinessContract>>("/api/v1/integrations/shopify/sku-readiness");
        var sku = Assert.Single(result!.Items);
        Assert.Equal(wearCycle, sku.WearCycle);
        Assert.Equal(duration, sku.WearDuration);
        Assert.Equal("Ready", sku.Status);
    }

    [Fact]
    public async Task InvalidLensCycle_CreatesExceptionWithoutCustomerOrOperation()
    {
        var seed = await _factory.SeedAsync();
        await SetSeedProductAsync(seed.SkuId, "Lens", null, null);
        using var client = _factory.CreateClient();
        using var request = CreateWebhookRequest("orders/create", "webhook-invalid-cycle", OrderPayload("invalid-cycle", SkuCode(seed.SkuId)));
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(request)).StatusCode);
        await ProcessQueuedEventsAsync();

        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        Assert.Empty(await operations.OperationLogs.ToListAsync());
        var eventRecord = await operations.ShopifyWebhookEvents.SingleAsync();
        Assert.Equal("RequiresAttention", eventRecord.Status);
        Assert.Contains("wear cycle", eventRecord.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnsupportedProductAndUnknownSku_CreateActionableExceptionsWithoutOperation()
    {
        var seed = await _factory.SeedAsync();
        await SetSeedProductAsync(seed.SkuId, "Solution", null, null);
        using var client = _factory.CreateClient();
        using var solutionRequest = CreateWebhookRequest("orders/create", "webhook-solution", OrderPayload("solution", SkuCode(seed.SkuId)));
        using var unknownRequest = CreateWebhookRequest("orders/create", "webhook-unknown", OrderPayload("unknown", "MISSING-SKU"));
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(solutionRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(unknownRequest)).StatusCode);
        await ProcessQueuedEventsAsync();

        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        Assert.Empty(await operations.OperationLogs.ToListAsync());
        Assert.Equal(2, await operations.ShopifyWebhookEvents.CountAsync(value => value.Status == "RequiresAttention"));
        Assert.Equal(2, await notifications.NotificationLogs.CountAsync(value => value.AlertType == "ShopifyOrderException"));
    }

    [Fact]
    public async Task SeparateShopifyLinesWithSameSku_RemainSeparateAndCanBeAllocated()
    {
        var seed = await _factory.SeedAsync();
        var skuCode = SkuCode(seed.SkuId);
        var payload = JsonSerializer.Serialize(new
        {
            id = "customer-lines",
            name = "#lines",
            email = "buyer@example.com",
            payment_gateway_names = new[] { "Card" },
            line_items = new[]
            {
                new { id = "right-line", variant_id = "right-variant", sku = skuCode, title = "Right eye", quantity = 1, price = "125.50", properties = new[] { new { name = "Eye", value = "Right" } } },
                new { id = "left-line", variant_id = "left-variant", sku = skuCode, title = "Left eye", quantity = 1, price = "125.50", properties = new[] { new { name = "Eye", value = "Left" } } }
            }
        });
        using var client = _factory.CreateClient();
        using var request = CreateWebhookRequest("orders/create", "webhook-lines", payload);
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(request)).StatusCode);
        await ProcessQueuedEventsAsync();

        Guid operationId;
        DateOnly expiry = new(2028, 6, 1);
        using (var scope = _factory.Services.CreateScope())
        {
            var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var operation = await operations.OperationLogs.Include(value => value.OperationLines).SingleAsync();
            operationId = operation.Id;
            Assert.Equal(2, operation.OperationLines.Count);
            Assert.All(operation.OperationLines, line => Assert.Equal("Pieces", line.EntryMode));
            inventory.InventoryBatches.Add(new InventoryBatch { Id = Guid.NewGuid(), LocationId = seed.OnlineLocationId, SkuId = seed.SkuId, LotNumber = "ONLINE-1", ExpiryDate = expiry, Quantity = 4, CreatedAt = DateTime.UtcNow });
            await inventory.SaveChangesAsync();
        }

        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsWrite, LenseePermissions.OperationsRead);
        using var allocationResponse = await client.PutAsJsonAsync($"/api/v1/operations/{operationId}/shopify-allocation", new
        {
            lines = new[]
        {
            new { operationLineId = Guid.Empty, lotNumber = "ONLINE-1", expiryDate = expiry }
        }
        });
        Assert.Equal(HttpStatusCode.BadRequest, allocationResponse.StatusCode);

        using var detailResponse = await client.GetAsync($"/api/v1/operations/{operationId}");
        var detail = await detailResponse.Content.ReadFromJsonAsync<OperationDetailContract>();
        using var successResponse = await client.PutAsJsonAsync($"/api/v1/operations/{operationId}/shopify-allocation", new { lines = detail!.Lines.Select(line => new { operationLineId = line.Id, lotNumber = "ONLINE-1", expiryDate = expiry }) });
        Assert.Equal(HttpStatusCode.NoContent, successResponse.StatusCode);
        Assert.All(detail.Lines, line => Assert.NotNull(line.ShopifyLineItemId));
    }

    [Fact]
    public async Task CancelWebhook_CancelsDraftImportedBySku()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        using var create = CreateWebhookRequest("orders/create", "webhook-create-1004", OrderPayload("1004", SkuCode(seed.SkuId)));
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(create)).StatusCode);
        await ProcessQueuedEventsAsync();
        using var cancellation = CreateWebhookRequest("orders/cancelled", "webhook-cancel-1004", "{\"id\":\"customer-1004\"}");
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(cancellation)).StatusCode);
        await ProcessQueuedEventsAsync();
        using var scope = _factory.Services.CreateScope();
        Assert.Equal("Cancelled", (await scope.ServiceProvider.GetRequiredService<OperationsDbContext>().OperationLogs.SingleAsync()).Status);
    }

    private async Task SetSeedProductAsync(Guid skuId, string productType, string? wearCycle, string? duration)
    {
        using var scope = _factory.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var sku = await catalog.Skus.Include(value => value.Product).SingleAsync(value => value.Id == skuId);
        sku.Product.ProductType = productType;
        sku.Product.OpenedExpiryRate = wearCycle;
        sku.Product.OpenedExpiryDuration = duration;
        await catalog.SaveChangesAsync();
    }

    private static string SkuCode(Guid skuId) => $"LEN-{skuId:N}";

    private static HttpRequestMessage CreateWebhookRequest(string topic, string webhookId, string payload, string? signature = null, string? shopDomain = null)
    {
        var body = Encoding.UTF8.GetBytes(payload);
        signature ??= Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(OperationsEndpointFactory.ShopifyWebhookSecret), body));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/integrations/shopify/webhooks") { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Shopify-Hmac-Sha256", signature);
        request.Headers.Add("X-Shopify-Webhook-Id", webhookId);
        request.Headers.Add("X-Shopify-Topic", topic);
        request.Headers.Add("X-Shopify-Shop-Domain", shopDomain ?? OperationsEndpointFactory.ShopifyStoreDomain);
        return request;
    }

    private static HttpRequestMessage CreateLegacyWebhookRequest(string topic, string webhookId, string payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/integrations/shopify/legacy-webhooks/{OperationsEndpointFactory.ShopifyLegacyWebhookPathSecret}")
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(payload))
        };
        return request.WithShopifyHeaders(topic, webhookId);
    }

    private static string OrderPayload(string id, string sku, string gateway = "Card") => JsonSerializer.Serialize(new
    {
        id = $"customer-{id}",
        name = $"#{id}",
        email = "buyer@example.com",
        phone = "+20 100 000 0000",
        payment_gateway_names = new[] { gateway },
        customer = new { id = $"shopify-buyer-{id}", first_name = "Online", last_name = "Buyer", email = "buyer@example.com", phone = "+20 100 000 0000" },
        shipping_address = new { first_name = "Online", last_name = "Buyer", address1 = "15 Nile Street", city = "Cairo", country = "EG", phone = "+20 100 000 0000" },
        line_items = new[] { new { id = $"line-{id}", variant_id = $"variant-{id}", sku, title = "Lens", variant_title = "Hazel", quantity = 2, price = "125.50", properties = new[] { new { name = "Eye", value = "Right" } } } }
    });

    private async Task ProcessQueuedEventsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<Lensee.Host.Infrastructure.ShopifyIntegrationService>();
        foreach (var id in await service.ClaimDueEventsAsync(CancellationToken.None)) await service.ProcessQueuedEventAsync(id, CancellationToken.None);
    }

    private sealed class PagedContract<T> { public List<T> Items { get; init; } = []; }
    private sealed class ShopifySkuReadinessContract { public string? WearCycle { get; init; } public string? WearDuration { get; init; } public string? Status { get; init; } }
    private sealed class OperationDetailContract { public List<OperationLineContract> Lines { get; init; } = []; }
    private sealed class OperationLineContract { public Guid Id { get; init; } public string? ShopifyLineItemId { get; init; } }
}

internal static class ShopifyRequestExtensions
{
    public static HttpRequestMessage WithShopifyHeaders(this HttpRequestMessage request, string topic, string webhookId)
    {
        request.Content!.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Shopify-Webhook-Id", webhookId);
        request.Headers.Add("X-Shopify-Topic", topic);
        request.Headers.Add("X-Shopify-Shop-Domain", OperationsEndpointFactory.ShopifyStoreDomain);
        return request;
    }
}
