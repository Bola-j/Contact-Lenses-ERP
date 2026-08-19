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
        group.MapPost("/events/{id:guid}/resolve", ResolveEventAsync).RequireAuthorization("integrations.shopify.manage");
        group.MapGet("/sku-readiness", ListSkuReadinessAsync).RequireAuthorization("integrations.shopify.read");
        group.MapGet("/sku-readiness/products", ListSkuReadinessProductsAsync).RequireAuthorization("integrations.shopify.read");
        return group;
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

    private static async Task<IResult> RetryEventAsync(Guid id, OperationsDbContext operations, ICurrentUser currentUser, IClock clock, CancellationToken cancellationToken)
    {
        var eventRecord = await operations.ShopifyWebhookEvents.FindAsync([id], cancellationToken);
        if (eventRecord is null) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(eventRecord.ProtectedPayload))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["event"] = ["The retained payload has expired and cannot be retried."] });
        }
        eventRecord.Status = "Queued";
        eventRecord.Detail = "Manual retry requested.";
        eventRecord.AttemptCount = 0;
        eventRecord.NextAttemptAt = clock.EgyptNow;
        eventRecord.LeaseUntil = null;
        eventRecord.ResolvedAt = null;
        eventRecord.ResolvedBy = null;
        eventRecord.ResolutionNote = null;
        await operations.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToEventResponse(eventRecord));
    }

    private static async Task<IResult> ResolveEventAsync(Guid id, ShopifyEventResolutionRequest request, OperationsDbContext operations, ICurrentUser currentUser, IClock clock, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Note))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Note)] = ["Resolution note is required."] });
        }
        var eventRecord = await operations.ShopifyWebhookEvents.FindAsync([id], cancellationToken);
        if (eventRecord is null) return Results.NotFound();
        eventRecord.Status = "Resolved";
        eventRecord.ResolvedAt = clock.EgyptNow;
        eventRecord.ResolvedBy = currentUser.UserId;
        eventRecord.ResolutionNote = request.Note.Trim();
        eventRecord.LeaseUntil = null;
        await operations.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToEventResponse(eventRecord));
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
        var pieceSaleAllowed = product.SellMode is "SinglePiece" or "Both";
        var validWearCycle = CatalogValidation.HasValidOpenedExpiryRate(product.OpenedExpiryRate);
        var status = !isLens ? "UnsupportedProduct" : !pieceSaleAllowed ? "PieceSaleDisabled" : !validWearCycle ? "NeedsWearCycle" : "Ready";
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
