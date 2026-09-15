using Lensee.Modules.Payments.Data;
using Lensee.SharedKernel.Data;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

/// <summary>
/// Keeps the merchant collection workflow's atomic persistence boundary out of
/// the HTTP endpoint layer. Related contexts are enlisted only when a workflow
/// transition needs them.
/// </summary>
public static class MerchantCollectionWorkspacePersistence
{
    public static Task RunAsync(
        PaymentsDbContext paymentsDbContext,
        Func<Task> action,
        CancellationToken cancellationToken,
        params DbContext[] relatedContexts) =>
        SharedDbTransaction.ExecuteAsync(paymentsDbContext, action, cancellationToken, relatedContexts);

    public static Task SaveAsync(PaymentsDbContext paymentsDbContext, CancellationToken cancellationToken) =>
        paymentsDbContext.SaveChangesAsync(cancellationToken);

    public static Task SaveAsync(SharedDbContext sharedDbContext, CancellationToken cancellationToken) =>
        sharedDbContext.SaveChangesAsync(cancellationToken);
}
