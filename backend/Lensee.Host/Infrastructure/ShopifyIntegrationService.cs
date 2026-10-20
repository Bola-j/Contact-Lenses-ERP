using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Catalog.Services;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Inventory.Services;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Host.Endpoints;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lensee.Host.Infrastructure;

public sealed class ShopifyOptions
{
    public bool Enabled { get; init; }
    public string? WebhookSecret { get; init; }
    public string? LegacyWebhookPathSecret { get; init; }
    public string? StoreDomain { get; init; }
    public DateTime? ActivationAtUtc { get; init; }
    public bool ReconciliationEnabled { get; init; }
    public string? AdminAccessToken { get; init; }
    public string AdminApiVersion { get; init; } = "2026-07";
    public int ReconciliationIntervalMinutes { get; init; } = 15;
    public Guid OnlineLocationId { get; init; }
    public string[] CodGatewayNames { get; init; } = [];
    public int MaxBodyBytes { get; init; } = 262_144;
    public int PayloadRetentionDays { get; init; } = 30;
}

public sealed record ShopifyWebhookResult(int StatusCode, string Status, string? Detail = null, Guid? OperationId = null);
public sealed record ShopifyEventActionResult(string Status, ShopifyWebhookEvent? EventRecord = null);
public sealed record ShopifyVariantLinkUpdateResult(string Status, string? Detail = null, string? Field = null, ShopifyVariantLinkResponse? Link = null);
public sealed record ShopifyWebhookEnvelope(string WebhookId, string Topic, string? ShopDomain, string? EventId, string? ApiVersion, string? TriggeredAt, string VerificationMode);

public sealed class ShopifyIntegrationService
{
    private const string Shopify = "Shopify";
    private const string Draft = "Draft";
    private const string Reserved = "Reserved";
    private const string Shipped = "Shipped";
    private const string Completed = "Completed";
    private const string Cancelled = "Cancelled";
    private const string RetailSale = "RetailSale";
    private const string CashHandToHand = "CashHandToHand";
    private const string BankTransfer = "BankTransfer";

    private readonly ShopifyOptions _options;
    private readonly OperationsDbContext _operations;
    private readonly CrmDbContext _crm;
    private readonly CatalogDbContext _catalog;
    private readonly InventoryDbContext _inventory;
    private readonly NotificationsDbContext _notifications;
    private readonly IdentityDbContext _identity;
    private readonly ILogger<ShopifyIntegrationService> _logger;
    private readonly StockLedgerService _ledger;
    private readonly IClock _clock;
    private readonly IDataProtector _payloadProtector;
    private readonly IAuditLogWriter _auditLogWriter;

    public ShopifyIntegrationService(
        IOptions<ShopifyOptions> options,
        OperationsDbContext operations,
        CrmDbContext crm,
        CatalogDbContext catalog,
        InventoryDbContext inventory,
        NotificationsDbContext notifications,
        IdentityDbContext identity,
        ILogger<ShopifyIntegrationService> logger,
        StockLedgerService ledger,
        IClock clock,
        IDataProtectionProvider dataProtectionProvider,
        IAuditLogWriter auditLogWriter)
    {
        _options = options.Value;
        _operations = operations;
        _crm = crm;
        _catalog = catalog;
        _inventory = inventory;
        _notifications = notifications;
        _identity = identity;
        _logger = logger;
        _ledger = ledger;
        _clock = clock;
        _payloadProtector = dataProtectionProvider.CreateProtector("Lensee.Shopify.WebhookPayload.v1");
        _auditLogWriter = auditLogWriter;
    }

    private bool IsBaseConfigured =>
        _options.Enabled &&
        !string.IsNullOrWhiteSpace(_options.StoreDomain);

    public bool IsConfigured => IsBaseConfigured && !string.IsNullOrWhiteSpace(_options.WebhookSecret);

    public bool IsLegacyWebhookConfigured => IsBaseConfigured && IsValidLegacyPathSecret(_options.LegacyWebhookPathSecret);

    public bool IsIntakeConfigured => IsConfigured || IsLegacyWebhookConfigured;

    public int MaxBodyBytes => Math.Max(1, _options.MaxBodyBytes);

    public async Task<ShopifyVariantLinkUpdateResult> UpdateVariantLinkAsync(Guid id, Guid skuId, long revision, string? reason,
        Guid actorId, string actorName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return new ShopifyVariantLinkUpdateResult("Validation", "A reason is required to change a Shopify variant link.", "Reason");
        var mapping = await _operations.ShopifyVariantMappings.FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (mapping is null) return new ShopifyVariantLinkUpdateResult("NotFound");
        if (mapping.Revision != revision)
            return new ShopifyVariantLinkUpdateResult("Conflict", "This link changed since it was loaded. Refresh and review the current mapping.");
        var sku = await _catalog.Skus.Include(value => value.Product)
            .FirstOrDefaultAsync(value => value.Id == skuId && value.IsActive && value.DeletedAt == null && value.Product.IsActive && value.Product.DeletedAt == null, cancellationToken);
        if (sku is null) return new ShopifyVariantLinkUpdateResult("Validation", "Select an active ERP SKU.", "SkuId");
        if (!CatalogValidation.IsLensProduct(sku.Product.ProductType) || sku.Product.SellMode is not ("SinglePiece" or "Both" or "SealedPackOnly") || sku.Product.PiecesPerPack is null or <= 0 || !CatalogValidation.HasValidOpenedExpiryRate(sku.Product.OpenedExpiryRate))
            return new ShopifyVariantLinkUpdateResult("Validation", "The selected SKU is not ready for Shopify sales.", "SkuId");

        var oldSkuId = mapping.SkuId;
        var oldMode = mapping.EntryMode;
        mapping.SkuId = sku.Id;
        mapping.EntryMode = sku.Product.SellMode == "SealedPackOnly" ? "Packs" : "Pieces";
        mapping.Status = "Active";
        mapping.Detail = null;
        mapping.UpdatedAt = _clock.EgyptNow;
        mapping.Revision++;
        try
        {
            await SharedDbTransaction.ExecuteAsync(_operations, async () =>
            {
                await _operations.SaveChangesAsync(cancellationToken);
                await _auditLogWriter.WriteForUserAsync(actorId, actorName, "ShopifyVariantMapping", mapping.Id, "Changed", new
                {
                    mapping.ShopDomain,
                    mapping.ShopifyVariantId,
                    PreviousSkuId = oldSkuId,
                    mapping.SkuId,
                    PreviousEntryMode = oldMode,
                    mapping.EntryMode,
                    Reason = reason.Trim(),
                    Revision = mapping.Revision
                }, cancellationToken: cancellationToken);
            }, cancellationToken, _identity);
        }
        catch (DbUpdateConcurrencyException)
        { return new ShopifyVariantLinkUpdateResult("Conflict", "This link changed while it was being saved. Refresh and review the current mapping."); }
        return new ShopifyVariantLinkUpdateResult("Updated", Link: new ShopifyVariantLinkResponse(mapping.Id, mapping.ShopDomain,
            mapping.ShopifyVariantId, mapping.ShopifySku, mapping.SkuId, sku.SkuCode, mapping.Status, mapping.Detail, mapping.UpdatedAt, mapping.Revision));
    }

