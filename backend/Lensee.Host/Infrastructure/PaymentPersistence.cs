using Lensee.Modules.Identity.Data;
using Lensee.Modules.Payments.Data;

namespace Lensee.Host.Infrastructure;

/// <summary>Persists the payment and audit contexts that share a payment transaction.</summary>
public static class PaymentPersistence
{
    public static async Task PersistAsync(
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        CancellationToken cancellationToken)
    {
        await paymentsDbContext.SaveChangesAsync(cancellationToken);
        await identityDbContext.SaveChangesAsync(cancellationToken);
    }
}
