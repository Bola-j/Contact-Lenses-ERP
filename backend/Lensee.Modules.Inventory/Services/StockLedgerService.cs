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
        return new InventoryReceiptLedgerResult(batch, transaction.Id);
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

        if (_dbContext.Database.IsRelational())
        {
            var lockKey = $"inventory-supply:{locationId:N}";
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"select pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
                cancellationToken);
        }

        var now = _clock.EgyptNow;
        var skuIds = lines.Select(line => line.SkuId).Distinct().OrderBy(id => id).ToArray();
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
        return batch;
    }

    public async Task ReleaseQuarantinedBatchAsync(Guid batchId, int quantity, Guid userId, CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
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
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    public async Task WriteOffQuarantinedBatchAsync(Guid batchId, int quantity, Guid userId, CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        await using var transaction = _dbContext.Database.IsRelational()
            ? await _dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var batch = _dbContext.Database.IsRelational()
            ? await _dbContext.InventoryBatches.FromSqlInterpolated($"select * from inventory.inventory_batches where id = {batchId} for update").AsTracking().SingleOrDefaultAsync(cancellationToken)
            : await _dbContext.InventoryBatches.SingleOrDefaultAsync(value => value.Id == batchId, cancellationToken);
        if (batch is null || !batch.IsQuarantined || batch.Quantity < quantity)
            throw new InvalidOperationException("Quarantined batch is unavailable or the write-off quantity exceeds its balance.");
        batch.Quantity -= quantity;
        AddTransaction(batch.LocationId, batch.SkuId, InventoryTransactionTypes.QuarantineWriteOff, -quantity, InventoryQuantityUnits.Pack, userId, batch.CreatedFrom, _clock.EgyptNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
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
        return batch;
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
            AddTransaction(locationId, skuId, InventoryTransactionTypes.StocktakeAdjustment, delta, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
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
        AddTransaction(locationId, skuId, InventoryTransactionTypes.StocktakeAdjustment, delta, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
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
        AddTransaction(locationId, skuId, transactionType, -quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
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
        AddTransaction(locationId, skuId, transactionType, -quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
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
        CancellationToken cancellationToken = default,
        string transactionType = InventoryTransactionTypes.Sale)
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

        if (looseLots.Sum(lot => lot.LoosePieceQuantity) < pieceQuantity)
        {
            throw new InvalidOperationException("Insufficient loose pieces. Edit the piece quantity or use Packs.");
        }

        foreach (var lot in looseLots)
        {
            if (remaining == 0)
            {
                break;
            }

            var take = Math.Min(lot.LoosePieceQuantity, remaining);
            lot.LoosePieceQuantity -= take;
            remaining -= take;
            allocations.Add(new PieceAllocation(lot.Id, lot.SourceBatchId, take, lot.LotNumber, lot.BatchExpiryDate, lot.PieceExpiryDate, false));
        }

        AddTransaction(locationId, skuId, transactionType, -pieceQuantity, InventoryQuantityUnits.Piece, userId, referenceOperationId, _clock.EgyptNow);
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
        CancellationToken cancellationToken = default,
        string transactionType = InventoryTransactionTypes.Sale,
        bool allowOpeningSelectedPacks = false)
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

        var availableLoosePieces = looseLots.Sum(lot => lot.LoosePieceQuantity);
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

            var take = Math.Min(lot.LoosePieceQuantity, remaining);
            lot.LoosePieceQuantity -= take;
            remaining -= take;
            allocations.Add(new PieceAllocation(lot.Id, lot.SourceBatchId, take, lot.LotNumber, lot.BatchExpiryDate, lot.PieceExpiryDate, false));
        }

        if (remaining > 0)
        {
            var packsToOpen = (remaining + piecesPerPack - 1) / piecesPerPack;
            var batch = await LoadSelectedBatchAsync(locationId, skuId, normalizedLot, expiryDate, cancellationToken);
            if (batch is null || batch.Quantity < packsToOpen)
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
        return allocations;
    }

    public async Task AdjustStocktakePiecesAsync(Guid locationId, Guid skuId, string? lotNumber, DateOnly? expiryDate, int delta, Guid userId, Guid? referenceOperationId = null, CancellationToken cancellationToken = default)
    {
        if (delta == 0) return;
        var normalizedLot = NormalizeBlank(lotNumber);
        var lots = await _dbContext.OpenedPieceLots
            .Where(value => value.LocationId == locationId && value.SkuId == skuId && value.LotNumber == normalizedLot && value.BatchExpiryDate == expiryDate)
            .OrderBy(value => value.PieceExpiryDate == null).ThenBy(value => value.PieceExpiryDate).ToListAsync(cancellationToken);
        if (delta < 0)
        {
            var remaining = -delta;
            foreach (var lot in lots)
            {
                var take = Math.Min(remaining, lot.LoosePieceQuantity);
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

        var lotSnapshot = await _dbContext.OpenedPieceLots
            .AsNoTracking()
            .Where(value => value.Id == openedPieceLotId && (value.PieceExpiryDate == null || value.PieceExpiryDate >= today))
            .Select(value => new { value.LocationId, value.SkuId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The original opened piece lot is unavailable or expired.");
        var restored = await _dbContext.OpenedPieceLots
            .Where(value => value.Id == openedPieceLotId && (value.PieceExpiryDate == null || value.PieceExpiryDate >= today))
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.LoosePieceQuantity, value => value.LoosePieceQuantity + pieceQuantity), cancellationToken);
        if (restored != 1)
            throw new InvalidOperationException("The original opened piece lot is unavailable or expired.");
        AddTransaction(lotSnapshot.LocationId, lotSnapshot.SkuId, InventoryTransactionTypes.ReturnRestore, pieceQuantity, InventoryQuantityUnits.Piece, userId, referenceOperationId, _clock.EgyptNow);
        await _dbContext.SaveChangesAsync(cancellationToken);
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
        AddTransaction(locationId, skuId, transactionType, -quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
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
        AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveWithRep, -quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
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
        AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveReleaseInWarehouse, quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
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
        AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveReleaseInWarehouse, released, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
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
            AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveWithRep, -quantity, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        await ExecuteRelationalWriteAsync(async () =>
        {
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
        AddTransaction(locationId, skuId, InventoryTransactionTypes.ReserveReleaseWithRep, released, InventoryQuantityUnits.Pack, userId, referenceOperationId, now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return released;
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
        if (_dbContext.Database.CurrentTransaction is not null)
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
