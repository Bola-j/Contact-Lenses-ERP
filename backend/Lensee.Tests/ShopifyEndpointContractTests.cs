using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Finance.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lensee.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class ShopifyEndpointContractTestCollection
{
    public const string CollectionName = "Shopify endpoint contract tests";
}

[Collection(ShopifyEndpointContractTestCollection.CollectionName)]
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

    [Fact]
    public async Task RememberedVariantLink_HoldsWhenCopiedSkuChanges()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        var firstPayload = OrderPayload("remembered-link-first", SkuCode(seed.SkuId));
        using var firstRequest = CreateWebhookRequest("orders/create", "webhook-remembered-first", firstPayload);
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(firstRequest)).StatusCode);
        await ProcessQueuedEventsAsync();

        var changedSkuPayload = OrderPayload("remembered-link-changed", "DIFFERENT-COPIED-SKU")
            .Replace("variant-remembered-link-changed", "variant-remembered-link-first", StringComparison.Ordinal);
        using var changedRequest = CreateWebhookRequest("orders/create", "webhook-remembered-changed", changedSkuPayload);
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(changedRequest)).StatusCode);
        await ProcessQueuedEventsAsync();

        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var firstEvent = await operations.ShopifyWebhookEvents.SingleAsync(value => value.ShopifyOrderId == "customer-remembered-link-first");
        var changedEvent = await operations.ShopifyWebhookEvents.SingleAsync(value => value.ShopifyOrderId == "customer-remembered-link-changed");
        Assert.Equal("Imported", firstEvent.Status);
        Assert.Equal("RequiresAttention", changedEvent.Status);
        Assert.Contains("different copied SKU", changedEvent.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await operations.OperationLogs.ToListAsync());
    }

    [Fact]
    public async Task UnresolvedVariantLink_AutoBindsWhenCorrectedCopyHasUniqueSku()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        var initialPayload = OrderPayload("unresolved-link-first", "MISSING-SKU");
        using var initialRequest = CreateWebhookRequest("orders/create", "webhook-unresolved-first", initialPayload);
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(initialRequest)).StatusCode);
        await ProcessQueuedEventsAsync();

        var correctedPayload = OrderPayload("unresolved-link-corrected", SkuCode(seed.SkuId))
            .Replace("variant-unresolved-link-corrected", "variant-unresolved-link-first", StringComparison.Ordinal);
        using var correctedRequest = CreateWebhookRequest("orders/create", "webhook-unresolved-corrected", correctedPayload);
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(correctedRequest)).StatusCode);
        await ProcessQueuedEventsAsync();

        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var initialEvent = await operations.ShopifyWebhookEvents.SingleAsync(value => value.ShopifyOrderId == "customer-unresolved-link-first");
        var correctedEvent = await operations.ShopifyWebhookEvents.SingleAsync(value => value.ShopifyOrderId == "customer-unresolved-link-corrected");
        var mapping = await operations.ShopifyVariantMappings.SingleAsync(value => value.ShopifyVariantId == "variant-unresolved-link-first");
        Assert.Equal("RequiresAttention", initialEvent.Status);
        Assert.Equal("Imported", correctedEvent.Status);
        Assert.Equal(seed.SkuId, mapping.SkuId);
        Assert.Equal(SkuCode(seed.SkuId), mapping.ShopifySku);
        Assert.Equal("Active", mapping.Status);
        Assert.Equal(2, mapping.Revision);
        Assert.Single(await operations.OperationLogs.ToListAsync());
    }

    [Fact]
    public async Task RecoveryEquivalentPayloadsKeepCanonicalHash_ButMaterialPropertyChangeNeedsReview()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        var orderId = "canonical-hash-equivalence";
        var createdAt = DateTime.UtcNow.AddMinutes(4);
        var firstPayload = MinimalWebhookOrderPayload(orderId, SkuCode(seed.SkuId), createdAt, createdAt, "pending");
        using var create = CreateWebhookRequest("orders/create", "webhook-canonical-create", firstPayload);
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(create)).StatusCode);
        await ProcessQueuedEventsAsync();

        string initialHash;
        using (var scope = _factory.Services.CreateScope())
        {
            var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            initialHash = (await operations.ShopifyOrderLinks.SingleAsync(value => value.ShopifyOrderId == $"customer-{orderId}")).CommercialSnapshotHash!;
        }

        var equivalentPayload = RecoveryOrderPayload(orderId, SkuCode(seed.SkuId), createdAt, createdAt.AddMinutes(1), "paid");
        using var equivalent = CreateWebhookRequest("orders/updated", "webhook-canonical-equivalent", equivalentPayload);
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(equivalent)).StatusCode);
        await ProcessQueuedEventsAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            var link = await operations.ShopifyOrderLinks.SingleAsync(value => value.ShopifyOrderId == $"customer-{orderId}");
            Assert.Equal(initialHash, link.CommercialSnapshotHash);
            Assert.False(link.RequiresReview);
            Assert.Equal("paid", link.FinancialStatus);
            Assert.True(link.FinancialStatusTrusted);
        }

        var materialNode = JsonNode.Parse(firstPayload)!.AsObject();
        materialNode["updated_at"] = createdAt.AddMinutes(2).ToString("O");
        materialNode["line_items"]!.AsArray()[0]!["properties"]!.AsArray()[0]!["value"] = "Left";
        using var material = CreateWebhookRequest("orders/updated", "webhook-canonical-material", materialNode.ToJsonString());
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(material)).StatusCode);
        await ProcessQueuedEventsAsync();

        using var finalScope = _factory.Services.CreateScope();
        var finalOperations = finalScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var finalLink = await finalOperations.ShopifyOrderLinks.SingleAsync(value => value.ShopifyOrderId == $"customer-{orderId}");
        var materialEvent = await finalOperations.ShopifyWebhookEvents.SingleAsync(value =>
            value.Topic == "orders/updated" && value.ShopifyOrderId == $"customer-{orderId}" && value.Status == "RequiresAttention");
        Assert.Equal(initialHash, finalLink.CommercialSnapshotHash);
        Assert.True(finalLink.RequiresReview);
        Assert.Equal("RequiresAttention", materialEvent.Status);
    }

    [Fact]
    public async Task RetryAndResolveRejectStaleProcessingEventsAndClearLeaseOnValidRetry()
    {
        await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        using var receipt = CreateWebhookRequest("orders/create", "webhook-stale-action", OrderPayload("stale-action", "UNKNOWN-SHOPIFY-SKU"));
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(receipt)).StatusCode);
        await ProcessQueuedEventsAsync();

        Guid eventId;
        using (var scope = _factory.Services.CreateScope())
        {
            var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            var eventRecord = await operations.ShopifyWebhookEvents.SingleAsync(value =>
                value.Topic == "orders/create" && value.ShopifyOrderId == "customer-stale-action");
            Assert.Equal("RequiresAttention", eventRecord.Status);
            eventRecord.Status = "Processing";
            eventRecord.LeaseToken = Guid.NewGuid();
            eventRecord.LeaseUntil = DateTime.UtcNow.AddMinutes(2);
            eventId = eventRecord.Id;
            await operations.SaveChangesAsync();
        }

        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.IntegrationsShopifyManage);
        var retryUri = $"/api/v1/integrations/shopify/events/{eventId}/retry";
        var resolveUri = $"/api/v1/integrations/shopify/events/{eventId}/resolve";
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(retryUri, content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(resolveUri, new { note = "Concurrent worker owns this event." })).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            var eventRecord = await operations.ShopifyWebhookEvents.SingleAsync(value => value.Id == eventId);
            Assert.Equal("Processing", eventRecord.Status);
            Assert.True(eventRecord.LeaseToken.HasValue);
            Assert.True(eventRecord.LeaseUntil.HasValue && eventRecord.LeaseUntil.Value > DateTime.UtcNow);
            eventRecord.Status = "RequiresAttention";
            await operations.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(retryUri, content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync(retryUri, content: null)).StatusCode);
        using var finalScope = _factory.Services.CreateScope();
        var finalEvent = await finalScope.ServiceProvider.GetRequiredService<OperationsDbContext>()
            .ShopifyWebhookEvents.SingleAsync(value => value.Id == eventId);
        Assert.Equal("Queued", finalEvent.Status);
        Assert.Null(finalEvent.LeaseToken);
        Assert.Null(finalEvent.LeaseUntil);
    }

    [Fact]
    public async Task UnlinkedShopifyCustomer_RemainsOtherPaymentsWithoutCreatingOrMatchingMerchant()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        using var request = CreateWebhookRequest("orders/create", "webhook-unlinked-customer", OrderPayload("unlinked-customer", SkuCode(seed.SkuId)));

        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(request)).StatusCode);
        await ProcessQueuedEventsAsync();

        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var crm = scope.ServiceProvider.GetRequiredService<Lensee.Modules.CRM.Data.CrmDbContext>();
        var operation = await operations.OperationLogs.SingleAsync();
        Assert.Null(operation.ClientId);
        Assert.Equal("Online Buyer", operation.ClientName);
        Assert.Equal("201000000000", operation.BuyerPhone);
        Assert.Equal("buyer@example.com", operation.BuyerEmail);
        Assert.Empty(await crm.Merchants.Where(value => value.ExternalProvider == "Shopify").ToListAsync());
    }

    [Fact]
    public async Task SameDeliveryIdInDifferentTopics_HasIndependentDurableIdentity()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        using var create = CreateWebhookRequest("orders/create", "same-provider-delivery", OrderPayload("topic-identity", SkuCode(seed.SkuId)));
        using var cancellation = CreateWebhookRequest("orders/cancelled", "same-provider-delivery", "{\"id\":\"customer-topic-identity\"}");

        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(create)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(cancellation)).StatusCode);
        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        Assert.Equal(2, await operations.ShopifyWebhookEvents.CountAsync());
    }

    [Fact]
    public async Task MonetaryRefundNotice_DoesNotImplicitlyCreateInventoryOrFinanceEffects()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        using var order = CreateWebhookRequest("orders/create", "webhook-refund-order", OrderPayload("refund-order", SkuCode(seed.SkuId)));
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(order)).StatusCode);
        await ProcessQueuedEventsAsync();

        using var refund = CreateWebhookRequest("refunds/create", "webhook-refund-money", "{\"order_id\":\"customer-refund-order\",\"id\":\"refund-1\"}");
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(refund)).StatusCode);
        await ProcessQueuedEventsAsync();

        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        Assert.Single(await operations.OperationLogs.ToListAsync());
        Assert.Empty(await finance.FinanceLedgerEntries.ToListAsync());
        Assert.Single(await notifications.NotificationLogs.Where(value => value.AlertType == "ShopifyRefundException").ToListAsync());
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
    public async Task SkuReadiness_ReportsSealedPackOnlyAsReadyForPackSales()
    {
        var seed = await _factory.SeedAsync();
        await SetSeedProductAsync(seed.SkuId, "Lens", "Daily", "30 days");
        using (var scope = _factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var sku = await catalog.Skus.Include(value => value.Product).SingleAsync(value => value.Id == seed.SkuId);
            sku.Product.SellMode = "SealedPackOnly";
            sku.Product.PiecesPerPack = 30;
            await catalog.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.IntegrationsShopifyRead);
        var result = await client.GetFromJsonAsync<PagedContract<ShopifySkuReadinessContract>>("/api/v1/integrations/shopify/sku-readiness");
        var skuReadiness = Assert.Single(result!.Items);
        Assert.Equal("SealedPackOnly", skuReadiness.SellMode);
        Assert.Equal(30, skuReadiness.PiecesPerPack);
        Assert.Equal("Ready", skuReadiness.Status);
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
            created_at = DateTime.UtcNow.AddMinutes(5).ToString("O"),
            updated_at = DateTime.UtcNow.AddMinutes(5).ToString("O"),
            email = "buyer@example.com",
            currency = "EGP",
            presentment_currency = "EGP",
            subtotal_price = "251.00",
            current_subtotal_price = "251.00",
            total_discounts = "0.00",
            total_shipping_price_set = new { shop_money = new { amount = "0.00", currency_code = "EGP" }, presentment_money = new { amount = "0.00", currency_code = "EGP" } },
            total_tax = "0.00",
            current_total_tax = "0.00",
            total_price = "251.00",
            current_total_price = "251.00",
            taxes_included = false,
            payment_gateway_names = new[] { "Card" },
            line_items = new[]
            {
                new { id = "right-line", variant_id = "right-variant", sku = skuCode, title = "Right eye", quantity = 1, price = "125.50", total_discount = "0.00", original_total_set = new { shop_money = new { amount = "125.50", currency_code = "EGP" } }, discounted_total_set = new { shop_money = new { amount = "125.50", currency_code = "EGP" } }, discount_allocations = Array.Empty<object>(), properties = new[] { new { name = "Eye", value = "Right" } } },
                new { id = "left-line", variant_id = "left-variant", sku = skuCode, title = "Left eye", quantity = 1, price = "125.50", total_discount = "0.00", original_total_set = new { shop_money = new { amount = "125.50", currency_code = "EGP" } }, discounted_total_set = new { shop_money = new { amount = "125.50", currency_code = "EGP" } }, discount_allocations = Array.Empty<object>(), properties = new[] { new { name = "Eye", value = "Left" } } }
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
            inventory.InventoryBatches.Add(new InventoryBatch { Id = Guid.NewGuid(), LocationId = seed.SubLocationId, SkuId = seed.SkuId, LotNumber = "ONLINE-1", ExpiryDate = expiry, Quantity = 4, CreatedAt = DateTime.UtcNow });
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
        using var successResponse = await client.PutAsJsonAsync($"/api/v1/operations/{operationId}/shopify-allocation", new { sourceLocationId = seed.SubLocationId, lines = detail!.Lines.Select(line => new { operationLineId = line.Id, lotNumber = "ONLINE-1", expiryDate = expiry }) });
        Assert.Equal(HttpStatusCode.NoContent, successResponse.StatusCode);
        Assert.All(detail.Lines, line => Assert.NotNull(line.ShopifyLineItemId));

        using var mappingScope = _factory.Services.CreateScope();
        var mappings = mappingScope.ServiceProvider.GetRequiredService<OperationsDbContext>().ShopifyVariantMappings;
        var eyeMappings = await mappings.Where(mapping => mapping.ShopifyVariantId == "right-variant" || mapping.ShopifyVariantId == "left-variant")
            .OrderBy(mapping => mapping.ShopifyVariantId).ToListAsync();
        Assert.Equal(new[] { "left-variant", "right-variant" }, eyeMappings.Select(mapping => mapping.ShopifyVariantId));
        Assert.All(eyeMappings, mapping => Assert.Equal(seed.SkuId, mapping.SkuId));
        Assert.All(eyeMappings, mapping => Assert.Equal("Active", mapping.Status));
    }

    [Fact]
    public async Task ShopifyAllocation_WritesOneExplicitAuditWithoutMiddlewareDuplicate()
    {
        using var factory = new OperationsEndpointFactory(useRealAuditWriter: true);
        var seed = await factory.SeedAsync();
        using var client = factory.CreateClient();
        using var request = CreateWebhookRequest("orders/create", "webhook-audit-allocation", OrderPayload("audit-allocation", SkuCode(seed.SkuId)));
        Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(request)).StatusCode);

        using (var processingScope = factory.Services.CreateScope())
        {
            var service = processingScope.ServiceProvider.GetRequiredService<Lensee.Host.Infrastructure.ShopifyIntegrationService>();
            foreach (var eventId in await service.ClaimDueEventsAsync(CancellationToken.None))
            {
                await service.ProcessQueuedEventAsync(eventId, CancellationToken.None);
            }
        }

        using (var diagnosticScope = factory.Services.CreateScope())
        {
            var received = await diagnosticScope.ServiceProvider.GetRequiredService<OperationsDbContext>().ShopifyWebhookEvents.SingleAsync();
            Assert.Equal("Imported", received.Status);
            Assert.NotNull(received.OperationId);
        }
        Guid operationId;
        Guid operationLineId;
        DateOnly expiry = new(2028, 6, 1);
        using (var setupScope = factory.Services.CreateScope())
        {
            var operations = setupScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
            var inventory = setupScope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var operation = await operations.OperationLogs.Include(value => value.OperationLines).SingleAsync();
            operationId = operation.Id;
            operationLineId = Assert.Single(operation.OperationLines).Id;
            inventory.InventoryBatches.Add(new InventoryBatch
            {
                Id = Guid.NewGuid(),
                LocationId = seed.SubLocationId,
                SkuId = seed.SkuId,
                LotNumber = "ONLINE-AUDIT",
                ExpiryDate = expiry,
                Quantity = 4,
                CreatedAt = DateTime.UtcNow
            });
            await inventory.SaveChangesAsync();
        }

        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsWrite, LenseePermissions.OperationsRead);
        using var allocationResponse = await client.PutAsJsonAsync($"/api/v1/operations/{operationId}/shopify-allocation", new
        {
            sourceLocationId = seed.SubLocationId,
            lines = new[] { new { operationLineId, lotNumber = "ONLINE-AUDIT", expiryDate = expiry } }
        });

        Assert.Equal(HttpStatusCode.NoContent, allocationResponse.StatusCode);
        Assert.Equal(["UpdateAllocation"], await factory.GetOperationAuditActionsAsync(operationId));
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

    private static string MinimalWebhookOrderPayload(string id, string sku, DateTime createdAt, DateTime updatedAt, string financialStatus) => JsonSerializer.Serialize(new
    {
        id = $"customer-{id}",
        name = "#Canonical",
        created_at = createdAt.ToString("O"),
        updated_at = updatedAt.ToString("O"),
        financial_status = financialStatus,
        email = "buyer@example.com",
        phone = "+20 100 000 0000",
        currency = "EGP",
        presentment_currency = "EGP",
        subtotal_price = "251.00",
        total_discounts = "0.00",
        total_shipping_price = "0.00",
        total_tax = "0.00",
        total_price = "251.00",
        taxes_included = false,
        shipping_address = CanonicalTestAddress(),
        billing_address = CanonicalTestAddress(),
        shipping_lines = new[] { new { id = "ship-hash", title = "Standard", code = "standard", price = "0.00", discounted_price = "0.00", discount_allocations = Array.Empty<object>(), tax_lines = Array.Empty<object>() } },
        line_items = new[] { new { id = "line-hash", variant_id = "variant-hash", sku, title = "Lens", variant_title = "Hazel", quantity = 2, price = "125.50", total_discount = "0.00", properties = new[] { new { name = "Eye", value = "Right" } } } }
    });

    private static string RecoveryOrderPayload(string id, string sku, DateTime createdAt, DateTime updatedAt, string financialStatus)
    {
        var payload = JsonSerializer.Serialize(new
        {
            id = $"customer-{id}",
            name = "#Canonical",
            created_at = createdAt.ToString("O"),
            updated_at = updatedAt.ToString("O"),
            financial_status = financialStatus,
            email = "buyer@example.com",
            phone = "+20 100 000 0000",
            currency = "EGP",
            presentment_currency = "EGP",
            taxes_included = false,
            subtotal_price = "251.000",
            subtotal_price_set = TestMoneyBag("251.000"),
            current_subtotal_price = "251.000",
            current_subtotal_price_set = TestMoneyBag("251.000"),
            total_discounts = "0.000",
            total_discounts_set = TestMoneyBag("0.000"),
            current_total_discounts = "0.000",
            current_total_discounts_set = TestMoneyBag("0.000"),
            total_shipping_price = "0.000",
            total_shipping_price_set = TestMoneyBag("0.000"),
            current_shipping_price = "0.000",
            current_shipping_price_set = TestMoneyBag("0.000"),
            total_tax = "0.000",
            total_tax_set = TestMoneyBag("0.000"),
            current_total_tax = "0.000",
            current_total_tax_set = TestMoneyBag("0.000"),
            total_price = "251.000",
            total_price_set = TestMoneyBag("251.000"),
            current_total_price = "251.000",
            current_total_price_set = TestMoneyBag("251.000"),
            total_cash_rounding_payment_adjustment_set = TestMoneyBag("0.000"),
            total_cash_rounding_refund_adjustment_set = TestMoneyBag("0.000"),
            shipping_address = CanonicalTestAddress(),
            billing_address = CanonicalTestAddress(),
            shipping_lines = new[] { new { id = "ship-hash", title = "Standard", code = "standard", price = "0.000", price_set = TestMoneyBag("0.000"), discounted_price = "0.000", discounted_price_set = TestMoneyBag("0.000"), discount_allocations = Array.Empty<object>(), tax_lines = Array.Empty<object>() } },
            line_items = new[] { new { id = "line-hash", variant_id = "variant-hash", sku, title = "Lens", variant_title = "Hazel", quantity = 2, current_quantity = 2, price = "125.500", price_set = TestMoneyBag("125.500"), original_total_set = TestMoneyBag("251.000"), discounted_total_set = TestMoneyBag("251.000"), total_discount = "0.000", discount_allocations = Array.Empty<object>(), tax_lines = Array.Empty<object>(), properties = new[] { new { name = "Eye", value = "Right" } } } }
        });
        var scaled = payload.Replace("125.500", "125.5000", StringComparison.Ordinal)
            .Replace("251.000", "251.0000", StringComparison.Ordinal)
            .Replace("0.000", "0.0000", StringComparison.Ordinal);
        return ReverseJsonPropertyOrder(scaled);
    }

    private static object TestMoneyBag(string amount) => new
    {
        shop_money = new { amount, currency_code = "EGP" },
        presentment_money = new { amount, currency_code = "EGP" }
    };

    private static object CanonicalTestAddress() => new
    {
        first_name = "Online",
        last_name = "Buyer",
        address1 = "15 Nile Street",
        address2 = (string?)null,
        city = "Cairo",
        province = "Cairo",
        province_code = (string?)null,
        zip = "11511",
        country = "Egypt",
        country_code = "EG",
        phone = "+20 100 000 0000",
        company = (string?)null
    };

    private static string ReverseJsonPropertyOrder(string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteReversed(document.RootElement, writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteReversed(JsonElement value, Utf8JsonWriter writer)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().Reverse())
            {
                writer.WritePropertyName(property.Name);
                WriteReversed(property.Value, writer);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) WriteReversed(item, writer);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }

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
        request.Headers.Add("X-Shopify-Triggered-At", DateTime.UtcNow.ToString("O"));
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
        created_at = DateTime.UtcNow.AddMinutes(5).ToString("O"),
        updated_at = DateTime.UtcNow.AddMinutes(5).ToString("O"),
        email = "buyer@example.com",
        phone = "+20 100 000 0000",
        currency = "EGP",
        presentment_currency = "EGP",
        subtotal_price = "251.00",
        current_subtotal_price = "251.00",
        total_discounts = "0.00",
        total_shipping_price_set = new { shop_money = new { amount = "0.00", currency_code = "EGP" }, presentment_money = new { amount = "0.00", currency_code = "EGP" } },
        total_tax = "0.00",
        current_total_tax = "0.00",
        total_price = "251.00",
        current_total_price = "251.00",
        taxes_included = false,
        payment_gateway_names = new[] { gateway },
        customer = new { id = $"shopify-buyer-{id}", first_name = "Online", last_name = "Buyer", email = "buyer@example.com", phone = "+20 100 000 0000" },
        shipping_address = new { first_name = "Online", last_name = "Buyer", address1 = "15 Nile Street", city = "Cairo", country = "EG", phone = "+20 100 000 0000" },
        line_items = new[] { new { id = $"line-{id}", variant_id = $"variant-{id}", sku, title = "Lens", variant_title = "Hazel", quantity = 2, price = "125.50", total_discount = "0.00", original_total_set = new { shop_money = new { amount = "251.00", currency_code = "EGP" } }, discounted_total_set = new { shop_money = new { amount = "251.00", currency_code = "EGP" } }, discount_allocations = Array.Empty<object>(), properties = new[] { new { name = "Eye", value = "Right" } } } }
    });

    private async Task ProcessQueuedEventsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<Lensee.Host.Infrastructure.ShopifyIntegrationService>();
        foreach (var id in await service.ClaimDueEventsAsync(CancellationToken.None)) await service.ProcessQueuedEventAsync(id, CancellationToken.None);
    }

    private sealed class PagedContract<T> { public List<T> Items { get; init; } = []; }
    private sealed class ShopifySkuReadinessContract { public string? WearCycle { get; init; } public string? WearDuration { get; init; } public string? Status { get; init; } public string? SellMode { get; init; } public int? PiecesPerPack { get; init; } }
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
