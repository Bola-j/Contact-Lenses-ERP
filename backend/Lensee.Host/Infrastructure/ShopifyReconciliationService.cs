using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lensee.Host.Infrastructure;

public sealed record ShopifyReconciliationStatus(bool Enabled, bool Available, string Status,
    DateTime? LastRunAt, DateTime? LastSuccessAt, DateTime? NextRunAt, string? LastError, string? Checkpoint,
    DateTime? ActivatedAtUtc);

public sealed class ShopifyOrderRefreshException(int statusCode, int? retryAfterSeconds = null)
    : Exception("Shopify order refresh could not finish. Check app access and connectivity; the original receipt is retained.")
{
    public int StatusCode { get; } = statusCode;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>Read-only Shopify recovery. Checkpoints advance only after complete source pages have
/// entered the durable inbox. Source financial status is never independent payment verification.</summary>
public sealed class ShopifyReconciliationService(OperationsDbContext db, IOptions<ShopifyOptions> options,
    IHttpClientFactory http, IServiceScopeFactory scopes, IClock clock)
{
    private readonly ShopifyOptions _options = options.Value;
    private string Domain => (_options.StoreDomain ?? "").Trim().ToLowerInvariant();
    private DateTime Now => DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Unspecified);
    private bool Available => _options.Enabled && _options.ReconciliationEnabled && !string.IsNullOrWhiteSpace(_options.AdminAccessToken) &&
        Regex.IsMatch(Domain, @"^[a-z0-9][a-z0-9-]*\.myshopify\.com$", RegexOptions.CultureInvariant) &&
        Regex.IsMatch(_options.AdminApiVersion, @"^20\d{2}-(01|04|07|10)$", RegexOptions.CultureInvariant);
    private static DateTime? Utc(DateTime? date) => date.HasValue ? DateTime.SpecifyKind(date.Value, DateTimeKind.Utc) : null;

    public async Task<ShopifyReconciliationStatus> GetStatusAsync(CancellationToken ct)
    {
        var row = await db.ShopifyReconciliationCheckpoints.AsNoTracking().SingleOrDefaultAsync(x => x.ShopDomain == Domain, ct);
        return new(_options.ReconciliationEnabled, Available,
            !_options.ReconciliationEnabled ? "Disabled" : !Available ? "ConfigurationRequired" : row?.LeaseUntil > Now ? "Processing" :
                row?.LastError is not null ? "RetryScheduled" : "Ready",
            Utc(row?.RequestedAt), Utc(row?.LastSuccessAt), Utc(row?.NextRunAt), row?.LastError,
            row?.Cursor is null ? null : "Page checkpoint saved", Utc(row?.ActivatedAtUtc));
    }

    public async Task<ShopifyReconciliationStatus> RequestRunAsync(CancellationToken ct)
    {
        if (!Available) return await GetStatusAsync(ct);
        var row = await ShopifyActivation.EnsureAsync(db, clock, _options, ct);
        if (row.LeaseUntil > Now) return await GetStatusAsync(ct);
        row.RequestedAt = row.NextRunAt = Now;
        row.Revision++;
        await db.SaveChangesAsync(ct);
        return await GetStatusAsync(ct);
    }

    /// <summary>Fetch current commercial facts into new durable receipts; never rewrite a captured delivery.</summary>
    public async Task<bool> RefreshOrderAsync(string shopDomain, string orderId, CancellationToken ct)
    {
        if (!Available || !string.Equals(shopDomain.Trim(), Domain, StringComparison.OrdinalIgnoreCase))
            throw new ShopifyOrderRefreshException(StatusCodes.Status409Conflict);
        if (!Regex.IsMatch(orderId, @"^\d{1,30}$", RegexOptions.CultureInvariant)) return false;
        try
        {
            var checkpoint = await ShopifyActivation.EnsureAsync(db, clock, _options, ct);
            var data = await QueryAsync(ShopifyRecoveryQueries.Orders, new { query = $"id:{orderId}", after = (string?)null }, ct);
            foreach (var order in data.GetProperty("orders").GetProperty("nodes").EnumerateArray())
            {
                if (Read(order, "legacyResourceId") != orderId) continue;
                var linked = await db.ShopifyOrderLinks.AsNoTracking().AnyAsync(x => x.ShopifyOrderId == orderId, ct);
                var created = DateTimeOffset.Parse(Read(order, "createdAt")!, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime;
                if (!linked && created < DateTime.SpecifyKind(checkpoint.ActivatedAtUtc, DateTimeKind.Utc)) return false;
                await EnqueueOrderAsync(order, ct);
                return true;
            }
            return false;
        }
        catch (ShopifyThrottleException throttle)
        { throw new ShopifyOrderRefreshException(StatusCodes.Status503ServiceUnavailable, (int)Math.Ceiling(throttle.Delay.TotalSeconds)); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception)
        { throw new ShopifyOrderRefreshException(StatusCodes.Status503ServiceUnavailable); }
    }

    public async Task RunDueAsync(CancellationToken ct)
    {
        if (!Available) return;
        var row = await ShopifyActivation.EnsureAsync(db, clock, _options, ct);
        if (row.NextRunAt > Now || row.LeaseUntil > Now) return;
        if (db.Database.IsRelational())
        {
            var now = Now;
            var lease = now.AddMinutes(2);
            var revision = row.Revision;
            var claimed = await db.ShopifyReconciliationCheckpoints.Where(x => x.Id == row.Id && x.Revision == revision &&
                    x.NextRunAt <= now && (x.LeaseUntil == null || x.LeaseUntil <= now))
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LeaseUntil, lease).SetProperty(x => x.Revision, revision + 1), ct);
            if (claimed != 1) return;
            await db.Entry(row).ReloadAsync(ct);
        }
        else { row.Revision++; row.LeaseUntil = Now.AddMinutes(2); }
        row.RequestedAt = Now;
        row.WindowStartedUtc ??= (row.UpdatedThroughUtc ?? row.ActivatedAtUtc).AddMinutes(-5);
        row.WindowEndedUtc ??= Now;
        await db.SaveChangesAsync(ct);
        try
        {
            for (var page = 0; page < 20; page++)
            {
                // Include old linked orders in the update scan; admission enforces activation for new links.
                var query = $"updated_at:>='{row.WindowStartedUtc:yyyy-MM-ddTHH:mm:ssZ}' updated_at:<='{row.WindowEndedUtc:yyyy-MM-ddTHH:mm:ssZ}'";
                var data = await QueryAsync(ShopifyRecoveryQueries.Orders, new { query, after = row.Cursor }, ct);
                var connection = data.GetProperty("orders");
                foreach (var order in connection.GetProperty("nodes").EnumerateArray())
                {
                    var id = Read(order, "legacyResourceId") ?? throw new InvalidDataException();
                    var linked = await db.ShopifyOrderLinks.AsNoTracking().AnyAsync(x => x.ShopifyOrderId == id, ct);
                    var created = DateTimeOffset.Parse(Read(order, "createdAt")!, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime;
                    if (!linked && created < DateTime.SpecifyKind(row.ActivatedAtUtc, DateTimeKind.Utc)) continue;
                    await EnqueueOrderAsync(order, ct);
                }
                var info = connection.GetProperty("pageInfo");
                var more = info.GetProperty("hasNextPage").GetBoolean();
                var cursor = more ? Read(info, "endCursor") : null;
                if (more && (string.IsNullOrWhiteSpace(cursor) || cursor == row.Cursor)) throw new InvalidDataException();
                row.Cursor = cursor;
                row.Revision++;
                row.LeaseUntil = Now.AddMinutes(2);
                row.LastError = null;
                if (!more)
                {
                    row.UpdatedThroughUtc = row.WindowEndedUtc;
                    row.WindowStartedUtc = row.WindowEndedUtc = null;
                    row.LastSuccessAt = Now;
                    row.NextRunAt = Now.AddMinutes(Math.Max(1, _options.ReconciliationIntervalMinutes));
                    row.LeaseUntil = null;
                }
                await db.SaveChangesAsync(ct);
                if (!more) return;
            }
            row.LeaseUntil = null;
            row.NextRunAt = Now.AddSeconds(5);
            row.Revision++;
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            // Provider response bodies/headers can contain credentials and personal data; never expose them.
            row.LastError = error is ShopifyThrottleException ? "Shopify throttled recovery; retry scheduled." :
                "Shopify recovery could not finish. Check app access, permissions and connectivity; progress is retained.";
            row.NextRunAt = Now.Add(error is ShopifyThrottleException throttle ? throttle.Delay : TimeSpan.FromMinutes(1));
            row.LeaseUntil = null;
            row.Revision++;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); }
        }
    }

    private async Task EnqueueOrderAsync(JsonElement order, CancellationToken ct)
    {
        var id = Read(order, "legacyResourceId") ?? throw new InvalidDataException();
        var snapshot = await ToWebhookAsync(order, ct);
        await EnqueueAsync("orders/create", id, snapshot, ct);
        await EnqueueAsync("orders/updated", id, snapshot, ct);
        if (snapshot["cancelled_at"] is not null) await EnqueueAsync("orders/cancelled", id, snapshot, ct);
        foreach (var refund in order.GetProperty("refunds").EnumerateArray())
        {
            var refundId = Read(refund, "legacyResourceId") ?? throw new InvalidDataException();
            await EnqueueAsync("refunds/create", refundId, new JsonObject
            {
                ["id"] = refundId,
                ["order_id"] = id,
                ["created_at"] = Read(refund, "createdAt"),
                ["total_refunded_set"] = Copy(refund, "totalRefundedSet")
            }, ct);
        }
    }

    private async Task EnqueueAsync(string topic, string identity, JsonObject snapshot, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(snapshot.ToJsonString());
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var envelope = new ShopifyWebhookEnvelope($"recovery:{identity}:{digest[..32]}", topic, Domain, null,
            _options.AdminApiVersion, snapshot["updated_at"]?.ToString(), "AdminApi");
        // Separate DbContext prevents a receipt race's tracker recovery from detaching checkpoint progress.
        await using var scope = scopes.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ShopifyIntegrationService>().ReceiveReconciledAsync(envelope, bytes, ct);
        if (result.StatusCode >= 300) throw new InvalidDataException("Recovery receipt was not durably accepted.");
    }

    private async Task<JsonElement> QueryAsync(string query, object variables, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://{Domain}/admin/api/{_options.AdminApiVersion}/graphql.json");
        request.Headers.Add("X-Shopify-Access-Token", _options.AdminAccessToken);
        request.Content = JsonContent.Create(new { query, variables });
        using var response = await http.CreateClient("shopify-reconciliation").SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var delay = response.Headers.RetryAfter?.Delta ??
                (response.Headers.RetryAfter?.Date - new DateTimeOffset(DateTime.SpecifyKind(Now, DateTimeKind.Utc))) ?? TimeSpan.FromMinutes(1);
            throw new ShopifyThrottleException(TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 3600)));
        }
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = json.RootElement;
        if (root.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0)
        {
            if (errors.EnumerateArray().Any(x => x.TryGetProperty("extensions", out var ext) && Read(ext, "code") == "THROTTLED"))
                throw new ShopifyThrottleException(TimeSpan.FromMinutes(1));
            throw new InvalidDataException("Shopify query failed.");
        }
        return root.GetProperty("data").Clone();
    }

    private async Task<JsonObject> ToWebhookAsync(JsonElement order, CancellationToken ct)
    {
        var lines = new JsonArray();
        var connection = order.GetProperty("lineItems");
        string? previousCursor = null;
        while (true)
        {
            foreach (var line in connection.GetProperty("nodes").EnumerateArray())
            {
                var variant = line.GetProperty("variant");
                var item = new JsonObject
                {
                    ["id"] = Read(line, "id")!.Split('/')[^1],
                    ["variant_id"] = variant.ValueKind == JsonValueKind.Null ? null : Read(variant, "id")?.Split('/')[^1],
                    ["sku"] = Read(line, "sku"),
                    ["title"] = Read(line, "title"),
                    ["variant_title"] = Read(line, "variantTitle"),
                    ["quantity"] = line.GetProperty("quantity").GetInt32(),
                    ["current_quantity"] = line.GetProperty("currentQuantity").GetInt32(),
                    ["price"] = MoneyAmount(line.GetProperty("originalUnitPriceSet")),
                    ["price_set"] = Copy(line, "originalUnitPriceSet"),
                    ["original_total_set"] = Copy(line, "originalTotalSet"),
                    ["discounted_total_set"] = Copy(line, "discountedTotalSet"),
                    ["discount_allocations"] = Discounts(line.GetProperty("discountAllocations")),
                    ["tax_lines"] = Taxes(line.GetProperty("taxLines")),
                    ["properties"] = new JsonArray(line.GetProperty("customAttributes").EnumerateArray()
                        .Select(x => (JsonNode)new JsonObject { ["name"] = Read(x, "key"), ["value"] = Read(x, "value") }).ToArray())
                };
                lines.Add(item);
            }
            var info = connection.GetProperty("pageInfo");
            if (!info.GetProperty("hasNextPage").GetBoolean()) break;
            var cursor = Read(info, "endCursor");
            if (string.IsNullOrWhiteSpace(cursor) || cursor == previousCursor) throw new InvalidDataException();
            previousCursor = cursor;
            var next = await QueryAsync(ShopifyRecoveryQueries.Lines, new { id = Read(order, "id"), after = cursor }, ct);
            connection = next.GetProperty("order").GetProperty("lineItems");
        }
        var shipping = new JsonArray();
        connection = order.GetProperty("shippingLines");
        previousCursor = null;
        while (true)
        {
            foreach (var line in connection.GetProperty("nodes").EnumerateArray()) shipping.Add(new JsonObject
            {
                ["id"] = Read(line, "id")?.Split('/')[^1],
                ["title"] = Read(line, "title"),
                ["code"] = Read(line, "code"),
                ["price"] = MoneyAmount(line.GetProperty("originalPriceSet")),
                ["price_set"] = Copy(line, "originalPriceSet"),
                ["discounted_price"] = MoneyAmount(line.GetProperty("discountedPriceSet")),
                ["discounted_price_set"] = Copy(line, "discountedPriceSet"),
                ["discount_allocations"] = Discounts(line.GetProperty("discountAllocations")),
                ["tax_lines"] = Taxes(line.GetProperty("taxLines"))
            });
            var info = connection.GetProperty("pageInfo");
            if (!info.GetProperty("hasNextPage").GetBoolean()) break;
            var cursor = Read(info, "endCursor");
            if (string.IsNullOrWhiteSpace(cursor) || cursor == previousCursor) throw new InvalidDataException();
            previousCursor = cursor;
            var next = await QueryAsync(ShopifyRecoveryQueries.Shipping, new { id = Read(order, "id"), after = cursor }, ct);
            connection = next.GetProperty("order").GetProperty("shippingLines");
        }
        var result = new JsonObject
        {
            ["id"] = Read(order, "legacyResourceId"),
            ["name"] = Read(order, "name"),
            ["created_at"] = Read(order, "createdAt"),
            ["email"] = Read(order, "email"),
            ["phone"] = Read(order, "phone"),
            ["updated_at"] = Read(order, "updatedAt"),
            ["cancelled_at"] = Read(order, "cancelledAt"),
            ["financial_status"] = Read(order, "displayFinancialStatus")?.ToLowerInvariant(),
            ["currency"] = Read(order, "currencyCode"),
            ["presentment_currency"] = Read(order, "presentmentCurrencyCode"),
            ["test"] = order.GetProperty("test").GetBoolean(),
            ["edited"] = order.GetProperty("edited").GetBoolean(),
            ["taxes_included"] = order.GetProperty("taxesIncluded").GetBoolean(),
            ["payment_gateway_names"] = Copy(order, "paymentGatewayNames"),
            ["billing_address"] = Address(order.GetProperty("billingAddress")),
            ["shipping_address"] = Address(order.GetProperty("shippingAddress")),
            ["line_items"] = lines,
            ["shipping_lines"] = shipping,
            ["total_cash_rounding_payment_adjustment_set"] = Copy(order.GetProperty("totalCashRoundingAdjustment"), "paymentSet"),
            ["total_cash_rounding_refund_adjustment_set"] = Copy(order.GetProperty("totalCashRoundingAdjustment"), "refundSet")
        };
        foreach (var (field, key) in new[] { ("subtotalPriceSet", "subtotal_price"), ("totalDiscountsSet", "total_discounts"),
            ("totalTaxSet", "total_tax"), ("totalPriceSet", "total_price"), ("totalShippingPriceSet", "total_shipping_price"),
            ("currentSubtotalPriceSet", "current_subtotal_price"), ("currentTotalDiscountsSet", "current_total_discounts"),
            ("currentTotalTaxSet", "current_total_tax"), ("currentTotalPriceSet", "current_total_price"), ("currentShippingPriceSet", "current_shipping_price") })
        {
            var money = order.GetProperty(field);
            result[key] = money.ValueKind == JsonValueKind.Null ? null : MoneyAmount(money);
            result[key + "_set"] = money.ValueKind == JsonValueKind.Null ? null : JsonNode.Parse(money.GetRawText());
        }
        return result;
    }

    private static JsonNode? Copy(JsonElement value, string key) => value.TryGetProperty(key, out var field) &&
        field.ValueKind != JsonValueKind.Null ? JsonNode.Parse(field.GetRawText()) : null;
    private static string? Read(JsonElement value, string key) => value.TryGetProperty(key, out var field) &&
        field.ValueKind != JsonValueKind.Null ? field.ToString() : null;
    private static string? MoneyAmount(JsonElement bag) => Read(bag.GetProperty("shop_money"), "amount");
    private static JsonArray Discounts(JsonElement values) => new(values.EnumerateArray().Select(x => (JsonNode)new JsonObject
    {
        ["amount"] = MoneyAmount(x.GetProperty("allocatedAmountSet")),
        ["amount_set"] = Copy(x, "allocatedAmountSet")
    }).ToArray());
    private static JsonArray Taxes(JsonElement values) => new(values.EnumerateArray().Select(x => (JsonNode)new JsonObject
    {
        ["title"] = Read(x, "title"),
        ["rate"] = Copy(x, "rate"),
        ["price"] = MoneyAmount(x.GetProperty("priceSet")),
        ["price_set"] = Copy(x, "priceSet")
    }).ToArray());
    private static JsonObject? Address(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : new JsonObject
    {
        ["first_name"] = Read(value, "firstName"),
        ["last_name"] = Read(value, "lastName"),
        ["address1"] = Read(value, "address1"),
        ["address2"] = Read(value, "address2"),
        ["city"] = Read(value, "city"),
        ["province"] = Read(value, "province"),
        ["zip"] = Read(value, "zip"),
        ["country"] = Read(value, "country"),
        ["phone"] = Read(value, "phone"),
        ["company"] = Read(value, "company"),
        ["province_code"] = Read(value, "provinceCode"),
        ["country_code"] = Read(value, "countryCode")
    };
    private sealed class ShopifyThrottleException(TimeSpan delay) : Exception("Shopify throttled recovery.") { public TimeSpan Delay { get; } = delay; }
}

public sealed class ShopifyReconciliationWorker(IServiceScopeFactory scopes, ILogger<ShopifyReconciliationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ShopifyReconciliationService>().RunDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogError("Shopify recovery pass failed; durable progress retained."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
