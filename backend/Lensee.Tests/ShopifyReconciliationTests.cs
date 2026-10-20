using System.Net;
using System.Text;
using System.Text.Json;
using Lensee.Host.Infrastructure;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Lensee.Tests;

[Collection(ShopifyEndpointContractTestCollection.CollectionName)]
public sealed class ShopifyReconciliationTests
{
    [Fact]
    public async Task Refresh_EnqueuesCurrentOrderThroughInbox_AndPreservesOriginalReceipts()
    {
        using var source = new OperationsEndpointFactory();
        var handler = new RecoveryHandler(_ => Page("771", "2026-10-19T00:01:00Z"));
        using var host = CreateHost(source, new RecoveryClock(), handler);
        using var scope = host.Services.CreateScope();
        var recovery = scope.ServiceProvider.GetRequiredService<ShopifyReconciliationService>();
        Assert.True(await recovery.RefreshOrderAsync(OperationsEndpointFactory.ShopifyStoreDomain, "771", CancellationToken.None));
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var originalIds = await db.ShopifyWebhookEvents.Select(x => x.Id).ToListAsync();
        Assert.Equal(2, originalIds.Count);
        handler.Reply = _ =>
        {
            var response = Page("771", "2026-10-19T00:01:00Z");
            var json = response.Content!.ReadAsStringAsync().GetAwaiter().GetResult().Replace("TEST-SKU", "CORRECTED-SKU");
            response.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return response;
        };
        Assert.True(await recovery.RefreshOrderAsync(OperationsEndpointFactory.ShopifyStoreDomain, "771", CancellationToken.None));
        Assert.Equal(4, await db.ShopifyWebhookEvents.CountAsync());
        Assert.Equal(2, await db.ShopifyWebhookEvents.CountAsync(x => originalIds.Contains(x.Id) && x.ProtectedPayload != null));
        Assert.False(await recovery.RefreshOrderAsync(OperationsEndpointFactory.ShopifyStoreDomain, "772", CancellationToken.None));
        await Assert.ThrowsAsync<ShopifyOrderRefreshException>(() => recovery.RefreshOrderAsync("unrelated.myshopify.com", "771", CancellationToken.None));
    }

    [Fact]
    public async Task Recovery_RetainsRefundAmountAndParentWithoutPostingMoney()
    {
        using var source = new OperationsEndpointFactory();
        using var host = CreateHost(source, new RecoveryClock(), new RecoveryHandler(_ => Page("REFUND-ORDER", "2026-10-19T00:01:00Z", refund: true)));
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ShopifyReconciliationService>().RunDueAsync(CancellationToken.None);
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        await scope.ServiceProvider.GetRequiredService<ShopifyIntegrationService>().ClaimDueEventsAsync(CancellationToken.None);
        var refund = await db.ShopifyWebhookEvents.SingleAsync(x => x.Topic == "refunds/create");
        await scope.ServiceProvider.GetRequiredService<ShopifyIntegrationService>().ProcessQueuedEventAsync(refund.Id, CancellationToken.None);
        var review = Assert.Single(await db.ShopifyRefundReviews.ToListAsync());
        Assert.Equal("REFUND-ORDER", review.ShopifyOrderId);
        Assert.Equal("RECOVERED-REFUND", review.ShopifyRefundId);
        Assert.Equal(5m, review.Amount);
        Assert.Equal("EGP", review.Currency);
        Assert.Empty(await db.OperationLogs.ToListAsync());
    }

    [Fact]
    public async Task Recovery_QueuesCompleteSnapshot_OverlapDoesNotDuplicate_AndSkipsPreActivationOrders()
    {
        using var source = new OperationsEndpointFactory();
        var clock = new RecoveryClock();
        var handler = new RecoveryHandler(_ => Page("771", "2026-10-19T00:01:00Z"));
        using var host = CreateHost(source, clock, handler);
        using var scope = host.Services.CreateScope();
        var recovery = scope.ServiceProvider.GetRequiredService<ShopifyReconciliationService>();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        await recovery.RunDueAsync(CancellationToken.None);
        var events = await db.ShopifyWebhookEvents.ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, x => { Assert.Equal("AdminApi", x.VerificationMode); Assert.Equal("Queued", x.Status); });
        var checkpoint = Assert.Single(await db.ShopifyReconciliationCheckpoints.ToListAsync());
        Assert.NotNull(checkpoint.LastSuccessAt);
        clock.UtcNow = clock.UtcNow.AddMinutes(16);
        await recovery.RunDueAsync(CancellationToken.None);
        Assert.Equal(2, await db.ShopifyWebhookEvents.CountAsync());

