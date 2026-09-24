using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lensee.Host.Infrastructure;

/// <summary>
/// Application-boundary persistence primitives. Endpoint handlers may compose
/// domain work, but relational saves and transaction creation stay behind this
/// service so the transaction boundary is explicit and auditable.
/// </summary>
public static class PersistenceBoundary
{
    public static Task<int> CommitAsync(DbContext context, CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);

    public static async Task<IDbContextTransaction?> OpenTransactionAsync(DbContext context, CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational()) return null;
        return await context.Database.BeginTransactionAsync(cancellationToken);
    }
}
