using Lensee.Modules.Catalog.Data;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.Modules.Reporting.Data;
using Lensee.SharedKernel.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Lensee.Host.Infrastructure;

public sealed class PendingMigrationsHealthCheck : IHealthCheck
{
    private readonly IServiceScopeFactory _scopeFactory;

    public PendingMigrationsHealthCheck(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var pending = new List<string>();

        await AddPendingMigrationsAsync(scope.ServiceProvider.GetRequiredService<IdentityDbContext>(), pending, cancellationToken);
        await AddPendingMigrationsAsync(scope.ServiceProvider.GetRequiredService<CatalogDbContext>(), pending, cancellationToken);
        await AddPendingMigrationsAsync(scope.ServiceProvider.GetRequiredService<InventoryDbContext>(), pending, cancellationToken);
        await AddPendingMigrationsAsync(scope.ServiceProvider.GetRequiredService<CrmDbContext>(), pending, cancellationToken);
        await AddPendingMigrationsAsync(scope.ServiceProvider.GetRequiredService<OperationsDbContext>(), pending, cancellationToken);
        await AddPendingMigrationsAsync(scope.ServiceProvider.GetRequiredService<PaymentsDbContext>(), pending, cancellationToken);
        await AddPendingMigrationsAsync(scope.ServiceProvider.GetRequiredService<NotificationsDbContext>(), pending, cancellationToken);
        await AddPendingMigrationsAsync(scope.ServiceProvider.GetRequiredService<ReportingDbContext>(), pending, cancellationToken);
        await AddPendingMigrationsAsync(scope.ServiceProvider.GetRequiredService<SharedDbContext>(), pending, cancellationToken);

        return pending.Count == 0
            ? HealthCheckResult.Healthy("No pending EF migrations.")
            : HealthCheckResult.Unhealthy("Pending EF migrations detected.", data: new Dictionary<string, object> { ["pendingMigrations"] = pending });
    }

    private static async Task AddPendingMigrationsAsync(DbContext dbContext, ICollection<string> pending, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            return;
        }

        var migrations = await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
        foreach (var migration in migrations)
        {
            pending.Add($"{dbContext.GetType().Name}:{migration}");
        }
    }
}
