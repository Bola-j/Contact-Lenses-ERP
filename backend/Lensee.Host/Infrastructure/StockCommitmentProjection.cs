using Lensee.Host.Endpoints;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Inventory.Services;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

/// <summary>Read-only bridge from existing operation allocation snapshots to Inventory planning.</summary>
public sealed class StockCommitmentProjection(OperationsDbContext operations) : IReservedPackAllocationReader
{
    public async Task<IReadOnlyDictionary<Guid, int>> LoadReservedPackAllocationsAsync(Guid locationId,
        Guid? excludeOperationId, CancellationToken ct)
    {
        var orders = await operations.OperationLogs.AsNoTracking().Include(x => x.OperationVersions)
            .Where(x => !x.IsDeleted && x.Status == "Reserved" && x.SourceLocationId == locationId &&
                (!excludeOperationId.HasValue || x.Id != excludeOperationId.Value))
            .ToListAsync(ct);
        var held = new Dictionary<Guid, int>();
        foreach (var order in orders)
            foreach (var allocation in OperationsEndpoints.ReadTransferAllocations(order))
                foreach (var batch in allocation.Allocations)
                    held[batch.BatchId] = checked(held.GetValueOrDefault(batch.BatchId) + batch.Quantity);
        return held;
    }
}
