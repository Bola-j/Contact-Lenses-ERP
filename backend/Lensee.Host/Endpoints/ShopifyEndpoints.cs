using Lensee.Host.Infrastructure;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Catalog.Services;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static class ShopifyEndpoints
{
    public static RouteGroupBuilder MapShopifyEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/integrations/shopify").WithTags("Shopify");
        group.MapPost("/webhooks", ReceiveWebhookAsync).AllowAnonymous().RequireRateLimiting("shopify-webhooks");
        group.MapPost("/legacy-webhooks/{pathSecret}", ReceiveLegacyWebhookAsync).AllowAnonymous().RequireRateLimiting("shopify-legacy-webhooks");
        group.MapGet("/status", GetStatusAsync).RequireAuthorization("integrations.shopify.read");
        group.MapGet("/events", ListEventsAsync).RequireAuthorization("integrations.shopify.read");
        group.MapPost("/events/{id:guid}/retry", RetryEventAsync).RequireAuthorization("integrations.shopify.manage");
        group.MapPost("/events/{id:guid}/refresh", RefreshEventAsync).RequireAuthorization("integrations.shopify.manage");
        group.MapPost("/events/{id:guid}/resolve", ResolveEventAsync).RequireAuthorization("integrations.shopify.manage");
        group.MapGet("/sku-readiness", ListSkuReadinessAsync).RequireAuthorization("integrations.shopify.read");
        group.MapGet("/sku-readiness/products", ListSkuReadinessProductsAsync).RequireAuthorization("integrations.shopify.read");
        group.MapGet("/variant-links", ListVariantLinksAsync).RequireAuthorization("integrations.shopify.read");
        group.MapPut("/variant-links/{id:guid}", UpdateVariantLinkAsync).RequireAuthorization("integrations.shopify.manage");
        group.MapGet("/queue-health", GetQueueHealthAsync).RequireAuthorization("integrations.shopify.read");
        return group;
    }

    private static async Task<IResult> ListVariantLinksAsync(int? page, int? pageSize, string? status, string? search,
        OperationsDbContext operations, CatalogDbContext catalog, CancellationToken cancellationToken)
    {
        var request = new PageRequest(page ?? 1, pageSize ?? 25);
        var rows = await operations.ShopifyVariantMappings.AsNoTracking().OrderBy(value => value.ShopDomain).ThenBy(value => value.ShopifySku)
            .ThenBy(value => value.ShopifyVariantId).ToListAsync(cancellationToken);
        var skuIds = rows.Where(value => value.SkuId.HasValue).Select(value => value.SkuId!.Value).Distinct().ToArray();
        var skus = skuIds.Length == 0 ? [] : await catalog.Skus.AsNoTracking().Where(value => skuIds.Contains(value.Id))
            .Select(value => new { value.Id, value.SkuCode }).ToDictionaryAsync(value => value.Id, cancellationToken);
        var projected = rows.Select(value => new ShopifyVariantLinkResponse(value.Id, value.ShopDomain, value.ShopifyVariantId,
            value.ShopifySku, value.SkuId, value.SkuId.HasValue && skus.TryGetValue(value.SkuId.Value, out var sku) ? sku.SkuCode : null,
            value.Status, value.Detail, value.UpdatedAt, value.Revision)).ToList();
        if (!string.IsNullOrWhiteSpace(status)) projected = projected.Where(value => string.Equals(value.Status, status.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            projected = projected.Where(value => value.ShopDomain.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                value.ShopifyVariantId.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                (value.ShopifySku?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (value.SkuCode?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        }
        var total = projected.Count;
        return Results.Ok(new PagedResult<ShopifyVariantLinkResponse>(projected.Skip(request.Skip).Take(request.PageSize).ToList(), request.Page, request.PageSize, total));
    }

    private static async Task<IResult> UpdateVariantLinkAsync(Guid id, ShopifyVariantLinkUpdateRequest request,
        ShopifyIntegrationService integration, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var result = await integration.UpdateVariantLinkAsync(id, request.SkuId, request.Revision, request.Reason,
            currentUser.UserId ?? Guid.Empty, currentUser.Principal.Identity?.Name ?? "ERP user", cancellationToken);
        return result.Status switch
        {
            "NotFound" => Results.NotFound(),
            "Conflict" => Results.Conflict(new { code = "shopify-variant-link-revision-conflict", detail = result.Detail }),
            "Validation" => Results.ValidationProblem(new Dictionary<string, string[]> { [result.Field!] = [result.Detail!] }),
            _ => Results.Ok(result.Link)
        };
    }

    private static async Task<IResult> GetQueueHealthAsync(OperationsDbContext operations, IClock clock, CancellationToken cancellationToken)
    {
        var now = clock.EgyptNow;
        var events = operations.ShopifyWebhookEvents.AsNoTracking();
        var queued = await events.CountAsync(value => value.Status == "Queued", cancellationToken);
        var processing = await events.CountAsync(value => value.Status == "Processing", cancellationToken);
        var retrying = await events.CountAsync(value => value.Status == "Retrying", cancellationToken);
        var attention = await events.CountAsync(value => value.Status == "RequiresAttention" || value.Status == "ReviewRequired", cancellationToken);
        var oldest = await events.Where(value => value.Status == "Queued" || value.Status == "Processing" || value.Status == "Retrying")
            .OrderBy(value => value.ReceivedAt).Select(value => (DateTime?)value.ReceivedAt).FirstOrDefaultAsync(cancellationToken);
        var expired = await events.CountAsync(value => value.Status == "Processing" && value.LeaseUntil < now, cancellationToken);
        var unpaidReservationLinks = operations.ShopifyOrderLinks.AsNoTracking()
            .Where(value => value.Operation.Status == "Reserved" && (!value.FinancialStatusTrusted || value.FinancialStatus != "paid" || value.RequiresReview));
        var unpaidReservations = await unpaidReservationLinks.CountAsync(cancellationToken);
        var oldestUnpaidReservationAt = await unpaidReservationLinks.Where(value => value.Operation.ConfirmedAt != null)
            .OrderBy(value => value.Operation.ConfirmedAt)
            .Select(value => value.Operation.ConfirmedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return Results.Ok(new ShopifyQueueHealthResponse(queued, processing, retrying, attention, oldest, expired,
            unpaidReservations, oldestUnpaidReservationAt));
    }

    private static async Task<IResult> ReceiveWebhookAsync(HttpRequest request, ShopifyIntegrationService integration, CancellationToken cancellationToken)
    {
        if (!integration.IsConfigured)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Shopify integration is not configured.");
        }
        if (request.ContentLength is > 0 && request.ContentLength > integration.MaxBodyBytes)
        {
            return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Webhook body is too large.");
        }
        var body = await ReadBodyWithinLimitAsync(request.Body, integration.MaxBodyBytes, cancellationToken);
        if (body is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Webhook body is too large.");
        }
        var signature = request.Headers["X-Shopify-Hmac-Sha256"].FirstOrDefault();
        if (!integration.VerifySignature(body, signature))
        {
            return Results.Unauthorized();
        }

        var result = await integration.ReceiveAsync(new ShopifyWebhookEnvelope(
            request.Headers["X-Shopify-Webhook-Id"].FirstOrDefault() ?? string.Empty,
            request.Headers["X-Shopify-Topic"].FirstOrDefault() ?? string.Empty,
            request.Headers["X-Shopify-Shop-Domain"].FirstOrDefault(),
            request.Headers["X-Shopify-Event-Id"].FirstOrDefault(),
            request.Headers["X-Shopify-API-Version"].FirstOrDefault(),
            request.Headers["X-Shopify-Triggered-At"].FirstOrDefault(), "Hmac"), body, cancellationToken);
        return Results.Json(new ShopifyWebhookResponse(result.Status, result.Detail, result.OperationId), statusCode: result.StatusCode);
    }

    private static async Task<IResult> ReceiveLegacyWebhookAsync(string pathSecret, HttpRequest request, ShopifyIntegrationService integration, CancellationToken cancellationToken)
    {
        if (!integration.IsLegacyWebhookConfigured)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Temporary Shopify legacy webhook intake is not configured.");
        }
        if (!integration.VerifyLegacyPathSecret(pathSecret)) return Results.NotFound();
        if (request.ContentLength is > 0 && request.ContentLength > integration.MaxBodyBytes)
        {
            return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Webhook body is too large.");
        }
        var body = await ReadBodyWithinLimitAsync(request.Body, integration.MaxBodyBytes, cancellationToken);
        if (body is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Webhook body is too large.");
        }
        var topic = request.Headers["X-Shopify-Topic"].FirstOrDefault() ?? string.Empty;
        var receivedWebhookId = request.Headers["X-Shopify-Webhook-Id"].FirstOrDefault();
        var webhookId = string.IsNullOrWhiteSpace(receivedWebhookId)
            ? $"legacy:{topic}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)).ToLowerInvariant()}"
            : receivedWebhookId;
        var result = await integration.ReceiveAsync(new ShopifyWebhookEnvelope(
            webhookId,
            topic,
            request.Headers["X-Shopify-Shop-Domain"].FirstOrDefault(),
            request.Headers["X-Shopify-Event-Id"].FirstOrDefault(),
            request.Headers["X-Shopify-API-Version"].FirstOrDefault(),
            request.Headers["X-Shopify-Triggered-At"].FirstOrDefault(), "LegacyPath"), body, cancellationToken);
        return Results.Json(new ShopifyWebhookResponse(result.Status, result.Detail, result.OperationId), statusCode: result.StatusCode);
    }

    private static async Task<byte[]?> ReadBodyWithinLimitAsync(Stream body, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var stream = new MemoryStream(Math.Min(maximumBytes, 81920));
        var buffer = new byte[Math.Min(maximumBytes + 1, 81920)];
        int read;
        while ((read = await body.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            if (stream.Length + read > maximumBytes) return null;
            await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        return stream.ToArray();
    }

    private static IResult GetStatusAsync(ShopifyIntegrationService integration) =>
        Results.Ok(new ShopifyIntegrationStatusResponse(integration.IsConfigured, integration.IsLegacyWebhookConfigured, integration.MaxBodyBytes));

    private static async Task<IResult> ListEventsAsync(int? page, int? pageSize, string? status, OperationsDbContext operations, CancellationToken cancellationToken)
    {
        var request = new PageRequest(page ?? 1, pageSize ?? 25);
        var query = operations.ShopifyWebhookEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(value => value.Status == status.Trim());
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(value => value.ReceivedAt).Skip(request.Skip).Take(request.PageSize)
            .Select(value => ToEventResponse(value)).ToListAsync(cancellationToken);
        return Results.Ok(new PagedResult<ShopifyWebhookEventResponse>(rows, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> RetryEventAsync(Guid id, ShopifyIntegrationService integration, CancellationToken cancellationToken)
    {
        var result = await integration.RetryEventAsync(id, cancellationToken);
        return result.Status switch
        {
            "NotFound" => Results.NotFound(),
            "PayloadExpired" => Results.ValidationProblem(new Dictionary<string, string[]> { ["event"] = ["The retained payload has expired and cannot be retried."] }),
            "Conflict" => Results.Conflict(new { message = "This Shopify event changed state and can no longer be retried." }),
            _ => Results.Ok(ToEventResponse(result.EventRecord!))
        };
    }

    private static async Task<IResult> RefreshEventAsync(Guid id, OperationsDbContext operations,
        ShopifyReconciliationService reconciliation, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var eventRecord = await operations.ShopifyWebhookEvents.AsNoTracking()
            .Where(value => value.Id == id)
            .Select(value => new { value.ShopDomain, value.ShopifyOrderId })
            .SingleOrDefaultAsync(cancellationToken);
        if (eventRecord is null) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(eventRecord.ShopifyOrderId))
            return Results.Conflict(new { message = "This event has no Shopify order identity to refresh." });

        try
        {
            var queued = await reconciliation.RefreshOrderAsync(eventRecord.ShopDomain, eventRecord.ShopifyOrderId, cancellationToken);
            return queued
                ? Results.Accepted(value: new { status = "RefreshQueued", message = "A fresh Shopify snapshot was durably queued; the original receipt was retained." })
                : Results.NotFound(new { message = "The Shopify order was not found or is outside the activation window." });
        }
        catch (ShopifyOrderRefreshException exception)
        {
            if (exception.RetryAfterSeconds is { } retryAfter)
                httpContext.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Results.Problem(statusCode: exception.StatusCode, detail: exception.Message);
        }
    }

    private static async Task<IResult> ResolveEventAsync(Guid id, ShopifyEventResolutionRequest request, ShopifyIntegrationService integration, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Note))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Note)] = ["Resolution note is required."] });
        }
        var result = await integration.ResolveEventAsync(id, currentUser.UserId, request.Note, cancellationToken);
        return result.Status switch
        {
            "NotFound" => Results.NotFound(),
            "Validation" => Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Note)] = ["Resolution note is required."] }),
            "Conflict" => Results.Conflict(new { message = "This Shopify event changed state and can no longer be resolved." }),
            _ => Results.Ok(ToEventResponse(result.EventRecord!))
        };
    }

    private static async Task<IResult> ListSkuReadinessAsync(int? page, int? pageSize, string? search, string? status, Guid? productId, string? wearCycle, CatalogDbContext catalog, CancellationToken cancellationToken)
    {
        var request = new PageRequest(page ?? 1, pageSize ?? 25);
        var query = catalog.Skus.AsNoTracking().Include(value => value.Product)
            .Where(value => value.IsActive && value.DeletedAt == null && value.Product.IsActive && value.Product.DeletedAt == null);
        if (productId.HasValue)
        {
            query = query.Where(value => value.ProductId == productId.Value);
        }
        if (!string.IsNullOrWhiteSpace(wearCycle))
        {
            query = query.Where(value => value.Product.OpenedExpiryRate == wearCycle.Trim());
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(value => value.SkuCode.Contains(term) || value.Product.Name.Contains(term));
        }

        var rows = await query.OrderBy(value => value.SkuCode).ToListAsync(cancellationToken);
        var readiness = rows.Select(ToSkuReadiness).ToList();
        if (!string.IsNullOrWhiteSpace(status)) readiness = readiness.Where(value => string.Equals(value.Status, status.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        var total = readiness.Count;
        return Results.Ok(new PagedResult<ShopifySkuReadinessResponse>(readiness.Skip(request.Skip).Take(request.PageSize).ToList(), request.Page, request.PageSize, total));
    }

    private static async Task<IResult> ListSkuReadinessProductsAsync(CatalogDbContext catalog, CancellationToken cancellationToken)
    {
        var products = await catalog.Skus.AsNoTracking()
            .Where(value => value.IsActive && value.DeletedAt == null && value.Product.IsActive && value.Product.DeletedAt == null)
            .GroupBy(value => new { value.ProductId, value.Product.Name })
            .OrderBy(group => group.Key.Name)
            .Select(group => new ShopifySkuProductFilterResponse(group.Key.ProductId, group.Key.Name))
            .ToListAsync(cancellationToken);

        return Results.Ok(products);
    }

    private static ShopifySkuReadinessResponse ToSkuReadiness(Sku sku)
    {
        var product = sku.Product;
        var isLens = CatalogValidation.IsLensProduct(product.ProductType);
        var supportedSellMode = product.SellMode is "SinglePiece" or "Both" or "SealedPackOnly";
        var validPackConfiguration = product.PiecesPerPack is > 0;
        var validWearCycle = CatalogValidation.HasValidOpenedExpiryRate(product.OpenedExpiryRate);
        var status = !isLens ? "UnsupportedProduct" : !supportedSellMode ? "PieceSaleDisabled" : !validPackConfiguration ? "NeedsPackConfiguration" : !validWearCycle ? "NeedsWearCycle" : "Ready";
        return new(sku.Id, sku.SkuCode, product.Name, product.ProductType, sku.PowerSign, sku.PowerValue, sku.ColorName, sku.Size, product.PiecesPerPack, product.SellMode, isLens && validWearCycle ? product.OpenedExpiryRate : null, isLens && validWearCycle ? product.OpenedExpiryDuration : null, status);
    }

    private static ShopifyWebhookEventResponse ToEventResponse(ShopifyWebhookEvent value) => new(value.Id, value.Topic, value.ShopDomain, value.VerificationMode, value.EventId, value.ApiVersion, value.Status, value.Detail, value.ShopifyOrderId, value.OperationId, value.ReceivedAt, value.TriggeredAt, value.ProcessedAt, value.NextAttemptAt, value.AttemptCount, value.ResolvedAt, value.ResolutionNote, value.ProtectedPayload is not null);
}

public sealed record ShopifyWebhookResponse(string Status, string? Detail, Guid? OperationId);
public sealed record ShopifySkuReadinessResponse(Guid SkuId, string SkuCode, string ProductName, string ProductType, string? PowerSign, decimal? PowerValue, string? ColorName, string? Size, int? PiecesPerPack, string? SellMode, string? WearCycle, string? WearDuration, string Status);
public sealed record ShopifySkuProductFilterResponse(Guid Id, string Name);
public sealed record ShopifyIntegrationStatusResponse(bool IsConfigured, bool IsLegacyWebhookConfigured, int MaxBodyBytes);
public sealed record ShopifyEventResolutionRequest(string Note);
public sealed record ShopifyWebhookEventResponse(Guid Id, string Topic, string ShopDomain, string VerificationMode, string? EventId, string? ApiVersion, string Status, string? Detail, string? ShopifyOrderId, Guid? OperationId, DateTime ReceivedAt, DateTime? TriggeredAt, DateTime? ProcessedAt, DateTime? NextAttemptAt, int AttemptCount, DateTime? ResolvedAt, string? ResolutionNote, bool PayloadAvailable);
public sealed record ShopifyVariantLinkUpdateRequest(Guid SkuId, long Revision, string Reason);
public sealed record ShopifyVariantLinkResponse(Guid Id, string ShopDomain, string ShopifyVariantId, string? ShopifySku, Guid? SkuId, string? SkuCode, string Status, string? Detail, DateTime UpdatedAt, long Revision);
public sealed record ShopifyQueueHealthResponse(int Queued, int Processing, int Retrying, int RequiresAttention,
    DateTime? OldestPendingAt, int ExpiredLeases, int UnpaidReservations, DateTime? OldestUnpaidReservationAt = null);
