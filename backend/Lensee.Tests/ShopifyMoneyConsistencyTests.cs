using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lensee.Modules.Operations.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lensee.Tests;

[Collection(ShopifyEndpointContractTestCollection.CollectionName)]
public sealed class ShopifyMoneyConsistencyTests : IClassFixture<OperationsEndpointFactory>
{
    private readonly OperationsEndpointFactory _factory;

    public ShopifyMoneyConsistencyTests(OperationsEndpointFactory factory) => _factory = factory;

    [Fact]
    public async Task Webhook_HoldsWhenScalarTotalDisagreesWithMoneyBag()
    {
        await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        var payload = JsonSerializer.Serialize(new
        {
            id = "moneybag-mismatch-order",
            name = "#moneybag-mismatch-order",
            created_at = DateTime.UtcNow.AddMinutes(5).ToString("O"),
            updated_at = DateTime.UtcNow.AddMinutes(5).ToString("O"),
            currency = "EGP",
            presentment_currency = "USD",
            subtotal_price = "251.00",
            total_discounts = "0.00",
            total_shipping_price = "0.00",
            total_shipping_price_set = MoneyBag("0.00", "0.00"),
            total_tax = "0.00",
            total_price = "251.00",
            total_price_set = MoneyBag("251.01", "8.10"),
            taxes_included = false,
            line_items = new[]
            {
                new
                {
                    id = "moneybag-mismatch-line", variant_id = "moneybag-mismatch-variant", sku = "TEST-SKU",
                    quantity = 2, price = "125.50", total_discount = "0.00",
                    original_total_set = MoneyBag("251.00", "8.10"),
                    discounted_total_set = MoneyBag("251.00", "8.10"),
                    discount_allocations = Array.Empty<object>()
                }
            }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/integrations/shopify/webhooks")
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(payload))
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var signature = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(OperationsEndpointFactory.ShopifyWebhookSecret), Encoding.UTF8.GetBytes(payload)));
        request.Headers.Add("X-Shopify-Hmac-Sha256", signature);
        request.Headers.Add("X-Shopify-Webhook-Id", "moneybag-mismatch-delivery");
        request.Headers.Add("X-Shopify-Topic", "orders/create");
        request.Headers.Add("X-Shopify-Shop-Domain", OperationsEndpointFactory.ShopifyStoreDomain);
        request.Headers.Add("X-Shopify-Triggered-At", DateTime.UtcNow.ToString("O"));
        Assert.Equal(System.Net.HttpStatusCode.Accepted, (await client.SendAsync(request)).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<Lensee.Host.Infrastructure.ShopifyIntegrationService>();
            foreach (var id in await service.ClaimDueEventsAsync(CancellationToken.None))
                await service.ProcessQueuedEventAsync(id, CancellationToken.None);
        }

        using var verifyScope = _factory.Services.CreateScope();
        var operations = verifyScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var webhook = await operations.ShopifyWebhookEvents.SingleAsync(value => value.Topic == "orders/create" && value.ShopifyOrderId == "moneybag-mismatch-order");
        Assert.Equal("RequiresAttention", webhook.Status);
        Assert.Contains("does not match money bag", webhook.Detail ?? string.Empty);
        Assert.Empty(await operations.ShopifyOrderLinks.ToListAsync());
    }

    private static object MoneyBag(string shopAmount, string presentmentAmount) => new
    {
        shop_money = new { amount = shopAmount, currency_code = "EGP" },
        presentment_money = new { amount = presentmentAmount, currency_code = "USD" }
    };
}
