using System.Security.Cryptography;
using System.Text;
using Lensee.SharedKernel.Abstractions;

namespace Lensee.Host.Infrastructure;

/// <summary>Captures successful mutating endpoints that do not emit a more specific audit event themselves.</summary>
public sealed class AuditMutationMiddleware
{
    public const string AuditWrittenItemKey = "lensee.audit-written";
    private static readonly HashSet<string> IgnoredPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/v1/auth/refresh"
    };

    private readonly RequestDelegate _next;

    public AuditMutationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IAuditLogWriter auditLogWriter, ICurrentUser currentUser)
    {
        var requestBody = await ReadRequestBodyAsync(context);
        await _next(context);

        if (!IsSuccessfulMutation(context) || context.Items.ContainsKey(AuditWrittenItemKey) || currentUser.UserId is not { } userId)
        {
            return;
        }

        var routeId = context.Request.RouteValues.Values
            .Select(value => value?.ToString())
            .FirstOrDefault(value => Guid.TryParse(value, out _));
        var entityId = Guid.TryParse(routeId, out var parsedId)
            ? parsedId
            : CreateEventId(context.TraceIdentifier, context.Request.Method, context.Request.Path);
        var entityType = EntityTypeFromPath(context.Request.Path);
        var action = ActionFrom(context.Request.Method, context.Request.Path);

        await auditLogWriter.WriteAsync(
            entityType,
            entityId,
            action,
            AuditEventPayload.FromRequest(action, entityType, entityId, requestBody),
            cancellationToken: context.RequestAborted);
    }

    private static async Task<string?> ReadRequestBodyAsync(HttpContext context)
    {
        if (context.Request.ContentLength is not > 0 || context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) != true)
        {
            return null;
        }

        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(context.RequestAborted);
        context.Request.Body.Position = 0;
        return body;
    }

    private static bool IsSuccessfulMutation(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api/v1") &&
        !IgnoredPaths.Contains(context.Request.Path.Value ?? string.Empty) &&
        context.Request.Method is "POST" or "PUT" or "PATCH" or "DELETE" &&
        context.Response.StatusCode is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices;

    private static string EntityTypeFromPath(PathString path)
    {
        var value = path.Value ?? string.Empty;
        if (value.Contains("/crm/merchants", StringComparison.OrdinalIgnoreCase)) return "Merchant";
        if (value.Contains("/crm/representatives", StringComparison.OrdinalIgnoreCase)) return "Representative";
        if (value.Contains("/inventory/receipts", StringComparison.OrdinalIgnoreCase)) return "InventoryReceipt";
        if (value.Contains("/inventory", StringComparison.OrdinalIgnoreCase)) return "Inventory";
        if (value.Contains("/operations", StringComparison.OrdinalIgnoreCase)) return "Operation";
        if (value.Contains("/payments", StringComparison.OrdinalIgnoreCase)) return "Payment";
        if (value.Contains("/stocktakes", StringComparison.OrdinalIgnoreCase)) return "Stocktake";
        if (value.Contains("/supply", StringComparison.OrdinalIgnoreCase)) return "SupplyShipment";
        if (value.Contains("/notifications", StringComparison.OrdinalIgnoreCase)) return "Notification";
        if (value.Contains("/reports", StringComparison.OrdinalIgnoreCase)) return "Export";
        if (value.Contains("/integrations/shopify", StringComparison.OrdinalIgnoreCase)) return "ShopifyWebhookEvent";
        return path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries).Skip(2).FirstOrDefault() switch
        {
            _ => "System"
        };
    }

    private static string ActionFrom(string method, PathString path)
    {
        var value = path.Value ?? string.Empty;
        if (value.EndsWith("/confirm", StringComparison.OrdinalIgnoreCase)) return "Confirmed";
        if (value.EndsWith("/cancel", StringComparison.OrdinalIgnoreCase)) return "Cancelled";
        if (value.EndsWith("/approve", StringComparison.OrdinalIgnoreCase)) return "Approved";
        if (value.EndsWith("/reject", StringComparison.OrdinalIgnoreCase)) return "Rejected";
        if (value.EndsWith("/deactivate", StringComparison.OrdinalIgnoreCase)) return "Deactivated";
        if (value.EndsWith("/reactivate", StringComparison.OrdinalIgnoreCase)) return "Reactivated";
        return method switch { "POST" => "Created", "PUT" or "PATCH" => "Updated", "DELETE" => "Deleted", _ => "Changed" };
    }

    private static Guid CreateEventId(string traceIdentifier, string method, PathString path)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{traceIdentifier}:{method}:{path}"));
        return new Guid(bytes[..16]);
    }
}
