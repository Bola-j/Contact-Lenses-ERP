using Lensee.Modules.Inventory.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Modules.Inventory.Services;

public sealed class StockLedgerService
{
    private readonly InventoryDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IReservedPackAllocationReader? _reservedPackReader;

    public StockLedgerService(InventoryDbContext dbContext, IClock clock, IReservedPackAllocationReader? reservedPackReader = null)
    {
        _dbContext = dbContext;
        _clock = clock;
        _reservedPackReader = reservedPackReader;
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

    public async Task<InventoryReceiptLedgerResult> ReceiveReceiptWithLedgerAsync(
        Guid locationId,
        Guid skuId,
        int quantity,
        Guid userId,
        string? lotNumber = null,
        DateOnly? expiryDate = null,
        string? notes = null,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        InventoryReceiptLedgerResult? result = null;
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
            var now = _clock.EgyptNow;
            var batch = await FindBatchAsync(locationId, skuId, lotNumber, expiryDate, cancellationToken);
            if (batch is null)
            {
                batch = new InventoryBatch { Id = Guid.NewGuid(), LocationId = locationId, SkuId = skuId, LotNumber = NormalizeBlank(lotNumber), ExpiryDate = expiryDate, Quantity = 0, Notes = NormalizeBlank(notes), CreatedBy = userId, CreatedAt = now };
                _dbContext.InventoryBatches.Add(batch);
            }
            else if (!string.IsNullOrWhiteSpace(notes))
            {
                batch.Notes = notes.Trim();
            }

            batch.Quantity += quantity;
            var balance = await GetOrCreateBalanceAsync(locationId, skuId, cancellationToken);
            ApplyAvailableDelta(balance, quantity, now);
            var transaction = AddTransaction(locationId, skuId, InventoryTransactionTypes.Receipt, quantity, InventoryQuantityUnits.Pack, userId, null, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            result = new InventoryReceiptLedgerResult(batch, transaction.Id);
        }, cancellationToken);
        return result!;
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

    public async Task ReceiveSupplyBatchAsync(
        Guid locationId,
        IReadOnlyCollection<SupplyReceiptLine> lines,
        Guid userId,
        Guid referenceOperationId,
        CancellationToken cancellationToken = default)
    {
        if (lines.Count == 0) return;
        if (lines.Any(line => line.Quantity <= 0)) throw new ArgumentOutOfRangeException(nameof(lines), "Every quantity must be greater than zero.");

        await ExecuteRelationalWriteAsync(async () =>
        {
            var skuIds = lines.Select(line => line.SkuId).Distinct().OrderBy(id => id).ToArray();
            await AcquireStockLocksAsync(skuIds.Select(skuId => new StockKey(locationId, skuId)), cancellationToken);

            if (_dbContext.Database.IsRelational())
            {
                var lockKey = $"inventory-supply:{locationId:N}";
                await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"select pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                    cancellationToken);
            }

            var now = _clock.EgyptNow;
            var existingBatches = await _dbContext.InventoryBatches
                .Where(batch => batch.LocationId == locationId && skuIds.Contains(batch.SkuId))
                .ToListAsync(cancellationToken);
            var batchesByKey = existingBatches
                .GroupBy(batch => (batch.SkuId, LotNumber: NormalizeBlank(batch.LotNumber), batch.ExpiryDate))
                .ToDictionary(group => group.Key, group => group.OrderBy(batch => batch.Id).First());
            var balances = await _dbContext.StockBalances
                .Where(balance => balance.LocationId == locationId && skuIds.Contains(balance.SkuId))
                .ToDictionaryAsync(balance => balance.SkuId, cancellationToken);

            foreach (var line in lines.OrderBy(line => line.SkuId).ThenBy(line => line.ExpiryDate).ThenBy(line => line.LotNumber))
            {
                var key = (line.SkuId, LotNumber: NormalizeBlank(line.LotNumber), line.ExpiryDate);
                if (!batchesByKey.TryGetValue(key, out var batch))
                {
                    batch = new InventoryBatch
                    {
                        Id = Guid.NewGuid(),
                        LocationId = locationId,
                        SkuId = line.SkuId,
                        LotNumber = key.LotNumber,
                        ExpiryDate = line.ExpiryDate,
                        Quantity = 0,
                        Notes = NormalizeBlank(line.Notes),
                        CreatedFrom = referenceOperationId,
                        CreatedBy = userId,
                        CreatedAt = now
                    };
                    batchesByKey.Add(key, batch);
                    _dbContext.InventoryBatches.Add(batch);
                }
                else if (!string.IsNullOrWhiteSpace(line.Notes))
                {
                    batch.Notes = line.Notes.Trim();
                }
                batch.Quantity += line.Quantity;

                if (!balances.TryGetValue(line.SkuId, out var balance))
                {
                    balance = new StockBalance
                    {
                        Id = Guid.NewGuid(),
                        LocationId = locationId,
                        SkuId = line.SkuId,
                        AvailableQty = 0,
                        ReservedInWarehouseQty = 0,
                        ReservedWithRepQty = 0,
                        RowVersion = 0,
                        LastUpdated = now
                    };
                    balances.Add(line.SkuId, balance);
                    _dbContext.StockBalances.Add(balance);
                }
                ApplyAvailableDelta(balance, line.Quantity, now);
                AddTransaction(locationId, line.SkuId, InventoryTransactionTypes.SupplyIn, line.Quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
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

    public async Task ReceiveReturnPiecesAsync(
        Guid locationId,
        Guid skuId,
        int pieceQuantity,
        Guid userId,
        string? lotNumber = null,
        DateOnly? expiryDate = null,
        string? notes = null,
        Guid? referenceOperationId = null,
        CancellationToken cancellationToken = default)
    {
        await ReceivePiecesAsync(locationId, skuId, pieceQuantity, userId, InventoryTransactionTypes.ReturnIn, lotNumber, expiryDate, notes, referenceOperationId, cancellationToken);
    }

    public async Task ReceivePiecesAsync(
        Guid locationId,
        Guid skuId,
        int pieceQuantity,
        Guid userId,
        string transactionType,
        string? lotNumber = null,
        DateOnly? expiryDate = null,
        string? notes = null,
        Guid? referenceOperationId = null,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(pieceQuantity, nameof(pieceQuantity));
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
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

            _dbContext.OpenedPieceLots.Add(new OpenedPieceLot
            {
                Id = Guid.NewGuid(),
                LocationId = locationId,
                SkuId = skuId,
                SourceBatchId = batch.Id,
                LotNumber = batch.LotNumber,
                BatchExpiryDate = batch.ExpiryDate,
                OpenedDate = DateOnly.FromDateTime(now),
                PieceExpiryDate = batch.ExpiryDate,
                LoosePieceQuantity = pieceQuantity,
                CreatedFrom = referenceOperationId,
                CreatedBy = userId,
                CreatedAt = now
            });
            AddTransaction(locationId, skuId, transactionType, pieceQuantity, InventoryQuantityUnits.Piece, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<InventoryBatch> ReceiveQuarantinedReturnAsync(
        Guid locationId,
        Guid skuId,
        int quantity,
        Guid userId,
        string? lotNumber = null,
        DateOnly? expiryDate = null,
        string? notes = null,
        Guid? referenceOperationId = null,
        CancellationToken cancellationToken = default) =>
        await ReceiveInternalAsync(locationId, skuId, quantity, userId, InventoryTransactionTypes.QuarantineIn,
            lotNumber, expiryDate, notes, referenceOperationId, cancellationToken, isQuarantined: true);

    public async Task<InventoryBatch> ReceiveReturnAndWriteOffAsync(
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
        EnsurePositive(quantity, nameof(quantity));
        InventoryBatch? result = null;
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
            var now = _clock.EgyptNow;
            var batch = await FindBatchAsync(locationId, skuId, lotNumber, expiryDate, cancellationToken);
            if (batch is null)
            {
                batch = new InventoryBatch { Id = Guid.NewGuid(), LocationId = locationId, SkuId = skuId, LotNumber = NormalizeBlank(lotNumber), ExpiryDate = expiryDate, Quantity = 0, Notes = NormalizeBlank(notes), CreatedFrom = referenceOperationId, CreatedBy = userId, CreatedAt = now };
                _dbContext.InventoryBatches.Add(batch);
            }
            batch.Quantity += quantity;
            AddTransaction(locationId, skuId, InventoryTransactionTypes.ReturnIn, quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            batch.Quantity -= quantity;
            AddTransaction(locationId, skuId, InventoryTransactionTypes.WriteOff, -quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            result = batch;
        }, cancellationToken);
        return result!;
    }

    public async Task ReleaseQuarantinedBatchAsync(Guid batchId, int quantity, Guid userId, CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        await ExecuteRelationalWriteAsync(async () =>
        {
            var identity = await _dbContext.InventoryBatches.AsNoTracking().Where(value => value.Id == batchId)
                .Select(value => new { value.LocationId, value.SkuId }).SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Quarantined batch was not found.");
            await AcquireStockLocksAsync([new StockKey(identity.LocationId, identity.SkuId)], cancellationToken);
            var batch = _dbContext.Database.IsRelational()
                ? await _dbContext.InventoryBatches.FromSqlInterpolated($"select * from inventory.inventory_batches where id = {batchId} for update").AsTracking().SingleOrDefaultAsync(cancellationToken)
                : await _dbContext.InventoryBatches.SingleOrDefaultAsync(value => value.Id == batchId, cancellationToken);
            if (batch is null || !batch.IsQuarantined || batch.Quantity < quantity)
                throw new InvalidOperationException("Quarantined batch is unavailable or the release quantity exceeds its balance.");

            batch.Quantity -= quantity;
            if (batch.Quantity == 0) batch.IsQuarantined = false;
            else _dbContext.InventoryBatches.Add(new InventoryBatch
            {
                Id = Guid.NewGuid(),
                LocationId = batch.LocationId,
                SkuId = batch.SkuId,
                LotNumber = batch.LotNumber,
                ExpiryDate = batch.ExpiryDate,
                Quantity = quantity,
                CreatedFrom = batch.CreatedFrom,
                CreatedBy = userId,
                Notes = batch.Notes,
                CreatedAt = _clock.EgyptNow,
                IsQuarantined = false
            });
            var balance = await GetOrCreateBalanceAsync(batch.LocationId, batch.SkuId, cancellationToken);
            ApplyAvailableDelta(balance, quantity, _clock.EgyptNow);
            AddTransaction(batch.LocationId, batch.SkuId, InventoryTransactionTypes.QuarantineRelease, quantity, InventoryQuantityUnits.Pack, userId, batch.CreatedFrom, _clock.EgyptNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task WriteOffQuarantinedBatchAsync(Guid batchId, int quantity, Guid userId, CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        await ExecuteRelationalWriteAsync(async () =>
        {
            var identity = await _dbContext.InventoryBatches.AsNoTracking().Where(value => value.Id == batchId)
                .Select(value => new { value.LocationId, value.SkuId }).SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("Quarantined batch was not found.");
            await AcquireStockLocksAsync([new StockKey(identity.LocationId, identity.SkuId)], cancellationToken);
            var batch = _dbContext.Database.IsRelational()
                ? await _dbContext.InventoryBatches.FromSqlInterpolated($"select * from inventory.inventory_batches where id = {batchId} for update").AsTracking().SingleOrDefaultAsync(cancellationToken)
                : await _dbContext.InventoryBatches.SingleOrDefaultAsync(value => value.Id == batchId, cancellationToken);
            if (batch is null || !batch.IsQuarantined || batch.Quantity < quantity)
                throw new InvalidOperationException("Quarantined batch is unavailable or the write-off quantity exceeds its balance.");
            batch.Quantity -= quantity;
            AddTransaction(batch.LocationId, batch.SkuId, InventoryTransactionTypes.QuarantineWriteOff, -quantity, InventoryQuantityUnits.Pack, userId, batch.CreatedFrom, _clock.EgyptNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<InventoryBatch> ReceiveExpiredReturnAndWriteOffAsync(
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
        EnsurePositive(quantity, nameof(quantity));
        var today = DateOnly.FromDateTime(_clock.EgyptNow);
        if (expiryDate is null || expiryDate >= today)
        {
            throw new InvalidOperationException("Only expired merchant returns can be written off during receipt.");
        }
        InventoryBatch? result = null;
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
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

            var balance = await GetOrCreateBalanceAsync(locationId, skuId, cancellationToken);
            batch.Quantity += quantity;
            ApplyAvailableDelta(balance, quantity, now);
            AddTransaction(locationId, skuId, InventoryTransactionTypes.ReturnIn, quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);

            batch.Quantity -= quantity;
            ApplyAvailableDelta(balance, -quantity, now);
            AddTransaction(locationId, skuId, InventoryTransactionTypes.WriteOff, -quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            result = batch;
        }, cancellationToken);
        return result!;
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
            await ExecuteRelationalWriteAsync(async () =>
            {
                await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
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
                AddTransaction(locationId, skuId, InventoryTransactionTypes.StocktakeAdjustment, delta, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }, cancellationToken);
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
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
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

            if (delta < 0)
            {
                var pieceHolds = await GetActivePiecePackHoldsByBatchAsync(locationId, skuId, cancellationToken);
                var operationHolds = _reservedPackReader is null ? new Dictionary<Guid, int>() :
                    await _reservedPackReader.LoadReservedPackAllocationsAsync(locationId, referenceOperationId, cancellationToken);
                if (nextBatchQuantity < pieceHolds.GetValueOrDefault(batch.Id) + operationHolds.GetValueOrDefault(batch.Id))
                    throw new InvalidOperationException("Selected batch contains reserved packs and cannot be reduced by stocktake.");
            }

            batch.Quantity = nextBatchQuantity;
            var balance = await GetOrCreateBalanceAsync(locationId, skuId, cancellationToken);
            ApplyAvailableDelta(balance, delta, now);
            AddTransaction(locationId, skuId, InventoryTransactionTypes.StocktakeAdjustment, delta, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
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
        CancellationToken cancellationToken = default,
        bool isQuarantined = false)
    {
        EnsurePositive(quantity, nameof(quantity));
        EnsureTransactionType(transactionType);
        if (transactionType != InventoryTransactionTypes.Receipt &&
            transactionType != InventoryTransactionTypes.SupplyIn &&
            transactionType != InventoryTransactionTypes.ChangeOut &&
            transactionType != InventoryTransactionTypes.ReturnIn &&
            transactionType != InventoryTransactionTypes.QuarantineIn)
        {
            throw new InvalidOperationException($"{transactionType} is not a receiving transaction type.");
        }
        InventoryBatch? result = null;
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
            var now = _clock.EgyptNow;
            var batch = await FindBatchAsync(locationId, skuId, lotNumber, expiryDate, cancellationToken, isQuarantined);
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
                    IsQuarantined = isQuarantined,
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
            if (!isQuarantined) ApplyAvailableDelta(balance, quantity, now);
            AddTransaction(locationId, skuId, transactionType, quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            result = batch;
        }, cancellationToken);
        return result!;
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
        IReadOnlyList<BatchAllocation>? result = null;
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
            var now = _clock.EgyptNow;
            var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
            if (balance is null || balance.AvailableQty < quantity)
            {
                throw new InvalidOperationException("Available stock is insufficient.");
            }

            var batches = await LoadFefoBatchesAsync(locationId, skuId, minimumExpiryDate, cancellationToken);
            var pieceHolds = await GetActivePiecePackHoldsByBatchAsync(locationId, skuId, cancellationToken);
            var operationHolds = _reservedPackReader is null
                ? new Dictionary<Guid, int>()
                : await _reservedPackReader.LoadReservedPackAllocationsAsync(locationId, referenceOperationId, cancellationToken);
            var remaining = quantity;
            var allocations = new List<BatchAllocation>();
            foreach (var batch in batches)
            {
                if (remaining == 0)
                {
                    break;
                }

                var free = Math.Max(0, batch.Quantity - pieceHolds.GetValueOrDefault(batch.Id) - operationHolds.GetValueOrDefault(batch.Id));
                var allocated = Math.Min(free, remaining);
                if (allocated == 0) continue;
                batch.Quantity -= allocated;
                remaining -= allocated;
                allocations.Add(new BatchAllocation(batch.Id, allocated));
            }

            if (remaining > 0)
            {
                throw new InvalidOperationException("Batch stock is insufficient.");
            }

            ApplyAvailableDelta(balance, -quantity, now);
            AddTransaction(locationId, skuId, transactionType, -quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            result = allocations;
        }, cancellationToken);
        return result!;
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
        BatchAllocation? result = null;
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
            var batch = await LoadSelectedBatchAsync(locationId, skuId, lotNumber, expiryDate, cancellationToken);
            var pieceHolds = await GetActivePiecePackHoldsByBatchAsync(locationId, skuId, cancellationToken);
            var operationHolds = _reservedPackReader is null
                ? new Dictionary<Guid, int>()
                : await _reservedPackReader.LoadReservedPackAllocationsAsync(locationId, referenceOperationId, cancellationToken);
            if (batch is null || batch.Quantity - pieceHolds.GetValueOrDefault(batch.Id) - operationHolds.GetValueOrDefault(batch.Id) < quantity)
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
            AddTransaction(locationId, skuId, transactionType, -quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            result = new BatchAllocation(batch.Id, quantity, batch.LotNumber, batch.ExpiryDate);
        }, cancellationToken);
        return result!;
    }

    public async Task<IReadOnlyList<PieceAllocation>> IssuePiecesFefoAsync(
        Guid locationId,
        Guid skuId,
        int pieceQuantity,
        int piecesPerPack,
        Guid userId,
        Guid? referenceOperationId = null,
        DateOnly? minimumExpiryDate = null,
        CancellationToken cancellationToken = default,
        string transactionType = InventoryTransactionTypes.Sale)
    {
        EnsurePositive(pieceQuantity, nameof(pieceQuantity));
        EnsurePositive(piecesPerPack, nameof(piecesPerPack));

        IReadOnlyList<PieceAllocation>? issued = null;
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);

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

            await EnsureEligibleOpenedSourcesAsync(locationId, skuId, looseLots.Select(value => value.SourceBatchId), today, cancellationToken);

            var heldByLot = await GetActivePieceLooseHoldsByLotAsync(locationId, skuId, cancellationToken);
            if (looseLots.Sum(lot => Math.Max(0, lot.LoosePieceQuantity - heldByLot.GetValueOrDefault(lot.Id))) < pieceQuantity)
            {
                throw new InvalidOperationException("Insufficient loose pieces. Edit the piece quantity or use Packs.");
            }

            foreach (var lot in looseLots)
            {
                if (remaining == 0)
                {
                    break;
                }

                var take = Math.Min(Math.Max(0, lot.LoosePieceQuantity - heldByLot.GetValueOrDefault(lot.Id)), remaining);
                lot.LoosePieceQuantity -= take;
                remaining -= take;
                allocations.Add(new PieceAllocation(lot.Id, lot.SourceBatchId, take, lot.LotNumber, lot.BatchExpiryDate, lot.PieceExpiryDate, false));
            }

            AddTransaction(locationId, skuId, transactionType, -pieceQuantity, InventoryQuantityUnits.Piece, userId, referenceOperationId, _clock.EgyptNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
            issued = allocations;
        }, cancellationToken);
        return issued!;
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
        BatchAllocation? result = null;
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
            var batch = await LoadSelectedBatchAsync(locationId, skuId, lotNumber, expiryDate, cancellationToken);
            var pieceHolds = await GetActivePiecePackHoldsByBatchAsync(locationId, skuId, cancellationToken);
            var operationHolds = _reservedPackReader is null
                ? new Dictionary<Guid, int>()
                : await _reservedPackReader.LoadReservedPackAllocationsAsync(locationId, referenceOperationId, cancellationToken);
            if (batch is null || batch.IsQuarantined || batch.Quantity - pieceHolds.GetValueOrDefault(batch.Id) - operationHolds.GetValueOrDefault(batch.Id) < quantity)
            {
                throw new InvalidOperationException("Selected batch stock is insufficient.");
            }

            await ReserveInWarehouseAsync(locationId, skuId, quantity, userId, referenceOperationId, cancellationToken);
            result = new BatchAllocation(batch.Id, quantity, batch.LotNumber, batch.ExpiryDate);
        }, cancellationToken);
        return result!;
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
        CancellationToken cancellationToken = default,
        string transactionType = InventoryTransactionTypes.Sale,
        bool allowOpeningSelectedPacks = false)
    {
        EnsurePositive(pieceQuantity, nameof(pieceQuantity));
        EnsurePositive(piecesPerPack, nameof(piecesPerPack));

        IReadOnlyList<PieceAllocation>? issued = null;
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);

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

            await EnsureEligibleOpenedSourcesAsync(locationId, skuId, looseLots.Select(value => value.SourceBatchId), today, cancellationToken);

            var heldByLot = await GetActivePieceLooseHoldsByLotAsync(locationId, skuId, cancellationToken);
            var availableLoosePieces = looseLots.Sum(lot => Math.Max(0, lot.LoosePieceQuantity - heldByLot.GetValueOrDefault(lot.Id)));
            if (availableLoosePieces < pieceQuantity && !allowOpeningSelectedPacks)
            {
                throw new InvalidOperationException($"Insufficient loose pieces for the selected batch ({normalizedLot ?? "no lot"} / {expiryDate?.ToString("yyyy-MM-dd") ?? "no expiry"}). Edit the piece quantity or use Packs.");
            }

            foreach (var lot in looseLots)
            {
                if (remaining == 0)
                {
                    break;
                }

                var take = Math.Min(Math.Max(0, lot.LoosePieceQuantity - heldByLot.GetValueOrDefault(lot.Id)), remaining);
                lot.LoosePieceQuantity -= take;
                remaining -= take;
                allocations.Add(new PieceAllocation(lot.Id, lot.SourceBatchId, take, lot.LotNumber, lot.BatchExpiryDate, lot.PieceExpiryDate, false));
            }

            if (remaining > 0)
            {
                var packsToOpen = checked((int)(((long)remaining + piecesPerPack - 1) / piecesPerPack));
                var batch = await LoadSelectedBatchAsync(locationId, skuId, normalizedLot, expiryDate, cancellationToken);
                var activePackHolds = await GetActivePiecePackHoldsByBatchAsync(locationId, skuId, cancellationToken);
                var operationPacks = _reservedPackReader is null
                    ? new Dictionary<Guid, int>()
                    : await _reservedPackReader.LoadReservedPackAllocationsAsync(locationId, referenceOperationId, cancellationToken);
                if (batch is null || batch.Quantity - activePackHolds.GetValueOrDefault(batch.Id) - operationPacks.GetValueOrDefault(batch.Id) < packsToOpen)
                {
                    throw new InvalidOperationException($"Selected batch stock is insufficient for {remaining} additional piece(s).");
                }

                var now = _clock.EgyptNow;
                var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
                if (balance is null || balance.AvailableQty < packsToOpen)
                {
                    throw new InvalidOperationException("Available stock is insufficient.");
                }

                batch.Quantity -= packsToOpen;
                ApplyAvailableDelta(balance, -packsToOpen, now);
                var openedLot = new OpenedPieceLot
                {
                    Id = Guid.NewGuid(),
                    LocationId = locationId,
                    SkuId = skuId,
                    SourceBatchId = batch.Id,
                    LotNumber = batch.LotNumber,
                    BatchExpiryDate = batch.ExpiryDate,
                    OpenedDate = DateOnly.FromDateTime(now),
                    PieceExpiryDate = batch.ExpiryDate,
                    LoosePieceQuantity = (packsToOpen * piecesPerPack) - remaining,
                    CreatedFrom = referenceOperationId,
                    CreatedBy = userId,
                    CreatedAt = now
                };
                _dbContext.OpenedPieceLots.Add(openedLot);
                var pieceIssue = AddTransaction(locationId, skuId, transactionType, -pieceQuantity, InventoryQuantityUnits.Piece, userId, referenceOperationId, now);
                var packOpen = AddTransaction(locationId, skuId, InventoryTransactionTypes.PackOpen, -packsToOpen, InventoryQuantityUnits.Pack, userId, referenceOperationId, now, pieceIssue.Id, piecesPerPack);
                allocations.Add(new PieceAllocation(openedLot.Id, batch.Id, remaining, batch.LotNumber, batch.ExpiryDate, batch.ExpiryDate, true));
                allocations = allocations.Select(allocation => allocation with
                {
                    PieceIssueTransactionId = pieceIssue.Id,
                    PackOpenTransactionId = allocation.OpenedPack ? packOpen.Id : null
                }).ToList();
            }

            if (remaining == 0)
            {
                var pieceIssue = AddTransaction(locationId, skuId, transactionType, -pieceQuantity, InventoryQuantityUnits.Piece, userId, referenceOperationId, _clock.EgyptNow);
                allocations = allocations.Select(allocation => allocation with { PieceIssueTransactionId = pieceIssue.Id }).ToList();
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            issued = allocations;
        }, cancellationToken);
        return issued!;
    }

    public async Task AdjustStocktakePiecesAsync(Guid locationId, Guid skuId, string? lotNumber, DateOnly? expiryDate, int delta, Guid userId, Guid? referenceOperationId = null, CancellationToken cancellationToken = default)
    {
        if (delta == 0) return;
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
            var normalizedLot = NormalizeBlank(lotNumber);
            var lots = await _dbContext.OpenedPieceLots
                .Where(value => value.LocationId == locationId && value.SkuId == skuId && value.LotNumber == normalizedLot && value.BatchExpiryDate == expiryDate)
                .OrderBy(value => value.PieceExpiryDate == null).ThenBy(value => value.PieceExpiryDate).ToListAsync(cancellationToken);
            if (delta < 0)
            {
                var held = await GetActivePieceLooseHoldsByLotAsync(locationId, skuId, cancellationToken);
                var remaining = -delta;
                foreach (var lot in lots)
                {
                    var take = Math.Min(remaining, Math.Max(0, lot.LoosePieceQuantity - held.GetValueOrDefault(lot.Id)));
                    lot.LoosePieceQuantity -= take;
                    remaining -= take;
                    if (remaining == 0) break;
                }
                if (remaining > 0) throw new InvalidOperationException("Selected loose-piece stock is insufficient.");
            }
            else
            {
                var batch = await FindBatchAsync(locationId, skuId, normalizedLot, expiryDate, cancellationToken);
                if (batch is null) throw new InvalidOperationException("A stock batch is required for a positive loose-piece adjustment.");
                _dbContext.OpenedPieceLots.Add(new OpenedPieceLot { Id = Guid.NewGuid(), LocationId = locationId, SkuId = skuId, SourceBatchId = batch.Id, LotNumber = normalizedLot, BatchExpiryDate = expiryDate, OpenedDate = DateOnly.FromDateTime(_clock.EgyptNow), PieceExpiryDate = expiryDate, LoosePieceQuantity = delta, CreatedFrom = referenceOperationId, CreatedBy = userId, CreatedAt = _clock.EgyptNow });
            }
            AddTransaction(locationId, skuId, InventoryTransactionTypes.StocktakeAdjustment, delta, InventoryQuantityUnits.Piece, userId, referenceOperationId, _clock.EgyptNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task AcquireStockLocksAsync(IEnumerable<StockKey> keys, CancellationToken cancellationToken = default)
    {
        if (!_dbContext.Database.IsRelational()) return;
        if (_dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Stock locks require an active inventory transaction.");
        foreach (var key in keys.Distinct().OrderBy(key => key.LocationId).ThenBy(key => key.SkuId))
        {
            var lockKey = $"inventory-stock:{key.LocationId:N}:{key.SkuId:N}";
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"select pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        }
    }

    public async Task<IReadOnlyDictionary<Guid, int>> GetActivePieceLooseHoldsByLotAsync(Guid locationId, Guid? skuId = null, CancellationToken cancellationToken = default) =>
        await (from allocation in _dbContext.StockPieceReservationAllocations
               join reservation in _dbContext.StockPieceReservations on allocation.ReservationId equals reservation.Id
               where reservation.Status == "Active" && reservation.LocationId == locationId &&
                     (skuId == null || reservation.SkuId == skuId) && allocation.SourceKind == "OpenedPieceLot" &&
                     allocation.SourceOpenedPieceLotId != null
               group allocation by allocation.SourceOpenedPieceLotId!.Value into grouped
               select new { Id = grouped.Key, Quantity = grouped.Sum(value => value.PieceQuantity) })
            .ToDictionaryAsync(value => value.Id, value => value.Quantity, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> GetActivePiecePackHoldsByBatchAsync(Guid locationId, Guid? skuId = null, CancellationToken cancellationToken = default) =>
        await (from allocation in _dbContext.StockPieceReservationAllocations
               join reservation in _dbContext.StockPieceReservations on allocation.ReservationId equals reservation.Id
               where reservation.Status == "Active" && reservation.LocationId == locationId &&
                     (skuId == null || reservation.SkuId == skuId) && allocation.SourceKind == "SealedBatch"
               group allocation by allocation.SourceBatchId into grouped
               select new { Id = grouped.Key, Quantity = grouped.Sum(value => value.PhysicalPacksHeld) })
            .ToDictionaryAsync(value => value.Id, value => value.Quantity, cancellationToken);

    public async Task ReservePieceLineAsync(PieceReservationRequest request, IReadOnlyDictionary<Guid, int> currentOperationPacksByBatch, CancellationToken cancellationToken = default)
    {
        EnsurePositive(request.RequestedPieceQuantity, nameof(request.RequestedPieceQuantity));
        EnsurePositive(request.PiecesPerPackSnapshot, nameof(request.PiecesPerPackSnapshot));
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(request.LocationId, request.SkuId)], cancellationToken);
            if (await _dbContext.StockPieceReservations.AnyAsync(value => value.OperationLineId == request.OperationLineId, cancellationToken))
                throw new InvalidOperationException("This operation line already has a piece reservation.");

            var now = _clock.EgyptNow;
            var normalizedLot = NormalizeBlank(request.LotNumber);
            var today = DateOnly.FromDateTime(now);
            var loose = await _dbContext.OpenedPieceLots.Where(value => value.LocationId == request.LocationId && value.SkuId == request.SkuId &&
                    value.LotNumber == normalizedLot && value.BatchExpiryDate == request.ExpiryDate && value.LoosePieceQuantity > 0 &&
                    (value.PieceExpiryDate == null || value.PieceExpiryDate >= today))
                .OrderBy(value => value.PieceExpiryDate == null).ThenBy(value => value.PieceExpiryDate).ThenBy(value => value.CreatedAt)
                .ToListAsync(cancellationToken);
            if (loose.Count > 0)
            {
                var sourceBatchIds = loose.Select(value => value.SourceBatchId).Distinct().ToArray();
                var validBatchIds = await _dbContext.InventoryBatches
                    .Where(value => sourceBatchIds.Contains(value.Id) && value.LocationId == request.LocationId && value.SkuId == request.SkuId &&
                        !value.IsQuarantined && (value.ExpiryDate == null || value.ExpiryDate >= today))
                    .Select(value => value.Id).ToListAsync(cancellationToken);
                var validSet = validBatchIds.ToHashSet();
                if (loose.Any(value => !validSet.Contains(value.SourceBatchId)))
                    throw new InvalidOperationException("A selected loose-piece lot has missing or quarantined source-batch provenance.");
            }
            var heldLoose = await GetActivePieceLooseHoldsByLotAsync(request.LocationId, request.SkuId, cancellationToken);
            var remaining = request.RequestedPieceQuantity;
            var children = new List<StockPieceReservationAllocation>();
            foreach (var lot in loose)
            {
                var free = Math.Max(0, lot.LoosePieceQuantity - heldLoose.GetValueOrDefault(lot.Id));
                var take = Math.Min(free, remaining);
                if (take == 0) continue;
                children.Add(new StockPieceReservationAllocation
                {
                    Id = Guid.NewGuid(),
                    SourceKind = "OpenedPieceLot",
                    SourceOpenedPieceLotId = lot.Id,
                    SourceBatchId = lot.SourceBatchId,
                    PieceQuantity = take,
                    PhysicalPacksHeld = 0,
                    LotNumber = lot.LotNumber,
                    ExpiryDate = lot.BatchExpiryDate,
                    CreatedAt = now
                });
                remaining -= take;
                if (remaining == 0) break;
            }

            var sealedPacks = 0;
            if (remaining > 0)
            {
                var neededPacks = checked((int)(((long)remaining + request.PiecesPerPackSnapshot - 1) / request.PiecesPerPackSnapshot));
                var batch = await LoadSelectedBatchAsync(request.LocationId, request.SkuId, normalizedLot, request.ExpiryDate, cancellationToken);
                var heldPacks = await GetActivePiecePackHoldsByBatchAsync(request.LocationId, request.SkuId, cancellationToken);
                var existingOperationHolds = _reservedPackReader is null ? new Dictionary<Guid, int>() :
                    await _reservedPackReader.LoadReservedPackAllocationsAsync(request.LocationId, request.OperationId, cancellationToken);
                var otherPackReservations = currentOperationPacksByBatch.GetValueOrDefault(batch?.Id ?? Guid.Empty) +
                    existingOperationHolds.GetValueOrDefault(batch?.Id ?? Guid.Empty);
                if (batch is null || batch.IsQuarantined || batch.ExpiryDate is { } expiry && expiry < today ||
                    batch.Quantity - heldPacks.GetValueOrDefault(batch.Id) - otherPackReservations < neededPacks)
                    throw new InvalidOperationException("Selected batch cannot cover the exact piece quantity.");
                sealedPacks = neededPacks;
                children.Add(new StockPieceReservationAllocation
                {
                    Id = Guid.NewGuid(),
                    SourceKind = "SealedBatch",
                    SourceBatchId = batch.Id,
                    PieceQuantity = remaining,
                    PhysicalPacksHeld = neededPacks,
                    LotNumber = batch.LotNumber,
                    ExpiryDate = batch.ExpiryDate,
                    CreatedAt = now
                });
            }

            if (sealedPacks > 0)
                await ReserveInWarehouseAsync(request.LocationId, request.SkuId, sealedPacks, request.UserId, request.OperationId, cancellationToken);
            if (children.Sum(value => value.PieceQuantity) != request.RequestedPieceQuantity ||
                children.Any(value => value.SourceKind == "SealedBatch" && value.PhysicalPacksHeld <
                    checked((int)(((long)value.PieceQuantity + request.PiecesPerPackSnapshot - 1) / request.PiecesPerPackSnapshot))))
                throw new InvalidOperationException("Piece reservation allocation does not match the exact requested quantity.");
            var reservation = new StockPieceReservation
            {
                Id = Guid.NewGuid(),
                OperationId = request.OperationId,
                OperationLineId = request.OperationLineId,
                LocationId = request.LocationId,
                SkuId = request.SkuId,
                RequestedPieceQuantity = request.RequestedPieceQuantity,
                PiecesPerPackSnapshot = request.PiecesPerPackSnapshot,
                Status = "Active",
                CreatedAt = now,
                Allocations = children
            };
            foreach (var child in children) child.ReservationId = reservation.Id;
            _dbContext.StockPieceReservations.Add(reservation);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<PieceAllocation>> ConsumePieceLineReservationAsync(Guid operationId, Guid operationLineId, int expectedQuantity, string transactionType, Guid userId, CancellationToken cancellationToken = default)
    {
        EnsurePositive(expectedQuantity, nameof(expectedQuantity));
        EnsureTransactionType(transactionType);
        IReadOnlyList<PieceAllocation>? result = null;
        await ExecuteRelationalWriteAsync(async () =>
        {
            var reservation = await _dbContext.StockPieceReservations
                .SingleOrDefaultAsync(value => value.OperationLineId == operationLineId && value.OperationId == operationId, cancellationToken)
                ?? throw new InvalidOperationException("The piece reservation is missing; this order requires inventory review.");
            await AcquireStockLocksAsync([new StockKey(reservation.LocationId, reservation.SkuId)], cancellationToken);
            await _dbContext.Entry(reservation).ReloadAsync(cancellationToken);
            var allocations = await _dbContext.StockPieceReservationAllocations.AsNoTracking()
                .Where(value => value.ReservationId == reservation.Id).ToListAsync(cancellationToken);
            if (reservation.Status != "Active" || reservation.RequestedPieceQuantity != expectedQuantity)
                throw new InvalidOperationException("The active piece reservation does not match the sale line.");

            var today = DateOnly.FromDateTime(_clock.EgyptNow);
            if (allocations.Sum(value => value.PieceQuantity) != expectedQuantity)
                throw new InvalidOperationException("The active piece reservation does not match the sale line.");
            var looseIds = allocations.Where(value => value.SourceKind == "OpenedPieceLot").Select(value => value.SourceOpenedPieceLotId!.Value).ToArray();
            var looseLots = await _dbContext.OpenedPieceLots.Where(value => looseIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, cancellationToken);
            var otherHolds = await _dbContext.StockPieceReservationAllocations.Join(_dbContext.StockPieceReservations,
                    allocation => allocation.ReservationId, parent => parent.Id, (allocation, parent) => new { allocation, parent })
                .Where(value => value.parent.Status == "Active" && value.parent.LocationId == reservation.LocationId && value.parent.SkuId == reservation.SkuId &&
                    value.parent.Id != reservation.Id && value.allocation.SourceKind == "OpenedPieceLot" && value.allocation.SourceOpenedPieceLotId != null)
                .GroupBy(value => value.allocation.SourceOpenedPieceLotId!.Value)
                .Select(group => new { Id = group.Key, Quantity = group.Sum(value => value.allocation.PieceQuantity) })
                .ToDictionaryAsync(value => value.Id, value => value.Quantity, cancellationToken);
            foreach (var allocation in allocations.Where(value => value.SourceKind == "OpenedPieceLot"))
            {
                var lot = looseLots.GetValueOrDefault(allocation.SourceOpenedPieceLotId!.Value);
                if (lot is null || lot.LocationId != reservation.LocationId || lot.SkuId != reservation.SkuId ||
                    lot.SourceBatchId != allocation.SourceBatchId || lot.LotNumber != allocation.LotNumber || lot.BatchExpiryDate != allocation.ExpiryDate ||
                    lot.PieceExpiryDate is { } expiry && expiry < today ||
                    lot.LoosePieceQuantity - otherHolds.GetValueOrDefault(lot.Id) < allocation.PieceQuantity)
                    throw new InvalidOperationException("A pinned loose-piece source is no longer available; review is required.");
                var sourceBatchValid = await _dbContext.InventoryBatches.AnyAsync(value => value.Id == allocation.SourceBatchId &&
                    value.LocationId == reservation.LocationId && value.SkuId == reservation.SkuId && !value.IsQuarantined &&
                    (value.ExpiryDate == null || value.ExpiryDate >= today), cancellationToken);
                if (!sourceBatchValid) throw new InvalidOperationException("A pinned loose-piece batch is missing, quarantined, or expired; review is required.");
                lot.LoosePieceQuantity -= allocation.PieceQuantity;
            }

            var sealedAllocations = allocations.Where(value => value.SourceKind == "SealedBatch").ToArray();
            var packQuantity = sealedAllocations.Sum(value => value.PhysicalPacksHeld);
            var allocationsBuffer = new List<PieceAllocation>();
            var now = _clock.EgyptNow;
            if (packQuantity > 0)
            {
                var balance = await GetFreshBalanceAsync(reservation.LocationId, reservation.SkuId, cancellationToken)
                    ?? throw new InvalidOperationException("Reserved pack balance is missing.");
                if (balance.ReservedInWarehouseQty < packQuantity) throw new InvalidOperationException("Pinned pack reservation is no longer present.");
                var batchIds = sealedAllocations.Select(value => value.SourceBatchId).Distinct().ToArray();
                var batches = await _dbContext.InventoryBatches.Where(value => batchIds.Contains(value.Id) && value.LocationId == reservation.LocationId && value.SkuId == reservation.SkuId)
                    .ToDictionaryAsync(value => value.Id, cancellationToken);
                foreach (var item in sealedAllocations)
                    if (!batches.TryGetValue(item.SourceBatchId, out var batch) || batch.IsQuarantined || batch.LocationId != reservation.LocationId ||
                        batch.SkuId != reservation.SkuId || batch.LotNumber != item.LotNumber || batch.ExpiryDate != item.ExpiryDate ||
                        batch.Quantity < item.PhysicalPacksHeld || batch.ExpiryDate is { } batchExpiry && batchExpiry < today)
                        throw new InvalidOperationException("A pinned sealed batch is no longer available; review is required.");

                balance.ReservedInWarehouseQty -= packQuantity;
                balance.RowVersion++;
                balance.LastUpdated = now;
                foreach (var item in sealedAllocations) batches[item.SourceBatchId].Quantity -= item.PhysicalPacksHeld;
            }
            var issue = AddTransaction(reservation.LocationId, reservation.SkuId, transactionType, -expectedQuantity, InventoryQuantityUnits.Piece, userId, operationId, now);
            foreach (var item in sealedAllocations)
            {
                var batch = await _dbContext.InventoryBatches.SingleAsync(value => value.Id == item.SourceBatchId, cancellationToken);
                var opened = new OpenedPieceLot
                {
                    Id = Guid.NewGuid(),
                    LocationId = reservation.LocationId,
                    SkuId = reservation.SkuId,
                    SourceBatchId = batch.Id,
                    LotNumber = batch.LotNumber,
                    BatchExpiryDate = batch.ExpiryDate,
                    OpenedDate = DateOnly.FromDateTime(now),
                    PieceExpiryDate = batch.ExpiryDate,
                    LoosePieceQuantity = item.PhysicalPacksHeld * reservation.PiecesPerPackSnapshot - item.PieceQuantity,
                    CreatedFrom = operationId,
                    CreatedBy = userId,
                    CreatedAt = now
                };
                _dbContext.OpenedPieceLots.Add(opened);
                var packOpen = AddTransaction(reservation.LocationId, reservation.SkuId, InventoryTransactionTypes.PackOpen,
                    -item.PhysicalPacksHeld, InventoryQuantityUnits.Pack, userId, operationId, now, issue.Id, reservation.PiecesPerPackSnapshot);
                allocationsBuffer.Add(new PieceAllocation(opened.Id, batch.Id, item.PieceQuantity, batch.LotNumber, batch.ExpiryDate, batch.ExpiryDate, true, issue.Id, packOpen.Id));
            }
            foreach (var item in allocations.Where(value => value.SourceKind == "OpenedPieceLot"))
            {
                var lot = looseLots[item.SourceOpenedPieceLotId!.Value];
                allocationsBuffer.Add(new PieceAllocation(lot.Id, lot.SourceBatchId, item.PieceQuantity, lot.LotNumber, lot.BatchExpiryDate, lot.PieceExpiryDate, false, issue.Id));
            }
            reservation.Status = "Consumed";
            reservation.ConsumedAt = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
            result = allocationsBuffer;
            allocationsBuffer = [];
        }, cancellationToken);
        return result!;
    }

    public async Task ReleasePieceLineReservationAsync(Guid operationLineId, Guid userId, CancellationToken cancellationToken = default)
    {
        await ExecuteRelationalWriteAsync(async () =>
        {
            var reservation = await _dbContext.StockPieceReservations
                .SingleOrDefaultAsync(value => value.OperationLineId == operationLineId, cancellationToken);
            if (reservation is null || reservation.Status != "Active") return;
            await AcquireStockLocksAsync([new StockKey(reservation.LocationId, reservation.SkuId)], cancellationToken);
            await _dbContext.Entry(reservation).ReloadAsync(cancellationToken);
            if (reservation.Status != "Active") return;
            var allocations = await _dbContext.StockPieceReservationAllocations.AsNoTracking().Where(value => value.ReservationId == reservation.Id).ToListAsync(cancellationToken);
            var packs = allocations.Where(value => value.SourceKind == "SealedBatch").Sum(value => value.PhysicalPacksHeld);
            if (packs > 0) await ReleaseInWarehouseAsync(reservation.LocationId, reservation.SkuId, packs, userId, reservation.OperationId, cancellationToken);
            reservation.Status = "Released";
            reservation.ReleasedAt = _clock.EgyptNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task ReleasePieceReservationsAsync(Guid operationId, Guid userId, CancellationToken cancellationToken = default)
    {
        await ExecuteRelationalWriteAsync(async () =>
        {
            var active = await _dbContext.StockPieceReservations.Where(value => value.OperationId == operationId && value.Status == "Active")
                .Select(value => new { value.OperationLineId, value.LocationId, value.SkuId }).ToListAsync(cancellationToken);
            await AcquireStockLocksAsync(active.Select(value => new StockKey(value.LocationId, value.SkuId)), cancellationToken);
            foreach (var row in active.OrderBy(value => value.LocationId).ThenBy(value => value.SkuId).ThenBy(value => value.OperationLineId))
                await ReleasePieceLineReservationAsync(row.OperationLineId, userId, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// Restores returned pieces to the exact loose lot from which they were
    /// issued. Pack stock is deliberately not changed: the pack was already
    /// opened when the original piece sale was made.
    /// </summary>
    public async Task RestorePiecesToOpenedLotAsync(
        Guid openedPieceLotId,
        int pieceQuantity,
        Guid userId,
        Guid? referenceOperationId = null,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(pieceQuantity, nameof(pieceQuantity));
        var today = DateOnly.FromDateTime(_clock.EgyptNow);
        if (!_dbContext.Database.IsRelational())
        {
            var lot = await _dbContext.OpenedPieceLots.FirstOrDefaultAsync(value => value.Id == openedPieceLotId, cancellationToken)
                ?? throw new InvalidOperationException("The original opened piece lot no longer exists.");
            if (lot.PieceExpiryDate is { } expiry && expiry < today)
                throw new InvalidOperationException("Expired loose pieces cannot be returned to available inventory.");
            lot.LoosePieceQuantity += pieceQuantity;
            AddTransaction(lot.LocationId, lot.SkuId, InventoryTransactionTypes.ReturnRestore, pieceQuantity, InventoryQuantityUnits.Piece, userId, referenceOperationId, _clock.EgyptNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await ExecuteRelationalWriteAsync(async () =>
        {
            var lotSnapshot = await _dbContext.OpenedPieceLots
                .AsNoTracking()
                .Where(value => value.Id == openedPieceLotId && (value.PieceExpiryDate == null || value.PieceExpiryDate >= today))
                .Select(value => new { value.LocationId, value.SkuId })
                .SingleOrDefaultAsync(cancellationToken)
                ?? throw new InvalidOperationException("The original opened piece lot is unavailable or expired.");
            await AcquireStockLocksAsync([new StockKey(lotSnapshot.LocationId, lotSnapshot.SkuId)], cancellationToken);
            var restored = await _dbContext.OpenedPieceLots
                .Where(value => value.Id == openedPieceLotId && (value.PieceExpiryDate == null || value.PieceExpiryDate >= today))
                .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.LoosePieceQuantity, value => value.LoosePieceQuantity + pieceQuantity), cancellationToken);
            if (restored != 1)
                throw new InvalidOperationException("The original opened piece lot is unavailable or expired.");
            AddTransaction(lotSnapshot.LocationId, lotSnapshot.SkuId, InventoryTransactionTypes.ReturnRestore, pieceQuantity, InventoryQuantityUnits.Piece, userId, referenceOperationId, _clock.EgyptNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
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
            AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveInWarehouse, -quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
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
                throw new StockWriteConflictException();
            }

            await ReloadTrackedBalanceAsync(locationId, skuId, cancellationToken);
            AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveInWarehouse, -quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
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
        IReadOnlyList<BatchAllocation>? result = null;
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
            result = await PlanReserveInWarehouseFefoAsync(locationId, skuId, quantity, minimumExpiryDate, cancellationToken, referenceOperationId);
            await ReserveInWarehouseAsync(locationId, skuId, quantity, userId, referenceOperationId, cancellationToken);
        }, cancellationToken);
        return result!;
    }

    public async Task<IReadOnlyList<BatchAllocation>> PlanReserveInWarehouseFefoAsync(
        Guid locationId,
        Guid skuId,
        int quantity,
        DateOnly? minimumExpiryDate = null,
        CancellationToken cancellationToken = default,
        Guid? excludeOperationId = null)
    {
        EnsurePositive(quantity, nameof(quantity));
        var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
        if (balance is null || balance.AvailableQty < quantity)
        {
            throw new InvalidOperationException("Available stock is insufficient.");
        }

        var batches = await LoadFefoBatchesAsync(locationId, skuId, minimumExpiryDate, cancellationToken);
        var pieceHolds = await GetActivePiecePackHoldsByBatchAsync(locationId, skuId, cancellationToken);
        var operationHolds = _reservedPackReader is null
            ? new Dictionary<Guid, int>()
            : await _reservedPackReader.LoadReservedPackAllocationsAsync(locationId, excludeOperationId, cancellationToken);
        var remaining = quantity;
        var allocations = new List<BatchAllocation>();
        foreach (var batch in batches)
        {
            if (remaining == 0)
            {
                break;
            }

            var free = Math.Max(0, batch.Quantity - pieceHolds.GetValueOrDefault(batch.Id) - operationHolds.GetValueOrDefault(batch.Id));
            var allocated = Math.Min(free, remaining);
            if (allocated == 0) continue;
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
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
            var pieceHolds = await GetActivePiecePackHoldsByBatchAsync(locationId, skuId, cancellationToken);
            var operationHolds = _reservedPackReader is null ? new Dictionary<Guid, int>() :
                await _reservedPackReader.LoadReservedPackAllocationsAsync(locationId, referenceOperationId, cancellationToken);
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

                if (batch.Quantity < allocation.Quantity || batch.Quantity - allocation.Quantity <
                    pieceHolds.GetValueOrDefault(allocation.BatchId) + operationHolds.GetValueOrDefault(allocation.BatchId))
                {
                    throw new InvalidOperationException("Allocated batch stock is insufficient or held for piece reservations.");
                }

                batch.Quantity -= allocation.Quantity;
            }

            balance.ReservedInWarehouseQty -= quantity;
            balance.RowVersion++;
            balance.LastUpdated = now;
            AddTransaction(locationId, skuId, transactionType, -quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
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
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
            var pieceHolds = await GetActivePiecePackHoldsByBatchAsync(locationId, skuId, cancellationToken);
            var operationHolds = _reservedPackReader is null ? new Dictionary<Guid, int>() :
                await _reservedPackReader.LoadReservedPackAllocationsAsync(locationId, referenceOperationId, cancellationToken);
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

                if (batch.Quantity < allocation.Quantity || batch.Quantity - allocation.Quantity <
                    pieceHolds.GetValueOrDefault(allocation.BatchId) + operationHolds.GetValueOrDefault(allocation.BatchId))
                {
                    throw new InvalidOperationException("Allocated batch stock is insufficient or held for piece reservations.");
                }

                batch.Quantity -= allocation.Quantity;
            }

            balance.ReservedInWarehouseQty -= quantity;
            balance.ReservedWithRepQty += quantity;
            balance.RowVersion++;
            balance.LastUpdated = now;
            AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveWithRep, -quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task ReleaseInWarehouseAsync(Guid locationId, Guid skuId, int quantity, Guid userId, Guid? referenceOperationId = null, CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
            var now = _clock.EgyptNow;
            var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
            if (balance is null || balance.ReservedInWarehouseQty < quantity)
            {
                throw new InvalidOperationException("Reserved stock is insufficient.");
            }

            balance.ReservedInWarehouseQty -= quantity;
            ApplyAvailableDelta(balance, quantity, now);
            AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveReleaseInWarehouse, quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<int> ReleaseInWarehouseUpToAsync(Guid locationId, Guid skuId, int quantity, Guid userId, Guid? referenceOperationId = null, CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        var releasedResult = 0;
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
            var now = _clock.EgyptNow;
            var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
            var released = Math.Min(balance?.ReservedInWarehouseQty ?? 0, quantity);
            if (released == 0 || balance is null)
            {
                return;
            }

            balance.ReservedInWarehouseQty -= released;
            ApplyAvailableDelta(balance, released, now);
            AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveReleaseInWarehouse, released, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            releasedResult = released;
        }, cancellationToken);
        return releasedResult;
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
            AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveWithRep, -quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
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
                throw new StockWriteConflictException();
            }

            await ReloadTrackedBalanceAsync(locationId, skuId, cancellationToken);
            AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveWithRep, -quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task ReleaseWithRepAsync(Guid locationId, Guid skuId, int quantity, Guid userId, Guid? referenceOperationId = null, CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
            var now = _clock.EgyptNow;
            var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
            if (balance is null || balance.ReservedWithRepQty < quantity)
            {
                throw new InvalidOperationException("Reserved stock is insufficient.");
            }

            balance.ReservedWithRepQty -= quantity;
            ApplyAvailableDelta(balance, quantity, now);
            AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveReleaseWithRep, quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);
    }

    public async Task<int> ReleaseWithRepUpToAsync(Guid locationId, Guid skuId, int quantity, Guid userId, Guid? referenceOperationId = null, CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        var releasedResult = 0;
        await ExecuteRelationalWriteAsync(async () =>
        {
            await AcquireStockLocksAsync([new StockKey(locationId, skuId)], cancellationToken);
            var now = _clock.EgyptNow;
            var balance = await GetBalanceAsync(locationId, skuId, cancellationToken);
            var released = Math.Min(balance?.ReservedWithRepQty ?? 0, quantity);
            if (released == 0 || balance is null)
            {
                return;
            }

            balance.ReservedWithRepQty -= released;
            ApplyAvailableDelta(balance, released, now);
            AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveReleaseWithRep, released, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            releasedResult = released;
        }, cancellationToken);
        return releasedResult;
    }

    private async Task<InventoryBatch?> FindBatchAsync(Guid locationId, Guid skuId, string? lotNumber, DateOnly? expiryDate, CancellationToken cancellationToken, bool isQuarantined = false) =>
        await _dbContext.InventoryBatches.FirstOrDefaultAsync(batch =>
            batch.LocationId == locationId &&
            batch.SkuId == skuId &&
            batch.LotNumber == NormalizeBlank(lotNumber) &&
            batch.ExpiryDate == expiryDate &&
            batch.IsQuarantined == isQuarantined,
            cancellationToken);

    private async Task<List<InventoryBatch>> LoadFefoBatchesAsync(Guid locationId, Guid skuId, DateOnly? minimumExpiryDate, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(_clock.EgyptNow);
        return await _dbContext.InventoryBatches
            .Where(batch =>
                batch.LocationId == locationId &&
                batch.SkuId == skuId &&
                batch.Quantity > 0 &&
                !batch.IsQuarantined &&
                (batch.ExpiryDate == null || batch.ExpiryDate >= today))
            .OrderBy(batch => batch.ExpiryDate == null)
            .ThenBy(batch => batch.ExpiryDate)
            .ThenBy(batch => batch.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private async Task EnsureEligibleOpenedSourcesAsync(Guid locationId, Guid skuId, IEnumerable<Guid> sourceBatchIds, DateOnly today, CancellationToken cancellationToken)
    {
        var ids = sourceBatchIds.Distinct().ToArray();
        if (ids.Length == 0) return;
        var eligible = await _dbContext.InventoryBatches
            .Where(value => ids.Contains(value.Id) && value.LocationId == locationId && value.SkuId == skuId &&
                !value.IsQuarantined && (value.ExpiryDate == null || value.ExpiryDate >= today))
            .Select(value => value.Id).ToListAsync(cancellationToken);
        if (eligible.Count != ids.Length)
            throw new InvalidOperationException("An opened piece lot has missing, quarantined, expired, or mismatched source-batch provenance.");
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
            !batch.IsQuarantined &&
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

    private async Task ExecuteRelationalWriteAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational() || _dbContext.Database.CurrentTransaction is not null)
        {
            await action();
            return;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await action();
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
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

    private StockTransaction AddTransaction(
        Guid locationId,
        Guid skuId,
        string transactionType,
        int quantityChange,
        string quantityUnit,
        Guid userId,
        Guid? referenceOperationId,
        DateTime now,
        Guid? relatedTransactionId = null,
        int? conversionPiecesPerPack = null)
    {
        EnsureTransactionType(transactionType);
        if (quantityChange == 0)
            throw new ArgumentOutOfRangeException(nameof(quantityChange), "A transaction quantity cannot be zero.");
        if (!InventoryQuantityUnits.IsValid(quantityUnit))
            throw new InvalidOperationException($"Unsupported inventory quantity unit: {quantityUnit}.");
        if (transactionType == InventoryTransactionTypes.PackOpen && (quantityUnit != InventoryQuantityUnits.Pack || conversionPiecesPerPack is not > 0 || relatedTransactionId is null))
            throw new InvalidOperationException("Pack-opening transactions require a pack unit, related piece issue, and pack-size snapshot.");
        var transaction = new StockTransaction
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            SkuId = skuId,
            TransactionType = transactionType,
            QuantityChange = quantityChange,
            QuantityUnit = quantityUnit,
            RelatedTransactionId = relatedTransactionId,
            ConversionPiecesPerPack = conversionPiecesPerPack,
            ReferenceOperationId = referenceOperationId,
            UserId = userId,
            CreatedAt = now
        };
        _dbContext.StockTransactions.Add(transaction);
        return transaction;
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

public sealed record SupplyReceiptLine(Guid SkuId, int Quantity, string? LotNumber, DateOnly? ExpiryDate, string? Notes);

public readonly record struct StockKey(Guid LocationId, Guid SkuId);

public sealed record PieceReservationRequest(
    Guid OperationId,
    Guid OperationLineId,
    Guid LocationId,
    Guid SkuId,
    string? LotNumber,
    DateOnly? ExpiryDate,
    int RequestedPieceQuantity,
    int PiecesPerPackSnapshot,
    Guid UserId);

public sealed record BatchAllocation(Guid BatchId, int Quantity, string? LotNumber = null, DateOnly? ExpiryDate = null);

public sealed record InventoryReceiptLedgerResult(InventoryBatch Batch, Guid StockTransactionId);

public sealed record PieceAllocation(
    Guid OpenedPieceLotId,
    Guid? SourceBatchId,
    int Quantity,
    string? LotNumber,
    DateOnly? BatchExpiryDate,
    DateOnly? PieceExpiryDate,
    bool OpenedPack,
    Guid? PieceIssueTransactionId = null,
    Guid? PackOpenTransactionId = null);
