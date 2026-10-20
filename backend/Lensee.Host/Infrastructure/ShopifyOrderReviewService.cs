using Lensee.Modules.Operations.Data;

namespace Lensee.Host.Infrastructure;

public sealed class ShopifyOrderReviewService(OperationsDbContext operationsDbContext)
{
    public async Task MarkReservationMissingAsync(ShopifyOrderLink? orderLink, CancellationToken cancellationToken)
    {
        if (orderLink is null) return;
        orderLink.RequiresReview = true;
        await operationsDbContext.SaveChangesAsync(cancellationToken);
    }
}
