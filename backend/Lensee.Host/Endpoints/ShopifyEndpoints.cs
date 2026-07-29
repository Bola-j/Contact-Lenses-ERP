using Lensee.Host.Infrastructure;
using Lensee.Modules.Catalog.Data;
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
        group.MapGet("/variant-mappings", ListVariantMappingsAsync).RequireAuthorization("integrations.shopify.read");
        group.MapPost("/variant-mappings", CreateVariantMappingAsync).RequireAuthorization("integrations.shopify.manage");
        group.MapPut("/variant-mappings/{id:guid}", UpdateVariantMappingAsync).RequireAuthorization("integrations.shopify.manage");
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

    private static async Task<IResult> ListVariantMappingsAsync(OperationsDbContext operations, CatalogDbContext catalog, CancellationToken cancellationToken)
    {
        var mappings = await operations.ShopifyVariantMappings.OrderBy(value => value.ShopifyVariantId).ToListAsync(cancellationToken);
        var skuIds = mappings.Select(value => value.SkuId).Distinct().ToArray();
        var skus = await catalog.Skus.Where(value => skuIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, cancellationToken);
        return Results.Ok(mappings.Select(value => ToResponse(value, skus.GetValueOrDefault(value.SkuId))).ToList());
    }

    private static async Task<IResult> CreateVariantMappingAsync(ShopifyVariantMappingRequest request, OperationsDbContext operations, CatalogDbContext catalog, IClock clock, CancellationToken cancellationToken)
    {
        var errors = await ValidateMappingAsync(request, null, operations, catalog, cancellationToken);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var mapping = new ShopifyVariantMapping { Id = Guid.NewGuid(), ShopifyVariantId = request.ShopifyVariantId.Trim(), SkuId = request.SkuId, EntryMode = request.EntryMode == "Pieces" ? "Pieces" : "Packs", IsActive = request.IsActive, CreatedAt = clock.EgyptNow, UpdatedAt = clock.EgyptNow };
        operations.ShopifyVariantMappings.Add(mapping);
        await operations.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/integrations/shopify/variant-mappings/{mapping.Id}", ToResponse(mapping, await catalog.Skus.FindAsync([mapping.SkuId], cancellationToken)));
    }

    private static async Task<IResult> UpdateVariantMappingAsync(Guid id, ShopifyVariantMappingRequest request, OperationsDbContext operations, CatalogDbContext catalog, IClock clock, CancellationToken cancellationToken)
    {
        var mapping = await operations.ShopifyVariantMappings.FindAsync([id], cancellationToken);
        if (mapping is null) return Results.NotFound();
        var errors = await ValidateMappingAsync(request, id, operations, catalog, cancellationToken);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        mapping.ShopifyVariantId = request.ShopifyVariantId.Trim();
        mapping.SkuId = request.SkuId;
        mapping.EntryMode = request.EntryMode == "Pieces" ? "Pieces" : "Packs";
        mapping.IsActive = request.IsActive;
        mapping.UpdatedAt = clock.EgyptNow;
        await operations.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(mapping, await catalog.Skus.FindAsync([mapping.SkuId], cancellationToken)));
    }

    private static async Task<Dictionary<string, string[]>> ValidateMappingAsync(ShopifyVariantMappingRequest request, Guid? currentId, OperationsDbContext operations, CatalogDbContext catalog, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.ShopifyVariantId)) errors[nameof(request.ShopifyVariantId)] = ["Shopify variant ID is required."];
        if (request.EntryMode is not ("Packs" or "Pieces")) errors[nameof(request.EntryMode)] = ["Entry mode must be Packs or Pieces."];
        if (!await catalog.Skus.AnyAsync(value => value.Id == request.SkuId && value.IsActive && value.DeletedAt == null, cancellationToken)) errors[nameof(request.SkuId)] = ["SKU must exist and be active."];
        if (!string.IsNullOrWhiteSpace(request.ShopifyVariantId) && await operations.ShopifyVariantMappings.AnyAsync(value => value.ShopifyVariantId == request.ShopifyVariantId.Trim() && value.Id != currentId, cancellationToken)) errors[nameof(request.ShopifyVariantId)] = ["Shopify variant ID is already mapped."];
        return errors;
    }

    private static ShopifyVariantMappingResponse ToResponse(ShopifyVariantMapping mapping, Sku? sku) => new(mapping.Id, mapping.ShopifyVariantId, mapping.SkuId, sku?.SkuCode, mapping.EntryMode, mapping.IsActive, mapping.UpdatedAt);

    private static ShopifyWebhookEventResponse ToEventResponse(ShopifyWebhookEvent value) => new(value.Id, value.Topic, value.ShopDomain, value.VerificationMode, value.EventId, value.ApiVersion, value.Status, value.Detail, value.ShopifyOrderId, value.OperationId, value.ReceivedAt, value.TriggeredAt, value.ProcessedAt, value.NextAttemptAt, value.AttemptCount, value.ResolvedAt, value.ResolutionNote, value.ProtectedPayload is not null);
}

public sealed record ShopifyWebhookResponse(string Status, string? Detail, Guid? OperationId);
public sealed record ShopifyVariantMappingRequest(string ShopifyVariantId, Guid SkuId, string EntryMode, bool IsActive = true);
public sealed record ShopifyVariantMappingResponse(Guid Id, string ShopifyVariantId, Guid SkuId, string? SkuCode, string EntryMode, bool IsActive, DateTime UpdatedAt);
public sealed record ShopifyIntegrationStatusResponse(bool IsConfigured, bool IsLegacyWebhookConfigured, int MaxBodyBytes);
public sealed record ShopifyEventResolutionRequest(string Note);
public sealed record ShopifyWebhookEventResponse(Guid Id, string Topic, string ShopDomain, string VerificationMode, string? EventId, string? ApiVersion, string Status, string? Detail, string? ShopifyOrderId, Guid? OperationId, DateTime ReceivedAt, DateTime? TriggeredAt, DateTime? ProcessedAt, DateTime? NextAttemptAt, int AttemptCount, DateTime? ResolvedAt, string? ResolutionNote, bool PayloadAvailable);
