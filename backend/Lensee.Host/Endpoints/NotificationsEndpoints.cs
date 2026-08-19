using Lensee.Modules.Catalog.Data;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.Modules.Reporting.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Primitives;
using Lensee.SharedKernel.Security;
using Lensee.Host.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace Lensee.Host.Endpoints;

public static class NotificationsEndpoints
{
    private const string InApp = "InApp";
    private const string LowStock = "LowStock";
    private const string Expiry = "Expiry";
    private const string UnresolvedReserves = "UnresolvedReserves";
    private const string OpenPaymentWeeklySummary = "OpenPaymentWeeklySummary";

    public static RouteGroupBuilder MapNotificationsEndpoints(this IEndpointRouteBuilder routes)
    {
        var notifications = routes.MapGroup("/api/v1/notifications").WithTags("Notifications");
        notifications.MapGet("/", ListNotificationsAsync).RequireAuthorization();
        notifications.MapGet("/types", ListNotificationTypesAsync).RequireAuthorization();
        notifications.MapGet("/unread-count", GetUnreadCountAsync).RequireAuthorization();
        notifications.MapGet("/{id:guid}/resolve", ResolveNotificationAsync).RequireAuthorization();
        notifications.MapPatch("/{id:guid}/read", MarkReadAsync).RequireAuthorization();
        notifications.MapPatch("/read-all", MarkAllReadAsync).RequireAuthorization();

        var alerts = routes.MapGroup("/api/v1/alerts").WithTags("Alerts");
        alerts.MapPost("/run/low-stock", RunLowStockAlertsAsync).RequireAuthorization("settings.write");
        alerts.MapPost("/run/expiry", RunExpiryAlertsAsync).RequireAuthorization("settings.write");
        alerts.MapPost("/run/unresolved-reserves", RunUnresolvedReserveAlertsAsync).RequireAuthorization("settings.write");
        alerts.MapPost("/run/open-payment-summary", RunOpenPaymentSummaryAsync).RequireAuthorization("settings.write");
        alerts.MapPost("/run/outstanding-balances", () => Results.Problem(statusCode: StatusCodes.Status410Gone, title: "Outstanding balance alerts were retired."))
            .RequireAuthorization("settings.write");

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

    private static async Task<IResult> ResolveNotificationAsync(
        Guid id,
        NotificationsDbContext notificationsDbContext,
        InventoryDbContext inventoryDbContext,
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        CrmDbContext crmDbContext,
        ReportingDbContext reportingDbContext,
        NavigationReferenceService navigationReferences,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var notification = await VisibleNotifications(notificationsDbContext, currentUser)
            .AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (notification is null) return Results.NotFound();

        var destination = await ResolveDestinationAsync(notification, currentUser, inventoryDbContext, operationsDbContext, paymentsDbContext, crmDbContext, reportingDbContext, navigationReferences, cancellationToken);
        return Results.Ok(destination);
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
            var message = $"Low stock at {row.Location.Name}: {skuLabel ?? "Unknown SKU"} has {row.AvailableQty} available pack(s), target is {row.TargetQty} pack(s), shortage is {shortage} pack(s). Review inventory targets and replenishment.";
            created += await AddForRolesAsync(
                notificationsDbContext,
                LowStock,
                message,
                row.Id,
                "StockBalance",
                [LenseeRoles.Admin, LenseeRoles.CLevel],
                clock.EgyptNow,
                cancellationToken,
                $"{skuLabel ?? "Unknown SKU"} at {row.Location.Name}",
                System.Text.Json.JsonSerializer.Serialize(new { row.LocationId, row.SkuId }));
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
            var message = $"Expiry alert at {batch.Location.Name}: {skuLabel ?? "Unknown SKU"} lot {batch.LotNumber ?? "No lot"} has {batch.Quantity} pack(s) and is {state} on {batch.ExpiryDate:yyyy-MM-dd}. Review the batch before selling or transferring.";
            created += await AddForRolesAsync(
                notificationsDbContext,
                Expiry,
                message,
                batch.Id,
                "InventoryBatch",
                [LenseeRoles.Admin, LenseeRoles.CLevel],
                clock.EgyptNow,
                cancellationToken,
                $"{skuLabel ?? "Unknown SKU"} / {batch.LotNumber ?? "No lot"}",
                System.Text.Json.JsonSerializer.Serialize(new { batch.LocationId, batch.SkuId, batch.LotNumber, batch.ExpiryDate }));
        }

        await notificationsDbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new AlertRunResponse(batches.Count, created));
    }

    private static async Task<IResult> RunUnresolvedReserveAlertsAsync(
        NotificationsDbContext notificationsDbContext,
        OperationsDbContext operationsDbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var cutoff = clock.EgyptNow.AddHours(-24);
        var staleGeneric = await notificationsDbContext.NotificationLogs
            .Where(value => value.AlertType == UnresolvedReserves && value.ReferenceId == null && !value.IsRead)
            .ToListAsync(cancellationToken);
        foreach (var stale in staleGeneric) stale.IsRead = true;
        var candidates = await operationsDbContext.OperationLogs
            .Include(operation => operation.OperationVersions)
            .Where(operation => !operation.IsDeleted &&
                (operation.OperationType == "Reserve" || operation.OperationType == "WholesaleSale" || operation.OperationType == "RetailSale") &&
                (operation.Status == "Reserved" || operation.Status == "Shipped"))
            .ToListAsync(cancellationToken);
        var matches = candidates.Where(operation => (operation.OperationVersions.OrderByDescending(version => version.EditedAt).Select(version => (DateTime?)version.EditedAt).FirstOrDefault() ?? operation.CreatedAt) < cutoff).ToList();
        var activeIds = matches.Select(value => value.Id).ToHashSet();
        var resolvedAlerts = await notificationsDbContext.NotificationLogs.Where(value => value.AlertType == UnresolvedReserves && value.ReferenceId.HasValue && !value.IsRead).ToListAsync(cancellationToken);
        foreach (var alert in resolvedAlerts.Where(value => !activeIds.Contains(value.ReferenceId!.Value))) alert.IsRead = true;
        var created = 0;
        foreach (var operation in matches)
        {
            var transitionAt = operation.OperationVersions.OrderByDescending(version => version.EditedAt).Select(version => (DateTime?)version.EditedAt).FirstOrDefault() ?? operation.CreatedAt;
            var context = System.Text.Json.JsonSerializer.Serialize(new { operation.Status, transitionAt });
            foreach (var role in new[] { LenseeRoles.Admin, LenseeRoles.CLevel })
            {
                var exists = await notificationsDbContext.NotificationLogs.AnyAsync(value => value.AlertType == UnresolvedReserves && value.ReferenceId == operation.Id && value.TargetRole == role && value.ReferenceContextJson == context, cancellationToken);
                if (exists) continue;
                var id = Guid.NewGuid();
                notificationsDbContext.NotificationLogs.Add(new NotificationLog { Id = id, AlertType = UnresolvedReserves, Message = $"{operation.OperationNumber} ({operation.OperationType}) remains {operation.Status} for {(int)(clock.EgyptNow - transitionAt).TotalHours} hour(s). Review and progress the operation.", ReferenceId = operation.Id, ReferenceType = "Operation", ReferenceCode = operation.OperationNumber, ReferenceTitle = operation.OperationNumber, ReferenceContextJson = context, TargetRole = role, Channel = InApp, CreatedAt = clock.EgyptNow, NotificationNumber = RecordCode("NOT", id) });
                created++;
            }
        }

        await notificationsDbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new AlertRunResponse(matches.Count, created));
    }

    private static async Task<IResult> RunOpenPaymentSummaryAsync(
        NotificationsDbContext notificationsDbContext,
        PaymentsDbContext paymentsDbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var now = clock.EgyptNow;
        var retired = await notificationsDbContext.NotificationLogs.Where(value => value.AlertType == "OutstandingBalances" && !value.IsRead).ToListAsync(cancellationToken);
        foreach (var alert in retired) alert.IsRead = true;
        var weekStart = DateOnly.FromDateTime(now).AddDays(-((int)now.DayOfWeek + 2) % 7);
        var weekKey = $"OPENPAY-{weekStart:yyyyMMdd}";
        var logs = await paymentsDbContext.MainPaymentLogs.Where(value => !value.IsDeleted && value.Status != "Completed" && value.Status != "Cancelled" && value.Status != "Rejected").ToListAsync(cancellationToken);
        var total = logs.Sum(value => value.TotalAmount);
        var paid = logs.Sum(value => value.AmountPaid);
        var remaining = logs.Sum(value => Math.Max(value.TotalAmount - value.AmountPaid, 0));
        var merchants = logs.Select(value => value.MerchantId).Where(value => value.HasValue).Distinct().Count();
        var statuses = string.Join(", ", logs.GroupBy(value => value.Status).OrderBy(value => value.Key).Select(value => $"{value.Key}: {value.Count()}"));
        var message = $"Open-payment weekly summary: {logs.Count} log(s), {merchants} merchant(s), total {total:0.##}, paid {paid:0.##}, remaining {remaining:0.##}. Statuses: {(statuses.Length == 0 ? "none" : statuses)}.";
        var created = 0;
        foreach (var role in new[] { LenseeRoles.Admin, LenseeRoles.ERPAdmin, LenseeRoles.CLevel, LenseeRoles.Accountant })
        {
            var exists = await notificationsDbContext.NotificationLogs.AnyAsync(value => value.AlertType == OpenPaymentWeeklySummary && value.ReferenceCode == weekKey && value.TargetRole == role, cancellationToken);
            if (exists) continue;
            var id = Guid.NewGuid();
            notificationsDbContext.NotificationLogs.Add(new NotificationLog { Id = id, AlertType = OpenPaymentWeeklySummary, Message = message, ReferenceType = "PaymentLog", ReferenceCode = weekKey, ReferenceTitle = "Open-payment weekly summary", ReferenceContextJson = System.Text.Json.JsonSerializer.Serialize(new { weekKey, logCount = logs.Count, merchants, total, paid, remaining, statuses }), TargetRole = role, Channel = InApp, CreatedAt = now, NotificationNumber = RecordCode("NOT", id) });
            created++;
        }

        await notificationsDbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(new AlertRunResponse(logs.Count, created));
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
        CancellationToken cancellationToken,
        string? referenceTitle = null,
        string? referenceContextJson = null)
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

            var id = Guid.NewGuid();
            dbContext.NotificationLogs.Add(new NotificationLog
            {
                Id = id,
                AlertType = alertType,
                Message = message,
                ReferenceId = referenceId,
                ReferenceType = referenceType,
                ReferenceCode = ReferenceCode(referenceType, referenceId),
                ReferenceTitle = referenceTitle,
                ReferenceContextJson = referenceContextJson,
                TargetRole = role,
                Channel = InApp,
                CreatedAt = now,
                NotificationNumber = RecordCode("NOT", id)
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
            notification.ReferenceCode ?? ReferenceCode(notification.ReferenceType, notification.ReferenceId),
            notification.ReferenceTitle,
            notification.ReferenceContextJson,
            notification.NotificationNumber ?? RecordCode("NOT", notification.Id),
            notification.TargetUserId,
            notification.TargetRole,
            notification.Channel,
            notification.IsRead,
            notification.CreatedAt,
            actionLabel,
            actionUrl);
    }

    private static async Task<NotificationDestinationResponse> ResolveDestinationAsync(
        NotificationLog notification,
        ICurrentUser currentUser,
        InventoryDbContext inventory,
        OperationsDbContext operations,
        PaymentsDbContext payments,
        CrmDbContext crm,
        ReportingDbContext reporting,
        NavigationReferenceService navigationReferences,
        CancellationToken cancellationToken)
    {
        var type = notification.ReferenceType?.Trim() ?? string.Empty;
        var id = notification.ReferenceId;
        var (route, focus, permission, label) = DestinationDefinition(type);
        var code = notification.ReferenceCode ?? ReferenceCode(type, id);
        if (id is null || string.IsNullOrWhiteSpace(route))
        {
            return new NotificationDestinationResponse(notification.Id, notification.NotificationNumber ?? RecordCode("NOT", notification.Id), type, id, code, notification.ReferenceTitle, null, null, null, null, "Unavailable", "This notification does not have a record destination.");
        }

        if (!currentUser.Principal.HasClaim("permission", permission!))
        {
            return new NotificationDestinationResponse(notification.Id, notification.NotificationNumber ?? RecordCode("NOT", notification.Id), type, id, code, notification.ReferenceTitle, route, focus, label, null, "Forbidden", "You do not have permission to open this record.");
        }

        var exists = type.ToLowerInvariant() switch
        {
            "stockbalance" => await inventory.StockBalances.AnyAsync(value => value.Id == id.Value, cancellationToken),
            "inventorybatch" => await inventory.InventoryBatches.AnyAsync(value => value.Id == id.Value, cancellationToken),
            "operation" => await operations.OperationLogs.AnyAsync(value => value.Id == id.Value && !value.IsDeleted, cancellationToken),
            "paymentlog" => await payments.MainPaymentLogs.AnyAsync(value => value.Id == id.Value && !value.IsDeleted, cancellationToken),
            "stocktake" => await operations.StocktakeSessions.AnyAsync(value => value.Id == id.Value, cancellationToken),
            "supplyshipment" => await operations.SupplyShipments.AnyAsync(value => value.Id == id.Value, cancellationToken),
            "merchant" => await crm.Merchants.AnyAsync(value => value.Id == id.Value && !value.IsDeleted, cancellationToken),
            "merchantexpiryrecall" => await operations.MerchantExpiryRecalls.AnyAsync(value => value.Id == id.Value, cancellationToken),
            "exportlog" => await reporting.ExportLogs.AnyAsync(value => value.Id == id.Value, cancellationToken),
            _ => false
        };

        if (!exists || currentUser.UserId is null || !NavigationReferenceService.TryGetDestinationByType(type, out var navigationDestination))
        {
            return new NotificationDestinationResponse(notification.Id, notification.NotificationNumber ?? RecordCode("NOT", notification.Id), type, id, code, notification.ReferenceTitle, route, focus, label, null, "Unavailable", "The referenced record is no longer available.");
        }

        return new NotificationDestinationResponse(notification.Id, notification.NotificationNumber ?? RecordCode("NOT", notification.Id), type, id, code, notification.ReferenceTitle, route, focus, label, navigationReferences.Issue(currentUser.UserId.Value, navigationDestination, id.Value), "Ready", null);
    }

    private static (string? Route, string? Focus, string? Permission, string? Label) DestinationDefinition(string referenceType) =>
        referenceType.ToLowerInvariant() switch
        {
            "stockbalance" => ("#/inventory", "stock-balance", LenseePermissions.InventoryRead, "Open inventory balance"),
            "inventorybatch" => ("#/inventory", "inventory-batch", LenseePermissions.InventoryRead, "Open inventory batch"),
            "operation" => ("#/operations", "operation", LenseePermissions.OperationsRead, "Open operation"),
            "paymentlog" => ("#/payments", "payment", LenseePermissions.PaymentsRead, "Open payment"),
            "stocktake" => ("#/stocktakes", "stocktake", LenseePermissions.OperationsRead, "Open stocktake"),
            "supplyshipment" => ("#/supply", "supply-shipment", LenseePermissions.SupplyRead, "Open shipment"),
            "merchant" => ("#/crm", "merchant", LenseePermissions.OperationsRead, "Open merchant"),
            "merchantexpiryrecall" => ("#/notifications", "merchant-expiry-recall", LenseePermissions.OperationsRead, "Open merchant recall"),
            "exportlog" => ("#/reports", "export", LenseePermissions.ReportsRead, "Open export"),
            _ => (null, null, null, null)
        };

    private static string? ReferenceCode(string? referenceType, Guid? id) => id is null ? null : RecordCode(referenceType?.ToLowerInvariant() switch
    {
        "stockbalance" => "BAL",
        "inventorybatch" => "BATCH",
        "operation" => "OP",
        "paymentlog" => "PAY",
        "stocktake" => "STK",
        "supplyshipment" => "SUP",
        "merchant" => "MER",
        "merchantexpiryrecall" => "MRC",
        "exportlog" => "EXP",
        _ => "REC"
    }, id.Value);

    private static string RecordCode(string prefix, Guid id) => $"{prefix}-{id:N}".ToUpperInvariant();

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
            string.Equals(notification.AlertType, OpenPaymentWeeklySummary, StringComparison.OrdinalIgnoreCase) ||
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

        if (string.Equals(notification.ReferenceType, MerchantExpiryRecallService.AlertType, StringComparison.OrdinalIgnoreCase))
        {
            return ("Open merchant recalls", "#/notifications");
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
    string? ReferenceCode,
    string? ReferenceTitle,
    string? ReferenceContextJson,
    string NotificationNumber,
    Guid? TargetUserId,
    string? TargetRole,
    string Channel,
    bool IsRead,
    DateTime CreatedAt,
    string? ActionLabel,
    string? ActionUrl);

public sealed record NotificationDestinationResponse(Guid NotificationId, string NotificationNumber, string RecordType, Guid? RecordId, string? RecordCode, string? RecordTitle, string? Route, string? Focus, string? ActionLabel, string? NavigationReference, string Status, string? Message);

public sealed record UnreadCountResponse(int Count);

public sealed record NotificationTypeResponse(string AlertType, int Count, int UnreadCount);

public sealed record AlertRunResponse(int MatchedItems, int CreatedNotifications);
