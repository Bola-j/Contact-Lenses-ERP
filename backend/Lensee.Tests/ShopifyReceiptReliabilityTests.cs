using System.Net;
using System.Security.Cryptography;
using System.Text;
using Lensee.Host.Infrastructure;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Lensee.Tests;

[Collection(ShopifyEndpointContractTestCollection.CollectionName)]
public sealed class ShopifyReceiptReliabilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedPersistence_IsNotAcknowledgedAsDuplicate_AndSameDeliveryCanRetry(bool failActivation)
    {
        using var source = new OperationsEndpointFactory();
        var failure = new FailReceiptSave(failActivation);
        var databaseName = "shopify-failed-receipt-" + Guid.NewGuid();
        using var host = source.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.RemoveAll<DbContextOptions<OperationsDbContext>>();
            services.AddDbContext<OperationsDbContext>(options => options.UseInMemoryDatabase(databaseName).AddInterceptors(failure));
        }));
        using var client = host.CreateClient();
        using var first = Request("TEST-receipt-retry");
        var rejected = await client.SendAsync(first);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        Assert.DoesNotContain("Duplicate", await rejected.Content.ReadAsStringAsync());
        using (var scope = host.Services.CreateScope())
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<OperationsDbContext>().ShopifyWebhookEvents.ToListAsync());
        failure.Enabled = false;
        using var second = Request("TEST-receipt-retry");
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(second)).StatusCode);
        using var third = Request("TEST-receipt-retry");
        var duplicate = await client.SendAsync(third);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("Duplicate", await duplicate.Content.ReadAsStringAsync());
        using var finalScope = host.Services.CreateScope();
        Assert.Single(await finalScope.ServiceProvider.GetRequiredService<OperationsDbContext>().ShopifyWebhookEvents.ToListAsync());
    }

    [Fact]
    public async Task SignedBurstAboveOldMinuteLimit_IsDurablyQueuedWithout429()
    {
        using var source = new OperationsEndpointFactory();
        using var host = source.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.RemoveAll<IHostedService>()));
        using var client = host.CreateClient();
        // Production startup persists this cutoff before accepting traffic. InMemory
        // does not enforce its unique index, so initialize it before concurrent requests.
        using (var startup = host.Services.CreateScope())
            await ShopifyActivation.EnsureAsync(startup.ServiceProvider.GetRequiredService<OperationsDbContext>(),
                startup.ServiceProvider.GetRequiredService<IClock>(), startup.ServiceProvider.GetRequiredService<IOptions<ShopifyOptions>>().Value,
                CancellationToken.None);
        var responses = await Task.WhenAll(Enumerable.Range(0, 80).Select(async number =>
        {
            using var request = Request($"TEST-burst-{number}");
            using var response = await client.SendAsync(request);
            return (Status: response.StatusCode, Body: await response.Content.ReadAsStringAsync());
        }));
        Assert.All(responses, response => Assert.True(response.Status == HttpStatusCode.Accepted, $"{response.Status}: {response.Body}"));
        using var scope = host.Services.CreateScope();
        Assert.Equal(80, await scope.ServiceProvider.GetRequiredService<OperationsDbContext>().ShopifyWebhookEvents.CountAsync());
    }

    private static HttpRequestMessage Request(string id)
    {
        var bytes = Encoding.UTF8.GetBytes("{\"id\":\"TEST-synthetic-receipt\"}");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/integrations/shopify/webhooks") { Content = new ByteArrayContent(bytes) };
        request.Headers.Add("X-Shopify-Hmac-Sha256", Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(OperationsEndpointFactory.ShopifyWebhookSecret), bytes)));
        request.Headers.Add("X-Shopify-Webhook-Id", id);
        request.Headers.Add("X-Shopify-Topic", "orders/create");
        request.Headers.Add("X-Shopify-Shop-Domain", OperationsEndpointFactory.ShopifyStoreDomain);
        return request;
    }

    private sealed class FailReceiptSave(bool failActivation) : SaveChangesInterceptor
    {
        public bool Enabled { get; set; } = true;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && (data.Context!.ChangeTracker.Entries<ShopifyWebhookEvent>().Any(x => x.State == EntityState.Added)
                || (failActivation && data.Context.ChangeTracker.Entries<ShopifyReconciliationCheckpoint>().Any(x => x.State == EntityState.Added))))
                throw new DbUpdateException("Synthetic receipt persistence failure.");
            return ValueTask.FromResult(result);
        }
    }
}
