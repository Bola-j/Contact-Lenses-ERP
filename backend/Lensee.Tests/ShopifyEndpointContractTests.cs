using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Operations.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lensee.Tests;

public sealed class ShopifyEndpointContractTests : IClassFixture<OperationsEndpointFactory>
{
    private readonly OperationsEndpointFactory _factory;

    public ShopifyEndpointContractTests(OperationsEndpointFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Webhook_RejectsInvalidHmac()
    {
        await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        using var request = CreateWebhookRequest("orders/create", "webhook-invalid", OrderPayload("1001", "v-1001"), "not-a-valid-signature");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_RejectsVerifiedRequestFromAnotherShopDomain()
    {
        await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        using var request = CreateWebhookRequest("orders/create", "webhook-wrong-store", OrderPayload("wrong-store", "v-1001"), shopDomain: "another-store.myshopify.com");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_RejectsBodyOverConfiguredLimitBeforeQueuing()
    {
        await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        using var request = CreateWebhookRequest("orders/create", "webhook-oversized", new string('x', 262_145));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<OperationsDbContext>().ShopifyWebhookEvents.ToListAsync());
    }

    [Fact]
    public async Task CreateWebhook_IsIdempotent_AndPreservesShopifyCustomerSnapshot()
    {
        var seed = await _factory.SeedAsync();
        await AddMappingAsync("v-1002", seed.SkuId);
        var payload = OrderPayload("1002", "v-1002", "Cash on Delivery");
        using var client = _factory.CreateClient();

        using var firstRequest = CreateWebhookRequest("orders/create", "webhook-1002", payload);
        var first = await client.SendAsync(firstRequest);
        using var replayRequest = CreateWebhookRequest("orders/create", "webhook-1002", payload);
        var replay = await client.SendAsync(replayRequest);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        await ProcessQueuedEventsAsync();
        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var operation = await operations.OperationLogs.Include(value => value.ShopifyOrderLink).Include(value => value.OperationLines).SingleAsync();
        Assert.Equal("Shopify", operation.SalesChannel);
        Assert.Equal("Draft", operation.Status);
        Assert.Equal("CashHandToHand", operation.PaymentMethod);
        Assert.Equal("customer-1002", operation.ShopifyOrderLink!.ShopifyOrderId);
        Assert.Equal("buyer@example.com", operation.BuyerEmail);
        Assert.True(operation.OperationLines.All(line => line.ExpiryDate is null));
        Assert.Equal(1, await operations.ShopifyWebhookEvents.CountAsync());
        Assert.Equal(1, await operations.ShopifyOrderLinks.CountAsync());
    }

    [Fact]
    public async Task UnmappedVariant_CreatesActionableExceptionWithoutOperation()
    {
        await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        using var request = CreateWebhookRequest("orders/create", "webhook-unmapped", OrderPayload("1003", "not-mapped"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await ProcessQueuedEventsAsync();
        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        Assert.Empty(await operations.OperationLogs.ToListAsync());
        Assert.Equal("RequiresAttention", (await operations.ShopifyWebhookEvents.SingleAsync()).Status);
        Assert.Equal(1, await notifications.NotificationLogs.CountAsync(value => value.AlertType == "ShopifyOrderException"));
    }

    [Fact]
    public async Task CancelWebhook_CancelsOnlyDraftImportedOperation()
    {
        var seed = await _factory.SeedAsync();
        await AddMappingAsync("v-1004", seed.SkuId);
        using var client = _factory.CreateClient();
        using var create = CreateWebhookRequest("orders/create", "webhook-create-1004", OrderPayload("1004", "v-1004"));
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(create)).StatusCode);
        await ProcessQueuedEventsAsync();
        using var cancellation = CreateWebhookRequest("orders/cancelled", "webhook-cancel-1004", "{\"id\":\"customer-1004\"}");

        var response = await client.SendAsync(cancellation);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await ProcessQueuedEventsAsync();
        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        Assert.Equal("Cancelled", (await operations.OperationLogs.SingleAsync()).Status);
    }

    private async Task AddMappingAsync(string variantId, Guid skuId)
    {
        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        operations.ShopifyVariantMappings.Add(new ShopifyVariantMapping
        {
            Id = Guid.NewGuid(),
            ShopifyVariantId = variantId,
            SkuId = skuId,
            EntryMode = "Packs",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await operations.SaveChangesAsync();
    }

    private static HttpRequestMessage CreateWebhookRequest(string topic, string webhookId, string payload, string? signature = null, string? shopDomain = null)
    {
        var body = Encoding.UTF8.GetBytes(payload);
        signature ??= Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(OperationsEndpointFactory.ShopifyWebhookSecret), body));
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/integrations/shopify/webhooks")
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Shopify-Hmac-Sha256", signature);
        request.Headers.Add("X-Shopify-Webhook-Id", webhookId);
        request.Headers.Add("X-Shopify-Topic", topic);
        request.Headers.Add("X-Shopify-Shop-Domain", shopDomain ?? OperationsEndpointFactory.ShopifyStoreDomain);
        return request;
    }

    private static string OrderPayload(string id, string variantId, string gateway = "Card") => JsonSerializer.Serialize(new
    {
        id = $"customer-{id}",
        name = $"#{id}",
        email = "buyer@example.com",
        phone = "+20 100 000 0000",
        payment_gateway_names = new[] { gateway },
        customer = new { id = $"shopify-buyer-{id}", first_name = "Online", last_name = "Buyer", email = "buyer@example.com", phone = "+20 100 000 0000" },
        shipping_address = new { first_name = "Online", last_name = "Buyer", address1 = "15 Nile Street", city = "Cairo", country = "EG", phone = "+20 100 000 0000" },
        line_items = new[] { new { variant_id = variantId, quantity = 2, price = "125.50" } }
    });

    private async Task ProcessQueuedEventsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<Lensee.Host.Infrastructure.ShopifyIntegrationService>();
        foreach (var id in await service.ClaimDueEventsAsync(CancellationToken.None))
        {
            await service.ProcessQueuedEventAsync(id, CancellationToken.None);
        }
    }
}
