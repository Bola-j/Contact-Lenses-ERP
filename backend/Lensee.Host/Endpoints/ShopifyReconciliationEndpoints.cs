using Lensee.Host.Infrastructure;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static class ShopifyReconciliationEndpoints
{
    public static IEndpointRouteBuilder MapShopifyReconciliationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/integrations/shopify").WithTags("Shopify");
        group.MapGet("/reconciliation", async (ShopifyReconciliationService recovery, CancellationToken ct) =>
            Results.Ok(await recovery.GetStatusAsync(ct))).RequireAuthorization("integrations.shopify.read");
        group.MapPost("/reconciliation/run", async (ShopifyReconciliationService recovery, IAuditLogWriter audit, CancellationToken ct) =>
        {
            if (!(await recovery.GetStatusAsync(ct)).Available)
                return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Shopify recovery requires app access and enabled reconciliation.");
            try
            {
                var requested = await recovery.RequestRunAsync(ct);
                await audit.WriteAsync("ShopifyReconciliation", Guid.Empty, "RecoveryRequested", cancellationToken: ct);
                return Results.Accepted("/api/v1/integrations/shopify/reconciliation", requested);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { code = "recovery-state-changed", detail = "Recovery state changed. Refresh its status before retrying." });
            }
        }).RequireAuthorization("integrations.shopify.manage");
        return routes;
    }
}
