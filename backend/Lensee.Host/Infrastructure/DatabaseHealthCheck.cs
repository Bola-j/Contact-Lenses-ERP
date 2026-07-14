using Lensee.Modules.Identity.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Lensee.Host.Infrastructure;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly IdentityDbContext _identityDbContext;

    public DatabaseHealthCheck(IdentityDbContext identityDbContext)
    {
        _identityDbContext = identityDbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _identityDbContext.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("PostgreSQL is reachable.")
                : HealthCheckResult.Unhealthy("PostgreSQL is not reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL connection failed.", exception);
        }
    }
}
