using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lensee.Host.Infrastructure;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.SharedKernel.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lensee.PostgresIntegrationTests;

public sealed class ShopifyIntakePostgresTests : IAsyncLifetime
{
    private const string ShopDomain = "lensee-qa.myshopify.com";
    private const string WebhookSecret = "shopify-postgres-integration-test-secret";
    private const string LegacyPathSecret = "qa-legacy-path-secret-0123456789abcdef";
    private const string LotNumber = "SHOPIFY-PG-LOT-01";
    private const string OtherLotNumber = "SHOPIFY-PG-LOT-02";
    private static readonly DateOnly BatchExpiry = new(2035, 12, 31);

    private readonly DateTime _activationAtUtc = DateTime.UtcNow.AddMinutes(-10);
    private readonly Guid _onlineLocationId = Guid.NewGuid();
    private readonly Guid _testActorId = Guid.NewGuid();
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("lensee_shopify")
        .WithUsername("lensee_user")
        .WithPassword("SomeStrongPassword123!")
        .Build();

    private PostgresApplicationFactory _migrationFactory = null!;
    private WebApplicationFactory<Program> _shopifyFactory = null!;
    private HttpClient _client = null!;
    private ShopifyFixtureSeed _seed = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _migrationFactory = new PostgresApplicationFactory(_postgres.GetConnectionString());
        await _migrationFactory.MigrateAllAsync();
        await _migrationFactory.SeedUserAsync(_testActorId);
        await SeedOnlineLocationAsync();

