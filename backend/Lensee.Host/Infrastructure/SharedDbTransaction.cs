using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lensee.Host.Infrastructure;

public static class SharedDbTransaction
{
    public static async Task ExecuteAsync(
        DbContext primaryContext,
        Func<Task> action,
        CancellationToken cancellationToken,
        params DbContext[] secondaryContexts)
    {
        var contexts = secondaryContexts
            .Where(context => !ReferenceEquals(context, primaryContext))
            .Distinct()
            .ToArray();

        if (!primaryContext.Database.IsRelational() || contexts.Any(context => !context.Database.IsRelational()))
        {
            await action();
            return;
        }

        var ownsTransaction = primaryContext.Database.CurrentTransaction is null;
        await using var ownedTransaction = ownsTransaction
            ? await primaryContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var transaction = primaryContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("The primary relational context did not expose its transaction.");
        var dbTransaction = transaction.GetDbTransaction();
        var associatedContexts = new List<DbContext>();

        try
        {
            foreach (var context in contexts)
            {
                await context.Database.UseTransactionAsync(dbTransaction, cancellationToken);
                associatedContexts.Add(context);
            }

            await action();
            if (ownsTransaction)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        finally
        {
            foreach (var context in associatedContexts)
            {
                await ClearExternalTransactionAsync(context, cancellationToken);
            }
        }
    }

    private static async Task ClearExternalTransactionAsync(DbContext context, CancellationToken cancellationToken)
    {
        try
        {
            await context.Database.UseTransactionAsync(null, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // The context may have already rejected or cleared the external transaction.
        }
    }
}
