using Lensee.Modules.Inventory.Data;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

public sealed class StocktakeBalanceLockService
{
    private readonly InventoryDbContext _inventoryDbContext;

    public StocktakeBalanceLockService(InventoryDbContext inventoryDbContext)
    {
        _inventoryDbContext = inventoryDbContext;
    }

    public async Task<IReadOnlyDictionary<Guid, int>> LockAndEnsureVersionsAsync(
        Guid locationId,
        IEnumerable<Guid> skuIds,
        CancellationToken cancellationToken)
    {
        var versions = new Dictionary<Guid, int>();
        foreach (var skuId in skuIds.Distinct().OrderBy(value => value))
        {
            if (_inventoryDbContext.Database.IsRelational())
            {
                var key = $"stocktake:{locationId:N}:{skuId:N}";
                await _inventoryDbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"select pg_advisory_xact_lock(hashtextextended({key}, 0))",
                    cancellationToken);
                await _inventoryDbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"select 1 from inventory.stock_balances where location_id = {locationId} and sku_id = {skuId} for update",
                    cancellationToken);
            }

            var balance = await _inventoryDbContext.StockBalances
                .SingleOrDefaultAsync(value => value.LocationId == locationId && value.SkuId == skuId, cancellationToken);
            if (balance is null)
            {
                balance = new StockBalance
                {
                    Id = Guid.NewGuid(),
                    LocationId = locationId,
                    SkuId = skuId,
                    AvailableQty = 0,
                    ReservedInWarehouseQty = 0,
                    ReservedWithRepQty = 0,
                    RowVersion = 0,
                    LastUpdated = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                };
                _inventoryDbContext.StockBalances.Add(balance);
                await _inventoryDbContext.SaveChangesAsync(cancellationToken);
            }

            versions.Add(skuId, balance.RowVersion);
        }

        return versions;
    }
}
