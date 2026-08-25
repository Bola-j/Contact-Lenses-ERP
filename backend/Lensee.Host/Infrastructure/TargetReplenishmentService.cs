using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

public sealed class TargetReplenishmentService
{
    private readonly OperationsDbContext _operations;
    private readonly InventoryDbContext _inventory;
    private readonly CatalogDbContext _catalog;
    private readonly NotificationsDbContext _notifications;
    private readonly IClock _clock;

    public TargetReplenishmentService(OperationsDbContext operations, InventoryDbContext inventory, CatalogDbContext catalog, NotificationsDbContext notifications, IClock clock)
    {
        _operations = operations; _inventory = inventory; _catalog = catalog; _notifications = notifications; _clock = clock;
    }

    public async Task<TargetReplenishmentRunResult> RunAsync(string trigger, Guid? locationId, Guid? skuId, CancellationToken cancellationToken)
    {
        var now = _clock.EgyptNow;
        var cairoDate = DateOnly.FromDateTime(now);
        var key = $"{trigger.ToUpperInvariant()}-{cairoDate:yyyyMMdd}";
        // Keep the persisted run key within the varchar(40) database contract.
        // Manual runs still need a unique key so the Inventory button can be
        // pressed more than once per Cairo day without colliding with the
        // scheduled run.
        if (trigger.Equals("Manual", StringComparison.OrdinalIgnoreCase))
        {
            var manualSuffix = Guid.NewGuid().ToString("N")[..16];
            key += $"-{manualSuffix}";
        }
        TargetReplenishmentRunResult? result = null;
        await SharedDbTransaction.ExecuteAsync(_operations, async () =>
        {
            if (_operations.Database.IsRelational())
            {
                // The lock is released automatically on commit or rollback. This avoids
                // retaining a pooled-session lock when an exception interrupts the run.
                await _operations.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
            }
            if (!trigger.Equals("Manual", StringComparison.OrdinalIgnoreCase) && await _operations.ReplenishmentRuns.AnyAsync(value => value.RunKey == key, cancellationToken))
            {
                result = new TargetReplenishmentRunResult(0, 0, true);
                return;
            }

            var main = await _inventory.Locations.FirstOrDefaultAsync(value => value.IsActive && value.LocationType == "MainWarehouse", cancellationToken);
            if (main is null)
            {
                result = new TargetReplenishmentRunResult(0, 0, false);
                return;
            }
            var destinations = await _inventory.Locations.Where(value => value.IsActive && value.LocationType != "MainWarehouse" && (!locationId.HasValue || value.Id == locationId.Value)).ToListAsync(cancellationToken);
            var destinationIds = destinations.Select(value => value.Id).ToArray();
            var balances = await _inventory.StockBalances.Where(value => destinationIds.Contains(value.LocationId) && value.TargetQty.HasValue && (!skuId.HasValue || value.SkuId == skuId.Value)).ToListAsync(cancellationToken);
            var incoming = await _operations.OperationLogs.Include(value => value.OperationLines).Where(value => !value.IsDeleted && value.AutomationType == "TargetReplenishment" && value.OperationType == "WarehouseTransfer" && (value.Status == "Draft" || value.Status == "Reserved" || value.Status == "Shipped") && value.DestinationLocationId.HasValue && destinationIds.Contains(value.DestinationLocationId.Value)).SelectMany(value => value.OperationLines, (operation, line) => new { Destination = operation.DestinationLocationId!.Value, line.SkuId, line.Quantity }).GroupBy(value => new { value.Destination, value.SkuId }).Select(group => new { group.Key.Destination, group.Key.SkuId, Quantity = group.Sum(value => value.Quantity) }).ToDictionaryAsync(value => (value.Destination, value.SkuId), value => value.Quantity, cancellationToken);
            var mainBalances = await _inventory.StockBalances.Where(value => value.LocationId == main.Id).ToDictionaryAsync(value => value.SkuId, value => Math.Max(value.AvailableQty - (value.TargetQty ?? 0), 0), cancellationToken);
            var created = 0; var uncovered = 0;
            var pendingCurrentVersions = new List<(OperationLog Operation, Guid VersionId)>();
            foreach (var group in balances.GroupBy(value => value.LocationId))
            {
            var destination = destinations.First(value => value.Id == group.Key);
            var lines = new List<(Guid SkuId, int Quantity)>();
            foreach (var balance in group)
            {
                var incomingQty = incoming.GetValueOrDefault((balance.LocationId, balance.SkuId));
                var shortage = Math.Max((balance.TargetQty ?? 0) - balance.AvailableQty - incomingQty, 0);
                var quantity = Math.Min(shortage, mainBalances.GetValueOrDefault(balance.SkuId));
                uncovered += shortage - quantity;
                if (quantity > 0) { lines.Add((balance.SkuId, quantity)); mainBalances[balance.SkuId] -= quantity; }
            }
            if (lines.Count == 0) continue;
            var operation = new OperationLog { Id = Guid.NewGuid(), OperationNumber = $"OP-{now:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}", OperationType = "WarehouseTransfer", Status = "Draft", SourceLocationId = main.Id, DestinationLocationId = destination.Id, Notes = "Target-stock replenishment", CreatedBy = Guid.Empty, CreatedActorName = "System - Target replenishment", CreatedAt = now, AutomationType = "TargetReplenishment" };
            foreach (var line in lines)
            {
                var sku = await _catalog.Skus.Include(value => value.Product).FirstAsync(value => value.Id == line.SkuId, cancellationToken);
                operation.OperationLines.Add(new OperationLine { Id = Guid.NewGuid(), OperationId = operation.Id, SkuId = line.SkuId, ProductNameSnapshot = sku.Product.Name, SkuCodeSnapshot = sku.SkuCode, Section = "Standard", Quantity = line.Quantity, EntryMode = "Packs", LineNotes = "Target-stock replenishment" });
            }
            _operations.OperationLogs.Add(operation);
            var version = new OperationVersion { Id = Guid.NewGuid(), OperationId = operation.Id, VersionNumber = 1, SnapshotData = "{}", Reason = "Draft replenishment created", EditedBy = Guid.Empty, EditedActorName = "System - Target replenishment", EditedAt = now };
            operation.OperationVersions.Add(version);
            _operations.OperationVersions.Add(version);
            pendingCurrentVersions.Add((operation, version.Id));
            created++;
            foreach (var role in new[] { LenseeRoles.Admin, LenseeRoles.ERPAdmin, LenseeRoles.WarehouseClerk })
            {
                var id = Guid.NewGuid();
                _notifications.NotificationLogs.Add(new NotificationLog { Id = id, AlertType = "Replenishment", Message = $"Replenishment {operation.OperationNumber} was created as a Draft. Review and confirm the warehouse transfer.", ReferenceId = operation.Id, ReferenceType = "Operation", ReferenceCode = operation.OperationNumber, TargetRole = role, Channel = "InApp", CreatedAt = now, NotificationNumber = $"NOT-{id:N}".ToUpperInvariant() });
            }
            }
            _operations.ReplenishmentRuns.Add(new ReplenishmentRun { Id = Guid.NewGuid(), RunKey = key, CairoDate = cairoDate, Trigger = trigger, Status = "Completed", StartedAt = now, CompletedAt = now, CreatedOperations = created, UncoveredQuantity = uncovered });
            await _operations.SaveChangesAsync(cancellationToken);
            foreach (var pending in pendingCurrentVersions) pending.Operation.CurrentVersionId = pending.VersionId;
            await _operations.SaveChangesAsync(cancellationToken);
            await _notifications.SaveChangesAsync(cancellationToken);
            result = new TargetReplenishmentRunResult(created, uncovered, false);
        }, cancellationToken, _inventory, _catalog, _notifications);

        return result ?? new TargetReplenishmentRunResult(0, 0, false);
    }
}

public sealed record TargetReplenishmentRunResult(int CreatedOperations, int UncoveredQuantity, bool AlreadyCompleted);
