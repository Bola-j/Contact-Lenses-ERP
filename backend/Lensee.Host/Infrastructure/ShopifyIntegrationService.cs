using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Inventory.Services;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Operations.Data;
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
    public string? StoreDomain { get; init; }
    public Guid OnlineLocationId { get; init; }
    public string[] CodGatewayNames { get; init; } = [];
    public int MaxBodyBytes { get; init; } = 262_144;
    public int PayloadRetentionDays { get; init; } = 30;
}

public sealed record ShopifyWebhookResult(int StatusCode, string Status, string? Detail = null, Guid? OperationId = null);
public sealed record ShopifyWebhookEnvelope(string WebhookId, string Topic, string? ShopDomain, string? EventId, string? ApiVersion, string? TriggeredAt);

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
    private const string CashTransaction = "CashTransaction";

    private readonly ShopifyOptions _options;
    private readonly OperationsDbContext _operations;
    private readonly CrmDbContext _crm;
    private readonly CatalogDbContext _catalog;
    private readonly InventoryDbContext _inventory;
    private readonly NotificationsDbContext _notifications;
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
        _ledger = ledger;
        _clock = clock;
        _payloadProtector = dataProtectionProvider.CreateProtector("Lensee.Shopify.WebhookPayload.v1");
        _auditLogWriter = auditLogWriter;
    }

    public bool IsConfigured =>
        _options.Enabled &&
        !string.IsNullOrWhiteSpace(_options.WebhookSecret) &&
        !string.IsNullOrWhiteSpace(_options.StoreDomain) &&
        _options.OnlineLocationId != Guid.Empty;

    public int MaxBodyBytes => Math.Max(1, _options.MaxBodyBytes);

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

    public async Task<ShopifyWebhookResult> ReceiveAsync(ShopifyWebhookEnvelope envelope, byte[] body, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return new ShopifyWebhookResult(StatusCodes.Status503ServiceUnavailable, "Disabled", "Shopify integration is not fully configured.");
        }
        if (string.IsNullOrWhiteSpace(envelope.WebhookId) || string.IsNullOrWhiteSpace(envelope.Topic) || string.IsNullOrWhiteSpace(envelope.ShopDomain))
        {
            return new ShopifyWebhookResult(StatusCodes.Status400BadRequest, "Invalid", "Shopify webhook ID, topic, and shop domain are required.");
        }
        if (body.Length > _options.MaxBodyBytes)
        {
            return new ShopifyWebhookResult(StatusCodes.Status413PayloadTooLarge, "Rejected", "Webhook body exceeds the configured limit.");
        }
        if (!string.Equals(envelope.ShopDomain.Trim(), _options.StoreDomain!.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new ShopifyWebhookResult(StatusCodes.Status403Forbidden, "Rejected", "Shop domain is not configured for this ERP integration.");
        }

        var existing = await _operations.ShopifyWebhookEvents
            .FirstOrDefaultAsync(value => value.WebhookId == envelope.WebhookId.Trim(), cancellationToken);
        if (existing is not null)
        {
            return new ShopifyWebhookResult(StatusCodes.Status200OK, "Duplicate", existing.Detail, existing.OperationId);
        }

        var now = _clock.EgyptNow;
        var topic = envelope.Topic.Trim().ToLowerInvariant();
        var status = topic is "orders/create" or "orders/cancelled" or "refunds/create" ? "Queued" : "Ignored";
        var eventRecord = new ShopifyWebhookEvent
        {
            Id = Guid.NewGuid(),
            WebhookId = envelope.WebhookId.Trim(),
            Topic = topic,
            ShopDomain = envelope.ShopDomain.Trim().ToLowerInvariant(),
            EventId = TrimToNull(envelope.EventId),
            ApiVersion = TrimToNull(envelope.ApiVersion),
            PayloadHash = Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant(),
            ProtectedPayload = _payloadProtector.Protect(Convert.ToBase64String(body)),
            Status = status,
            Detail = status == "Ignored" ? $"Unsupported Shopify topic '{envelope.Topic}'." : null,
            ReceivedAt = now,
            VerifiedAt = now,
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
            return new ShopifyWebhookResult(StatusCodes.Status200OK, "Duplicate", "Webhook was already accepted.");
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

        JsonDocument document;
        try
        {
            var raw = Convert.FromBase64String(_payloadProtector.Unprotect(eventRecord.ProtectedPayload));
            document = JsonDocument.Parse(raw);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            await MarkRequiresAttentionAsync(eventRecord, "Webhook payload cannot be decrypted or parsed.", cancellationToken);
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            var orderId = ReadValue(root, "id") ?? ReadValue(root, "order_id");
            eventRecord.ShopifyOrderId = orderId;
            try
            {
                ShopifyWebhookResult? result = null;
                await SharedDbTransaction.ExecuteAsync(_operations, async () =>
                {
                    result = eventRecord.Topic switch
                    {
                        "orders/create" => await CreateOrderAsync(root, eventRecord, cancellationToken),
                        "orders/cancelled" => await CancelOrderAsync(root, eventRecord, cancellationToken),
                        "refunds/create" => await RegisterRefundExceptionAsync(root, eventRecord, cancellationToken),
                        _ => new ShopifyWebhookResult(StatusCodes.Status202Accepted, "Ignored", $"Unsupported Shopify topic '{eventRecord.Topic}'.")
                    };
                    eventRecord.Status = result.Status;
                    eventRecord.Detail = result.Detail;
                    eventRecord.OperationId = result.OperationId;
                    eventRecord.ProcessedAt = _clock.EgyptNow;
                    eventRecord.LeaseUntil = null;
                    await _operations.SaveChangesAsync(cancellationToken);
                }, cancellationToken, _crm, _inventory, _notifications);
                var completedResult = result ?? throw new InvalidOperationException("Shopify event processing did not produce an outcome.");
                if (completedResult.OperationId.HasValue)
                {
                    await _auditLogWriter.WriteSystemAsync(Shopify, "ShopifyWebhookEvent", eventRecord.Id, completedResult.Status, new { eventRecord.Topic, eventRecord.ShopifyOrderId, completedResult.OperationId }, cancellationToken: cancellationToken);
                }
            }
            catch (ShopifyBusinessException exception)
            {
                eventRecord.Status = "RequiresAttention";
                eventRecord.Detail = exception.Message;
                eventRecord.ProcessedAt = _clock.EgyptNow;
                eventRecord.LeaseUntil = null;
                await _operations.SaveChangesAsync(cancellationToken);
                await CreateExceptionNotificationAsync("ShopifyOrderException", exception.Message, orderId ?? "unknown", null, cancellationToken);
            }
            catch (Exception)
            {
                eventRecord.Status = eventRecord.AttemptCount >= 5 ? "RequiresAttention" : "Retrying";
                eventRecord.Detail = eventRecord.Status == "RequiresAttention" ? "Automatic processing exhausted; review the integration event." : "Transient processing failure; retry scheduled.";
                eventRecord.NextAttemptAt = eventRecord.Status == "Retrying" ? _clock.EgyptNow.Add(GetRetryDelay(eventRecord.AttemptCount)) : null;
                eventRecord.LeaseUntil = null;
                await _operations.SaveChangesAsync(cancellationToken);
                if (eventRecord.Status == "RequiresAttention")
                {
                    await CreateExceptionNotificationAsync("ShopifyOrderException", eventRecord.Detail, orderId ?? "unknown", eventRecord.OperationId, cancellationToken);
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
                .Where(value => (value.Status == "Queued" || value.Status == "Retrying") && value.NextAttemptAt <= now && (value.LeaseUntil == null || value.LeaseUntil < now))
                .OrderBy(value => value.ReceivedAt)
                .Select(value => value.Id)
                .Take(50)
                .ToListAsync(cancellationToken);
            var claimed = new List<Guid>();
            foreach (var id in candidateIds)
            {
                var updated = await _operations.ShopifyWebhookEvents
                    .Where(value => value.Id == id && (value.Status == "Queued" || value.Status == "Retrying") && value.NextAttemptAt <= now && (value.LeaseUntil == null || value.LeaseUntil < now))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(value => value.Status, "Processing")
                        .SetProperty(value => value.AttemptCount, value => value.AttemptCount + 1)
                        .SetProperty(value => value.LeaseUntil, now.AddMinutes(2)), cancellationToken);
                if (updated == 1)
                {
                    claimed.Add(id);
                    if (claimed.Count == 10) break;
                }
            }
            return claimed;
        }

        var rows = await _operations.ShopifyWebhookEvents
            .Where(value => (value.Status == "Queued" || value.Status == "Retrying") && value.NextAttemptAt <= now && (value.LeaseUntil == null || value.LeaseUntil < now))
            .OrderBy(value => value.ReceivedAt)
            .Take(10)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.Status = "Processing";
            row.AttemptCount++;
            row.LeaseUntil = now.AddMinutes(2);
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
            .Where(value => value.ProtectedPayload != null && value.ReceivedAt < cutoff)
            .ToListAsync(cancellationToken);
        foreach (var row in rows) row.ProtectedPayload = null;
        if (rows.Count > 0) await _operations.SaveChangesAsync(cancellationToken);
    }

    private async Task<ShopifyWebhookResult> CreateOrderAsync(JsonElement root, ShopifyWebhookEvent eventRecord, CancellationToken cancellationToken)
    {
        var orderId = RequireValue(root, "id", "Shopify order ID is required.");
        var existing = await _operations.ShopifyOrderLinks
            .FirstOrDefaultAsync(value => value.ShopifyOrderId == orderId, cancellationToken);
        if (existing is not null)
        {
            return new ShopifyWebhookResult(StatusCodes.Status200OK, "Duplicate", "Shopify order was already imported.", existing.OperationId);
        }

        var location = await _inventory.Locations
            .FirstOrDefaultAsync(value => value.Id == _options.OnlineLocationId && value.IsActive && value.LocationType == "Online", cancellationToken);
        if (location is null)
        {
            throw new ShopifyBusinessException("Configured Shopify Online location is missing, inactive, or not an Online location.");
        }

        var lineItems = ReadArray(root, "line_items");
        if (lineItems.Count == 0)
        {
            throw new ShopifyBusinessException($"Shopify order {orderId} has no line items.");
        }

        var variantIds = lineItems.Select(item => ReadValue(item, "variant_id")).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().Distinct().ToArray();
        if (variantIds.Length != lineItems.Count)
        {
            throw new ShopifyBusinessException($"Shopify order {orderId} contains a line without a variant ID.");
        }

        var mappings = await _operations.ShopifyVariantMappings
            .Where(value => value.IsActive && variantIds.Contains(value.ShopifyVariantId))
            .ToDictionaryAsync(value => value.ShopifyVariantId, cancellationToken);
        var missingVariants = variantIds.Where(value => !mappings.ContainsKey(value)).ToArray();
        if (missingVariants.Length > 0)
        {
            throw new ShopifyBusinessException($"Shopify order {orderId} has unmapped variant IDs: {string.Join(", ", missingVariants)}.");
        }

        var skuIds = mappings.Values.Select(value => value.SkuId).Distinct().ToArray();
        var skus = await _catalog.Skus.Include(value => value.Product)
            .Where(value => skuIds.Contains(value.Id) && value.IsActive && value.DeletedAt == null && value.Product.IsActive && value.Product.DeletedAt == null)
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        if (skus.Count != skuIds.Length)
        {
            throw new ShopifyBusinessException($"Shopify order {orderId} references an inactive or missing Lensee SKU.");
        }

        var customer = await ResolveCustomerAsync(root, orderId, cancellationToken);
        if (_crm.Entry(customer).State == EntityState.Detached)
        {
            _crm.Merchants.Add(customer);
        }
        var now = _clock.EgyptNow;
        var operation = new OperationLog
        {
            Id = Guid.NewGuid(),
            OperationNumber = $"OP-{now:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}",
            OperationType = RetailSale,
            Status = Draft,
            SalesChannel = Shopify,
            SourceLocationId = location.Id,
            ClientId = customer.Id,
            ClientName = customer.BusinessName,
            BuyerPhone = FirstPhone(customer.PhoneNumbers),
            BuyerEmail = customer.Email,
            ShippingAddress = ReadAddress(root),
            PaymentMethod = IsCod(root) ? CashHandToHand : CashTransaction,
            Notes = $"Imported from Shopify order {ReadValue(root, "name") ?? orderId}.",
            CreatedBy = Guid.Empty,
            CreatedActorName = Shopify,
            CreatedAt = now,
            CurrentVersionId = null
        };

        foreach (var item in lineItems)
        {
            var variantId = ReadValue(item, "variant_id")!;
            var mapping = mappings[variantId];
            var sku = skus[mapping.SkuId];
            var quantity = ReadInt(item, "quantity");
            if (quantity <= 0)
            {
                throw new ShopifyBusinessException($"Shopify order {orderId} has a non-positive quantity for variant {variantId}.");
            }
            var unitPrice = ReadDecimal(item, "price");
            operation.OperationLines.Add(new OperationLine
            {
                Id = Guid.NewGuid(),
                OperationId = operation.Id,
                SkuId = sku.Id,
                ProductNameSnapshot = sku.Product.Name,
                SkuCodeSnapshot = sku.SkuCode,
                MerchantNameSnapshot = customer.BusinessName,
                Section = "Standard",
                Quantity = quantity,
                EntryMode = mapping.EntryMode == "Pieces" ? "Pieces" : "Packs",
                BonusQuantity = 0,
                UnitPrice = unitPrice,
                LineTotal = unitPrice * quantity,
                LotNumber = null,
                ExpiryDate = null,
                LineNotes = $"Shopify variant {variantId}"
            });
        }

        _operations.OperationLogs.Add(operation);
        var initialVersion = new OperationVersion
        {
            Id = Guid.NewGuid(),
            OperationId = operation.Id,
            VersionNumber = 1,
            Reason = "Created from Shopify webhook",
            SnapshotData = JsonSerializer.Serialize(new { operation.OperationType, operation.Status, operation.SalesChannel, ShopifyOrderId = orderId }),
            EditedBy = Guid.Empty,
            EditedActorName = Shopify,
            EditedAt = now
        };
        operation.CurrentVersionId = initialVersion.Id;
        operation.OperationVersions.Add(initialVersion);
        _operations.OperationVersions.Add(initialVersion);
        _operations.ShopifyOrderLinks.Add(new ShopifyOrderLink
        {
            OperationId = operation.Id,
            ShopifyOrderId = orderId,
            ShopifyOrderNumber = ReadValue(root, "name") ?? ReadValue(root, "order_number"),
            PaymentReference = ReadValue(root, "payment_id") ?? ReadValue(root, "checkout_id"),
            CreatedAt = now,
            UpdatedAt = now
        });
        await _crm.SaveChangesAsync(cancellationToken);
        await _operations.SaveChangesAsync(cancellationToken);
        return new ShopifyWebhookResult(StatusCodes.Status201Created, "Imported", "Shopify order was imported as an unallocated draft.", operation.Id);
    }

    private async Task<ShopifyWebhookResult> CancelOrderAsync(JsonElement root, ShopifyWebhookEvent eventRecord, CancellationToken cancellationToken)
    {
        var orderId = RequireValue(root, "id", "Shopify order ID is required.");
        var link = await _operations.ShopifyOrderLinks.Include(value => value.Operation).ThenInclude(value => value.OperationLines)
            .FirstOrDefaultAsync(value => value.ShopifyOrderId == orderId, cancellationToken);
        if (link is null)
        {
            throw new ShopifyBusinessException($"Cancelled Shopify order {orderId} has no ERP operation.");
        }

        var operation = link.Operation;
        if (operation.Status == Draft)
        {
            operation.Status = Cancelled;
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
            operation.Status = Cancelled;
            AddAuditVersion(operation, "Cancelled from Shopify webhook and released reservations");
            link.UpdatedAt = _clock.EgyptNow;
            eventRecord.OperationId = operation.Id;
            return new ShopifyWebhookResult(StatusCodes.Status200OK, "Cancelled", "Reserved Shopify operation was cancelled and stock released.", operation.Id);
        }

        if (operation.Status is Shipped or Completed)
        {
            await CreateExceptionNotificationAsync("ShopifyCancellationException", $"Shopify order {orderId} was cancelled after ERP operation {operation.OperationNumber} was {operation.Status}. Review stock and finance manually.", orderId, operation.Id, cancellationToken);
            return new ShopifyWebhookResult(StatusCodes.Status202Accepted, "RequiresAttention", "Cancellation requires manual stock and finance review.", operation.Id);
        }

        return new ShopifyWebhookResult(StatusCodes.Status200OK, "Ignored", $"Operation is already {operation.Status}.", operation.Id);
    }

    private async Task<ShopifyWebhookResult> RegisterRefundExceptionAsync(JsonElement root, ShopifyWebhookEvent eventRecord, CancellationToken cancellationToken)
    {
        var orderId = RequireValue(root, "order_id", "Shopify refund order ID is required.");
        var link = await _operations.ShopifyOrderLinks.FirstOrDefaultAsync(value => value.ShopifyOrderId == orderId, cancellationToken);
        await CreateExceptionNotificationAsync("ShopifyRefundException", $"Shopify refund received for order {orderId}. Review stock return and financial adjustment manually.", orderId, link?.OperationId, cancellationToken);
        return new ShopifyWebhookResult(StatusCodes.Status202Accepted, "RequiresAttention", "Refund requires manual stock and finance review.", link?.OperationId);
    }

    private async Task<Merchant> ResolveCustomerAsync(JsonElement root, string orderId, CancellationToken cancellationToken)
    {
        var customer = TryGetObject(root, "customer");
        var externalId = customer.HasValue ? ReadValue(customer.Value, "id") : null;
        var email = NormalizeEmail(ReadValue(root, "email") ?? (customer.HasValue ? ReadValue(customer.Value, "email") : null));
        var phone = NormalizePhone(ReadValue(root, "phone") ?? (customer.HasValue ? ReadValue(customer.Value, "phone") : null) ?? ReadValue(TryGetObject(root, "shipping_address"), "phone"));
        var name = BuildCustomerName(root, customer);

        if (!string.IsNullOrWhiteSpace(externalId))
        {
            var byExternalId = await _crm.Merchants.FirstOrDefaultAsync(value => !value.IsDeleted && value.ExternalProvider == Shopify && value.ExternalCustomerId == externalId, cancellationToken);
            if (byExternalId is not null)
            {
                return byExternalId;
            }
        }

        var candidates = await _crm.Merchants.Where(value => !value.IsDeleted && value.ExternalProvider == Shopify).ToListAsync(cancellationToken);
        var byEmail = !string.IsNullOrWhiteSpace(email)
            ? candidates.FirstOrDefault(value => NormalizeEmail(value.Email) == email)
            : null;
        var byPhone = !string.IsNullOrWhiteSpace(phone)
            ? candidates.FirstOrDefault(value => value.PhoneNumbers.Any(number => NormalizePhone(number) == phone))
            : null;
        if (byEmail is not null && byPhone is not null && byEmail.Id != byPhone.Id)
        {
            throw new ShopifyBusinessException($"Shopify order {orderId} email and phone match different CRM customers.");
        }
        var matched = byEmail ?? byPhone;
        if (matched is not null)
        {
            if (!string.IsNullOrWhiteSpace(externalId) && string.IsNullOrWhiteSpace(matched.ExternalCustomerId))
            {
                matched.ExternalCustomerId = externalId;
                matched.ExternalProvider = Shopify;
                matched.UpdatedAt = _clock.EgyptNow;
            }
            return matched;
        }

        return new Merchant
        {
            Id = Guid.NewGuid(),
            BusinessName = name,
            ContactPersonName = name,
            PhoneNumbers = string.IsNullOrWhiteSpace(phone) ? [] : [phone],
            Email = email,
            Address = ReadAddress(root),
            BusinessType = "Other",
            Status = "Active",
            Notes = "Created from Shopify order.",
            ExternalProvider = Shopify,
            ExternalCustomerId = externalId,
            CreatedAt = _clock.EgyptNow,
            UpdatedAt = _clock.EgyptNow
        };
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

    private async Task MarkRequiresAttentionAsync(ShopifyWebhookEvent eventRecord, string detail, CancellationToken cancellationToken)
    {
        eventRecord.Status = "RequiresAttention";
        eventRecord.Detail = detail;
        eventRecord.ProcessedAt = _clock.EgyptNow;
        eventRecord.LeaseUntil = null;
        await _operations.SaveChangesAsync(cancellationToken);
        await CreateExceptionNotificationAsync("ShopifyOrderException", detail, eventRecord.ShopifyOrderId ?? "unknown", eventRecord.OperationId, cancellationToken);
    }

    private static TimeSpan GetRetryDelay(int attempt) => attempt switch
    {
        <= 1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(30),
        4 => TimeSpan.FromMinutes(120),
        _ => TimeSpan.FromMinutes(480)
    };

    private static DateTime? ParseTriggeredAt(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed.UtcDateTime : null;

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

    private sealed class ShopifyBusinessException : Exception
    {
        public ShopifyBusinessException(string message) : base(message) { }
    }
}
