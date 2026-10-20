namespace Lensee.Modules.Inventory.Services;

/// <summary>Reads batch-specific pack quantities reserved by operation allocations.</summary>
public interface IReservedPackAllocationReader
{
    Task<IReadOnlyDictionary<Guid, int>> LoadReservedPackAllocationsAsync(
        Guid locationId,
        Guid? excludeOperationId,
        CancellationToken cancellationToken);
}
