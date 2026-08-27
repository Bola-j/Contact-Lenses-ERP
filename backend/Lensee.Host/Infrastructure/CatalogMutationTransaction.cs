using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Identity.Data;
using Lensee.SharedKernel.Data;

namespace Lensee.Host.Infrastructure;

/// <summary>
/// Commits catalog state, its audit entry, and its outbox message as one PostgreSQL transaction.
/// </summary>
public sealed class CatalogMutationTransaction
{
    private readonly IdentityDbContext _identityDbContext;
    private readonly SharedDbContext _sharedDbContext;

    public CatalogMutationTransaction(IdentityDbContext identityDbContext, SharedDbContext sharedDbContext)
    {
        _identityDbContext = identityDbContext;
        _sharedDbContext = sharedDbContext;
    }

    public Task ExecuteAsync(CatalogDbContext catalogDbContext, Func<Task> action, CancellationToken cancellationToken) =>
        SharedDbTransaction.ExecuteAsync(catalogDbContext, action, cancellationToken, _identityDbContext, _sharedDbContext);
}
