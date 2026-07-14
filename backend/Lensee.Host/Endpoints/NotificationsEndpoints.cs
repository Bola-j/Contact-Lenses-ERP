using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Notifications.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Primitives;
using Lensee.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace Lensee.Host.Endpoints;

public static class NotificationsEndpoints
{
    private const string InApp = "InApp";
    private const string LowStock = "LowStock";
    private const string Expiry = "Expiry";
    private const string UnresolvedReserves = "UnresolvedReserves";
    private const string OutstandingBalances = "OutstandingBalances";

    public static RouteGroupBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder routes)
    {
        var notifications = routes.MapGroup("/api/v1/notifications").WithTags("Notifications");
        notifications.MapGet("/", ListNotificationsAsync).RequireAuthorization();
        notifications.MapGet("/types", ListNotificationTypesAsync).RequireAuthorization();
        notifications.MapGet("/unread-count", GetUnreadCountAsync).RequireAuthorization();
        notifications.MapPatch("/{id:guid}/read", MarkReadAsync).RequireAuthorization();
        notifications.MapPatch("/read-all", MarkAllReadAsync).RequireAuthorization();

        var alerts = routes.MapGroup("/api/v1/alerts").WithTags("Alerts");
        alerts.MapPost("/run/low-stock", RunLowStockAlertsAsync).RequireAuthorization("settings.write");
        alerts.MapPost("/run/expiry", RunExpiryAlertsAsync).RequireAuthorization("settings.write");
        alerts.MapPost("/run/unresolved-reserves", RunUnresolvedReserveAlertsAsync).RequireAuthorization("settings.write");
        alerts.MapPost("/run/outstanding-balances", RunOutstandingBalanceAlertsAsync).RequireAuthorization("settings.write");

        return notifications;
    }

    private static async Task<IResult> ListNotificationsAsync(
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? alertType,
        [FromQuery] bool? unreadOnly,
        NotificationsDbContext dbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var request = new PageRequest(page ?? 1, pageSize ?? 25);
        var query = VisibleNotifications(dbContext, currentUser);
        if (!string.IsNullOrWhiteSpace(alertType))
        {
            query = query.Where(notification => notification.AlertType == alertType.Trim());
        }
        if (unreadOnly == true)
        {
            query = query.Where(notification => !notification.IsRead);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(notification => notification.CreatedAt)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(notification => ToResponse(notification))
            .ToListAsync(cancellationToken);

        return Results.Ok(new PagedResult<NotificationResponse>(rows, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> ListNotificationTypesAsync(
        NotificationsDbContext dbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var visible = await VisibleNotifications(dbContext, currentUser)
            .Select(notification => new { notification.AlertType, notification.IsRead })
            .ToListAsync(cancellationToken);

        var rows = visible
            .GroupBy(notification => notification.AlertType)
            .Select(group => new NotificationTypeResponse(
                group.Key,
                group.Count(),
                group.Count(notification => !notification.IsRead)))
            .OrderBy(row => row.AlertType)
            .ToList();

        return Results.Ok(rows);
    }

    private static async Task<IResult> GetUnreadCountAsync(
        NotificationsDbContext dbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var count = await VisibleNotifications(dbContext, currentUser)
            .CountAsync(notification => !notification.IsRead, cancellationToken);
        return Results.Ok(new UnreadCountResponse(count));
    }

    private static async Task<IResult> MarkReadAsync(
        Guid id,
        NotificationsDbContext dbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var notification = await VisibleNotifications(dbContext, currentUser)
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (notification is null)
        {
            return Results.NotFound();
        }

        notification.IsRead = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(notification));
    }

    private static async Task<IResult> MarkAllReadAsync(
        NotificationsDbContext dbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var notifications = await VisibleNotifications(dbContext, currentUser)
            .Where(notification => !notification.IsRead)
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            notification.IsRead = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new UnreadCountResponse(0));
    }

    private static async Task<IResult> RunLowStockAlertsAsync(
        NotificationsDbContext notificationsDbContext,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var skuLabels = await SkuLabelsAsync(catalogDbContext, cancellationToken);
        var rows = await inventoryDbContext.StockBalances
            .Include(balance => balance.Location)
            .Where(balance => balance.TargetQty.HasValue && balance.AvailableQty < balance.TargetQty.Value)
            .ToListAsync(cancellationToken);

        var created = 0;
        foreach (var row in rows)
        {
            skuLabels.TryGetValue(row.SkuId, out var skuLabel);
            var shortage = row.TargetQty!.Value - row.AvailableQty;
            var message = $"Low stock at {row.Location.Name}: {skuLabel ?? row.SkuId.ToString()} has {row.AvailableQty} available pack(s), target is {row.TargetQty} pack(s), shortage is {shortage} pack(s). Review inventory targets and replenishment.";
            created += await AddForRolesAsync(
                notificationsDbContext,
                LowStock,
                message,
                row.Id,
                "StockBalance",
                [LenseeRoles.Admin, LenseeRoles.CLevel],
                clock.EgyptNow,
                cancellationToken);
        }

        await notificationsDbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new AlertRunResponse(rows.Count, created));
    }

    private static async Task<IResult> RunExpiryAlertsAsync(
        NotificationsDbContext notificationsDbContext,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.EgyptNow);
        var threshold = today.AddMonths(3);
        var skuLabels = await SkuLabelsAsync(catalogDbContext, cancellationToken);
        var batches = await inventoryDbContext.InventoryBatches
            .Include(batch => batch.Location)
            .Where(batch => batch.Quantity > 0 && batch.ExpiryDate.HasValue && batch.ExpiryDate.Value <= threshold)
            .ToListAsync(cancellationToken);

        var created = 0;
        foreach (var batch in batches)
        {
            skuLabels.TryGetValue(batch.SkuId, out var skuLabel);
            var state = batch.ExpiryDate!.Value < today ? "expired" : "near expiry";
            var message = $"Expiry alert at {batch.Location.Name}: {skuLabel ?? batch.SkuId.ToString()} lot {batch.LotNumber ?? "No lot"} has {batch.Quantity} pack(s) and is {state} on {batch.ExpiryDate:yyyy-MM-dd}. Review the batch before selling or transferring.";
            created += await AddForRolesAsync(
                notificationsDbContext,
                Expiry,
                message,
                batch.Id,
                "InventoryBatch",
                [LenseeRoles.Admin, LenseeRoles.CLevel],
                clock.EgyptNow,
                cancellationToken);
        }

        await notificationsDbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new AlertRunResponse(batches.Count, created));
    }

    private static async Task<IResult> RunUnresolvedReserveAlertsAsync(
        NotificationsDbContext notificationsDbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var created = await AddForRolesAsync(
            notificationsDbContext,
            UnresolvedReserves,
            "Unresolved reserve review is needed. Open Operations and check active reserve or reserved sales that have not progressed to shipment/completion.",
            null,
            "Operation",
            [LenseeRoles.Admin, LenseeRoles.CLevel],
            clock.EgyptNow,
            cancellationToken);

        await notificationsDbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new AlertRunResponse(1, created));
    }

    private static async Task<IResult> RunOutstandingBalanceAlertsAsync(
        NotificationsDbContext notificationsDbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var created = await AddForRolesAsync(
            notificationsDbContext,
            OutstandingBalances,
            "Outstanding remaining review is needed. Open Payments and review pending logs, unpaid installments, merchant remaining, and overdue collection items.",
            null,
            "PaymentLog",
            [LenseeRoles.Admin, LenseeRoles.Accountant],
            clock.EgyptNow,
            cancellationToken);

        await notificationsDbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new AlertRunResponse(1, created));
    }

    private static IQueryable<NotificationLog> VisibleNotifications(NotificationsDbContext dbContext, ICurrentUser currentUser)
    {
        var userId = currentUser.UserId;
        var role = currentUser.Role;

        return dbContext.NotificationLogs.Where(notification =>
            (userId.HasValue && notification.TargetUserId == userId.Value) ||
            (!string.IsNullOrWhiteSpace(role) && notification.TargetRole == role));
    }

    private static async Task<int> AddForRolesAsync(
        NotificationsDbContext dbContext,
        string alertType,
        string message,
        Guid? referenceId,
        string referenceType,
        IReadOnlyCollection<string> roles,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var created = 0;
        foreach (var role in roles)
        {
            var exists = await dbContext.NotificationLogs.AnyAsync(notification =>
                notification.AlertType == alertType &&
                notification.ReferenceId == referenceId &&
                notification.ReferenceType == referenceType &&
                notification.TargetRole == role,
                cancellationToken);
            if (exists)
            {
                continue;
            }

            dbContext.NotificationLogs.Add(new NotificationLog
            {
                Id = Guid.NewGuid(),
                AlertType = alertType,
                Message = message,
                ReferenceId = referenceId,
                ReferenceType = referenceType,
                TargetRole = role,
                Channel = InApp,
                CreatedAt = now
            });
            created++;
        }

        return created;
    }

    private static async Task<Dictionary<Guid, string>> SkuLabelsAsync(CatalogDbContext dbContext, CancellationToken cancellationToken) =>
        await dbContext.Skus
            .Include(sku => sku.Product)
            .Select(sku => new
            {
                sku.Id,
                Label = sku.SkuCode + " - " + sku.Product.Name
            })
            .ToDictionaryAsync(value => value.Id, value => value.Label, cancellationToken);

    private static NotificationResponse ToResponse(NotificationLog notification)
    {
        var (actionLabel, actionUrl) = NotificationLink(notification);
        return new(
            notification.Id,
            notification.AlertType,
            notification.Message,
            notification.ReferenceId,
            notification.ReferenceType,
            notification.TargetUserId,
            notification.TargetRole,
            notification.Channel,
            notification.IsRead,
            notification.CreatedAt,
            actionLabel,
            actionUrl);
    }

    private static (string? Label, string? Url) NotificationLink(NotificationLog notification)
    {
        if (string.Equals(notification.ReferenceType, "StockBalance", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(notification.ReferenceType, "InventoryBatch", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(notification.AlertType, LowStock, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(notification.AlertType, Expiry, StringComparison.OrdinalIgnoreCase))
        {
            return ("Open inventory", "#/inventory");
        }

        if (string.Equals(notification.ReferenceType, "PaymentLog", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(notification.AlertType, OutstandingBalances, StringComparison.OrdinalIgnoreCase) ||
            notification.AlertType.StartsWith("Payment", StringComparison.OrdinalIgnoreCase))
        {
            return ("Open payments", "#/payments");
        }

        if (string.Equals(notification.ReferenceType, "Operation", StringComparison.OrdinalIgnoreCase) ||
            notification.AlertType.Contains("Operation", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(notification.AlertType, UnresolvedReserves, StringComparison.OrdinalIgnoreCase))
        {
            return ("Open operations", "#/operations");
        }

        if (string.Equals(notification.ReferenceType, "Stocktake", StringComparison.OrdinalIgnoreCase) ||
            notification.AlertType.Contains("Stocktake", StringComparison.OrdinalIgnoreCase))
        {
            return ("Open stocktakes", "#/stocktakes");
        }

        if (string.Equals(notification.ReferenceType, "Merchant", StringComparison.OrdinalIgnoreCase))
        {
            return ("Open CRM", "#/crm");
        }

        if (notification.AlertType.Contains("Report", StringComparison.OrdinalIgnoreCase) ||
            notification.AlertType.Contains("Export", StringComparison.OrdinalIgnoreCase))
        {
            return ("Open reports", "#/reports");
        }

        return (null, null);
    }
}

public sealed record NotificationResponse(
    Guid Id,
    string AlertType,
    string Message,
    Guid? ReferenceId,
    string? ReferenceType,
    Guid? TargetUserId,
    string? TargetRole,
    string Channel,
    bool IsRead,
    DateTime CreatedAt,
    string? ActionLabel,
    string? ActionUrl);

public sealed record UnreadCountResponse(int Count);

public sealed record NotificationTypeResponse(string AlertType, int Count, int UnreadCount);

public sealed record AlertRunResponse(int MatchedItems, int CreatedNotifications);