        handler.Reply = _ => Page("OLD-772", "2026-10-18T00:00:00Z");
        clock.UtcNow = clock.UtcNow.AddMinutes(16);
        await recovery.RunDueAsync(CancellationToken.None);
        Assert.Equal(2, await db.ShopifyWebhookEvents.CountAsync());
    }

    [Fact]
    public async Task Recovery_ThrottlingRetainsWindow_RespectsRetryAfter_ThenResumes()
    {
        using var source = new OperationsEndpointFactory();
        var clock = new RecoveryClock();
        var calls = 0;
        var handler = new RecoveryHandler(_ => ++calls == 1 ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Headers = { RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(90)) }
        } : Page());
        using var host = CreateHost(source, clock, handler);
        using var scope = host.Services.CreateScope();
        var recovery = scope.ServiceProvider.GetRequiredService<ShopifyReconciliationService>();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        await recovery.RunDueAsync(CancellationToken.None);
        var checkpoint = Assert.Single(await db.ShopifyReconciliationCheckpoints.ToListAsync());
        Assert.NotNull(checkpoint.WindowStartedUtc);
        Assert.NotNull(checkpoint.WindowEndedUtc);
        Assert.Null(checkpoint.UpdatedThroughUtc);
        Assert.Contains("throttled", checkpoint.LastError!);
        Assert.Equal(clock.UtcNow.AddSeconds(90), checkpoint.NextRunAt);
        await recovery.RunDueAsync(CancellationToken.None);
        Assert.Equal(1, calls);
        clock.UtcNow = clock.UtcNow.AddSeconds(91);
        await recovery.RunDueAsync(CancellationToken.None);
        Assert.Equal(2, calls);
        Assert.NotNull(checkpoint.UpdatedThroughUtc);
        Assert.Null(checkpoint.LastError);
    }

    [Fact]
    public async Task Recovery_PersistsPageBeforeFailure_AndRedactsCredentials()
    {
        using var source = new OperationsEndpointFactory();
        var clock = new RecoveryClock();
        var calls = 0;
        var handler = new RecoveryHandler(_ => ++calls == 1 ? Page(more: true, cursor: "TEST-PAGE-2") :
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var host = CreateHost(source, clock, handler);
        using var scope = host.Services.CreateScope();
        var recovery = scope.ServiceProvider.GetRequiredService<ShopifyReconciliationService>();
        await recovery.RunDueAsync(CancellationToken.None);
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var checkpoint = Assert.Single(await db.ShopifyReconciliationCheckpoints.ToListAsync());
        Assert.Equal("TEST-PAGE-2", checkpoint.Cursor);
        Assert.Null(checkpoint.UpdatedThroughUtc);
        Assert.NotNull(checkpoint.LastError);
        var status = JsonSerializer.Serialize(await recovery.GetStatusAsync(CancellationToken.None));
        Assert.DoesNotContain("test-only-admin-token", status);
        Assert.DoesNotContain("TEST-PAGE-2", status);
        clock.UtcNow = clock.UtcNow.AddMinutes(2);
        string? resumedBody = null;
        handler.Reply = request => { resumedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult(); return Page(); };
        await recovery.RunDueAsync(CancellationToken.None);
        Assert.Contains("TEST-PAGE-2", resumedBody);
        Assert.Null(checkpoint.Cursor);
        Assert.NotNull(checkpoint.LastSuccessAt);
    }

    [Fact]
    public async Task Recovery_RejectsExternalHost_WithoutTransmittingToken()
    {
        using var source = new OperationsEndpointFactory();
        var handler = new RecoveryHandler(_ => throw new InvalidOperationException("Must not send HTTP"));
        using var host = CreateHost(source, new RecoveryClock(), handler, "example.com");
        using var scope = host.Services.CreateScope();
        var recovery = scope.ServiceProvider.GetRequiredService<ShopifyReconciliationService>();
        await recovery.RunDueAsync(CancellationToken.None);
        Assert.False((await recovery.GetStatusAsync(CancellationToken.None)).Available);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<OperationsDbContext>().ShopifyReconciliationCheckpoints.ToListAsync());
    }

    [Fact]
    public async Task ActivationCutoff_RemainsImmutableAfterConfigChange()
    {
        using var source = new OperationsEndpointFactory();
        await source.SeedAsync();
        using var scope = source.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var clock = new RecoveryClock();
        var first = await ShopifyActivation.EnsureAsync(db, clock, new ShopifyOptions
        {
            StoreDomain = "activation-test.myshopify.com",
            ActivationAtUtc = clock.UtcNow
        }, CancellationToken.None);
        var again = await ShopifyActivation.EnsureAsync(db, clock, new ShopifyOptions
        {
            StoreDomain = "activation-test.myshopify.com",
            ActivationAtUtc = clock.UtcNow.AddYears(-1)
        }, CancellationToken.None);
        Assert.Equal(first.ActivatedAtUtc, again.ActivatedAtUtc);
    }

    private static Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> CreateHost(OperationsEndpointFactory source,
        RecoveryClock clock, RecoveryHandler handler, string domain = OperationsEndpointFactory.ShopifyStoreDomain) =>
        source.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // These tests drive recovery explicitly; background workers would race the checkpoint assertions.
            services.RemoveAll<IHostedService>();
            services.Replace(ServiceDescriptor.Singleton<IOptions<ShopifyOptions>>(Options.Create(new ShopifyOptions
            {
                Enabled = true,
                ReconciliationEnabled = true,
                StoreDomain = domain,
                WebhookSecret = OperationsEndpointFactory.ShopifyWebhookSecret,
                AdminAccessToken = "test-only-admin-token",
                OnlineLocationId = source.ShopifyOnlineLocationId,
                ActivationAtUtc = new DateTime(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc)
            })));
            services.Replace(ServiceDescriptor.Singleton<IClock>(clock));
            services.Replace(ServiceDescriptor.Singleton<IHttpClientFactory>(new RecoveryHttpFactory(handler)));
        }));

    private static object Money(string amount) => new
    {
        shop_money = new { amount, currency_code = "EGP" },
        presentment_money = new { amount, currency_code = "EGP" }
    };

    private static HttpResponseMessage Page(string? id = null, string? created = null, bool more = false, string? cursor = null, bool refund = false)
    {
        object[] orders = id is null ? [] : [new {
            id = $"gid://shopify/Order/{id}", legacyResourceId = id, name = "TEST-RECOVERY-" + id,
            email = "test@example.invalid", phone = "+201000000000", createdAt = created, updatedAt = "2026-10-19T00:02:00Z",
            cancelledAt = (string?)null, edited = false, displayFinancialStatus = "PAID", currencyCode = "EGP",
            presentmentCurrencyCode = "EGP", test = true, taxesIncluded = false, paymentGatewayNames = new[] { "TEST Gateway" },
            billingAddress = new { firstName = "TEST", lastName = "Buyer" }, shippingAddress = new { address1 = "TEST Address", city = "Cairo", country = "Egypt" },
            refunds = refund ? new object[] { new { legacyResourceId = "RECOVERED-REFUND", createdAt = "2026-10-19T00:03:00Z", totalRefundedSet = Money("5.00") } } : Array.Empty<object>(), subtotalPriceSet = Money("90.00"), totalDiscountsSet = Money("10.00"),
            totalTaxSet = Money("0.00"), totalPriceSet = Money("95.00"), totalShippingPriceSet = Money("5.00"),
            currentSubtotalPriceSet = Money("90.00"), currentTotalDiscountsSet = Money("10.00"), currentTotalTaxSet = Money("0.00"),
            currentTotalPriceSet = Money("95.00"), currentShippingPriceSet = Money("5.00"),
            totalCashRoundingAdjustment = new { paymentSet = Money("0.00"), refundSet = Money("0.00") },
            lineItems = new { nodes = new[] { new {
                id = "gid://shopify/LineItem/772", sku = "TEST-SKU", title = "TEST Lens", variantTitle = "Blue", quantity = 2, currentQuantity = 2,
                variant = new { id = "gid://shopify/ProductVariant/773" }, originalUnitPriceSet = Money("50.00"),
                originalTotalSet = Money("100.00"), discountedTotalSet = Money("90.00"),
                discountAllocations = new[] { new { allocatedAmountSet = Money("10.00") } }, taxLines = Array.Empty<object>(), customAttributes = Array.Empty<object>()
            } }, pageInfo = new { hasNextPage = false, endCursor = (string?)null } },
            shippingLines = new { nodes = new[] { new { id = "gid://shopify/ShippingLine/774", title = "TEST Shipping", code = "TEST",
                originalPriceSet = Money("5.00"), discountedPriceSet = Money("5.00"), discountAllocations = Array.Empty<object>(), taxLines = Array.Empty<object>()
            } }, pageInfo = new { hasNextPage = false, endCursor = (string?)null } }
        }];
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                data = new { orders = new { nodes = orders, pageInfo = new { hasNextPage = more, endCursor = cursor } } }
            }), Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecoveryHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Reply { get; set; } = reply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Reply(request));
    }
    private sealed class RecoveryHttpFactory(RecoveryHandler handler) : IHttpClientFactory
    { public HttpClient CreateClient(string name) => new(handler, disposeHandler: false); }
    private sealed class RecoveryClock : IClock
    {
        public DateTime UtcNow { get; set; } = new(2026, 10, 19, 0, 5, 0, DateTimeKind.Unspecified);
        public DateTime EgyptNow => UtcNow.AddHours(3);
    }
}