    public async Task<ShopifyEventActionResult> RetryEventAsync(Guid id, CancellationToken cancellationToken)
    {
        var eventRecord = await _operations.ShopifyWebhookEvents.FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (eventRecord is null) return new ShopifyEventActionResult("NotFound");
        if (string.IsNullOrWhiteSpace(eventRecord.ProtectedPayload)) return new ShopifyEventActionResult("PayloadExpired");
        if (!IsOperatorActionableStatus(eventRecord.Status)) return new ShopifyEventActionResult("Conflict");

        var now = _clock.EgyptNow;
        if (!_operations.Database.IsRelational())
        {
            eventRecord.Status = "Queued";
            eventRecord.Detail = "Manual retry requested.";
            eventRecord.AttemptCount = 0;
            eventRecord.NextAttemptAt = now;
            eventRecord.ProcessedAt = null;
            eventRecord.LeaseUntil = null;
            eventRecord.LeaseToken = null;
            eventRecord.ResolvedAt = null;
            eventRecord.ResolvedBy = null;
            eventRecord.ResolutionNote = null;
            await _operations.SaveChangesAsync(cancellationToken);
            return new ShopifyEventActionResult("Updated", eventRecord);
        }

        var changed = await _operations.ShopifyWebhookEvents
            .Where(value => value.Id == id && (value.Status == "RequiresAttention" || value.Status == "ReviewRequired") && value.ProtectedPayload != null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, "Queued")
                .SetProperty(value => value.Detail, "Manual retry requested.")
                .SetProperty(value => value.AttemptCount, 0)
                .SetProperty(value => value.NextAttemptAt, now)
                .SetProperty(value => value.ProcessedAt, (DateTime?)null)
                .SetProperty(value => value.LeaseUntil, (DateTime?)null)
                .SetProperty(value => value.LeaseToken, (Guid?)null)
                .SetProperty(value => value.ResolvedAt, (DateTime?)null)
                .SetProperty(value => value.ResolvedBy, (Guid?)null)
                .SetProperty(value => value.ResolutionNote, (string?)null), cancellationToken);
        if (changed != 1) return new ShopifyEventActionResult("Conflict");
        eventRecord = await _operations.ShopifyWebhookEvents.AsNoTracking().FirstAsync(value => value.Id == id, cancellationToken);
        return new ShopifyEventActionResult("Updated", eventRecord);
    }

    public async Task<ShopifyEventActionResult> ResolveEventAsync(Guid id, Guid? actorId, string note, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(note)) return new ShopifyEventActionResult("Validation");
        var eventRecord = await _operations.ShopifyWebhookEvents.FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (eventRecord is null) return new ShopifyEventActionResult("NotFound");
        if (!IsOperatorActionableStatus(eventRecord.Status)) return new ShopifyEventActionResult("Conflict");

        var now = _clock.EgyptNow;
        var normalizedNote = note.Trim();
        if (!_operations.Database.IsRelational())
        {
            eventRecord.Status = "Resolved";
            eventRecord.ResolvedAt = now;
            eventRecord.ResolvedBy = actorId;
            eventRecord.ResolutionNote = normalizedNote;
            eventRecord.LeaseUntil = null;
            eventRecord.LeaseToken = null;
            eventRecord.NextAttemptAt = null;
            await _operations.SaveChangesAsync(cancellationToken);
            return new ShopifyEventActionResult("Updated", eventRecord);
        }

        var changed = await _operations.ShopifyWebhookEvents
            .Where(value => value.Id == id && (value.Status == "RequiresAttention" || value.Status == "ReviewRequired"))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, "Resolved")
                .SetProperty(value => value.ResolvedAt, now)
                .SetProperty(value => value.ResolvedBy, actorId)
                .SetProperty(value => value.ResolutionNote, normalizedNote)
                .SetProperty(value => value.LeaseUntil, (DateTime?)null)
                .SetProperty(value => value.LeaseToken, (Guid?)null)
                .SetProperty(value => value.NextAttemptAt, (DateTime?)null), cancellationToken);
        if (changed != 1) return new ShopifyEventActionResult("Conflict");
        eventRecord = await _operations.ShopifyWebhookEvents.AsNoTracking().FirstAsync(value => value.Id == id, cancellationToken);
        return new ShopifyEventActionResult("Updated", eventRecord);
    }

    private static bool IsOperatorActionableStatus(string status) => status is "RequiresAttention" or "ReviewRequired";

    public bool VerifySignature(byte[] body, string? signature)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.WebhookSecret!));
        var expected = hmac.ComputeHash(body);
        try
        {
            var received = Convert.FromBase64String(signature);
            return CryptographicOperations.FixedTimeEquals(expected, received);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public bool VerifyLegacyPathSecret(string? pathSecret)
    {
        if (!IsLegacyWebhookConfigured || string.IsNullOrWhiteSpace(pathSecret)) return false;
        var expected = Encoding.UTF8.GetBytes(_options.LegacyWebhookPathSecret!);
        var received = Encoding.UTF8.GetBytes(pathSecret);
        return expected.Length == received.Length && CryptographicOperations.FixedTimeEquals(expected, received);
    }

    public Task<ShopifyWebhookResult> ReceiveAsync(ShopifyWebhookEnvelope envelope, byte[] body, CancellationToken cancellationToken) =>
        ReceiveCoreAsync(envelope, body, allowAdminApi: false, cancellationToken);

    internal Task<ShopifyWebhookResult> ReceiveReconciledAsync(ShopifyWebhookEnvelope envelope, byte[] body, CancellationToken cancellationToken) =>
        ReceiveCoreAsync(envelope with { VerificationMode = "AdminApi" }, body, allowAdminApi: true, cancellationToken);

    private async Task<ShopifyWebhookResult> ReceiveCoreAsync(ShopifyWebhookEnvelope envelope, byte[] body, bool allowAdminApi, CancellationToken cancellationToken)
    {
        var acceptsMode = envelope.VerificationMode switch
        {
            "Hmac" => IsConfigured,
            "LegacyPath" => IsLegacyWebhookConfigured,
            "AdminApi" => allowAdminApi && _options.ReconciliationEnabled && !string.IsNullOrWhiteSpace(_options.AdminAccessToken),
            _ => false
        };
        if (!acceptsMode)
        {
            return new ShopifyWebhookResult(StatusCodes.Status503ServiceUnavailable, "Disabled", "Shopify integration is not fully configured.");
        }
        if ((string.IsNullOrWhiteSpace(envelope.WebhookId) && string.IsNullOrWhiteSpace(envelope.EventId)) || string.IsNullOrWhiteSpace(envelope.Topic) || string.IsNullOrWhiteSpace(envelope.ShopDomain))
        {
            return new ShopifyWebhookResult(StatusCodes.Status400BadRequest, "Invalid", "Shopify delivery ID, topic, and shop domain are required.");
        }
        if (body.Length > _options.MaxBodyBytes)
        {
            return new ShopifyWebhookResult(StatusCodes.Status413PayloadTooLarge, "Rejected", "Webhook body exceeds the configured limit.");
        }
        if (!string.Equals(envelope.ShopDomain.Trim(), _options.StoreDomain!.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new ShopifyWebhookResult(StatusCodes.Status403Forbidden, "Rejected", "Shop domain is not configured for this ERP integration.");
        }

        try
        {
            await ShopifyActivation.EnsureAsync(_operations, _clock, _options, cancellationToken);
        }
        catch (DbUpdateException)
        {
            _operations.ChangeTracker.Clear();
            return new ShopifyWebhookResult(StatusCodes.Status503ServiceUnavailable, "Retry",
                "Shopify activation checkpoint could not be durably stored. Please retry this delivery.");
        }

        var topic = envelope.Topic.Trim().ToLowerInvariant();
        var shopDomain = envelope.ShopDomain.Trim().ToLowerInvariant();
        var deliveryId = TrimToNull(envelope.WebhookId) ?? TrimToNull(envelope.EventId)!;
        // The existing durable inbox uniqueness constraint is intentionally used
        // as the cross-worker gate.  Persist a bounded hash of the natural
        // identity, rather than the provider's bare delivery id, so an id from a
        // different shop or topic cannot suppress a legitimate event.
        var deliveryKey = CreateDeliveryKey(shopDomain, topic, deliveryId);
        var existing = await _operations.ShopifyWebhookEvents
            .FirstOrDefaultAsync(value => value.WebhookId == deliveryKey, cancellationToken);
        if (existing is not null)
        {
            return new ShopifyWebhookResult(StatusCodes.Status200OK, "Duplicate", existing.Detail, existing.OperationId);
        }

        var now = _clock.EgyptNow;
        var status = topic is "orders/create" or "orders/updated" or "orders/paid" or "orders/cancelled" or "refunds/create" ? "Queued" : "Ignored";
        var eventRecord = new ShopifyWebhookEvent
        {
            Id = Guid.NewGuid(),
            WebhookId = deliveryKey,
            Topic = topic,
            ShopDomain = shopDomain,
            VerificationMode = envelope.VerificationMode,
            EventId = TrimToNull(envelope.EventId),
            ApiVersion = TrimToNull(envelope.ApiVersion),
            PayloadHash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(),
            ProtectedPayload = _payloadProtector.Protect(Convert.ToBase64String(body)),
            Status = status,
            Detail = status == "Ignored" ? $"Unsupported Shopify topic '{envelope.Topic}'." : null,
            ReceivedAt = now,
            VerifiedAt = envelope.VerificationMode is "Hmac" or "AdminApi" ? now : null,
            TriggeredAt = ParseTriggeredAt(envelope.TriggeredAt),
            NextAttemptAt = status == "Queued" ? now : null
        };
        _operations.ShopifyWebhookEvents.Add(eventRecord);
        try
        {
            await _operations.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _operations.ChangeTracker.Clear();
            var replay = await _operations.ShopifyWebhookEvents.AsNoTracking()
                .FirstOrDefaultAsync(value => value.WebhookId == deliveryKey, cancellationToken);
            if (replay is not null)
            {
                return new ShopifyWebhookResult(StatusCodes.Status200OK, "Duplicate", replay.Detail ?? "Webhook was already accepted.", replay.OperationId);
            }
            return new ShopifyWebhookResult(StatusCodes.Status503ServiceUnavailable, "Retry", "Webhook receipt could not be durably stored. Shopify should retry this delivery.");
        }
        return new ShopifyWebhookResult(StatusCodes.Status202Accepted, status, eventRecord.Detail, eventRecord.OperationId);
    }

    public async Task ProcessQueuedEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var eventRecord = await _operations.ShopifyWebhookEvents.FirstOrDefaultAsync(value => value.Id == eventId, cancellationToken);
        if (eventRecord is null || eventRecord.Status is not ("Processing" or "Queued" or "Retrying") || string.IsNullOrWhiteSpace(eventRecord.ProtectedPayload))
        {
            return;
        }
        var claimToken = eventRecord.LeaseToken;
        if (!claimToken.HasValue) return;

        JsonDocument document;
        try
        {
            var raw = Convert.FromBase64String(_payloadProtector.Unprotect(eventRecord.ProtectedPayload));
            document = JsonDocument.Parse(raw);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            await MarkRequiresAttentionAsync(eventRecord, claimToken.Value, "Webhook payload cannot be decrypted or parsed.", cancellationToken);
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            var orderId = eventRecord.Topic == "refunds/create"
                ? ReadValue(root, "order_id") ?? ReadValue(root, "id")
                : ReadValue(root, "id") ?? ReadValue(root, "order_id");
            eventRecord.ShopifyOrderId = orderId;
            try
            {
                ShopifyWebhookResult? result = null;
                var claimLost = false;
                await SharedDbTransaction.ExecuteAsync(_operations, async () =>
                {
                    if (_operations.Database.IsRelational() && !string.IsNullOrWhiteSpace(orderId))
                    {
                        var orderLockKey = $"{eventRecord.ShopDomain}:{orderId}";
                        await _operations.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({orderLockKey}, 0))", cancellationToken);
                    }
                    if (_operations.Database.IsRelational())
                    {
                        await _operations.Database.ExecuteSqlInterpolatedAsync($"select id from operations.shopify_webhook_events where id = {eventRecord.Id} for update", cancellationToken);
                    }
                    var currentClaim = await _operations.ShopifyWebhookEvents.AsNoTracking()
                        .Where(value => value.Id == eventRecord.Id)
                        .Select(value => new { value.Status, value.LeaseToken, value.LeaseUntil })
                        .SingleOrDefaultAsync(cancellationToken);
                    if (currentClaim is null || currentClaim.Status != "Processing" || currentClaim.LeaseToken != claimToken || currentClaim.LeaseUntil is not { } leaseUntil || leaseUntil <= _clock.EgyptNow)
                    {
                        claimLost = true;
                        return;
                    }
                    result = eventRecord.Topic switch
                    {
                        "orders/create" => await CreateOrderAsync(root, eventRecord, cancellationToken),
                        "orders/updated" or "orders/paid" => await UpdateOrderAsync(root, eventRecord, cancellationToken),
                        "orders/cancelled" => await CancelOrderAsync(root, eventRecord, cancellationToken),
                        "refunds/create" => await RegisterRefundExceptionAsync(root, eventRecord, cancellationToken),
                        _ => new ShopifyWebhookResult(StatusCodes.Status202Accepted, "Ignored", $"Unsupported Shopify topic '{eventRecord.Topic}'.")
                    };
                    eventRecord.Status = result.Status;
                    eventRecord.Detail = result.Detail;
                    eventRecord.OperationId = result.OperationId;
                    eventRecord.ProcessedAt = _clock.EgyptNow;
                    eventRecord.LeaseUntil = null;
                    eventRecord.LeaseToken = null;
                    await _operations.SaveChangesAsync(cancellationToken);
                }, cancellationToken, _crm, _inventory, _notifications);
                if (claimLost) return;
                var completedResult = result ?? throw new InvalidOperationException("Shopify event processing did not produce an outcome.");
                if (completedResult.OperationId.HasValue)
                {
                    await _auditLogWriter.WriteSystemAsync(Shopify, "ShopifyWebhookEvent", eventRecord.Id, completedResult.Status, new { eventRecord.Topic, eventRecord.ShopifyOrderId, completedResult.OperationId }, cancellationToken: cancellationToken);
                }
            }
            catch (ShopifyBusinessException exception)
            {
                if (await TryCompleteClaimFailureAsync(eventRecord.Id, claimToken.Value, "RequiresAttention", exception.Message, null, orderId, cancellationToken))
                    await CreateExceptionNotificationAsync("ShopifyOrderException", exception.Message, orderId ?? "unknown", null, cancellationToken);
            }
            catch (Exception exception)
            {
                var latestAttempt = await _operations.ShopifyWebhookEvents.AsNoTracking()
                    .Where(value => value.Id == eventRecord.Id && value.LeaseToken == claimToken.Value)
                    .Select(value => (int?)value.AttemptCount).SingleOrDefaultAsync(cancellationToken);
                if (!latestAttempt.HasValue) return;
                var failureStatus = latestAttempt.Value >= 5 ? "RequiresAttention" : "Retrying";
                var failureDetail = failureStatus == "RequiresAttention" ? "Automatic processing exhausted; review the integration event." : "Transient processing failure; retry scheduled.";
                _logger.LogError(exception, "Shopify event processing failed for event {EventId}, topic {Topic}, attempt {AttemptCount}.", eventRecord.Id, eventRecord.Topic, latestAttempt.Value);
                var nextAttempt = failureStatus == "Retrying" ? _clock.EgyptNow.Add(GetRetryDelay(latestAttempt.Value)) : (DateTime?)null;
                if (await TryCompleteClaimFailureAsync(eventRecord.Id, claimToken.Value, failureStatus, failureDetail, nextAttempt, orderId, cancellationToken))
                {
                    if (failureStatus == "RequiresAttention")
                    {
                        var operationId = await _operations.ShopifyWebhookEvents.AsNoTracking().Where(value => value.Id == eventRecord.Id)
                            .Select(value => value.OperationId).SingleOrDefaultAsync(cancellationToken);
                        await CreateExceptionNotificationAsync("ShopifyOrderException", failureDetail, orderId ?? "unknown", operationId, cancellationToken);
                    }
                }
            }
        }
    }

    public async Task<IReadOnlyList<Guid>> ClaimDueEventsAsync(CancellationToken cancellationToken)
    {
        var now = _clock.EgyptNow;
        if (_operations.Database.IsRelational())
        {
            var candidateIds = await _operations.ShopifyWebhookEvents.AsNoTracking()
                .Where(value => ((value.Status == "Queued" || value.Status == "Retrying") && value.NextAttemptAt <= now) ||
                    (value.Status == "Processing" && (value.LeaseUntil == null || value.LeaseUntil < now)))
                .OrderBy(value => value.ReceivedAt)
                .Select(value => value.Id)
                .Take(50)
                .ToListAsync(cancellationToken);
            var claimed = new List<Guid>();
            foreach (var id in candidateIds)
            {
                var updated = await _operations.ShopifyWebhookEvents
                    .Where(value => value.Id == id && (((value.Status == "Queued" || value.Status == "Retrying") && value.NextAttemptAt <= now) ||
                        (value.Status == "Processing" && (value.LeaseUntil == null || value.LeaseUntil < now))))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(value => value.Status, "Processing")
                        .SetProperty(value => value.AttemptCount, value => value.AttemptCount + 1)
                        .SetProperty(value => value.LeaseUntil, now.AddMinutes(2))
                        .SetProperty(value => value.LeaseToken, Guid.NewGuid()), cancellationToken);
                if (updated == 1)
                {
                    claimed.Add(id);
                    if (claimed.Count == 10) break;
                }
            }
            return claimed;
        }

        var rows = await _operations.ShopifyWebhookEvents
            .Where(value => ((value.Status == "Queued" || value.Status == "Retrying") && value.NextAttemptAt <= now) ||
                (value.Status == "Processing" && (value.LeaseUntil == null || value.LeaseUntil < now)))
            .OrderBy(value => value.ReceivedAt)
            .Take(10)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.Status = "Processing";
            row.AttemptCount++;
            row.LeaseUntil = now.AddMinutes(2);
            row.LeaseToken = Guid.NewGuid();
        }
        if (rows.Count > 0)
        {
            await _operations.SaveChangesAsync(cancellationToken);
        }
        return rows.Select(value => value.Id).ToArray();
    }

    public async Task PurgeExpiredPayloadsAsync(CancellationToken cancellationToken)
    {
        var cutoff = _clock.EgyptNow.AddDays(-Math.Max(1, _options.PayloadRetentionDays));
        var rows = await _operations.ShopifyWebhookEvents
            .Where(value => value.ProtectedPayload != null && value.ReceivedAt < cutoff &&
                (value.Status == "Imported" || value.Status == "Cancelled" || value.Status == "Ignored" || value.Status == "Duplicate" || value.Status == "Resolved"))
            .ToListAsync(cancellationToken);
        foreach (var row in rows) row.ProtectedPayload = null;
        if (rows.Count > 0) await _operations.SaveChangesAsync(cancellationToken);
    }

    private async Task<ShopifyWebhookResult> CreateOrderAsync(JsonElement root, ShopifyWebhookEvent eventRecord, CancellationToken cancellationToken)
    {
        var orderId = RequireValue(root, "id", "Shopify order ID is required.");
        var shopDomain = eventRecord.ShopDomain;
        if (await _operations.ShopifyOrderCancellationTombstones.AnyAsync(value => value.ShopDomain == shopDomain && value.ShopifyOrderId == orderId, cancellationToken))
        {
            return new ShopifyWebhookResult(StatusCodes.Status200OK, "Cancelled", "The Shopify order was cancelled before it reached the ERP; its cancellation tombstone prevented import.");
        }
        var existing = await _operations.ShopifyOrderLinks
            .FirstOrDefaultAsync(value => value.ShopifyOrderId == orderId, cancellationToken);
        if (existing is not null)
        {
            return new ShopifyWebhookResult(StatusCodes.Status200OK, "Duplicate", "Shopify order was already imported.", existing.OperationId);
        }

        var checkpoint = await ShopifyActivation.EnsureAsync(_operations, _clock, _options, cancellationToken);
        var createdAtText = ReadValue(root, "created_at");
        if (!DateTimeOffset.TryParse(createdAtText, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var createdAt))
        {
            throw new ShopifyBusinessException($"Shopify order {orderId} has no valid created_at timestamp; activation cutoff cannot be checked safely.");
        }
        if (createdAt.UtcDateTime < DateTime.SpecifyKind(checkpoint.ActivatedAtUtc, DateTimeKind.Utc))
        {
            return new ShopifyWebhookResult(StatusCodes.Status200OK, "Ignored", "Order predates Shopify integration activation cutoff; no historical import created.");
        }
        var sourceUpdatedAt = ParseShopifyTimestamp(ReadValue(root, "updated_at"));
        if (!sourceUpdatedAt.HasValue)
        {
            throw new ShopifyBusinessException($"Shopify order {orderId} has no valid updated_at timestamp; its source payment state cannot be trusted safely.");
        }

        if (!ShopifyOrderMoneySnapshotBuilder.TryBuild(root, out var money, out var moneyError) || money is null)
        {
            throw new ShopifyBusinessException($"Shopify order {orderId} cannot be imported: {moneyError}");
        }
        var lineItems = ReadArray(root, "line_items");
        if (lineItems.Count == 0)
        {
            throw new ShopifyBusinessException($"Shopify order {orderId} has no line items.");
        }

        var commercialLinesById = money.Lines.Where(value => !string.IsNullOrWhiteSpace(value.LineItemId))
            .GroupBy(value => value.LineItemId!, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var incomingLines = lineItems.Select((item, index) => ReadOrderLine(item, index + 1, commercialLinesById)).ToList();
        var errors = new List<string>();
        if (incomingLines.Any(line => string.IsNullOrWhiteSpace(line.LineItemId)))
        {
            errors.Add("Every Shopify line requires a line-item ID.");
        }
        var duplicateLineIds = incomingLines.Where(line => !string.IsNullOrWhiteSpace(line.LineItemId))
            .GroupBy(line => line.LineItemId!, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToArray();
        if (duplicateLineIds.Length > 0)
        {
            errors.Add($"Shopify line-item IDs must be unique: {string.Join(", ", duplicateLineIds)}.");
        }
        foreach (var line in incomingLines)
        {
            if (string.IsNullOrWhiteSpace(line.RawSku)) errors.Add($"Line {line.Position} has no Shopify SKU.");
            if (line.Quantity <= 0) errors.Add($"Line {line.Position} has a non-positive quantity.");
            if (line.UnitPrice < 0 || !line.HasValidPrice) errors.Add($"Line {line.Position} has an invalid net price.");
        }

        var skuKeys = incomingLines.Where(line => !string.IsNullOrWhiteSpace(line.RawSku)).Select(line => NormalizeSkuKey(line.RawSku!)).Distinct().ToArray();
        var candidates = skuKeys.Length == 0
            ? []
            : await _catalog.Skus.Include(value => value.Product)
                .Where(value => value.IsActive && value.DeletedAt == null && value.Product.IsActive && value.Product.DeletedAt == null && skuKeys.Contains(value.SkuCode.Trim().ToUpper()))
                .ToListAsync(cancellationToken);
        var candidatesByKey = candidates.GroupBy(value => NormalizeSkuKey(value.SkuCode)).ToDictionary(group => group.Key, group => group.ToList());
        var variantIds = incomingLines.Where(value => !string.IsNullOrWhiteSpace(value.VariantId)).Select(value => value.VariantId!).Distinct().ToArray();
        var mappings = variantIds.Length == 0
            ? []
            : await _operations.ShopifyVariantMappings.Where(value => value.ShopDomain == shopDomain && variantIds.Contains(value.ShopifyVariantId)).ToListAsync(cancellationToken);
        var mappingsByVariant = mappings.ToDictionary(value => value.ShopifyVariantId, StringComparer.Ordinal);
        var resolvedLines = new List<ResolvedShopifyOrderLine>();
        foreach (var line in incomingLines.Where(line => !string.IsNullOrWhiteSpace(line.RawSku)))
        {
            var key = NormalizeSkuKey(line.RawSku!);
            var hasCodeMatch = candidatesByKey.TryGetValue(key, out var matched);
            var mapping = !string.IsNullOrWhiteSpace(line.VariantId) && mappingsByVariant.TryGetValue(line.VariantId, out var existingMapping) ? existingMapping : null;
            Sku? sku = null;
            if (mapping?.Status == "Active" && mapping.SkuId.HasValue)
            {
                if (!string.IsNullOrWhiteSpace(mapping.ShopifySku) && !string.Equals(NormalizeSkuKey(mapping.ShopifySku), key, StringComparison.Ordinal))
                {
                    errors.Add($"Line {line.Position} Shopify variant '{line.VariantId}' now carries a different copied SKU than its remembered link; review the variant link before import.");
                    continue;
                }
                if (hasCodeMatch && matched!.Count != 1)
                {
                    errors.Add($"Line {line.Position} SKU '{line.RawSku}' matches multiple active Lensee SKUs; resolve the duplicated ERP SKU codes before Shopify import.");
                    continue;
                }
                sku = await _catalog.Skus.Include(value => value.Product).FirstOrDefaultAsync(value => value.Id == mapping.SkuId && value.IsActive && value.DeletedAt == null && value.Product.IsActive && value.Product.DeletedAt == null, cancellationToken);
                if (sku is null)
                {
                    errors.Add($"Line {line.Position} Shopify variant '{line.VariantId}' is linked to an inactive or missing ERP SKU; resolve the link explicitly.");
                    continue;
                }
                if (hasCodeMatch && matched!.Count == 1 && matched[0].Id != sku.Id)
                {
                    errors.Add($"Line {line.Position} Shopify variant '{line.VariantId}' is linked to a different ERP SKU than its copied Shopify SKU code; review the variant link.");
                    continue;
                }
            }
            else if (!hasCodeMatch)
            {
                errors.Add($"Line {line.Position} SKU '{line.RawSku}' is not an active Lensee SKU.");
                continue;
            }
            else if (matched!.Count != 1)
            {
                errors.Add($"Line {line.Position} SKU '{line.RawSku}' matches multiple Lensee SKUs when compared without case.");
                continue;
            }
            else sku = matched[0];
            if (!CatalogValidation.IsLensProduct(sku!.Product.ProductType)) errors.Add($"Line {line.Position} SKU '{line.RawSku}' is not a Lens product.");
            else if (sku.Product.SellMode is not ("SinglePiece" or "Both" or "SealedPackOnly")) errors.Add($"Line {line.Position} SKU '{line.RawSku}' has an unsupported online sales unit.");
            else if (sku.Product.PiecesPerPack is null or <= 0) errors.Add($"Line {line.Position} SKU '{line.RawSku}' has no valid pieces-per-pack value.");
            else if (!CatalogValidation.HasValidOpenedExpiryRate(sku.Product.OpenedExpiryRate)) errors.Add($"Line {line.Position} SKU '{line.RawSku}' has no valid Daily, Monthly, or Annual wear cycle.");
            else
            {
                var mode = sku.Product.SellMode == "SealedPackOnly" ? "Packs" : "Pieces";
                if (mapping is not null && mapping.Status == "Unresolved" && !mapping.SkuId.HasValue)
                {
                    // An unresolved link has no ERP identity to redirect. A newly
                    // unique, validated copied SKU can safely complete that link.
                    mapping.SkuId = sku.Id;
                    mapping.ShopifySku = line.RawSku;
                    mapping.EntryMode = mode;
                    mapping.Status = "Active";
                    mapping.Detail = null;
                    mapping.UpdatedAt = _clock.EgyptNow;
                    mapping.Revision++;
                }
                else if (mapping is not null && (mapping.SkuId != sku.Id || mapping.EntryMode != mode))
                {
                    errors.Add($"Line {line.Position} Shopify variant link conflicts with the required online sale unit; review the variant link.");
                    continue;
                }
                if (mapping is null && !string.IsNullOrWhiteSpace(line.VariantId))
                {
                    mapping = new ShopifyVariantMapping
                    {
                        Id = Guid.NewGuid(),
                        ShopDomain = shopDomain,
                        ShopifyVariantId = line.VariantId!,
                        ShopifySku = line.RawSku,
                        SkuId = sku.Id,
                        EntryMode = mode,
                        Status = "Active",
                        CreatedAt = _clock.EgyptNow,
                        UpdatedAt = _clock.EgyptNow,
                        Revision = 1
                    };
                    _operations.ShopifyVariantMappings.Add(mapping);
                    mappingsByVariant[line.VariantId!] = mapping;
                }
                resolvedLines.Add(new ResolvedShopifyOrderLine(line, sku, mode));
            }
        }
        if (errors.Count > 0)
        {
            var detail = $"Shopify order {orderId} cannot be imported: {string.Join(" ", errors)}";
            foreach (var line in incomingLines.Where(value => !string.IsNullOrWhiteSpace(value.VariantId)))
            {
                if (mappingsByVariant.ContainsKey(line.VariantId!)) continue;
                _operations.ShopifyVariantMappings.Add(new ShopifyVariantMapping
                {
                    Id = Guid.NewGuid(),
                    ShopDomain = shopDomain,
                    ShopifyVariantId = line.VariantId!,
                    ShopifySku = line.RawSku,
                    EntryMode = "Pieces",
                    Status = "Unresolved",
                    Detail = Limit(detail, 1000),
                    CreatedAt = _clock.EgyptNow,
                    UpdatedAt = _clock.EgyptNow,
                    Revision = 1
                });
            }
            await _operations.SaveChangesAsync(cancellationToken);
            await CreateExceptionNotificationAsync("ShopifyOrderException", detail, orderId, null, cancellationToken);
            return new ShopifyWebhookResult(StatusCodes.Status202Accepted, "RequiresAttention", detail);
        }

        // Shopify identity is never an implicit CRM selection.  Only a
        // pre-existing, explicitly linked merchant can enter MerchantAccount;
        // external name, email and phone remain an identity snapshot.
        var merchant = await ResolveExplicitMerchantAsync(root, cancellationToken);
        var customer = TryGetObject(root, "customer");
        var buyerName = merchant?.BusinessName ?? BuildCustomerName(root, customer);
        var buyerEmail = NormalizeEmail(ReadValue(root, "email") ?? (customer.HasValue ? ReadValue(customer.Value, "email") : null));
        var buyerPhone = NormalizePhone(ReadValue(root, "phone") ?? (customer.HasValue ? ReadValue(customer.Value, "phone") : null) ?? ReadValue(TryGetObject(root, "shipping_address"), "phone"));
        var now = _clock.EgyptNow;
        var operation = new OperationLog
        {
            Id = Guid.NewGuid(),
            OperationNumber = $"OP-{now:yyyyMMddHHmmss}-{RandomNumberGenerator.GetInt32(100, 1000)}",
            OperationType = RetailSale,
            Status = Draft,
            SalesChannel = Shopify,
            // Shopify is commercial intake only. A warehouse employee selects
            // the active SubWarehouse and batch allocation before confirmation.
            SourceLocationId = null,
            ClientId = merchant?.Id,
            ClientName = buyerName,
            BuyerPhone = buyerPhone,
            BuyerEmail = buyerEmail,
            ShippingAddress = ReadAddress(root),
            DeliveryFee = money.Shipping,
            PaymentMethod = IsCod(root) ? CashHandToHand : null,
            PaymentStatus = IsCod(root) ? "DueOnDelivery" : "PendingReconciliation",
            ExternalPaymentProvider = null,
            ExternalPaymentReference = null,
            ExternalPaymentVerified = false,
            Notes = $"Imported from Shopify order {ReadValue(root, "name") ?? orderId}.",
            CreatedBy = Guid.Empty,
            CreatedActorName = Shopify,
            CreatedAt = now,
            CurrentVersionId = null
        };

        foreach (var resolved in resolvedLines)
        {
            var sku = resolved.Sku;
            var line = resolved.Line;
            operation.OperationLines.Add(new OperationLine
            {
                Id = Guid.NewGuid(),
                OperationId = operation.Id,
                SkuId = sku.Id,
                ProductNameSnapshot = sku.Product.Name,
                SkuCodeSnapshot = sku.SkuCode,
                MerchantNameSnapshot = merchant?.BusinessName,
                Section = "Standard",
                Quantity = line.Quantity,
                EntryMode = resolved.Mode,
                PiecesPerPackSnapshot = sku.Product.PiecesPerPack,
                NormalQuantity = line.Quantity,
                NormalValue = line.Commercial.NetLineTotal,
                BonusQuantity = 0,
                UnitPrice = line.UnitPrice,
                LineTotal = line.Commercial.NetLineTotal,
                LotNumber = null,
                ExpiryDate = null,
                LineNotes = $"Imported from Shopify line {line.LineItemId}",
                ShopifyLineItemId = Limit(line.LineItemId, 100),
                ShopifyVariantId = Limit(line.VariantId, 100),
                ShopifySkuSnapshot = LimitRaw(line.RawSku, 255),
                ShopifyTitleSnapshot = Limit(line.Title, 255),
                ShopifyVariantTitleSnapshot = Limit(line.VariantTitle, 255),
                ShopifyPropertiesSnapshot = line.PublicPropertiesSnapshot,
                ShopifyCommercialSnapshot = JsonSerializer.Serialize(new
                {
                    line.Commercial.OriginalLineTotal,
                    line.Commercial.Discount,
                    line.Commercial.NetLineTotal,
                    line.Commercial.Tax,
                    line.Commercial.Currency,
                    line.Commercial.PresentmentCurrency,
                    line.Commercial.SourceJson
                })
            });
        }

        _operations.OperationLogs.Add(operation);
        _operations.ShopifyOrderLinks.Add(new ShopifyOrderLink
        {
            OperationId = operation.Id,
            ShopifyOrderId = orderId,
            ShopifyOrderNumber = ReadValue(root, "name") ?? ReadValue(root, "order_number"),
            PaymentReference = null,
            ShopDomain = shopDomain,
            FinancialStatus = ReadValue(root, "financial_status")?.ToLowerInvariant(),
            FinancialStatusTrusted = eventRecord.VerificationMode is "Hmac" or "AdminApi",
            FinanceReconciliationStatus = "PendingEvidence",
            ReportedCurrency = money.Currency,
            PresentmentCurrency = money.PresentmentCurrency,
            ReportedSubtotal = money.Subtotal,
            ReportedDiscounts = money.Discounts,
            ReportedShipping = money.Shipping,
            ReportedTax = money.Tax,
            ReportedTotal = money.Total,
            SourceUpdatedAt = sourceUpdatedAt,
            CommercialSnapshot = money.SourceJson,
            CommercialSnapshotHash = money.Hash,
            CreatedAt = now,
            UpdatedAt = now
        });
        await _operations.SaveChangesAsync(cancellationToken);

        // OperationLog.CurrentVersionId and OperationVersion.OperationId form a
        // two-way FK. Insert the operation graph first, then the first version,
        // then point the operation at it. ProcessQueuedEventAsync wraps all three
        // saves in the same transaction for relational providers.
        var initialVersion = new OperationVersion
        {
            Id = Guid.NewGuid(),
            OperationId = operation.Id,
            VersionNumber = 1,
            Reason = "Created from Shopify webhook",
            SnapshotData = JsonSerializer.Serialize(new { operation.OperationType, operation.Status, operation.SalesChannel, ShopifyOrderId = orderId, Lines = operation.OperationLines.Select(line => new { line.ShopifyLineItemId, line.ShopifyVariantId, line.ShopifySkuSnapshot, line.Quantity, line.EntryMode }) }),
            EditedBy = Guid.Empty,
            EditedActorName = Shopify,
            EditedAt = now
        };
        operation.OperationVersions.Add(initialVersion);
        _operations.OperationVersions.Add(initialVersion);
        await _operations.SaveChangesAsync(cancellationToken);
        operation.CurrentVersionId = initialVersion.Id;
        await _operations.SaveChangesAsync(cancellationToken);
        return new ShopifyWebhookResult(StatusCodes.Status201Created, "Imported", "Shopify order was imported as an unallocated draft.", operation.Id);
    }

    private async Task<ShopifyWebhookResult> UpdateOrderAsync(JsonElement root, ShopifyWebhookEvent eventRecord, CancellationToken cancellationToken)
    {
        var orderId = RequireValue(root, "id", "Shopify order ID is required.");
        var link = await _operations.ShopifyOrderLinks.Include(value => value.Operation)
            .FirstOrDefaultAsync(value => value.ShopifyOrderId == orderId, cancellationToken);
        if (link is null)
        {
            return new ShopifyWebhookResult(StatusCodes.Status200OK, "Ignored", "Order update was retained, but this order was not imported after activation; historical orders are not backfilled.");
        }
        await LockShopifyOperationAsync(link.OperationId, cancellationToken);
        await _operations.Entry(link).ReloadAsync(cancellationToken);
        await _operations.Entry(link.Operation).ReloadAsync(cancellationToken);
        eventRecord.OperationId = link.OperationId;
        if (link.IsCancellationTombstone)
        {
            return new ShopifyWebhookResult(StatusCodes.Status200OK, "Ignored", "A recorded cancellation prevents stale order updates from changing this operation.", link.OperationId);
        }

        var updatedAt = ParseShopifyTimestamp(ReadValue(root, "updated_at"));
        if (!updatedAt.HasValue)
        {
            throw new ShopifyBusinessException($"Shopify update for order {orderId} has no valid updated_at timestamp.");
        }
        if (link.SourceUpdatedAt.HasValue && updatedAt.Value <= link.SourceUpdatedAt.Value)
        {
            return new ShopifyWebhookResult(StatusCodes.Status200OK, "Ignored", "A newer Shopify source update has already been applied.", link.OperationId);
        }

        if (!ShopifyOrderMoneySnapshotBuilder.TryBuild(root, out var money, out var moneyError) || money is null)
        {
            link.RequiresReview = true;
            link.SourceUpdatedAt = updatedAt;
            link.UpdatedAt = _clock.EgyptNow;
            await CreateExceptionNotificationAsync("ShopifyOrderException", $"Shopify update for order {orderId} needs review: {moneyError}", orderId, link.OperationId, cancellationToken);
            return new ShopifyWebhookResult(StatusCodes.Status202Accepted, "RequiresAttention", $"Shopify update needs commercial review: {moneyError}", link.OperationId);
        }

        var changedCommercialFacts = !string.Equals(link.CommercialSnapshotHash, money.Hash, StringComparison.OrdinalIgnoreCase);
        link.SourceUpdatedAt = updatedAt;
        link.UpdatedAt = _clock.EgyptNow;
        var financialStatus = ReadValue(root, "financial_status")?.ToLowerInvariant();
        if (eventRecord.VerificationMode is "Hmac" or "AdminApi" && !string.IsNullOrWhiteSpace(financialStatus))
        {
            link.FinancialStatus = financialStatus;
            link.FinancialStatusTrusted = true;
        }
        if (changedCommercialFacts)
        {
            link.RequiresReview = true;
            var detail = $"Shopify order {orderId} changed its commercial totals after import; review the order before any dispatch.";
            await CreateExceptionNotificationAsync("ShopifyOrderException", detail, orderId, link.OperationId, cancellationToken);
            return new ShopifyWebhookResult(StatusCodes.Status202Accepted, "RequiresAttention", detail, link.OperationId);
        }

        link.CommercialSnapshot = money.SourceJson;
        link.CommercialSnapshotHash = money.Hash;
        link.ReportedCurrency = money.Currency;
        link.PresentmentCurrency = money.PresentmentCurrency;
        link.ReportedSubtotal = money.Subtotal;
        link.ReportedDiscounts = money.Discounts;
        link.ReportedShipping = money.Shipping;
        link.ReportedTax = money.Tax;
        link.ReportedTotal = money.Total;
        return new ShopifyWebhookResult(StatusCodes.Status200OK, "Updated", "Shopify reported status was refreshed; ERP verification and Finance reconciliation remain unchanged.", link.OperationId);
    }

    private async Task<ShopifyWebhookResult> CancelOrderAsync(JsonElement root, ShopifyWebhookEvent eventRecord, CancellationToken cancellationToken)
    {
        var orderId = RequireValue(root, "id", "Shopify order ID is required.");
        var link = await _operations.ShopifyOrderLinks
            .Include(value => value.Operation).ThenInclude(value => value.OperationLines)
            .Include(value => value.Operation).ThenInclude(value => value.OperationVersions)
            .FirstOrDefaultAsync(value => value.ShopifyOrderId == orderId, cancellationToken);
        if (link is null)
        {
            var tombstone = await _operations.ShopifyOrderCancellationTombstones
                .FirstOrDefaultAsync(value => value.ShopDomain == eventRecord.ShopDomain && value.ShopifyOrderId == orderId, cancellationToken);
            if (tombstone is null)
            {
                _operations.ShopifyOrderCancellationTombstones.Add(new ShopifyOrderCancellationTombstone
                {
                    Id = Guid.NewGuid(),
                    ShopDomain = eventRecord.ShopDomain,
                    ShopifyOrderId = orderId,
                    CancelledAt = _clock.EgyptNow
                });
            }
            return new ShopifyWebhookResult(StatusCodes.Status200OK, "Cancelled", "Cancellation was retained before order import; a tombstone will prevent a later historical import.");
        }

        await LockShopifyOperationAsync(link.OperationId, cancellationToken);
        await _operations.Entry(link).ReloadAsync(cancellationToken);
        await _operations.Entry(link.Operation).ReloadAsync(cancellationToken);

        var operation = link.Operation;
        if (operation.Status == Draft)
        {
            operation.Status = Cancelled;
            link.IsCancellationTombstone = true;
            link.CancelledAt = _clock.EgyptNow;
            AddAuditVersion(operation, "Cancelled from Shopify webhook");
            link.UpdatedAt = _clock.EgyptNow;
            eventRecord.OperationId = operation.Id;
            return new ShopifyWebhookResult(StatusCodes.Status200OK, "Cancelled", "Draft Shopify operation was cancelled.", operation.Id);
        }
        if (operation.Status == Reserved)
        {
            foreach (var group in operation.OperationLines.Where(value => value.EntryMode == "Packs").GroupBy(value => value.SkuId))
            {
                await _ledger.ReleaseInWarehouseAsync(operation.SourceLocationId!.Value, group.Key, group.Sum(value => value.Quantity), Guid.Empty, operation.Id, cancellationToken);
            }
            await _ledger.ReleasePieceReservationsAsync(operation.Id, Guid.Empty, cancellationToken);
            operation.Status = Cancelled;
            link.IsCancellationTombstone = true;
            link.CancelledAt = _clock.EgyptNow;
            AddAuditVersion(operation, "Cancelled from Shopify webhook and released reservations");
            link.UpdatedAt = _clock.EgyptNow;
            eventRecord.OperationId = operation.Id;
            return new ShopifyWebhookResult(StatusCodes.Status200OK, "Cancelled", "Reserved Shopify operation was cancelled and stock released.", operation.Id);
        }

        if (operation.Status is Shipped or Completed or "Dispatched" or "Delivered" or "FailedDelivery")
        {
            link.IsCancellationTombstone = true;
            link.CancelledAt = _clock.EgyptNow;
            link.UpdatedAt = _clock.EgyptNow;
            await CreateExceptionNotificationAsync("ShopifyCancellationException", $"Shopify order {orderId} was cancelled after ERP operation {operation.OperationNumber} was {operation.Status}. Review stock and finance manually.", orderId, operation.Id, cancellationToken);
            return new ShopifyWebhookResult(StatusCodes.Status202Accepted, "RequiresAttention", "Cancellation requires manual stock and finance review.", operation.Id);
        }

        return new ShopifyWebhookResult(StatusCodes.Status200OK, "Ignored", $"Operation is already {operation.Status}.", operation.Id);
    }

    private async Task<ShopifyWebhookResult> RegisterRefundExceptionAsync(JsonElement root, ShopifyWebhookEvent eventRecord, CancellationToken cancellationToken)
    {
        var orderId = RequireValue(root, "order_id", "Shopify refund order ID is required.");
        var refundId = RequireValue(root, "id", "Shopify refund ID is required.");
        eventRecord.ShopifyRefundId = refundId;
        var prior = await _operations.ShopifyRefundReviews.FirstOrDefaultAsync(value => value.ShopDomain == eventRecord.ShopDomain && value.ShopifyRefundId == refundId, cancellationToken);
        if (prior is not null)
        {
            return new ShopifyWebhookResult(StatusCodes.Status200OK, "Duplicate", "Shopify refund was already retained for review.", prior.OperationId);
        }
        var link = await _operations.ShopifyOrderLinks.FirstOrDefaultAsync(value => value.ShopifyOrderId == orderId, cancellationToken);
        if (link is not null)
        {
            await LockShopifyOperationAsync(link.OperationId, cancellationToken);
            await _operations.Entry(link).ReloadAsync(cancellationToken);
        }
        var amount = ReadRefundAmount(root);
        var currency = ReadValue(root, "currency") ?? ReadMoneyBagCurrency(root, "total_refunded_set");
        _operations.ShopifyRefundReviews.Add(new ShopifyRefundReview
        {
            Id = Guid.NewGuid(),
            ShopDomain = eventRecord.ShopDomain,
            ShopifyRefundId = refundId,
            ShopifyOrderId = orderId,
            OperationId = link?.OperationId,
            Amount = amount,
            Currency = currency,
            Status = "RequiresAttention",
            ReceivedAt = _clock.EgyptNow,
            UpdatedAt = _clock.EgyptNow
        });
        await CreateExceptionNotificationAsync("ShopifyRefundException", $"Shopify refund {refundId} received for order {orderId}. Review stock return and financial adjustment manually.", orderId, link?.OperationId, cancellationToken);
        return new ShopifyWebhookResult(StatusCodes.Status202Accepted, "RequiresAttention", "Refund was retained once for manual stock and finance review; no inventory or payment was posted.", link?.OperationId);
    }

    private async Task<Merchant?> ResolveExplicitMerchantAsync(JsonElement root, CancellationToken cancellationToken)
    {
        var customer = TryGetObject(root, "customer");
        var externalId = customer.HasValue ? ReadValue(customer.Value, "id") : null;
        if (string.IsNullOrWhiteSpace(externalId)) return null;

        return await _crm.Merchants.FirstOrDefaultAsync(value =>
            !value.IsDeleted && value.Status == "Active" && value.ExternalProvider == Shopify && value.ExternalCustomerId == externalId,
            cancellationToken);
    }

    private async Task CreateExceptionNotificationAsync(string alertType, string message, string orderId, Guid? operationId, CancellationToken cancellationToken)
    {
        _notifications.NotificationLogs.Add(new NotificationLog
        {
            Id = Guid.NewGuid(),
            AlertType = alertType,
            Message = message,
            ReferenceId = operationId,
            ReferenceType = operationId.HasValue ? "Operation" : "ShopifyOrder",
            TargetRole = LenseeRoles.Admin,
            Channel = "InApp",
            IsRead = false,
            CreatedAt = _clock.EgyptNow
        });
        await _notifications.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkRequiresAttentionAsync(ShopifyWebhookEvent eventRecord, Guid claimToken, string detail, CancellationToken cancellationToken)
    {
        if (!await TryCompleteClaimFailureAsync(eventRecord.Id, claimToken, "RequiresAttention", detail, null, eventRecord.ShopifyOrderId, cancellationToken)) return;
        await CreateExceptionNotificationAsync("ShopifyOrderException", detail, eventRecord.ShopifyOrderId ?? "unknown", eventRecord.OperationId, cancellationToken);
    }

    private async Task<bool> TryCompleteClaimFailureAsync(Guid eventId, Guid claimToken, string status, string detail,
        DateTime? nextAttemptAt, string? shopifyOrderId, CancellationToken cancellationToken)
    {
        var now = _clock.EgyptNow;
        if (!_operations.Database.IsRelational())
        {
            _operations.ChangeTracker.Clear();
            var row = await _operations.ShopifyWebhookEvents.SingleOrDefaultAsync(value => value.Id == eventId, cancellationToken);
            if (row is null || row.Status != "Processing" || row.LeaseToken != claimToken || row.LeaseUntil is not { } leaseUntil || leaseUntil <= now) return false;
            row.Status = status;
            row.Detail = detail;
            row.ProcessedAt = now;
            row.NextAttemptAt = nextAttemptAt;
            if (shopifyOrderId is not null) row.ShopifyOrderId = shopifyOrderId;
            row.LeaseUntil = null;
            row.LeaseToken = null;
            await _operations.SaveChangesAsync(cancellationToken);
            return true;
        }
        var claimQuery = _operations.ShopifyWebhookEvents
            .Where(value => value.Id == eventId && value.Status == "Processing" && value.LeaseToken == claimToken && value.LeaseUntil > now);
        var rows = shopifyOrderId is null
            ? await claimQuery.ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, status)
                .SetProperty(value => value.Detail, detail)
                .SetProperty(value => value.ProcessedAt, now)
                .SetProperty(value => value.NextAttemptAt, nextAttemptAt)
                .SetProperty(value => value.LeaseUntil, (DateTime?)null)
                .SetProperty(value => value.LeaseToken, (Guid?)null), cancellationToken)
            : await claimQuery.ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.Status, status)
                .SetProperty(value => value.Detail, detail)
                .SetProperty(value => value.ProcessedAt, now)
                .SetProperty(value => value.NextAttemptAt, nextAttemptAt)
                .SetProperty(value => value.LeaseUntil, (DateTime?)null)
                .SetProperty(value => value.LeaseToken, (Guid?)null)
                .SetProperty(value => value.ShopifyOrderId, shopifyOrderId), cancellationToken);
        return rows == 1;
    }

    private async Task LockShopifyOperationAsync(Guid operationId, CancellationToken cancellationToken)
    {
        if (!_operations.Database.IsRelational()) return;
        await _operations.Database.ExecuteSqlInterpolatedAsync(
            $"select id from operations.operation_logs where id = {operationId} for update", cancellationToken);
    }

    private static TimeSpan GetRetryDelay(int attempt)
    {
        var baseDelay = attempt switch
        {
            <= 1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(30),
            4 => TimeSpan.FromMinutes(120),
            _ => TimeSpan.FromMinutes(480)
        };
        return TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * (0.8 + Random.Shared.NextDouble() * 0.4));
    }

    private static DateTime? ParseTriggeredAt(string? value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed.UtcDateTime, DateTimeKind.Unspecified)
            : null;

    private static DateTime? ParseShopifyTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed.UtcDateTime, DateTimeKind.Unspecified)
            : null;

    private static decimal? ReadRefundAmount(JsonElement root)
    {
        var total = ReadShopMoneyAmount(root, "total_refunded_set");
        if (total.HasValue) return total;
        var transactions = ReadArray(root, "transactions");
        return transactions.Count == 0 ? null : transactions.Sum(value => ReadDecimal(value, "amount"));
    }

    private static string? ReadMoneyBagCurrency(JsonElement root, string property)
    {
        var bag = TryGetObject(root, property);
        var shopMoney = bag.HasValue ? TryGetObject(bag.Value, "shop_money") : null;
        return ReadValue(shopMoney, "currency_code");
    }

    private static decimal? ReadShopMoneyAmount(JsonElement root, string property)
    {
        var bag = TryGetObject(root, property);
        var shopMoney = bag.HasValue ? TryGetObject(bag.Value, "shop_money") : null;
        var amount = ReadValue(shopMoney, "amount");
        return decimal.TryParse(amount, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CreateDeliveryKey(string shopDomain, string topic, string deliveryId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{shopDomain}\n{topic}\n{deliveryId}"))).ToLowerInvariant();

    private static bool IsValidLegacyPathSecret(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length is >= 32 and <= 128 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private bool IsCod(JsonElement root)
    {
        var gateways = ReadArray(root, "payment_gateway_names").Select(value => value.GetString() ?? string.Empty);
        return gateways.Any(gateway => _options.CodGatewayNames.Any(value => string.Equals(value, gateway, StringComparison.OrdinalIgnoreCase)));
    }

    private static List<JsonElement> ReadArray(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToList() : [];

    private static JsonElement? TryGetObject(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;

    private static string? ReadValue(JsonElement? root, string property) => root.HasValue ? ReadValue(root.Value, property) : null;

    private static string? ReadValue(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
            ? value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : value.ToString()
            : null;

    private static ShopifyOrderLineInput ReadOrderLine(JsonElement item, int position, IReadOnlyDictionary<string, ShopifyCommercialLine> commercialLines)
    {
        var rawSku = item.TryGetProperty("sku", out var skuValue) && skuValue.ValueKind == JsonValueKind.String ? skuValue.GetString() : ReadValue(item, "sku");
        var lineId = ReadValue(item, "id");
        var hasCommercialLine = commercialLines.TryGetValue(lineId ?? string.Empty, out var commercial);
        var unitPrice = hasCommercialLine && commercial!.Quantity != 0 ? commercial.NetLineTotal / commercial.Quantity : 0m;
        return new ShopifyOrderLineInput(
            position,
            lineId,
            string.IsNullOrWhiteSpace(rawSku) ? null : rawSku,
            ReadValue(item, "variant_id"),
            ReadValue(item, "title") ?? ReadValue(item, "name"),
            ReadValue(item, "variant_title"),
            commercial?.Quantity ?? ReadInt(item, "quantity"),
            hasCommercialLine ? unitPrice : 0,
            hasCommercialLine,
            ReadPublicLineProperties(item),
            commercial!);
    }

    private static string? ReadPublicLineProperties(JsonElement item)
    {
        if (!item.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Array) return null;
        var values = properties.EnumerateArray()
            .Where(value => value.ValueKind == JsonValueKind.Object)
            .Select(value => new ShopifyLineProperty(Limit(ReadValue(value, "name"), 100), Limit(ReadValue(value, "value"), 500)))
            .Where(value => !string.IsNullOrWhiteSpace(value.Name) && !value.Name.StartsWith("_", StringComparison.Ordinal))
            .Take(20)
            .ToList();
        return values.Count == 0 ? null : JsonSerializer.Serialize(values);
    }

    private static string NormalizeSkuKey(string value) => value.Trim().ToUpperInvariant();

    private static string? Limit(string? value, int length)
    {
        var trimmed = TrimToNull(value);
        return trimmed is null ? null : trimmed.Length <= length ? trimmed : trimmed[..length];
    }

    private static string? LimitRaw(string? value, int length) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= length ? value : value[..length];

    private static string RequireValue(JsonElement root, string property, string error) => ReadValue(root, property) ?? throw new ShopifyBusinessException(error);

    private static int ReadInt(JsonElement root, string property) => int.TryParse(ReadValue(root, property), out var value) ? value : 0;

    private static decimal ReadDecimal(JsonElement root, string property) => decimal.TryParse(ReadValue(root, property), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static string? NormalizeEmail(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = new string(value.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? FirstPhone(IEnumerable<string>? values) => values?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string BuildCustomerName(JsonElement root, JsonElement? customer)
    {
        var first = ReadValue(TryGetObject(root, "billing_address"), "first_name");
        first ??= customer.HasValue ? ReadValue(customer.Value, "first_name") : null;
        var last = ReadValue(TryGetObject(root, "billing_address"), "last_name") ?? (customer.HasValue ? ReadValue(customer.Value, "last_name") : null);
        var name = string.Join(" ", new[] { first, last }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return !string.IsNullOrWhiteSpace(name) ? name : NormalizeEmail(ReadValue(root, "email")) ?? $"Shopify guest {ReadValue(root, "id")}";
    }

    private static string? ReadAddress(JsonElement root)
    {
        var address = TryGetObject(root, "shipping_address");
        if (!address.HasValue) return null;
        var values = new[] { "address1", "address2", "city", "province", "zip", "country" }
            .Select(key => ReadValue(address.Value, key)).Where(value => !string.IsNullOrWhiteSpace(value));
        var result = string.Join(", ", values);
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private void AddAuditVersion(OperationLog operation, string reason)
    {
        var versionNumber = operation.OperationVersions.DefaultIfEmpty().Max(value => value?.VersionNumber ?? 0) + 1;
        var version = new OperationVersion
        {
            Id = Guid.NewGuid(),
            OperationId = operation.Id,
            VersionNumber = versionNumber,
            Reason = reason,
            SnapshotData = JsonSerializer.Serialize(new { operation.OperationType, operation.Status, operation.SalesChannel }),
            EditedBy = Guid.Empty,
            EditedActorName = Shopify,
            EditedAt = _clock.EgyptNow
        };
        operation.OperationVersions.Add(version);
        operation.CurrentVersionId = version.Id;
        _operations.OperationVersions.Add(version);
    }

    private sealed record ShopifyOrderLineInput(int Position, string? LineItemId, string? RawSku, string? VariantId, string? Title, string? VariantTitle, int Quantity, decimal UnitPrice, bool HasValidPrice, string? PublicPropertiesSnapshot, ShopifyCommercialLine Commercial);
    private sealed record ResolvedShopifyOrderLine(ShopifyOrderLineInput Line, Sku Sku, string Mode);
    private sealed record ShopifyLineProperty(string? Name, string? Value);

    private sealed class ShopifyBusinessException : Exception
    {
        public ShopifyBusinessException(string message) : base(message) { }
    }
}
