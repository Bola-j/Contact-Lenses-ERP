using Lensee.Modules.Identity.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Host.Infrastructure;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static class AuditEndpoints
{
    public static RouteGroupBuilder MapAuditEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/audit")
            .WithTags("Audit")
            .RequireAuthorization("audit.read");

        group.MapGet("/", ListAsync).WithName("ListAuditHistory");
        group.MapGet("/{id:guid}", GetAsync).WithName("GetAuditEvent");
        group.MapGet("/{id:guid}/navigation-reference", GetNavigationReferenceAsync).WithName("GetAuditNavigationReference");
        return group;
    }

    private static async Task<IResult> ListAsync(
        int? page,
        int? pageSize,
        DateTime? from,
        DateTime? to,
        Guid? actorId,
        string? entityType,
        string? action,
        string? search,
        IdentityDbContext dbContext,
        OperationsDbContext operationsDbContext,
        CancellationToken cancellationToken)
    {
        var request = new AuditHistoryQuery(page ?? 1, pageSize ?? 50, from, to, actorId, entityType, action, search);
        var safePage = Math.Clamp(request.Page, 1, 10_000);
        var safePageSize = Math.Clamp(request.PageSize, 1, 100);
        var query = ApplyFilters(dbContext.AuditLogs.AsNoTracking(), request);
        var total = await query.CountAsync(cancellationToken);
        var auditEvents = await query
            .OrderByDescending(value => value.CreatedAt)
            .ThenByDescending(value => value.Id)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(cancellationToken);
        var operationNumbers = await GetOperationNumbersAsync(auditEvents, operationsDbContext, cancellationToken);
        var rows = auditEvents
            .Select(value => ToResponse(value, operationNumbers.GetValueOrDefault(value.EntityId)))
            .ToList();

        return Results.Ok(new PagedResult<AuditEventResponse>(rows, safePage, safePageSize, total));
    }

    private static async Task<IResult> GetAsync(Guid id, IdentityDbContext dbContext, OperationsDbContext operationsDbContext, CancellationToken cancellationToken)
    {
        var entry = await dbContext.AuditLogs.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (entry is null) return Results.NotFound();

        var operationNumbers = await GetOperationNumbersAsync([entry], operationsDbContext, cancellationToken);
        return Results.Ok(ToResponse(entry, operationNumbers.GetValueOrDefault(entry.EntityId)));
    }

    private static async Task<IResult> GetNavigationReferenceAsync(
        Guid id,
        IdentityDbContext dbContext,
        NavigationReferenceService navigationReferences,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var entry = await dbContext.AuditLogs.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (entry is null || currentUser.UserId is null ||
            !navigationReferences.TryGetDestinationForEntity(entry.EntityType, out var destination) ||
            !currentUser.Principal.HasClaim("permission", destination.Permission))
        {
            return Results.NotFound();
        }

        return Results.Ok(new AuditNavigationReferenceResponse(destination.Route, navigationReferences.Issue(currentUser.UserId.Value, destination, entry.EntityId)));
    }

    private static IQueryable<AuditLog> ApplyFilters(IQueryable<AuditLog> query, AuditHistoryQuery request)
    {
        if (request.From is { } from) query = query.Where(value => value.CreatedAt >= from);
        if (request.To is { } to) query = query.Where(value => value.CreatedAt <= to);
        if (request.ActorId is { } actorId) query = query.Where(value => value.UserId == actorId);
        if (!string.IsNullOrWhiteSpace(request.EntityType)) query = query.Where(value => value.EntityType == request.EntityType.Trim());
        if (!string.IsNullOrWhiteSpace(request.Action)) query = query.Where(value => value.Action == request.Action.Trim());
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(value => value.EntityType.ToLower().Contains(term) || value.Action.ToLower().Contains(term) || (value.ActorName ?? string.Empty).ToLower().Contains(term) || (value.ChangedFields ?? string.Empty).ToLower().Contains(term));
        }

        return query;
    }

    private static async Task<IReadOnlyDictionary<Guid, string>> GetOperationNumbersAsync(
        IReadOnlyCollection<AuditLog> auditEvents,
        OperationsDbContext operationsDbContext,
        CancellationToken cancellationToken)
    {
        var operationIds = auditEvents
            .Where(value => value.EntityType.Equals("Operation", StringComparison.OrdinalIgnoreCase))
            .Select(value => value.EntityId)
            .Distinct()
            .ToArray();
        if (operationIds.Length == 0) return new Dictionary<Guid, string>();

        return await operationsDbContext.OperationLogs
            .AsNoTracking()
            .Where(value => operationIds.Contains(value.Id))
            .Select(value => new { value.Id, value.OperationNumber })
            .ToDictionaryAsync(value => value.Id, value => value.OperationNumber, cancellationToken);
    }

    private static AuditEventResponse ToResponse(AuditLog value, string? businessReference = null)
    {
        var presentation = AuditEventPresentation.From(value, businessReference);
        return new(
            value.Id,
            value.EntityType,
            value.EntityId,
            value.Action,
            value.ActorName ?? "Historical actor unavailable",
            value.ActorType ?? (value.UserId.HasValue ? "User" : "System"),
            value.UserId,
            value.CreatedAt,
            value.IpAddress,
            value.StockDeltaApplied,
            value.ChangedFields,
            SectionFor(value.EntityType),
            presentation.Summary,
            presentation.RecordName,
            presentation.Changes);
    }

    private static string SectionFor(string entityType) => entityType switch
    {
        "User" => "admin",
        "Category" or "Brand" or "Product" or "Sku" => "catalog",
        "Merchant" or "Representative" => "crm",
        "StockBalance" or "InventoryReceipt" => "inventory",
        "Operation" => "operations",
        "PaymentLog" or "PaymentSubLog" or "CashRecord" or "FinancialAdjustment" => "payments",
        "SupplyShipment" => "supply",
        "Stocktake" => "stocktakes",
        "Notification" => "notifications",
        "ShopifyWebhookEvent" or "Shopify" => "integrations",
        "Export" or "Reports" => "reports",
        _ => "dashboard"
    };
}

public sealed record AuditHistoryQuery(
    int Page = 1,
    int PageSize = 50,
    DateTime? From = null,
    DateTime? To = null,
    Guid? ActorId = null,
    string? EntityType = null,
    string? Action = null,
    string? Search = null);

public sealed record AuditEventResponse(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string Action,
    string ActorName,
    string ActorType,
    Guid? ActorId,
    DateTime HappenedAt,
    string? IpAddress,
    int? StockDeltaApplied,
    string? ChangedFields,
    string Section,
    string Summary,
    string RecordName,
    IReadOnlyList<AuditChange> Changes);

public sealed record AuditNavigationReferenceResponse(string Route, string NavigationReference);
