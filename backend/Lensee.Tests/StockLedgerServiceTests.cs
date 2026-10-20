using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Inventory.Services;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lensee.Tests;

public sealed class StockLedgerServiceTests
{
    [Fact]
    public async Task ReceiveAsync_CreatesBatchBalanceAndReceiptTransaction_InPacks()
    {
        await using var dbContext = CreateContext();
        var service = new StockLedgerService(dbContext, new FixedClock());
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var batch = await service.ReceiveAsync(locationId, skuId, 12, userId, "LOT-1", new DateOnly(2028, 6, 1));

        var balance = await dbContext.StockBalances.SingleAsync();
        var transaction = await dbContext.StockTransactions.SingleAsync();
        Assert.Equal(12, batch.Quantity);
        Assert.Equal(12, balance.AvailableQty);
        Assert.Equal(1, balance.RowVersion);
        Assert.Equal(InventoryTransactionTypes.Receipt, transaction.TransactionType);
        Assert.Equal(12, transaction.QuantityChange);
        Assert.Equal(InventoryQuantityUnits.Pack, transaction.QuantityUnit);
    }

    [Fact]
    public async Task IssueFefoAsync_AllocatesPacksEarliestExpiryFirst_AndNullExpiryLast()
    {
        await using var dbContext = CreateContext();
        var service = new StockLedgerService(dbContext, new FixedClock());
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var nullExpiry = await service.ReceiveAsync(locationId, skuId, 5, userId, "NULL", null);
        var late = await service.ReceiveAsync(locationId, skuId, 5, userId, "LATE", new DateOnly(2029, 1, 1));
        var early = await service.ReceiveAsync(locationId, skuId, 5, userId, "EARLY", new DateOnly(2028, 1, 1));

        var allocations = await service.IssueFefoAsync(locationId, skuId, 11, InventoryTransactionTypes.Sale, userId);

        Assert.Equal(new[] { early.Id, late.Id, nullExpiry.Id }, allocations.Select(value => value.BatchId));
        Assert.Equal(new[] { 5, 5, 1 }, allocations.Select(value => value.Quantity));
        Assert.Equal(4, (await dbContext.InventoryBatches.SingleAsync(batch => batch.Id == nullExpiry.Id)).Quantity);
        Assert.Equal(4, (await dbContext.StockTransactions.CountAsync()));
        Assert.Equal(4, (await dbContext.StockBalances.SingleAsync()).RowVersion);
    }

