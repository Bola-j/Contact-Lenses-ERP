using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

public static class ShopifyActivation
{
    /// <summary>Captures a one-time cutoff; later configuration changes never silently backfill history.</summary>
    public static async Task<ShopifyReconciliationCheckpoint> EnsureAsync(OperationsDbContext db, IClock clock,
        ShopifyOptions options, CancellationToken ct)
    {
        var domain = (options.StoreDomain ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(domain)) throw new InvalidOperationException("Shopify store domain is required.");
        var row = await db.ShopifyReconciliationCheckpoints.SingleOrDefaultAsync(x => x.ShopDomain == domain, ct);
        if (row is not null) return row;
        var now = DateTime.SpecifyKind(clock.UtcNow, DateTimeKind.Unspecified);
        var configured = options.ActivationAtUtc ?? clock.UtcNow;
        var cutoff = DateTime.SpecifyKind(configured.Kind == DateTimeKind.Local ? configured.ToUniversalTime() : configured, DateTimeKind.Unspecified);
        row = new ShopifyReconciliationCheckpoint
        {
            Id = Guid.NewGuid(),
            ShopDomain = domain,
            ActivatedAtUtc = cutoff,
            NextRunAt = now,
            Revision = 1
        };
        db.ShopifyReconciliationCheckpoints.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(row).State = EntityState.Detached;
            row = await db.ShopifyReconciliationCheckpoints.SingleOrDefaultAsync(x => x.ShopDomain == domain, ct);
            if (row is null) throw;
        }
        return row;
    }
}
