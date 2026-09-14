using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

public static class PaymentPersistence
{
    public static Task SaveAsync(DbContext dbContext, CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public static async Task PersistAsync(DbContext first, DbContext second, CancellationToken cancellationToken)
    {
        await first.SaveChangesAsync(cancellationToken);
        await second.SaveChangesAsync(cancellationToken);
    }
}