    [Fact]
    public async Task IssueFefoAsync_RejectsInsufficientBatchQuantity()
    {
        await using var dbContext = CreateContext();
        var service = new StockLedgerService(dbContext, new FixedClock());
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await service.ReceiveAsync(locationId, skuId, 2, userId);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.IssueFefoAsync(locationId, skuId, 3, InventoryTransactionTypes.Sale, userId));
    }

    [Fact]
    public async Task ReserveAndRelease_DoNotAllowNegativeQuantities()
    {
        await using var dbContext = CreateContext();
        var service = new StockLedgerService(dbContext, new FixedClock());
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await service.ReceiveAsync(locationId, skuId, 5, userId);
        await service.ReserveInWarehouseAsync(locationId, skuId, 3, userId);
        await service.ReleaseInWarehouseAsync(locationId, skuId, 2, userId);

        var balance = await dbContext.StockBalances.SingleAsync();
        Assert.Equal(4, balance.AvailableQty);
        Assert.Equal(1, balance.ReservedInWarehouseQty);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReleaseInWarehouseAsync(locationId, skuId, 2, userId));
    }

    [Fact]
    public async Task ReserveInWarehouseFefoAsync_ReservesPacksAndKeepsBatchesUntilCommitted()
    {
        await using var dbContext = CreateContext();
        var service = new StockLedgerService(dbContext, new FixedClock());
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var late = await service.ReceiveAsync(locationId, skuId, 4, userId, "LATE", new DateOnly(2029, 1, 1));
        var early = await service.ReceiveAsync(locationId, skuId, 3, userId, "EARLY", new DateOnly(2028, 1, 1));

        var allocations = await service.ReserveInWarehouseFefoAsync(locationId, skuId, 5, userId);

        var balance = await dbContext.StockBalances.SingleAsync();
        Assert.Equal(new[] { early.Id, late.Id }, allocations.Select(value => value.BatchId));
        Assert.Equal(new[] { 3, 2 }, allocations.Select(value => value.Quantity));
        Assert.Equal(2, balance.AvailableQty);
        Assert.Equal(5, balance.ReservedInWarehouseQty);
        Assert.Equal(3, (await dbContext.InventoryBatches.SingleAsync(batch => batch.Id == early.Id)).Quantity);
        Assert.Equal(4, (await dbContext.InventoryBatches.SingleAsync(batch => batch.Id == late.Id)).Quantity);
    }

    [Fact]
    public async Task ReserveInWarehouseFefoAsync_UsesShortDatedUnexpiredBatchesByFefo()
    {
        await using var dbContext = CreateContext();
        var service = new StockLedgerService(dbContext, new FixedClock());
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var shortDated = await service.ReceiveAsync(locationId, skuId, 4, userId, "SHORT", new DateOnly(2026, 9, 1));
        var valid = await service.ReceiveAsync(locationId, skuId, 5, userId, "VALID", new DateOnly(2027, 1, 1));

        var allocations = await service.ReserveInWarehouseFefoAsync(locationId, skuId, 3, userId, minimumExpiryDate: new DateOnly(2026, 12, 25));

        Assert.Equal(shortDated.Id, Assert.Single(allocations).BatchId);
        Assert.Equal(3, allocations[0].Quantity);
        Assert.Equal(4, (await dbContext.InventoryBatches.SingleAsync(batch => batch.Id == shortDated.Id)).Quantity);
        Assert.Equal(5, (await dbContext.InventoryBatches.SingleAsync(batch => batch.Id == valid.Id)).Quantity);
    }

    [Fact]
    public async Task PlanReserveInWarehouseFefoAsync_DoesNotMutateStock()
    {
        await using var dbContext = CreateContext();
        var service = new StockLedgerService(dbContext, new FixedClock());
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await service.ReceiveAsync(locationId, skuId, 5, userId, "VALID", new DateOnly(2028, 6, 1));

        var allocations = await service.PlanReserveInWarehouseFefoAsync(locationId, skuId, 3, minimumExpiryDate: new DateOnly(2027, 1, 1));

        var balance = await dbContext.StockBalances.SingleAsync();
        var batch = await dbContext.InventoryBatches.SingleAsync();
        Assert.Equal(3, Assert.Single(allocations).Quantity);
        Assert.Equal(5, balance.AvailableQty);
        Assert.Equal(0, balance.ReservedInWarehouseQty);
        Assert.Equal(5, batch.Quantity);
        Assert.Single(await dbContext.StockTransactions.ToListAsync());
    }


    [Fact]
    public async Task ReserveInWarehouseFefoAsync_RejectsWhenOnlyExpiredBatchesExist()
    {
        await using var dbContext = CreateContext();
        var service = new StockLedgerService(dbContext, new FixedClock());
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await service.ReceiveAsync(locationId, skuId, 4, userId, "EXPIRED", new DateOnly(2026, 1, 1));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReserveInWarehouseFefoAsync(locationId, skuId, 3, userId, minimumExpiryDate: new DateOnly(2026, 12, 25)));
    }

    [Fact]
    public async Task CommitReservedInWarehouseOutAsync_DecreasesReservedStockAndAllocatedBatches()
    {
        await using var dbContext = CreateContext();
        var service = new StockLedgerService(dbContext, new FixedClock());
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await service.ReceiveAsync(locationId, skuId, 5, userId, "LOT", new DateOnly(2028, 6, 1));
        var allocations = await service.ReserveInWarehouseFefoAsync(locationId, skuId, 4, userId);

        await service.CommitReservedInWarehouseOutAsync(locationId, skuId, allocations, userId);

        var balance = await dbContext.StockBalances.SingleAsync();
        var transaction = await dbContext.StockTransactions.OrderBy(value => value.CreatedAt).LastAsync();
        Assert.Equal(1, balance.AvailableQty);
        Assert.Equal(0, balance.ReservedInWarehouseQty);
        Assert.Equal(1, await dbContext.InventoryBatches.Select(batch => batch.Quantity).SingleAsync());
        Assert.Equal(InventoryTransactionTypes.SupplyOut, transaction.TransactionType);
        Assert.Equal(-4, transaction.QuantityChange);
    }

    [Fact]
    public async Task ReceiveSupplyAsync_AppendsSupplyInTransaction()
    {
        await using var dbContext = CreateContext();
        var service = new StockLedgerService(dbContext, new FixedClock());
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await service.ReceiveSupplyAsync(locationId, skuId, 6, userId, "TRANSFER", new DateOnly(2028, 6, 1));

        var transaction = await dbContext.StockTransactions.SingleAsync();
        Assert.Equal(InventoryTransactionTypes.SupplyIn, transaction.TransactionType);
        Assert.Equal(6, transaction.QuantityChange);
    }

    [Fact]
    public async Task IssueFefoAsync_RejectsInvalidTransactionType()
    {
        await using var dbContext = CreateContext();
        var service = new StockLedgerService(dbContext, new FixedClock());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.IssueFefoAsync(Guid.NewGuid(), Guid.NewGuid(), 1, "Manual", Guid.NewGuid()));
    }

    [Fact]
    public async Task IssuePiecesFromSelectionAsync_RecordsActualPieceIssue_WhenLooseStockSatisfiesRequest()
    {
        await using var dbContext = CreateContext();
        var service = new StockLedgerService(dbContext, new FixedClock());
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        dbContext.InventoryBatches.Add(new InventoryBatch
        {
            Id = batchId,
            LocationId = locationId,
            SkuId = skuId,
            LotNumber = "LOT",
            ExpiryDate = new DateOnly(2028, 6, 1),
            Quantity = 1,
            CreatedBy = userId,
            CreatedAt = new DateTime(2026, 6, 25)
        });
        dbContext.OpenedPieceLots.Add(new OpenedPieceLot
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            SkuId = skuId,
            SourceBatchId = batchId,
            LotNumber = "LOT",
            BatchExpiryDate = new DateOnly(2028, 6, 1),
            OpenedDate = new DateOnly(2026, 6, 25),
            PieceExpiryDate = new DateOnly(2028, 6, 1),
            LoosePieceQuantity = 5,
            CreatedBy = userId,
            CreatedAt = new DateTime(2026, 6, 25)
        });
        await dbContext.SaveChangesAsync();

        await service.IssuePiecesFromSelectionAsync(locationId, skuId, 3, 10, "LOT", new DateOnly(2028, 6, 1), userId);

        var transaction = Assert.Single(await dbContext.StockTransactions.ToListAsync());
        Assert.Equal(InventoryTransactionTypes.Sale, transaction.TransactionType);
        Assert.Equal(-3, transaction.QuantityChange);
        Assert.Equal(InventoryQuantityUnits.Piece, transaction.QuantityUnit);
        Assert.Equal(2, await dbContext.OpenedPieceLots.Select(value => value.LoosePieceQuantity).SingleAsync());
    }

    [Fact]
    public async Task IssuePiecesFromSelectionAsync_DoesNotOpenASealedPack_WhenSelectedLoosePiecesCoverANonMultipleRequest()
    {
        await using var dbContext = CreateContext();
        var service = new StockLedgerService(dbContext, new FixedClock());
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var expiry = new DateOnly(2028, 6, 1);

        await service.ReceiveAsync(locationId, skuId, 1, userId, "LOT", expiry);
        var batch = await dbContext.InventoryBatches.SingleAsync();
        dbContext.OpenedPieceLots.Add(new OpenedPieceLot
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            SkuId = skuId,
            SourceBatchId = batch.Id,
            LotNumber = "LOT",
            BatchExpiryDate = expiry,
            OpenedDate = new DateOnly(2026, 6, 25),
            PieceExpiryDate = expiry,
            LoosePieceQuantity = 2,
            CreatedBy = userId,
            CreatedAt = new DateTime(2026, 6, 25)
        });
        await dbContext.SaveChangesAsync();

        await service.IssuePiecesFromSelectionAsync(locationId, skuId, 2, 3, "LOT", expiry, userId, allowOpeningSelectedPacks: true);

        Assert.DoesNotContain(await dbContext.StockTransactions.ToListAsync(), value => value.TransactionType == InventoryTransactionTypes.PackOpen);
        Assert.Equal(1, await dbContext.InventoryBatches.Select(value => value.Quantity).SingleAsync());
        Assert.Equal(0, await dbContext.OpenedPieceLots.SumAsync(value => value.LoosePieceQuantity));
    }

    [Fact]
    public async Task IssuePiecesFromSelectionAsync_OpensOnlyTheAdditionalPackNeededAfterUsingSelectedLoosePieces()
    {
        await using var dbContext = CreateContext();
        var service = new StockLedgerService(dbContext, new FixedClock());
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var expiry = new DateOnly(2028, 6, 1);

        await service.ReceiveAsync(locationId, skuId, 2, userId, "LOT", expiry);
        var batch = await dbContext.InventoryBatches.SingleAsync();
        dbContext.OpenedPieceLots.Add(new OpenedPieceLot
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            SkuId = skuId,
            SourceBatchId = batch.Id,
            LotNumber = "LOT",
            BatchExpiryDate = expiry,
            OpenedDate = new DateOnly(2026, 6, 25),
            PieceExpiryDate = expiry,
            LoosePieceQuantity = 1,
            CreatedBy = userId,
            CreatedAt = new DateTime(2026, 6, 25)
        });
        await dbContext.SaveChangesAsync();

        await service.IssuePiecesFromSelectionAsync(locationId, skuId, 2, 3, "LOT", expiry, userId, allowOpeningSelectedPacks: true);

        var packOpen = await dbContext.StockTransactions.SingleAsync(value => value.TransactionType == InventoryTransactionTypes.PackOpen);
        Assert.Equal((-1, InventoryQuantityUnits.Pack), (packOpen.QuantityChange, packOpen.QuantityUnit));
        Assert.Equal(1, await dbContext.InventoryBatches.Select(value => value.Quantity).SingleAsync());
        Assert.Equal(2, await dbContext.OpenedPieceLots.SumAsync(value => value.LoosePieceQuantity));
    }

    [Fact]
    public async Task IssuePiecesFromSelectionAsync_RecordsOnePieceIssueAndLinkedPackOpen_AcrossLooseAndSealedStock()
    {
        await using var dbContext = CreateContext();
        var service = new StockLedgerService(dbContext, new FixedClock());
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await service.ReceiveAsync(locationId, skuId, 1, userId, "LOT", new DateOnly(2028, 6, 1));
        var batch = await dbContext.InventoryBatches.SingleAsync();
        dbContext.OpenedPieceLots.Add(new OpenedPieceLot
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            SkuId = skuId,
            SourceBatchId = batch.Id,
            LotNumber = "LOT",
            BatchExpiryDate = batch.ExpiryDate,
            OpenedDate = new DateOnly(2026, 6, 25),
            PieceExpiryDate = batch.ExpiryDate,
            LoosePieceQuantity = 2,
            CreatedBy = userId,
            CreatedAt = new DateTime(2026, 6, 25)
        });
        await dbContext.SaveChangesAsync();

        await service.IssuePiecesFromSelectionAsync(locationId, skuId, 3, 10, "LOT", new DateOnly(2028, 6, 1), userId, allowOpeningSelectedPacks: true);

        var issue = await dbContext.StockTransactions.SingleAsync(value => value.TransactionType == InventoryTransactionTypes.Sale);
        var packOpen = await dbContext.StockTransactions.SingleAsync(value => value.TransactionType == InventoryTransactionTypes.PackOpen);
        Assert.Equal((-3, InventoryQuantityUnits.Piece), (issue.QuantityChange, issue.QuantityUnit));
        Assert.Equal((-1, InventoryQuantityUnits.Pack), (packOpen.QuantityChange, packOpen.QuantityUnit));
        Assert.Equal(issue.Id, packOpen.RelatedTransactionId);
        Assert.Equal(10, packOpen.ConversionPiecesPerPack);
        // This fixture intentionally holds one sealed 10-piece pack plus two already-open
        // loose pieces. Issuing three consumes the loose pieces, opens the pack, and leaves 9.
        Assert.Equal(9, await dbContext.OpenedPieceLots.SumAsync(value => value.LoosePieceQuantity));
        Assert.Equal(0, await dbContext.InventoryBatches.Select(value => value.Quantity).SingleAsync());
    }

    [Fact]
    public async Task IssuePiecesFefoAsync_RecordsActualPieceIssue()
    {
        await using var dbContext = CreateContext();
        var service = new StockLedgerService(dbContext, new FixedClock());
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var firstBatchId = Guid.NewGuid();
        var secondBatchId = Guid.NewGuid();
        dbContext.InventoryBatches.AddRange(
            new InventoryBatch { Id = firstBatchId, LocationId = locationId, SkuId = skuId, Quantity = 0, ExpiryDate = new DateOnly(2028, 6, 1), CreatedBy = userId, CreatedAt = new DateTime(2026, 6, 25) },
            new InventoryBatch { Id = secondBatchId, LocationId = locationId, SkuId = skuId, Quantity = 0, ExpiryDate = new DateOnly(2028, 6, 1), CreatedBy = userId, CreatedAt = new DateTime(2026, 6, 25) });
        dbContext.OpenedPieceLots.AddRange(
            new OpenedPieceLot { Id = Guid.NewGuid(), LocationId = locationId, SkuId = skuId, SourceBatchId = firstBatchId, BatchExpiryDate = new DateOnly(2028, 6, 1), OpenedDate = new DateOnly(2026, 6, 25), LoosePieceQuantity = 1, CreatedBy = userId, CreatedAt = new DateTime(2026, 6, 25) },
            new OpenedPieceLot { Id = Guid.NewGuid(), LocationId = locationId, SkuId = skuId, SourceBatchId = secondBatchId, BatchExpiryDate = new DateOnly(2028, 6, 1), OpenedDate = new DateOnly(2026, 6, 25), LoosePieceQuantity = 3, CreatedBy = userId, CreatedAt = new DateTime(2026, 6, 25) });
        await dbContext.SaveChangesAsync();

        await service.IssuePiecesFefoAsync(locationId, skuId, 3, 10, userId);

        var transaction = Assert.Single(await dbContext.StockTransactions.ToListAsync());
        Assert.Equal((InventoryTransactionTypes.Sale, -3, InventoryQuantityUnits.Piece), (transaction.TransactionType, transaction.QuantityChange, transaction.QuantityUnit));
        Assert.Equal(1, await dbContext.OpenedPieceLots.SumAsync(value => value.LoosePieceQuantity));
    }

    [Fact]
    public async Task RestorePiecesToOpenedLotAsync_RecordsPieceReturnRestore()
    {
        await using var dbContext = CreateContext();
        var service = new StockLedgerService(dbContext, new FixedClock());
        var locationId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var lot = new OpenedPieceLot
        {
            Id = Guid.NewGuid(),
            LocationId = locationId,
            SkuId = skuId,
            SourceBatchId = Guid.NewGuid(),
            LotNumber = "LOT",
            OpenedDate = new DateOnly(2026, 6, 25),
            LoosePieceQuantity = 1,
            CreatedBy = userId,
            CreatedAt = new DateTime(2026, 6, 25)
        };
        dbContext.OpenedPieceLots.Add(lot);
        await dbContext.SaveChangesAsync();

        await service.RestorePiecesToOpenedLotAsync(lot.Id, 2, userId, operationId);

        var transaction = Assert.Single(await dbContext.StockTransactions.ToListAsync());
        Assert.Equal((InventoryTransactionTypes.ReturnRestore, 2, InventoryQuantityUnits.Piece, operationId), (transaction.TransactionType, transaction.QuantityChange, transaction.QuantityUnit, transaction.ReferenceOperationId));
        Assert.Equal(3, await dbContext.OpenedPieceLots.Select(value => value.LoosePieceQuantity).SingleAsync());
    }

    private static InventoryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase($"ledger-{Guid.NewGuid()}")
            .Options;
        return new InventoryDbContext(options);
    }
}

internal sealed class FixedClock : IClock
{
    public DateTime UtcNow { get; } = new(2026, 6, 25, 10, 0, 0);

    public DateTime EgyptNow { get; } = new(2026, 6, 25, 13, 0, 0);
}
