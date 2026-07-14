using Lensee.Modules.Inventory.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Modules.Inventory.Services;

public sealed class StockLedgerService
{
    private readonly InventoryDbContext _dbContext;
    private readonly IClock _clock;

    public StockLedgerService(InventoryDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<InventoryBatch> ReceiveAsync(
        Guid locationId,
        Guid skuId,
        int quantity,
        Guid userId,
        string? lotNumber = null,
        DateOnly? expiryDate = null,
        string? notes = null,
        Guid? referenceOperationId = null,
        CancellationToken cancellationToken = default)
    {
        return await ReceiveInternalAsync(
            locationId,
            skuId,
            quantity,
            userId,
            InventoryTransactionTypes.Receipt,
            lotNumber,
            expiryDate,
            notes,
            referenceOperationId,
            cancellationToken);
    }

    public async Task<InventoryBatch> ReceiveSupplyAsync(
        Guid locationId,
        Guid skuId,
        int quantity,
        Guid userId,
        string? lotNumber = null,
        DateOnly? expiryDate = null,
        string? notes = null,
        Guid? referenceOperationId = null,
        CancellationToken cancellationToken = default)
    {
        return await ReceiveInternalAsync(
            locationId,
            skuId,
            quantity,
            userId,
            InventoryTransactionTypes.SupplyIn,
            lotNumber,
            expiryDate,
            notes,
            referenceOperationId,
            cancellationToken);
    }

    public async Task<InventoryBatch> ReceiveReturnAsync(
        Guid locationId,
        Guid skuId,
        int quantity,
        Guid userId,
        string? lotNumber = null,
        DateOnly? expiryDate = null,
        string? notes = null,
        Guid? referenceOperationId = null,
        CancellationToken cancellationToken = default)
    {
        return await ReceiveInternalAsync(
            locationId,
            skuId,
            quantity,
            userId,
            InventoryTransactionTypes.ReturnIn,
            lotNumber,
            expiryDate,
            notes,
            referenceOperationId,
            cancellationToken);
    }

    public async Task<InventoryBatch> ReceiveChangeOutAsync(
        Guid locationId,
        Guid skuId,
        int quantity,
        Guid userId,
        string? lotNumber = null,
        DateOnly? expiryDate = null,
        string? notes = null,
        Guid? referenceOperationId = null,
        CancellationToken cancellationToken = default)
    {
        return await ReceiveInternalAsync(
            locationId,
            skuId,
            quantity,
            userId,
            InventoryTransactionTypes.ChangeOut,
            lotNumber,
            expiryDate,
            notes,
            referenceOperationId,
            cancellationToken);
    }

    public async Task AdjustStocktakeAsync(
        Guid locationId,
        Guid skuId,
        int delta,
        Guid userId,
        Guid? referenceOperationId = null,
        CancellationToken cancellationToken = default)
    {
        if (delta == 0)
        {
            return;
        }

        if (delta > 0)
        {
            var now = _clock.EgyptNow;
            var batch = await FindBatchAsync(locationId, skuId, null, null, cancellationToken);
            if (batch is null)
            {
                batch = new InventoryBatch
                {
                    Id = Guid.NewGuid(),
                    LocationId = locationId,
                    SkuId = skuId,
                    Quantity = 0,
                    Notes = "Stocktake positive adjustment",
                    CreatedFrom = referenceOperationId,
                    CreatedBy = userId,
                    CreatedAt = now
                };
                _dbContext.InventoryBatches.Add(batch);
            }

            batch.Quantity += delta;
            var balance = await GetOrCreateBalanceAsync(locationId, skuId, cancellationToken);
            ApplyAvailableDelta(balance, delta, now);
            AddTransaction(locationId, skuId, InventoryTransactionTypes.StocktakeAdjustment, delta, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await IssueFefoAsync(
            locationId,
            skuId,
            Math.Abs(delta),
            InventoryTransactionTypes.StocktakeAdjustment,
            userId,
            referenceOperationId,
            cancellationToken: cancellationToken);
    }

    public async Task AdjustStocktakeBatchAsync(
        Guid locationId,
        Guid skuId,
        string? lotNumber,
        DateOnly? expiryDate,
        int delta,
        Guid userId,
        Guid? referenceOperationId = null,
        CancellationToken cancellationToken = default)
    {
        if (delta == 0)
        {
            return;
        }

        var now = _clock.EgyptNow;
        var batch = await FindBatchAsync(locationId, skuId, lotNumber, expiryDate, cancellationToken);
        if (batch is null)
        {
            if (delta < 0)
            {
                throw new InvalidOperationException("Selected batch stock is insufficient.");
            }

            batch = new InventoryBatch
            {
                Id = Guid.NewGuid(),
                LocationId = locationId,
                SkuId = skuId,
                LotNumber = NormalizeBlank(lotNumber),
                ExpiryDate = expiryDate,
                Quantity = 0,
                Notes = "Stocktake positive adjustment",
                CreatedFrom = referenceOperationId,
                CreatedBy = userId,
                CreatedAt = now
            };
            _dbContext.InventoryBatches.Add(batch);
        }

        var nextBatchQuantity = batch.Quantity + delta;
        if (nextBatchQuantity < 0)
        {
            throw new InvalidOperationException("Selected batch stock cannot be negative.");
        }

        batch.Quantity = nextBatchQuantity;
        var balance = await GetOrCreateBalanceAsync(locationId, skuId, cancellationToken);
        ApplyAvailableDelta(balance, delta, now);
        AddTransaction(locationId, skuId, InventoryTransactionTypes.StocktakeAdjustment, delta, userId, referenceOperationId, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<InventoryBatch> ReceiveInternalAsync(
        Guid locationId,
        Guid skuId,
        int quantity,
        Guid userId,
        string transactionType,
        string? lotNumber = null,
        DateOnly? expiryDate = null,
        string? notes = null,
        Guid? referenceOperationId = null,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        EnsureTransactionType(transactionType);
        if (transactionType != InventoryTransactionTypes.Receipt &&
            transactionType != InventoryTransactionTypes.SupplyIn &&
            transactionType != InventoryTransactionTypes.ChangeOut &&
            transactionType != InventoryTransactionTypes.ReturnIn)
        {
            throw new InvalidOperationException($"{transactionType} is not a receiving transaction type.");
        }

        var now = _clock.EgyptNow;
        var batch = await FindBatchAsync(locationId, skuId, lotNumber, expiryDate, cancellationToken);
        if (batch is null)
        {
            batch = new InventoryBatch
            {
                Id = Guid.NewGuid(),
                LocationId = locationId,
                SkuId = skuId,
                LotNumber = NormalizeBlank(lotNumber),
                ExpiryDate = expiryDate,
                Quantity = 0,
                Notes = NormalizeBlank(notes),
                CreatedFrom = referenceOperationId,
                CreatedBy = userId,
                CreatedAt = now
            };
            _dbContext.InventoryBatches.Add(batch);
        }
        else if (!string.IsNullOrWhiteSpace(notes))
        {
            batch.Notes = notes.Trim();
        }

        batch.Quantity += quantity;
        var balance = await GetOrCreateBalanceAsync(locationId, skuId, cancellationToken);
        ApplyAvailableDelta(balance, quantity, now);
        AddTransaction(locationId, skuId, transactionType, quantity, userId, referenceOperationId, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return batch;
    }

    public async Task<IReadOnlyList<BatchAllocation>> IssueFefoAsync(
        Guid locationId,
        Guid skuId,
        int quantity,
        string transactionType,
        Guid userId,
        Guid? referenceOperationId = null,
        DateOnly? minimumExpiryDate = null,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        EnsureTransactionType(transactionType);
        if (transactionType is InventoryTransactionTypes.Receipt or InventoryTransactionTypes.ChangeOut or InventoryTransactionTypes.ReturnIn or InventoryTransactionTypes.SupplyIn)
        {
            throw new InvalidOperationException($"{transactionType} is not an issuing transaction type.");
        }

        var now = _clock.EgyptNow;
        var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
        if (balance is null || balance.AvailableQty < quantity)
        {
            throw new InvalidOperationException("Available stock is insufficient.");
        }

        var batches = await LoadFefoBatchesAsync(locationId, skuId, minimumExpiryDate, cancellationToken);
        var remaining = quantity;
        var allocations = new List<BatchAllocation>();
        foreach (var batch in batches)
        {
            if (remaining == 0)
            {
                break;
            }

            var allocated = Math.Min(batch.Quantity, remaining);
            batch.Quantity -= allocated;
            remaining -= allocated;
            allocations.Add(new BatchAllocation(batch.Id, allocated));
        }

        if (remaining > 0)
        {
            throw new InvalidOperationException("Batch stock is insufficient.");
        }

        ApplyAvailableDelta(balance, -quantity, now);
        AddTransaction(locationId, skuId, transactionType, -quantity, userId, referenceOperationId, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return allocations;
    }

    public async Task<BatchAllocation> IssueSelectedBatchAsync(
        Guid locationId,
        Guid skuId,
        int quantity,
        string transactionType,
        string? lotNumber,
        DateOnly? expiryDate,
        Guid userId,
        Guid? referenceOperationId = null,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        EnsureTransactionType(transactionType);
        var batch = await LoadSelectedBatchAsync(locationId, skuId, lotNumber, expiryDate, cancellationToken);
        if (batch is null || batch.Quantity < quantity)
        {
            throw new InvalidOperationException("Selected batch stock is insufficient.");
        }

        var now = _clock.EgyptNow;
        var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
        if (balance is null || balance.AvailableQty < quantity)
        {
            throw new InvalidOperationException("Available stock is insufficient.");
        }

        batch.Quantity -= quantity;
        ApplyAvailableDelta(balance, -quantity, now);
        AddTransaction(locationId, skuId, transactionType, -quantity, userId, referenceOperationId, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new BatchAllocation(batch.Id, quantity, batch.LotNumber, batch.ExpiryDate);
    }

    public async Task<IReadOnlyList<PieceAllocation>> IssuePiecesFefoAsync(
        Guid locationId,
        Guid skuId,
        int pieceQuantity,
        int piecesPerPack,
        Guid userId,
        Guid? referenceOperationId = null,
        DateOnly? minimumExpiryDate = null,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(pieceQuantity, nameof(pieceQuantity));
        EnsurePositive(piecesPerPack, nameof(piecesPerPack));

        var today = DateOnly.FromDateTime(_clock.EgyptNow);
        var remaining = pieceQuantity;
        var allocations = new List<PieceAllocation>();
        var looseLots = await _dbContext.OpenedPieceLots
            .Where(lot =>
                lot.LocationId == locationId &&
                lot.SkuId == skuId &&
                lot.LoosePieceQuantity > 0 &&
                (lot.PieceExpiryDate == null || lot.PieceExpiryDate >= today))
            .OrderBy(lot => lot.PieceExpiryDate == null)
            .ThenBy(lot => lot.PieceExpiryDate)
            .ThenBy(lot => lot.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var lot in looseLots)
        {
            if (remaining == 0)
            {
                break;
            }

            var take = Math.Min(lot.LoosePieceQuantity, remaining);
            lot.LoosePieceQuantity -= take;
            remaining -= take;
            allocations.Add(new PieceAllocation(lot.Id, null, take, lot.LotNumber, lot.BatchExpiryDate, lot.PieceExpiryDate, false));
        }

        while (remaining > 0)
        {
            var batchAllocations = await IssueFefoAsync(
                locationId,
                skuId,
                1,
                InventoryTransactionTypes.Sale,
                userId,
                referenceOperationId,
                null,
                cancellationToken);
            var batchAllocation = batchAllocations.Single();
            var batch = await _dbContext.InventoryBatches
                .AsNoTracking()
                .FirstAsync(value => value.Id == batchAllocation.BatchId, cancellationToken);
            var pieceExpiryDate = batch.ExpiryDate;
            var take = Math.Min(piecesPerPack, remaining);
            remaining -= take;
            var leftover = piecesPerPack - take;
            var lot = new OpenedPieceLot
            {
                Id = Guid.NewGuid(),
                LocationId = locationId,
                SkuId = skuId,
                SourceBatchId = batch.Id,
                LotNumber = batch.LotNumber,
                BatchExpiryDate = batch.ExpiryDate,
                OpenedDate = today,
                PieceExpiryDate = pieceExpiryDate,
                LoosePieceQuantity = leftover,
                CreatedFrom = referenceOperationId,
                CreatedBy = userId,
                CreatedAt = _clock.EgyptNow
            };
            _dbContext.OpenedPieceLots.Add(lot);
            allocations.Add(new PieceAllocation(lot.Id, batch.Id, take, batch.LotNumber, batch.ExpiryDate, pieceExpiryDate, true));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return allocations;
    }

    public async Task<BatchAllocation> ReserveSelectedBatchInWarehouseAsync(
        Guid locationId,
        Guid skuId,
        int quantity,
        string? lotNumber,
        DateOnly? expiryDate,
        Guid userId,
        Guid? referenceOperationId = null,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        var batch = await LoadSelectedBatchAsync(locationId, skuId, lotNumber, expiryDate, cancellationToken);
        if (batch is null || batch.Quantity < quantity)
        {
            throw new InvalidOperationException("Selected batch stock is insufficient.");
        }

        await ReserveInWarehouseAsync(locationId, skuId, quantity, userId, referenceOperationId, cancellationToken);
        return new BatchAllocation(batch.Id, quantity, batch.LotNumber, batch.ExpiryDate);
    }

    public async Task<IReadOnlyList<PieceAllocation>> IssuePiecesFromSelectionAsync(
        Guid locationId,
        Guid skuId,
        int pieceQuantity,
        int piecesPerPack,
        string? lotNumber,
        DateOnly? expiryDate,
        Guid userId,
        Guid? referenceOperationId = null,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(pieceQuantity, nameof(pieceQuantity));
        EnsurePositive(piecesPerPack, nameof(piecesPerPack));

        var today = DateOnly.FromDateTime(_clock.EgyptNow);
        var normalizedLot = NormalizeBlank(lotNumber);
        var remaining = pieceQuantity;
        var allocations = new List<PieceAllocation>();
        var looseLots = await _dbContext.OpenedPieceLots
            .Where(lot =>
                lot.LocationId == locationId &&
                lot.SkuId == skuId &&
                lot.LotNumber == normalizedLot &&
                lot.BatchExpiryDate == expiryDate &&
                lot.LoosePieceQuantity > 0 &&
                (lot.PieceExpiryDate == null || lot.PieceExpiryDate >= today))
            .OrderBy(lot => lot.CreatedAt)
            .ToListAsync(cancellationToken);

        foreach (var lot in looseLots)
        {
            if (remaining == 0)
            {
                break;
            }

            var take = Math.Min(lot.LoosePieceQuantity, remaining);
            lot.LoosePieceQuantity -= take;
            remaining -= take;
            allocations.Add(new PieceAllocation(lot.Id, null, take, lot.LotNumber, lot.BatchExpiryDate, lot.PieceExpiryDate, false));
        }

        if (remaining > 0)
        {
            var packsToOpen = (int)Math.Ceiling(remaining / (decimal)piecesPerPack);
            var batch = await LoadSelectedBatchAsync(locationId, skuId, lotNumber, expiryDate, cancellationToken);
            if (batch is null || batch.Quantity < packsToOpen)
            {
                throw new InvalidOperationException("Selected stock does not have enough pieces.");
            }

            var now = _clock.EgyptNow;
            var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
            if (balance is null || balance.AvailableQty < packsToOpen)
            {
                throw new InvalidOperationException("Available stock is insufficient.");
            }

            batch.Quantity -= packsToOpen;
            ApplyAvailableDelta(balance, -packsToOpen, now);
            AddTransaction(locationId, skuId, InventoryTransactionTypes.Sale, -packsToOpen, userId, referenceOperationId, now);

            var openedPieces = packsToOpen * piecesPerPack;
            var take = remaining;
            var leftover = openedPieces - take;
            remaining = 0;

            var lot = new OpenedPieceLot
            {
                Id = Guid.NewGuid(),
                LocationId = locationId,
                SkuId = skuId,
                SourceBatchId = batch.Id,
                LotNumber = batch.LotNumber,
                BatchExpiryDate = batch.ExpiryDate,
                OpenedDate = today,
                PieceExpiryDate = batch.ExpiryDate,
                LoosePieceQuantity = leftover,
                CreatedFrom = referenceOperationId,
                CreatedBy = userId,
                CreatedAt = now
            };
            _dbContext.OpenedPieceLots.Add(lot);
            allocations.Add(new PieceAllocation(lot.Id, batch.Id, take, batch.LotNumber, batch.ExpiryDate, batch.ExpiryDate, true));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return allocations;
    }

    public async Task ReserveInWarehouseAsync(Guid locationId, Guid skuId, int quantity, Guid userId, Guid? referenceOperationId = null, CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        var now = _clock.EgyptNow;
        if (!_dbContext.Database.IsRelational())
        {
            var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
            if (balance is null || balance.AvailableQty < quantity)
            {
                throw new InvalidOperationException("Available stock is insufficient.");
            }

            ApplyAvailableDelta(balance, -quantity, now);
            balance.ReservedInWarehouseQty += quantity;
            AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveInWarehouse, -quantity, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var updatedRows = await _dbContext.StockBalances
            .Where(balance =>
                balance.LocationId == locationId &&
                balance.SkuId == skuId &&
                balance.AvailableQty >= quantity)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(balance => balance.AvailableQty, balance => balance.AvailableQty - quantity)
                    .SetProperty(balance => balance.ReservedInWarehouseQty, balance => balance.ReservedInWarehouseQty + quantity)
                    .SetProperty(balance => balance.RowVersion, balance => balance.RowVersion + 1)
                    .SetProperty(balance => balance.LastUpdated, now),
                cancellationToken);
        if (updatedRows == 0)
        {
            throw new InvalidOperationException("Available stock is insufficient.");
        }

        await ReloadTrackedBalanceAsync(locationId, skuId, cancellationToken);
        AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveInWarehouse, -quantity, userId, referenceOperationId, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BatchAllocation>> ReserveInWarehouseFefoAsync(
        Guid locationId,
        Guid skuId,
        int quantity,
        Guid userId,
        Guid? referenceOperationId = null,
        DateOnly? minimumExpiryDate = null,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        var allocations = await PlanReserveInWarehouseFefoAsync(locationId, skuId, quantity, minimumExpiryDate, cancellationToken);

        await ReserveInWarehouseAsync(locationId, skuId, quantity, userId, referenceOperationId, cancellationToken);
        return allocations;
    }

    public async Task<IReadOnlyList<BatchAllocation>> PlanReserveInWarehouseFefoAsync(
        Guid locationId,
        Guid skuId,
        int quantity,
        DateOnly? minimumExpiryDate = null,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
        if (balance is null || balance.AvailableQty < quantity)
        {
            throw new InvalidOperationException("Available stock is insufficient.");
        }

        var batches = await LoadFefoBatchesAsync(locationId, skuId, minimumExpiryDate, cancellationToken);
        var remaining = quantity;
        var allocations = new List<BatchAllocation>();
        foreach (var batch in batches)
        {
            if (remaining == 0)
            {
                break;
            }

            var allocated = Math.Min(batch.Quantity, remaining);
            remaining -= allocated;
            allocations.Add(new BatchAllocation(batch.Id, allocated, batch.LotNumber, batch.ExpiryDate));
        }

        if (remaining > 0)
        {
            throw new InvalidOperationException("Batch stock is insufficient.");
        }

        return allocations;
    }

    public async Task CommitReservedInWarehouseOutAsync(
        Guid locationId,
        Guid skuId,
        IReadOnlyCollection<BatchAllocation> allocations,
        Guid userId,
        Guid? referenceOperationId = null,
        string transactionType = InventoryTransactionTypes.SupplyOut,
        CancellationToken cancellationToken = default)
    {
        EnsureTransactionType(transactionType);
        if (allocations.Count == 0)
        {
            throw new ArgumentException("At least one batch allocation is required.", nameof(allocations));
        }

        var quantity = allocations.Sum(allocation => allocation.Quantity);
        EnsurePositive(quantity, nameof(quantity));
        var now = _clock.EgyptNow;
        var balance = await GetFreshBalanceAsync(locationId, skuId, cancellationToken);
        if (balance is null || balance.ReservedInWarehouseQty < quantity)
        {
            throw new InvalidOperationException("Reserved stock is insufficient.");
        }

        var batchIds = allocations.Select(allocation => allocation.BatchId).ToArray();
        var batches = await _dbContext.InventoryBatches
            .Where(batch => batch.LocationId == locationId && batch.SkuId == skuId && batchIds.Contains(batch.Id))
            .ToDictionaryAsync(batch => batch.Id, cancellationToken);

        foreach (var allocation in allocations)
        {
            if (!batches.TryGetValue(allocation.BatchId, out var batch))
            {
                throw new InvalidOperationException("Allocated batch was not found.");
            }

            if (batch.Quantity < allocation.Quantity)
            {
                throw new InvalidOperationException("Allocated batch stock is insufficient.");
            }

            batch.Quantity -= allocation.Quantity;
        }

        balance.ReservedInWarehouseQty -= quantity;
        balance.RowVersion++;
        balance.LastUpdated = now;
        AddTransaction(locationId, skuId, transactionType, -quantity, userId, referenceOperationId, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MoveReservedInWarehouseToRepresentativeAsync(
        Guid locationId,
        Guid skuId,
        IReadOnlyCollection<BatchAllocation> allocations,
        Guid userId,
        Guid? referenceOperationId = null,
        CancellationToken cancellationToken = default)
    {
        if (allocations.Count == 0)
        {
            throw new ArgumentException("At least one batch allocation is required.", nameof(allocations));
        }

        var quantity = allocations.Sum(allocation => allocation.Quantity);
        EnsurePositive(quantity, nameof(quantity));
        var now = _clock.EgyptNow;
        var balance = await GetFreshBalanceAsync(locationId, skuId, cancellationToken);
        if (balance is null || balance.ReservedInWarehouseQty < quantity)
        {
            throw new InvalidOperationException("Reserved stock is insufficient.");
        }

        var batchIds = allocations.Select(allocation => allocation.BatchId).ToArray();
        var batches = await _dbContext.InventoryBatches
            .Where(batch => batch.LocationId == locationId && batch.SkuId == skuId && batchIds.Contains(batch.Id))
            .ToDictionaryAsync(batch => batch.Id, cancellationToken);

        foreach (var allocation in allocations)
        {
            if (!batches.TryGetValue(allocation.BatchId, out var batch))
            {
                throw new InvalidOperationException("Allocated batch was not found.");
            }

            if (batch.Quantity < allocation.Quantity)
            {
                throw new InvalidOperationException("Allocated batch stock is insufficient.");
            }

            batch.Quantity -= allocation.Quantity;
        }

        balance.ReservedInWarehouseQty -= quantity;
        balance.ReservedWithRepQty += quantity;
        balance.RowVersion++;
        balance.LastUpdated = now;
        AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveWithRep, -quantity, userId, referenceOperationId, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseInWarehouseAsync(Guid locationId, Guid skuId, int quantity, Guid userId, Guid? referenceOperationId = null, CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        var now = _clock.EgyptNow;
        var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
        if (balance is null || balance.ReservedInWarehouseQty < quantity)
        {
            throw new InvalidOperationException("Reserved stock is insufficient.");
        }

        balance.ReservedInWarehouseQty -= quantity;
        ApplyAvailableDelta(balance, quantity, now);
        AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveReleaseInWarehouse, quantity, userId, referenceOperationId, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ReleaseInWarehouseUpToAsync(Guid locationId, Guid skuId, int quantity, Guid userId, Guid? referenceOperationId = null, CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        var now = _clock.EgyptNow;
        var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
        var released = Math.Min(balance?.ReservedInWarehouseQty ?? 0, quantity);
        if (released == 0 || balance is null)
        {
            return 0;
        }

        balance.ReservedInWarehouseQty -= released;
        ApplyAvailableDelta(balance, released, now);
        AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveReleaseInWarehouse, released, userId, referenceOperationId, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return released;
    }

    public async Task ReserveWithRepAsync(Guid locationId, Guid skuId, int quantity, Guid userId, Guid? referenceOperationId = null, CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        var now = _clock.EgyptNow;
        if (!_dbContext.Database.IsRelational())
        {
            var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
            if (balance is null || balance.AvailableQty < quantity)
            {
                throw new InvalidOperationException("Available stock is insufficient.");
            }

            ApplyAvailableDelta(balance, -quantity, now);
            balance.ReservedWithRepQty += quantity;
            AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveWithRep, -quantity, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        var updatedRows = await _dbContext.StockBalances
            .Where(balance =>
                balance.LocationId == locationId &&
                balance.SkuId == skuId &&
                balance.AvailableQty >= quantity)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(balance => balance.AvailableQty, balance => balance.AvailableQty - quantity)
                    .SetProperty(balance => balance.ReservedWithRepQty, balance => balance.ReservedWithRepQty + quantity)
                    .SetProperty(balance => balance.RowVersion, balance => balance.RowVersion + 1)
                    .SetProperty(balance => balance.LastUpdated, now),
                cancellationToken);
        if (updatedRows == 0)
        {
            throw new InvalidOperationException("Available stock is insufficient.");
        }

        await ReloadTrackedBalanceAsync(locationId, skuId, cancellationToken);
        AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveWithRep, -quantity, userId, referenceOperationId, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseWithRepAsync(Guid locationId, Guid skuId, int quantity, Guid userId, Guid? referenceOperationId = null, CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        var now = _clock.EgyptNow;
        var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
        if (balance is null || balance.ReservedWithRepQty < quantity)
        {
            throw new InvalidOperationException("Reserved stock is insufficient.");
        }

        balance.ReservedWithRepQty -= quantity;
        ApplyAvailableDelta(balance, quantity, now);
        AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveReleaseWithRep, quantity, userId, referenceOperationId, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> ReleaseWithRepUpToAsync(Guid locationId, Guid skuId, int quantity, Guid userId, Guid? referenceOperationId = null, CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        var now = _clock.EgyptNow;
        var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
        var released = Math.Min(balance?.ReservedWithRepQty ?? 0, quantity);
        if (released == 0 || balance is null)
        {
            return 0;
        }

        balance.ReservedWithRepQty -= released;
        ApplyAvailableDelta(balance, released, now);
        AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveReleaseWithRep, released, userId, referenceOperationId, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return released;
    }

    private async Task<InventoryBatch?> FindBatchAsync(Guid locationId, Guid skuId, string? lotNumber, DateOnly? expiryDate, CancellationToken cancellationToken) =>
        await _dbContext.InventoryBatches.FirstOrDefaultAsync(batch =>
            batch.LocationId == locationId &&
            batch.SkuId == skuId &&
            batch.LotNumber == NormalizeBlank(lotNumber) &&
            batch.ExpiryDate == expiryDate,
            cancellationToken);

    private async Task<List<InventoryBatch>> LoadFefoBatchesAsync(Guid locationId, Guid skuId, DateOnly? minimumExpiryDate, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_clock.EgyptNow);
        return await _dbContext.InventoryBatches
            .Where(batch =>
                batch.LocationId == locationId &&
                batch.SkuId == skuId &&
                batch.Quantity > 0 &&
                (batch.ExpiryDate == null || batch.ExpiryDate >= today))
            .OrderBy(batch => batch.ExpiryDate == null)
            .ThenBy(batch => batch.ExpiryDate)
            .ThenBy(batch => batch.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private async Task<InventoryBatch?> LoadSelectedBatchAsync(Guid locationId, Guid skuId, string? lotNumber, DateOnly? expiryDate, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_clock.EgyptNow);
        var normalizedLot = NormalizeBlank(lotNumber);
        return await _dbContext.InventoryBatches.FirstOrDefaultAsync(batch =>
            batch.LocationId == locationId &&
            batch.SkuId == skuId &&
            batch.LotNumber == normalizedLot &&
            batch.ExpiryDate == expiryDate &&
            batch.Quantity > 0 &&
            (batch.ExpiryDate == null || batch.ExpiryDate >= today),
            cancellationToken);
    }

    private async Task<StockBalance?> GetBalanceAsync(Guid locationId, Guid skuId, CancellationToken cancellationToken) =>
        await _dbContext.StockBalances.FirstOrDefaultAsync(balance => balance.LocationId == locationId && balance.SkuId == skuId, cancellationToken);

    private async Task<StockBalance?> GetFreshBalanceAsync(Guid locationId, Guid skuId, CancellationToken cancellationToken)
    {
        var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
        if (balance is not null)
        {
            await _dbContext.Entry(balance).ReloadAsync(cancellationToken);
        }

        return balance;
    }

    private async Task ReloadTrackedBalanceAsync(Guid locationId, Guid skuId, CancellationToken cancellationToken)
    {
        var trackedBalance = _dbContext.ChangeTracker
            .Entries<StockBalance>()
            .FirstOrDefault(entry => entry.Entity.LocationId == locationId && entry.Entity.SkuId == skuId);
        if (trackedBalance is not null)
        {
            await trackedBalance.ReloadAsync(cancellationToken);
        }
    }

    private async Task<StockBalance> GetOrCreateBalanceAsync(Guid locationId, Guid skuId, CancellationToken cancellationToken)
    {
        var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
        if (balance is not null)
        {
            return balance;
        }

        balance = new StockBalance
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            SkuId = skuId,
            AvailableQty = 0,
            ReservedInWarehouseQty = 0,
            ReservedWithRepQty = 0,
            RowVersion = 0,
            LastUpdated = _clock.EgyptNow
        };
        _dbContext.StockBalances.Add(balance);
        return balance;
    }

    private static void ApplyAvailableDelta(StockBalance balance, int delta, DateTime now)
    {
        var nextAvailable = balance.AvailableQty + delta;
        if (nextAvailable < 0)
        {
            throw new InvalidOperationException("Available stock cannot be negative.");
        }

        if (balance.ReservedInWarehouseQty < 0 || balance.ReservedWithRepQty < 0)
        {
            throw new InvalidOperationException("Reserved stock cannot be negative.");
        }

        balance.AvailableQty = nextAvailable;
        balance.RowVersion++;
        balance.LastUpdated = now;
    }

    private void AddTransaction(Guid locationId, Guid skuId, string transactionType, int quantityChange, Guid userId, Guid? referenceOperationId, DateTime now)
    {
        EnsureTransactionType(transactionType);
        _dbContext.StockTransactions.Add(new StockTransaction
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            SkuId = skuId,
            TransactionType = transactionType,
            QuantityChange = quantityChange,
            ReferenceOperationId = referenceOperationId,
            UserId = userId,
            CreatedAt = now
        });
    }

    private static void EnsurePositive(int value, string name)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, "Quantity must be greater than zero.");
        }
    }

    private static void EnsureTransactionType(string transactionType)
    {
        if (!InventoryTransactionTypes.IsValid(transactionType))
        {
            throw new InvalidOperationException($"Unsupported inventory transaction type: {transactionType}.");
        }
    }

    private static string? NormalizeBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

}

public sealed record BatchAllocation(Guid BatchId, int Quantity, string? LotNumber = null, DateOnly? ExpiryDate = null);

public sealed record PieceAllocation(Guid OpenedPieceLotId, Guid? SourceBatchId, int Quantity, string? LotNumber, DateOnly? BatchExpiryDate, DateOnly? PieceExpiryDate, bool OpenedPack);