        _shopifyFactory = _migrationFactory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Shopify:Enabled"] = "true",
                ["Shopify:WebhookSecret"] = WebhookSecret,
                ["Shopify:LegacyWebhookPathSecret"] = LegacyPathSecret,
                ["Shopify:StoreDomain"] = ShopDomain,
                ["Shopify:ActivationAtUtc"] = _activationAtUtc.ToString("O"),
                ["Shopify:PayloadRetentionDays"] = "1",
                ["Shopify:OnlineLocationId"] = _onlineLocationId.ToString()
            });
        }));
        _client = _shopifyFactory.CreateClient();
        _seed = await SeedShopifyFixtureAsync();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_shopifyFactory is not null) await _shopifyFactory.DisposeAsync();
        if (_migrationFactory is not null) await _migrationFactory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [PostgreSqlIntegrationFact]
    public async Task ConcurrentSignedOrderReceipts_PersistBeforeAcknowledgementAndCreateOneCommercialDraft()
    {
        var orderId = NewProviderId();
        var payload = CreateOrderPayload(orderId, "paid", _activationAtUtc.AddMinutes(2), quantity: 2,
            unitPrice: "5.50", discount: "1.00", shipping: "2.00", tax: "1.00", total: "13.00", presentmentCurrency: "USD");

        var receipts = await Task.WhenAll(
            SendSignedWebhookAsync("orders/create", "delivery-concurrent-order", payload),
            SendSignedWebhookAsync("orders/create", "delivery-concurrent-order", payload));

        var receiptDiagnostics = string.Join(" | ", await Task.WhenAll(receipts.Select(async value =>
            $"{(int)value.StatusCode}: {await value.Content.ReadAsStringAsync()}")));
        Assert.True(receipts.Any(value => value.StatusCode == HttpStatusCode.Accepted), receiptDiagnostics);
        Assert.True(receipts.Any(value => value.StatusCode == HttpStatusCode.OK), receiptDiagnostics);
        foreach (var receipt in receipts) receipt.Dispose();
        var eventRowsBeforeWorker = await QueryOperationsAsync(db => db.ShopifyWebhookEvents
            .CountAsync(value => value.EventId == "delivery-concurrent-order"));
        Assert.Equal(1, eventRowsBeforeWorker);

        await ProcessDueEventsAsync();

        var imported = await QueryOperationsAsync(async db =>
        {
            var link = await db.ShopifyOrderLinks.SingleOrDefaultAsync(value => value.ShopifyOrderId == orderId);
            if (link is null) return null;
            var operation = await db.OperationLogs.Include(value => value.OperationLines)
                .SingleAsync(value => value.Id == link.OperationId);
            return new { Link = link, Operation = operation };
        });
        var eventDiagnostics = await QueryOperationsAsync(async db => string.Join(" | ", await db.ShopifyWebhookEvents
            .Where(value => value.EventId == "delivery-concurrent-order")
            .Select(value => value.Status + ": " + value.Detail + $" (attempt={value.AttemptCount}, order={value.ShopifyOrderId}, lease={value.LeaseToken}, until={value.LeaseUntil})")
            .ToListAsync()));
        Assert.True(imported is not null, eventDiagnostics);
        Assert.Equal("Draft", imported!.Operation.Status);
        Assert.Null(imported.Operation.SourceLocationId);
        Assert.Single(imported.Operation.OperationLines);
        Assert.Equal("Packs", imported.Operation.OperationLines.Single().EntryMode);
        Assert.Equal(_seed.SkuId, imported.Operation.OperationLines.Single().SkuId);
        Assert.False(imported.Operation.ExternalPaymentVerified);
        Assert.Null(imported.Operation.FinanceAccountId);
        Assert.Equal("paid", imported.Link.FinancialStatus, ignoreCase: true);
        Assert.True(imported.Link.FinancialStatusTrusted);
        Assert.Contains("13.00", imported.Link.CommercialSnapshot ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("1.00", imported.Link.CommercialSnapshot ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("EGP", imported.Link.CommercialSnapshot ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("presentment_money", imported.Link.CommercialSnapshot ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("USD", imported.Link.CommercialSnapshot ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("total_price_set", imported.Link.CommercialSnapshot ?? string.Empty, StringComparison.Ordinal);

        var payments = await QueryServiceAsync<PaymentsDbContext, int>(db => db.MainPaymentLogs
            .CountAsync(value => value.OperationId == imported.Operation.Id));
        Assert.Equal(0, payments);
    }

    [PostgreSqlIntegrationFact]
    public async Task ExpiredProcessingLease_IsReclaimedAndProcessedByTheInboxWorker()
    {
        var orderId = NewProviderId();
        var payload = CreateOrderPayload(orderId, "pending", DateTime.UtcNow.AddMinutes(-1));
        using var response = await SendSignedWebhookAsync("orders/create", "delivery-expired-lease", payload);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var eventId = await QueryOperationsAsync(db => db.ShopifyWebhookEvents
            .Where(value => value.EventId == "delivery-expired-lease")
            .Select(value => value.Id).SingleAsync());
        await ExecuteOperationsAsync(async db =>
        {
            var row = await db.ShopifyWebhookEvents.SingleAsync(value => value.Id == eventId);
            row.Status = "Processing";
            row.LeaseUntil = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-5), DateTimeKind.Unspecified);
            row.NextAttemptAt = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-5), DateTimeKind.Unspecified);
            await db.SaveChangesAsync();
        });

        var claimed = await ClaimDueEventsAsync();
        Assert.Contains(eventId, claimed);
        await ProcessEventAsync(eventId);

        var imported = await QueryOperationsAsync(async db =>
        {
            var link = await db.ShopifyOrderLinks.SingleOrDefaultAsync(value => value.ShopifyOrderId == orderId);
            var eventRow = await db.ShopifyWebhookEvents.SingleAsync(value => value.Id == eventId);
            return new { Link = link, Event = eventRow };
        });
        Assert.NotNull(imported.Link);
        Assert.Equal("Imported", imported.Event.Status);
    }

    [PostgreSqlIntegrationFact]
    public async Task ReclaimedLeaseFencesTheOldWorkerBeforeItCanApplyTheOrder()
    {
        var orderId = NewProviderId();
        using var response = await SendSignedWebhookAsync("orders/create", "delivery-lease-fence",
            CreateOrderPayload(orderId, "pending", DateTime.UtcNow));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var eventId = await QueryOperationsAsync(db => db.ShopifyWebhookEvents
            .Where(value => value.EventId == "delivery-lease-fence").Select(value => value.Id).SingleAsync());
        Assert.Contains(eventId, await ClaimDueEventsAsync());

        // Hold the per-order lock after the stale worker has read its claim token,
        // allowing a second worker to reclaim the expired lease first.
        await using var blockerScope = _shopifyFactory.Services.CreateAsyncScope();
        var blockerDb = blockerScope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        await using var blockerTransaction = await blockerDb.Database.BeginTransactionAsync();
        var orderLockKey = $"{ShopDomain}:{orderId}";
        await blockerDb.Database.ExecuteSqlInterpolatedAsync(
            $"select pg_advisory_xact_lock(hashtextextended({orderLockKey}, 0))");

        var staleWorker = ProcessEventInNewScopeAsync(eventId);
        await WaitForAdvisoryLockWaitAsync();

        await ExecuteOperationsAsync(async db =>
        {
            var row = await db.ShopifyWebhookEvents.SingleAsync(value => value.Id == eventId);
            row.LeaseUntil = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-5), DateTimeKind.Unspecified);
            await db.SaveChangesAsync();
        });
        Assert.Contains(eventId, await ClaimDueEventsAsync());
        var reclaimedToken = await QueryOperationsAsync(db => db.ShopifyWebhookEvents
            .Where(value => value.Id == eventId).Select(value => value.LeaseToken).SingleAsync());
        Assert.NotNull(reclaimedToken);

        await blockerTransaction.CommitAsync();
        await staleWorker;

        var fenced = await QueryOperationsAsync(async db => new
        {
            Event = await db.ShopifyWebhookEvents.SingleAsync(value => value.Id == eventId),
            Imported = await db.ShopifyOrderLinks.AnyAsync(value => value.ShopifyOrderId == orderId)
        });
        Assert.Equal("Processing", fenced.Event.Status);
        Assert.Equal(reclaimedToken, fenced.Event.LeaseToken);
        Assert.False(fenced.Imported);

        await ProcessEventAsync(eventId);
        var completed = await QueryOperationsAsync(async db => new
        {
            Event = await db.ShopifyWebhookEvents.SingleAsync(value => value.Id == eventId),
            Links = await db.ShopifyOrderLinks.CountAsync(value => value.ShopifyOrderId == orderId),
            Operations = await db.OperationLogs.CountAsync(value => value.SalesChannel == "Shopify")
        });
        Assert.Equal("Imported", completed.Event.Status);
        Assert.Null(completed.Event.LeaseToken);
        Assert.Equal(1, completed.Links);
        Assert.Equal(1, completed.Operations);
    }

    [PostgreSqlIntegrationFact]
    public async Task InconsistentOrUnsupportedCommercialSnapshot_IsHeldBeforeCreatingAnOperation()
    {
        var mismatchId = NewProviderId();
        using var mismatchReceipt = await SendSignedWebhookAsync("orders/create", "delivery-inconsistent-total",
            CreateOrderPayload(mismatchId, "paid", DateTime.UtcNow, total: "11.00"));
        Assert.Equal(HttpStatusCode.Accepted, mismatchReceipt.StatusCode);
        await ProcessDueEventsAsync();

        var centMismatchId = NewProviderId();
        using var centMismatchReceipt = await SendSignedWebhookAsync("orders/create", "delivery-cent-mismatch",
            CreateOrderPayload(centMismatchId, "paid", DateTime.UtcNow, total: "10.01"));
        Assert.Equal(HttpStatusCode.Accepted, centMismatchReceipt.StatusCode);
        await ProcessDueEventsAsync();

        var roundedId = NewProviderId();
        using var roundedReceipt = await SendSignedWebhookAsync("orders/create", "delivery-explicit-cash-rounding",
            CreateOrderPayload(roundedId, "paid", DateTime.UtcNow, total: "10.01", paymentCashRounding: "0.01"));
        Assert.Equal(HttpStatusCode.Accepted, roundedReceipt.StatusCode);
        await ProcessDueEventsAsync();

        var unsupportedId = NewProviderId();
        using var unsupportedReceipt = await SendSignedWebhookAsync("orders/create", "delivery-unsupported-currency",
            CreateOrderPayload(unsupportedId, "paid", DateTime.UtcNow, currency: "USD"));
        Assert.Equal(HttpStatusCode.Accepted, unsupportedReceipt.StatusCode);
        await ProcessDueEventsAsync();

        var held = await QueryOperationsAsync(async db => new
        {
            MismatchLink = await db.ShopifyOrderLinks.AnyAsync(value => value.ShopifyOrderId == mismatchId),
            CentMismatchLink = await db.ShopifyOrderLinks.AnyAsync(value => value.ShopifyOrderId == centMismatchId),
            RoundedLink = await db.ShopifyOrderLinks.SingleOrDefaultAsync(value => value.ShopifyOrderId == roundedId),
            UnsupportedLink = await db.ShopifyOrderLinks.AnyAsync(value => value.ShopifyOrderId == unsupportedId),
            MismatchEvent = await db.ShopifyWebhookEvents.SingleAsync(value => value.EventId == "delivery-inconsistent-total"),
            CentMismatchEvent = await db.ShopifyWebhookEvents.SingleAsync(value => value.EventId == "delivery-cent-mismatch"),
            UnsupportedEvent = await db.ShopifyWebhookEvents.SingleAsync(value => value.EventId == "delivery-unsupported-currency"),
            ShopifyOperations = await db.OperationLogs.CountAsync(value => value.SalesChannel == "Shopify")
        });
        Assert.False(held.MismatchLink);
        Assert.False(held.CentMismatchLink);
        Assert.NotNull(held.RoundedLink);
        Assert.Contains("0.01", held.RoundedLink!.CommercialSnapshot ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("rounding", held.RoundedLink.CommercialSnapshot ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.False(held.UnsupportedLink);
        Assert.Equal("RequiresAttention", held.MismatchEvent.Status);
        Assert.Equal("RequiresAttention", held.CentMismatchEvent.Status);
        Assert.Equal("RequiresAttention", held.UnsupportedEvent.Status);
        Assert.Equal(1, held.ShopifyOperations);
        Assert.Contains("total", held.MismatchEvent.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USD", held.UnsupportedEvent.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [PostgreSqlIntegrationFact]
    public async Task LegacyPathPaidNotice_IsNotTrustedForShopifyDispatch()
    {
        var orderId = NewProviderId();
        using var receipt = await SendLegacyWebhookAsync("orders/create", "delivery-legacy-paid",
            CreateOrderPayload(orderId, "paid", DateTime.UtcNow));
        Assert.Equal(HttpStatusCode.Accepted, receipt.StatusCode);
        await ProcessDueEventsAsync();

        var order = await QueryOperationsAsync(async db =>
        {
            var link = await db.ShopifyOrderLinks.SingleAsync(value => value.ShopifyOrderId == orderId);
            var operation = await db.OperationLogs.SingleAsync(value => value.Id == link.OperationId);
            return new { Link = link, Operation = operation };
        });
        Assert.Equal("paid", order.Link.FinancialStatus, ignoreCase: true);
        Assert.False(order.Link.FinancialStatusTrusted);
        Assert.False(order.Operation.ExternalPaymentVerified);
        Assert.Equal("Draft", order.Operation.Status);
    }

    [PostgreSqlIntegrationFact]
    public async Task TrustedShopifyPaidAllowsDispatchButDoesNotCreateProviderOrFinanceSuccess()
    {
        await SeedPackStockAsync();
        var paidOperationId = await ImportAndReserveAsync("pending", "paid-dispatch");
        var unpaidOperationId = await ImportAndReserveAsync("pending", "unpaid-dispatch");
        var paidOrderId = await QueryOperationsAsync(db => db.ShopifyOrderLinks
            .Where(value => value.OperationId == paidOperationId).Select(value => value.ShopifyOrderId).SingleAsync());
        using var paidNotice = await SendSignedWebhookAsync("orders/updated", "delivery-paid-update",
            CreateOrderPayload(paidOrderId, "paid", DateTime.UtcNow, updatedAtUtc: DateTime.UtcNow.AddMinutes(1)));
        Assert.Equal(HttpStatusCode.Accepted, paidNotice.StatusCode);
        await ProcessDueEventsAsync();

        using var paidDispatch = await PostAuthorizedAsync($"/api/v1/operations/{paidOperationId}/dispatch");
        Assert.True(paidDispatch.IsSuccessStatusCode, await paidDispatch.Content.ReadAsStringAsync());

        using var unpaidDispatch = await PostAuthorizedAsync($"/api/v1/operations/{unpaidOperationId}/dispatch");
        Assert.False(unpaidDispatch.IsSuccessStatusCode);

        var outcomes = await QueryOperationsAsync(async db =>
        {
            var paid = await db.OperationLogs.SingleAsync(value => value.Id == paidOperationId);
            var unpaid = await db.OperationLogs.SingleAsync(value => value.Id == unpaidOperationId);
            var paidLink = await db.ShopifyOrderLinks.SingleAsync(value => value.OperationId == paidOperationId);
            return new { Paid = paid, Unpaid = unpaid, PaidLink = paidLink };
        });
        Assert.Equal("Dispatched", outcomes.Paid.Status);
        Assert.Equal("Reserved", outcomes.Unpaid.Status);
        Assert.True(outcomes.PaidLink.FinancialStatusTrusted);
        Assert.Equal("paid", outcomes.PaidLink.FinancialStatus, ignoreCase: true);
        Assert.False(outcomes.Paid.ExternalPaymentVerified);
        Assert.Null(outcomes.Paid.ExternalPaymentProvider);
        Assert.Null(outcomes.Paid.ExternalPaymentReference);
        Assert.Null(outcomes.Paid.FinanceAccountId);

        var paymentLogCount = await QueryServiceAsync<PaymentsDbContext, int>(db => db.MainPaymentLogs
            .CountAsync(value => value.OperationId == paidOperationId || value.OperationId == unpaidOperationId));
        Assert.Equal(0, paymentLogCount);
    }

    [PostgreSqlIntegrationFact]
    public async Task PieceReservationPinsExactLooseAndSealedUnits_AndCancellationReleasesPhysicalCapacityOnce()
    {
        await SetShopifyProductSellModeAsync("Both");
        await SeedPackStockAsync(quantity: 1);
        await SeedPackStockAsync(quantity: 2, lotNumber: OtherLotNumber);
        await SeedLoosePiecesAsync(1);
        var (operationId, orderId) = await ImportAndReservePieceOrderAsync("pending", "piece-cancel");

        var reserved = await QueryServiceAsync<InventoryDbContext, PieceReservationSnapshot>(async db =>
        {
            var operation = await QueryOperationsAsync(async operations =>
            {
                var row = await operations.OperationLogs.Include(value => value.OperationLines)
                    .SingleAsync(value => value.Id == operationId);
                return new { row.Status, Line = row.OperationLines.Single() };
            });
            var balance = await db.StockBalances.SingleAsync(value => value.LocationId == _seed.LocationId && value.SkuId == _seed.SkuId);
            var batch = await db.InventoryBatches.SingleAsync(value => value.LocationId == _seed.LocationId && value.SkuId == _seed.SkuId && value.LotNumber == LotNumber);
            var loose = await db.OpenedPieceLots.Where(value => value.LocationId == _seed.LocationId && value.SkuId == _seed.SkuId).SumAsync(value => value.LoosePieceQuantity);
            var reservation = await db.StockPieceReservations.SingleAsync(value => value.OperationLineId == operation.Line.Id);
            var allocations = await db.StockPieceReservationAllocations.Where(value => value.ReservationId == reservation.Id).ToListAsync();
            var saleAndOpenCount = await db.StockTransactions.CountAsync(value => value.ReferenceOperationId == operationId &&
                (value.TransactionType == Lensee.Modules.Inventory.Services.InventoryTransactionTypes.Sale || value.TransactionType == Lensee.Modules.Inventory.Services.InventoryTransactionTypes.PackOpen));
            return new PieceReservationSnapshot(operation.Status, operation.Line.EntryMode, operation.Line.Quantity,
                balance.AvailableQty, balance.ReservedInWarehouseQty, batch.Quantity, loose, reservation.Status,
                reservation.RequestedPieceQuantity, allocations, saleAndOpenCount);
        });
        Assert.Equal("Reserved", reserved.OperationStatus);
        Assert.Equal("Pieces", reserved.LineMode);
        Assert.Equal(2, reserved.LineQuantity);
        Assert.Equal(2, reserved.BalanceAvailable);
        Assert.Equal(1, reserved.BalanceReserved);
        Assert.Equal(1, reserved.BatchQuantity);
        Assert.Equal(1, reserved.LooseQuantity);
        Assert.Equal("Active", reserved.ReservationStatus);
        Assert.Equal(2, reserved.RequestedPieceQuantity);
        Assert.Equal(2, reserved.Allocations.Sum(value => value.PieceQuantity));
        Assert.Contains(reserved.Allocations, value => value.SourceKind == "OpenedPieceLot" && value.PieceQuantity == 1 && value.PhysicalPacksHeld == 0);
        Assert.Contains(reserved.Allocations, value => value.SourceKind == "SealedBatch" && value.PieceQuantity == 1 && value.PhysicalPacksHeld == 1);
        Assert.Equal(0, reserved.SaleAndOpenCount);

        // SKU-wide stock remains available in another lot, but the exact batch pinned for the
        // piece order cannot be selected by a competing pack consumer.
        await using (var scope = _shopifyFactory.Services.CreateAsyncScope())
        {
            var ledger = scope.ServiceProvider.GetRequiredService<Lensee.Modules.Inventory.Services.StockLedgerService>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => ledger.ReserveSelectedBatchInWarehouseAsync(
                _seed.LocationId, _seed.SkuId, 1, LotNumber, BatchExpiry, _testActorId, Guid.NewGuid(), CancellationToken.None));
        }

        using var firstCancel = await SendSignedWebhookAsync("orders/cancelled", "delivery-piece-cancel-one",
            JsonSerializer.Serialize(new { id = orderId, updated_at = DateTime.UtcNow.ToString("O") }));
        using var duplicateCancel = await SendSignedWebhookAsync("orders/cancelled", "delivery-piece-cancel-two",
            JsonSerializer.Serialize(new { id = orderId, updated_at = DateTime.UtcNow.AddMinutes(1).ToString("O") }));
        Assert.Equal(HttpStatusCode.Accepted, firstCancel.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, duplicateCancel.StatusCode);
        await ProcessDueEventsAsync();

        var afterCancel = await QueryOperationsAsync(async db => new
        {
            Operation = await db.OperationLogs.SingleAsync(value => value.Id == operationId),
            Link = await db.ShopifyOrderLinks.SingleAsync(value => value.OperationId == operationId)
        });
        var lineId = await QueryOperationsAsync(db => db.OperationLines.Where(value => value.OperationId == operationId)
            .Select(value => value.Id).SingleAsync());
        var released = await QueryServiceAsync<InventoryDbContext, PieceReleaseSnapshot>(async db =>
        {
            var balance = await db.StockBalances.SingleAsync(value => value.LocationId == _seed.LocationId && value.SkuId == _seed.SkuId);
            var reservation = await db.StockPieceReservations.SingleAsync(value => value.OperationLineId == lineId);
            var releaseCount = await db.StockTransactions.CountAsync(value => value.ReferenceOperationId == operationId &&
                value.TransactionType == Lensee.Modules.Inventory.Services.InventoryTransactionTypes.ReserveReleaseInWarehouse);
            return new PieceReleaseSnapshot(balance.AvailableQty, balance.ReservedInWarehouseQty, reservation.Status, releaseCount);
        });
        var cancellationDiagnostics = await QueryOperationsAsync(async db => string.Join(" | ", await db.ShopifyWebhookEvents
            .Where(value => value.EventId == "delivery-piece-cancel-one" || value.EventId == "delivery-piece-cancel-two")
            .OrderBy(value => value.EventId)
            .Select(value => value.EventId + ": " + value.Status + " / " + value.Detail + $" (attempt={value.AttemptCount})")
            .ToListAsync()));
        Assert.True(afterCancel.Operation.Status == "Cancelled", $"status={afterCancel.Operation.Status}; events={cancellationDiagnostics}");
        Assert.True(afterCancel.Link.IsCancellationTombstone);
        Assert.Equal(3, released.AvailableQty);
        Assert.Equal(0, released.ReservedInWarehouseQty);
        Assert.Equal("Released", released.Status);
        Assert.Equal(1, released.ReleaseCount);
    }

    [PostgreSqlIntegrationFact]
    public async Task ConcurrentPieceConfirmations_CannotOversellSharedLooseAndSealedCapacity()
    {
        await SetShopifyProductSellModeAsync("Both");
        await SeedPackStockAsync(quantity: 1);
        await SeedLoosePiecesAsync(1);
        var first = await ImportPieceDraftAsync("pending", "piece-race-one");
        var second = await ImportPieceDraftAsync("pending", "piece-race-two");

        using var firstConfirm = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/operations/{first.OperationId}/confirm");
        using var secondConfirm = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/operations/{second.OperationId}/confirm");
        AddAdminHeaders(firstConfirm);
        AddAdminHeaders(secondConfirm);
        var responses = await Task.WhenAll(_client.SendAsync(firstConfirm), _client.SendAsync(secondConfirm));
        var responseDiagnostics = string.Join(" | ", await Task.WhenAll(responses.Select(async value =>
            $"{(int)value.StatusCode}: {await value.Content.ReadAsStringAsync()}")));
        Assert.True(responses.Count(value => value.IsSuccessStatusCode) == 1, responseDiagnostics);
        Assert.True(responses.Count(value => !value.IsSuccessStatusCode) == 1, responseDiagnostics);
        foreach (var response in responses) response.Dispose();

        var outcomes = await QueryOperationsAsync(async db => new
        {
            Statuses = await db.OperationLogs.Where(value => value.Id == first.OperationId || value.Id == second.OperationId)
                .Select(value => value.Status).ToListAsync(),
            ActiveReservations = await QueryServiceAsync<InventoryDbContext, int>(inventory => inventory.StockPieceReservations
                .CountAsync(value => value.Status == "Active" && (value.OperationId == first.OperationId || value.OperationId == second.OperationId)))
        });
        Assert.Contains("Reserved", outcomes.Statuses);
        Assert.Contains("Draft", outcomes.Statuses);
        Assert.Equal(1, outcomes.ActiveReservations);
        var stock = await QueryServiceAsync<InventoryDbContext, PieceReleaseSnapshot>(db => db.StockBalances
            .Where(value => value.LocationId == _seed.LocationId && value.SkuId == _seed.SkuId)
            .Select(value => new PieceReleaseSnapshot(value.AvailableQty, value.ReservedInWarehouseQty, "", 0)).SingleAsync());
        Assert.Equal(0, stock.AvailableQty);
        Assert.Equal(1, stock.ReservedInWarehouseQty);
    }

    [PostgreSqlIntegrationFact]
    public async Task PieceDispatchConsumesPinnedReservation_AndPostDispatchCancellationRequiresReviewWithoutReversal()
    {
        await SetShopifyProductSellModeAsync("Both");
        await SeedPackStockAsync();
        await SeedLoosePiecesAsync(1);
        var (operationId, orderId) = await ImportAndReservePieceOrderAsync("pending", "piece-dispatch");

        using var paidNotice = await SendSignedWebhookAsync("orders/updated", "delivery-piece-paid-update",
            CreateOrderPayload(orderId, "paid", DateTime.UtcNow, quantity: 2, total: "20.00", updatedAtUtc: DateTime.UtcNow.AddMinutes(1)));
        Assert.Equal(HttpStatusCode.Accepted, paidNotice.StatusCode);
        await ProcessDueEventsAsync();
        using var dispatch = await PostAuthorizedAsync($"/api/v1/operations/{operationId}/dispatch");
        Assert.True(dispatch.IsSuccessStatusCode, await dispatch.Content.ReadAsStringAsync());

        var lineId = await QueryOperationsAsync(db => db.OperationLines.Where(value => value.OperationId == operationId)
            .Select(value => value.Id).SingleAsync());
        var afterDispatch = await QueryServiceAsync<InventoryDbContext, PieceDispatchSnapshot>(async db =>
        {
            var balance = await db.StockBalances.SingleAsync(value => value.LocationId == _seed.LocationId && value.SkuId == _seed.SkuId);
            var batch = await db.InventoryBatches.SingleAsync(value => value.LocationId == _seed.LocationId && value.SkuId == _seed.SkuId && value.LotNumber == LotNumber);
            var loose = await db.OpenedPieceLots.Where(value => value.LocationId == _seed.LocationId && value.SkuId == _seed.SkuId).SumAsync(value => value.LoosePieceQuantity);
            var sale = await db.StockTransactions.SingleAsync(value => value.ReferenceOperationId == operationId && value.TransactionType == Lensee.Modules.Inventory.Services.InventoryTransactionTypes.Sale);
            var packOpen = await db.StockTransactions.SingleAsync(value => value.ReferenceOperationId == operationId && value.TransactionType == Lensee.Modules.Inventory.Services.InventoryTransactionTypes.PackOpen);
            var reservation = await db.StockPieceReservations.SingleAsync(value => value.OperationLineId == lineId);
            return new PieceDispatchSnapshot(balance.AvailableQty, balance.ReservedInWarehouseQty, batch.Quantity, loose, sale, packOpen, reservation.Status);
        });
        Assert.Equal(19, afterDispatch.AvailableQty);
        Assert.Equal(0, afterDispatch.ReservedInWarehouseQty);
        Assert.Equal(19, afterDispatch.BatchQuantity);
        Assert.Equal(1, afterDispatch.LooseQuantity);
        Assert.Equal((-2, Lensee.Modules.Inventory.Services.InventoryQuantityUnits.Piece), (afterDispatch.Sale.QuantityChange, afterDispatch.Sale.QuantityUnit));
        Assert.Equal((-1, Lensee.Modules.Inventory.Services.InventoryQuantityUnits.Pack), (afterDispatch.PackOpen.QuantityChange, afterDispatch.PackOpen.QuantityUnit));
        Assert.Equal("Consumed", afterDispatch.Status);

        using var cancellation = await SendSignedWebhookAsync("orders/cancelled", "delivery-piece-after-dispatch-cancel",
            JsonSerializer.Serialize(new { id = orderId, updated_at = DateTime.UtcNow.AddMinutes(2).ToString("O") }));
        Assert.Equal(HttpStatusCode.Accepted, cancellation.StatusCode);
        await ProcessDueEventsAsync();

        var afterCancel = await QueryOperationsAsync(async db => new
        {
            Operation = await db.OperationLogs.SingleAsync(value => value.Id == operationId),
            Link = await db.ShopifyOrderLinks.SingleAsync(value => value.OperationId == operationId),
            CancellationEvent = await db.ShopifyWebhookEvents.SingleAsync(value => value.EventId == "delivery-piece-after-dispatch-cancel")
        });
        Assert.Equal("Dispatched", afterCancel.Operation.Status);
        Assert.False(afterCancel.Operation.ExternalPaymentVerified);
        Assert.Equal("PendingEvidence", afterCancel.Link.FinanceReconciliationStatus);
        Assert.True(afterCancel.Link.IsCancellationTombstone);
        Assert.Equal("RequiresAttention", afterCancel.CancellationEvent.Status);

        var afterCancellationStock = await QueryServiceAsync<InventoryDbContext, PiecePostCancelStockSnapshot>(async db =>
        {
            var balance = await db.StockBalances.SingleAsync(value => value.LocationId == _seed.LocationId && value.SkuId == _seed.SkuId);
            var saleCount = await db.StockTransactions.CountAsync(value => value.ReferenceOperationId == operationId && value.TransactionType == Lensee.Modules.Inventory.Services.InventoryTransactionTypes.Sale);
            var packOpenCount = await db.StockTransactions.CountAsync(value => value.ReferenceOperationId == operationId && value.TransactionType == Lensee.Modules.Inventory.Services.InventoryTransactionTypes.PackOpen);
            var releaseCount = await db.StockTransactions.CountAsync(value => value.ReferenceOperationId == operationId && value.TransactionType == Lensee.Modules.Inventory.Services.InventoryTransactionTypes.ReserveReleaseInWarehouse);
            return new PiecePostCancelStockSnapshot(balance, saleCount, packOpenCount, releaseCount);
        });
        Assert.Equal((afterDispatch.AvailableQty, afterDispatch.ReservedInWarehouseQty), (afterCancellationStock.Balance.AvailableQty, afterCancellationStock.Balance.ReservedInWarehouseQty));
        Assert.Equal(1, afterCancellationStock.SaleCount);
        Assert.Equal(1, afterCancellationStock.PackOpenCount);
        Assert.Equal(0, afterCancellationStock.ReleaseCount);
    }

    [PostgreSqlIntegrationFact]
    public async Task PreActivationOrderIsNotBackfilled_AndPrecreateCancellationTombstonePreventsResurrection()
    {
        var preActivationId = NewProviderId();
        using var oldReceipt = await SendSignedWebhookAsync("orders/create", "delivery-pre-activation",
            CreateOrderPayload(preActivationId, "paid", _activationAtUtc.AddMinutes(-1)));
        Assert.Equal(HttpStatusCode.Accepted, oldReceipt.StatusCode);
        await ProcessDueEventsAsync();

        var cancellationId = NewProviderId();
        using var cancellationReceipt = await SendSignedWebhookAsync("orders/cancelled", "delivery-cancel-before-create",
            JsonSerializer.Serialize(new { id = cancellationId, updated_at = DateTime.UtcNow.ToString("O") }));
        Assert.Equal(HttpStatusCode.Accepted, cancellationReceipt.StatusCode);
        await ProcessDueEventsAsync();

        using var lateCreate = await SendSignedWebhookAsync("orders/create", "delivery-after-cancel",
            CreateOrderPayload(cancellationId, "paid", DateTime.UtcNow));
        Assert.Equal(HttpStatusCode.Accepted, lateCreate.StatusCode);
        await ProcessDueEventsAsync();

        var result = await QueryOperationsAsync(async db => new
        {
            OldLink = await db.ShopifyOrderLinks.AnyAsync(value => value.ShopifyOrderId == preActivationId),
            OldInbox = await db.ShopifyWebhookEvents.SingleAsync(value => value.EventId == "delivery-pre-activation"),
            Tombstone = await db.ShopifyOrderCancellationTombstones.SingleOrDefaultAsync(value => value.ShopifyOrderId == cancellationId),
            ResurrectedLink = await db.ShopifyOrderLinks.AnyAsync(value => value.ShopifyOrderId == cancellationId),
            ResurrectedInbox = await db.ShopifyWebhookEvents.SingleAsync(value => value.EventId == "delivery-after-cancel")
        });
        Assert.False(result.OldLink);
        Assert.Equal("Ignored", result.OldInbox.Status);
        Assert.Contains("activation cutoff", result.OldInbox.Detail ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Tombstone);
        Assert.False(result.ResurrectedLink);
        Assert.Equal("Cancelled", result.ResurrectedInbox.Status);
    }

    [PostgreSqlIntegrationFact]
    public async Task RefundNoticesAreLogicallyDeduplicated_AndUnresolvedPayloadsOutliveRetention()
    {
        var refundId = NewProviderId();
        var orderId = NewProviderId();
        var refundPayload = JsonSerializer.Serialize(new
        {
            id = refundId,
            order_id = orderId,
            created_at = DateTime.UtcNow.ToString("O"),
            amount = "3.25",
            currency = "EGP",
            transactions = Array.Empty<object>()
        });

        using var first = await SendSignedWebhookAsync("refunds/create", "delivery-refund-one", refundPayload);
        using var second = await SendSignedWebhookAsync("refunds/create", "delivery-refund-two", refundPayload);
        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);

        var eventIds = await ClaimDueEventsAsync();
        Assert.Equal(2, eventIds.Count);
        await Task.WhenAll(eventIds.Select(ProcessEventInNewScopeAsync));

        var reviewCount = await QueryOperationsAsync(db => db.ShopifyRefundReviews
            .CountAsync(value => value.ShopDomain == ShopDomain && value.ShopifyRefundId == refundId));
        Assert.Equal(1, reviewCount);

        using var normalReceipt = await SendSignedWebhookAsync("orders/create", "delivery-retained-terminal-event",
            CreateOrderPayload(NewProviderId(), "pending", DateTime.UtcNow));
        Assert.Equal(HttpStatusCode.Accepted, normalReceipt.StatusCode);
        await ProcessDueEventsAsync();

        await ExecuteOperationsAsync(async db =>
        {
            var rows = await db.ShopifyWebhookEvents.Where(value => value.EventId == "delivery-refund-one" || value.EventId == "delivery-refund-two" || value.EventId == "delivery-retained-terminal-event").ToListAsync();
            foreach (var row in rows)
                row.ReceivedAt = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(-3), DateTimeKind.Unspecified);
            await db.SaveChangesAsync();
        });
        await PurgeExpiredPayloadsAsync();

        var payloadStates = await QueryOperationsAsync(db => db.ShopifyWebhookEvents
            .Where(value => value.EventId == "delivery-refund-one" || value.EventId == "delivery-refund-two" || value.EventId == "delivery-retained-terminal-event")
            .Select(value => new { value.EventId, value.Status, value.ResolvedAt, HasPayload = value.ProtectedPayload != null })
            .ToListAsync());
        Assert.Equal(3, payloadStates.Count);
        var refundStates = payloadStates.Where(value => value.EventId != "delivery-retained-terminal-event").ToArray();
        Assert.Single(refundStates.Where(value => value.Status == "RequiresAttention" && value.HasPayload));
        Assert.Single(refundStates.Where(value => value.Status == "Duplicate" && !value.HasPayload));
        Assert.False(payloadStates.Single(value => value.EventId == "delivery-retained-terminal-event").HasPayload);
    }

    private async Task<ShopifyFixtureSeed> SeedShopifyFixtureAsync()
    {
        await using var scope = _shopifyFactory.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var categoryId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var skuCode = $"QA-SHOPIFY-{skuId:N}";

        catalog.Categories.Add(new Category { Id = categoryId, Name = $"Shopify QA {categoryId:N}" });
        catalog.Brands.Add(new Brand { Id = brandId, Name = $"Shopify QA {brandId:N}" });
        catalog.Products.Add(new Product
        {
            Id = productId,
            CategoryId = categoryId,
            BrandId = brandId,
            Name = $"Shopify QA product {productId:N}",
            ProductType = "Lens",
            ExpiryType = "Batch",
            SealedExpiryDuration = "24 months",
            OpenedExpiryRate = "Monthly",
            OpenedExpiryDuration = "30 days",
            PiecesPerPack = 2,
            SellMode = "SealedPackOnly",
            ClinicalParams = "{}",
            ExtendedAttributes = "{}",
            IsActive = true,
            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
        });
        catalog.Skus.Add(new Sku { Id = skuId, ProductId = productId, SkuCode = skuCode, ColorName = "Clear", IsActive = true, IsSellable = true });
        inventory.Locations.Add(new Location
        {
            Id = locationId,
            Name = $"Shopify QA Subwarehouse {locationId:N}",
            LocationType = "SubWarehouse",
            IsActive = true
        });
        await catalog.SaveChangesAsync();
        await inventory.SaveChangesAsync();
        return new ShopifyFixtureSeed(productId, skuId, skuCode, locationId);
    }

    private async Task SeedOnlineLocationAsync()
    {
        await using var scope = _migrationFactory.Services.CreateAsyncScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        inventory.Locations.Add(new Location
        {
            Id = _onlineLocationId,
            Name = $"Shopify QA Online {_onlineLocationId:N}",
            LocationType = "Online",
            IsActive = true
        });
        await inventory.SaveChangesAsync();
    }

    private async Task SeedPackStockAsync(int quantity = 20, string lotNumber = LotNumber)
    {
        await using var scope = _shopifyFactory.Services.CreateAsyncScope();
        var ledger = scope.ServiceProvider.GetRequiredService<Lensee.Modules.Inventory.Services.StockLedgerService>();
        await ledger.ReceiveAsync(_seed.LocationId, _seed.SkuId, quantity, Guid.NewGuid(), lotNumber, BatchExpiry,
            "Isolated Shopify PostgreSQL dispatch fixture", cancellationToken: CancellationToken.None);
    }

    private async Task SeedLoosePiecesAsync(int quantity)
    {
        await using var scope = _shopifyFactory.Services.CreateAsyncScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var batch = await inventory.InventoryBatches.SingleAsync(value =>
            value.LocationId == _seed.LocationId && value.SkuId == _seed.SkuId && value.LotNumber == LotNumber);
        inventory.OpenedPieceLots.Add(new OpenedPieceLot
        {
            Id = Guid.NewGuid(),
            LocationId = _seed.LocationId,
            SkuId = _seed.SkuId,
            SourceBatchId = batch.Id,
            LotNumber = batch.LotNumber,
            BatchExpiryDate = batch.ExpiryDate,
            OpenedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            PieceExpiryDate = batch.ExpiryDate,
            LoosePieceQuantity = quantity,
            CreatedBy = _testActorId,
            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
        });
        await inventory.SaveChangesAsync();
    }

    private async Task SetShopifyProductSellModeAsync(string sellMode)
    {
        await using var scope = _shopifyFactory.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var product = await catalog.Products.SingleAsync(value => value.Id == _seed.ProductId);
        product.SellMode = sellMode;
        await catalog.SaveChangesAsync();
    }

    private async Task<Guid> ImportAndReserveAsync(string financialStatus, string suffix)
    {
        var orderId = NewProviderId();
        using var receipt = await SendSignedWebhookAsync("orders/create", $"delivery-{suffix}",
            CreateOrderPayload(orderId, financialStatus, DateTime.UtcNow, quantity: 1));
        Assert.Equal(HttpStatusCode.Accepted, receipt.StatusCode);
        await ProcessDueEventsAsync();

        var operation = await QueryOperationsAsync(async db =>
        {
            var link = await db.ShopifyOrderLinks.SingleAsync(value => value.ShopifyOrderId == orderId);
            var value = await db.OperationLogs.Include(item => item.OperationLines).SingleAsync(item => item.Id == link.OperationId);
            return new { value.Id, value.ConcurrencyVersion, LineId = value.OperationLines.Single().Id };
        });

        using var allocation = await PutAuthorizedAsync($"/api/v1/operations/{operation.Id}/shopify-allocation", new
        {
            lines = new[] { new { operationLineId = operation.LineId, lotNumber = LotNumber, expiryDate = BatchExpiry } },
            expectedVersion = operation.ConcurrencyVersion,
            sourceLocationId = _seed.LocationId
        });
        Assert.True(allocation.IsSuccessStatusCode, await allocation.Content.ReadAsStringAsync());

        using var confirm = await PostAuthorizedAsync($"/api/v1/operations/{operation.Id}/confirm");
        Assert.True(confirm.IsSuccessStatusCode, await confirm.Content.ReadAsStringAsync());
        var status = await QueryOperationsAsync(db => db.OperationLogs.Where(value => value.Id == operation.Id)
            .Select(value => value.Status).SingleAsync());
        Assert.Equal("Reserved", status);
        return operation.Id;
    }

    private async Task<(Guid OperationId, string OrderId)> ImportAndReservePieceOrderAsync(string financialStatus, string suffix)
    {
        var operation = await ImportPieceDraftAsync(financialStatus, suffix);
        using var confirm = await PostAuthorizedAsync($"/api/v1/operations/{operation.OperationId}/confirm");
        Assert.True(confirm.IsSuccessStatusCode, await confirm.Content.ReadAsStringAsync());
        return operation;
    }

    private async Task<(Guid OperationId, string OrderId)> ImportPieceDraftAsync(string financialStatus, string suffix)
    {
        var orderId = NewProviderId();
        using var receipt = await SendSignedWebhookAsync("orders/create", $"delivery-{suffix}",
            CreateOrderPayload(orderId, financialStatus, DateTime.UtcNow, quantity: 2, total: "20.00"));
        Assert.Equal(HttpStatusCode.Accepted, receipt.StatusCode);
        await ProcessDueEventsAsync();

        var operation = await QueryOperationsAsync(async db =>
        {
            var link = await db.ShopifyOrderLinks.SingleAsync(value => value.ShopifyOrderId == orderId);
            var value = await db.OperationLogs.Include(item => item.OperationLines).SingleAsync(item => item.Id == link.OperationId);
            return new { value.Id, value.ConcurrencyVersion, LineId = value.OperationLines.Single().Id, EntryMode = value.OperationLines.Single().EntryMode };
        });
        Assert.Equal("Pieces", operation.EntryMode);

        using var allocation = await PutAuthorizedAsync($"/api/v1/operations/{operation.Id}/shopify-allocation", new
        {
            lines = new[] { new { operationLineId = operation.LineId, lotNumber = LotNumber, expiryDate = BatchExpiry } },
            expectedVersion = operation.ConcurrencyVersion,
            sourceLocationId = _seed.LocationId
        });
        Assert.True(allocation.IsSuccessStatusCode, await allocation.Content.ReadAsStringAsync());
        return (operation.Id, orderId);
    }

    private string CreateOrderPayload(string orderId, string financialStatus, DateTime createdAtUtc, int quantity = 1,
        string unitPrice = "10.00", string discount = "0.00", string shipping = "0.00", string tax = "0.00", string total = "10.00", string currency = "EGP", DateTime? updatedAtUtc = null, string? presentmentCurrency = null, string paymentCashRounding = "0.00") =>
        JsonSerializer.Serialize(new
        {
            id = orderId,
            name = $"#QA-{orderId}",
            created_at = createdAtUtc.ToUniversalTime().ToString("O"),
            updated_at = (updatedAtUtc ?? DateTime.UtcNow).ToUniversalTime().ToString("O"),
            financial_status = financialStatus,
            currency,
            presentment_currency = presentmentCurrency ?? currency,
            subtotal_price = (decimal.Parse(unitPrice, System.Globalization.CultureInfo.InvariantCulture) * quantity - decimal.Parse(discount, System.Globalization.CultureInfo.InvariantCulture)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            subtotal_price_set = TestMoneyBag((decimal.Parse(unitPrice, System.Globalization.CultureInfo.InvariantCulture) * quantity - decimal.Parse(discount, System.Globalization.CultureInfo.InvariantCulture)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), currency, presentmentCurrency ?? currency),
            total_discounts = discount,
            total_discounts_set = TestMoneyBag(discount, currency, presentmentCurrency ?? currency),
            total_shipping_price = shipping,
            total_shipping_price_set = TestMoneyBag(shipping, currency, presentmentCurrency ?? currency),
            total_tax = tax,
            total_tax_set = TestMoneyBag(tax, currency, presentmentCurrency ?? currency),
            total_price = total,
            total_price_set = TestMoneyBag(total, currency, presentmentCurrency ?? currency),
            total_cash_rounding_payment_adjustment_set = TestMoneyBag(paymentCashRounding, currency, presentmentCurrency ?? currency),
            total_cash_rounding_refund_adjustment_set = TestMoneyBag("0.00", currency, presentmentCurrency ?? currency),
            shipping_lines = new[] { new { price = shipping, price_set = TestMoneyBag(shipping, currency, presentmentCurrency ?? currency) } },
            line_items = new[]
            {
                new
                {
                    id = $"line-{orderId}",
                    variant_id = _seed.SkuId.ToString("N"),
                    sku = _seed.SkuCode,
                    title = "PostgreSQL Shopify fixture",
                    variant_title = "Sealed pack",
                    quantity,
                    price = unitPrice,
                    price_set = TestMoneyBag(unitPrice, currency, presentmentCurrency ?? currency),
                    total_discount = discount,
                    discount_allocations = decimal.Parse(discount, System.Globalization.CultureInfo.InvariantCulture) == 0m
                        ? Array.Empty<object>()
                        : new object[] { new { amount = discount, amount_set = TestMoneyBag(discount, currency, presentmentCurrency ?? currency) } },
                    tax_lines = decimal.Parse(tax, System.Globalization.CultureInfo.InvariantCulture) == 0m
                        ? Array.Empty<object>()
                        : new object[] { new { title = "QA tax", price = tax, price_set = TestMoneyBag(tax, currency, presentmentCurrency ?? currency) } }
                }
            }
        });

    private static object TestMoneyBag(string amount, string shopCurrency, string presentmentCurrency) => new
    {
        shop_money = new { amount, currency_code = shopCurrency },
        presentment_money = new { amount, currency_code = presentmentCurrency }
    };

    private async Task<HttpResponseMessage> SendSignedWebhookAsync(string topic, string deliveryId, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var signature = Convert.ToBase64String(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), bytes));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/integrations/shopify/webhooks")
        {
            Content = new ByteArrayContent(bytes)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Shopify-Hmac-Sha256", signature);
        request.Headers.Add("X-Shopify-Webhook-Id", deliveryId);
        request.Headers.Add("X-Shopify-Event-Id", deliveryId);
        request.Headers.Add("X-Shopify-Topic", topic);
        request.Headers.Add("X-Shopify-Shop-Domain", ShopDomain);
        request.Headers.Add("X-Shopify-API-Version", "2026-10");
        request.Headers.Add("X-Shopify-Triggered-At", DateTime.UtcNow.ToString("O"));
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendLegacyWebhookAsync(string topic, string deliveryId, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/integrations/shopify/legacy-webhooks/{LegacyPathSecret}")
        {
            Content = new ByteArrayContent(bytes)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Shopify-Webhook-Id", deliveryId);
        request.Headers.Add("X-Shopify-Event-Id", deliveryId);
        request.Headers.Add("X-Shopify-Topic", topic);
        request.Headers.Add("X-Shopify-Shop-Domain", ShopDomain);
        request.Headers.Add("X-Shopify-API-Version", "2026-10");
        request.Headers.Add("X-Shopify-Triggered-At", DateTime.UtcNow.ToString("O"));
        return await _client.SendAsync(request);
    }

    private async Task ProcessDueEventsAsync()
    {
        var ids = await ClaimDueEventsAsync();
        foreach (var id in ids) await ProcessEventAsync(id);
    }

    private async Task<IReadOnlyList<Guid>> ClaimDueEventsAsync()
    {
        await using var scope = _shopifyFactory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ShopifyIntegrationService>()
            .ClaimDueEventsAsync(CancellationToken.None);
    }

    private async Task WaitForAdvisoryLockWaitAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var waiting = await QueryOperationsAsync(async db =>
            {
                await db.Database.OpenConnectionAsync();
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "select count(*)::int from pg_stat_activity where wait_event_type = 'Lock' and query ilike '%pg_advisory_xact_lock%'";
                return Convert.ToInt32(await command.ExecuteScalarAsync());
            });
            if (waiting > 0) return;
            await Task.Delay(50);
        }
        Assert.True(false, "The stale Shopify worker did not reach the held per-order advisory lock in time.");
    }

    private async Task ProcessEventAsync(Guid eventId) => await ProcessEventInNewScopeAsync(eventId);

    private async Task ProcessEventInNewScopeAsync(Guid eventId)
    {
        await using var scope = _shopifyFactory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ShopifyIntegrationService>()
            .ProcessQueuedEventAsync(eventId, CancellationToken.None);
    }

    private async Task PurgeExpiredPayloadsAsync()
    {
        await using var scope = _shopifyFactory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ShopifyIntegrationService>()
            .PurgeExpiredPayloadsAsync(CancellationToken.None);
    }

    private async Task<HttpResponseMessage> PostAuthorizedAsync(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        AddAdminHeaders(request);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PutAuthorizedAsync(string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        AddAdminHeaders(request);
        return await _client.SendAsync(request);
    }

    private void AddAdminHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("X-Test-Role", LenseeRoles.Admin);
        request.Headers.Add("X-Test-UserId", _testActorId.ToString());
        request.Headers.Add("X-Test-Permissions", LenseePermissions.OperationsWrite);
    }

    private async Task<T> QueryOperationsAsync<T>(Func<OperationsDbContext, Task<T>> query)
    {
        await using var scope = _shopifyFactory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<OperationsDbContext>());
    }

    private async Task<T> QueryServiceAsync<TContext, T>(Func<TContext, Task<T>> query) where TContext : DbContext
    {
        await using var scope = _shopifyFactory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<TContext>());
    }

    private async Task ExecuteOperationsAsync(Func<OperationsDbContext, Task> action)
    {
        await using var scope = _shopifyFactory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<OperationsDbContext>());
    }

    private static string NewProviderId() => $"qa-{Guid.NewGuid():N}";

    private sealed record ShopifyFixtureSeed(Guid ProductId, Guid SkuId, string SkuCode, Guid LocationId);
    private sealed record PieceReservationSnapshot(string OperationStatus, string LineMode, int LineQuantity,
        int BalanceAvailable, int BalanceReserved, int BatchQuantity, int LooseQuantity, string ReservationStatus,
        int RequestedPieceQuantity, IReadOnlyList<StockPieceReservationAllocation> Allocations, int SaleAndOpenCount);
    private sealed record PieceReleaseSnapshot(int AvailableQty, int ReservedInWarehouseQty, string Status, int ReleaseCount);
    private sealed record PieceDispatchSnapshot(int AvailableQty, int ReservedInWarehouseQty, int BatchQuantity,
        int LooseQuantity, StockTransaction Sale, StockTransaction PackOpen, string Status);
    private sealed record PiecePostCancelStockSnapshot(StockBalance Balance, int SaleCount, int PackOpenCount, int ReleaseCount);
}
