using System.Text.Json;
using Lensee.Modules.Operations.Data;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

public sealed class MerchantBatchHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly OperationsDbContext _operations;

    public MerchantBatchHistoryService(OperationsDbContext operations)
    {
        _operations = operations;
    }

    public async Task<IReadOnlyList<MerchantBatchHistoryRow>> LoadAsync(
        Guid? merchantId = null,
        Guid? excludeOperationId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _operations.OperationLogs
            .AsNoTracking()
            .Include(operation => operation.OperationLines)
                .ThenInclude(line => line.PieceAllocations)
            .Include(operation => operation.OperationVersions)
            .Where(operation =>
                operation.ClientId.HasValue &&
                operation.Id != excludeOperationId &&
                !operation.IsDeleted &&
                ((operation.Status == "Completed" &&
                    (operation.OperationType == "WholesaleSale" || operation.OperationType == "RetailSale")) ||
                 (operation.Status == "Confirmed" &&
                    (operation.OperationType == "Return" || operation.OperationType == "Change"))));

        if (merchantId.HasValue)
        {
            query = query.Where(operation => operation.ClientId == merchantId.Value);
        }

        var operations = await query.ToListAsync(cancellationToken);
        var sold = new Dictionary<MerchantBatchHistoryKey, QuantityState>();
        var returned = new Dictionary<MerchantBatchHistoryKey, ValueState>();

        foreach (var operation in operations)
        {
            var operationMerchantId = operation.ClientId!.Value;
            if (operation.OperationType is "WholesaleSale" or "RetailSale")
            {
                var allocations = ReadAllocations(operation);
                if (allocations.Count > 0)
                {
                    foreach (var allocation in allocations)
                    {
                        foreach (var batch in allocation.Allocations)
                        {
                            AddSold(
                                sold,
                                new MerchantBatchHistoryKey(operationMerchantId, allocation.SkuId, NormalizeLot(batch.LotNumber), batch.ExpiryDate),
                                batch.Quantity,
                                ToPhysicalPieces(batch.Quantity, operation.OperationLines.Where(line => line.SkuId == allocation.SkuId && line.EntryMode == "Packs").Select(line => line.PiecesPerPackSnapshot).FirstOrDefault()),
                                operation.OperationLines.Where(line => line.SkuId == allocation.SkuId && line.EntryMode == "Packs").Sum(line => line.LineTotal) * batch.Quantity / Math.Max(1, operation.OperationLines.Where(line => line.SkuId == allocation.SkuId && line.EntryMode == "Packs").Sum(line => line.Quantity)),
                                operation.ConfirmedAt ?? operation.CreatedAt,
                                operation.OperationLines.Where(line => line.SkuId == allocation.SkuId && line.EntryMode == "Packs").Select(line => line.PiecesPerPackSnapshot).FirstOrDefault() is not > 0);
                        }
                    }
                }
                else
                {
                    foreach (var line in operation.OperationLines.Where(line => line.EntryMode == "Packs"))
                    {
                        AddSold(
                            sold,
                            new MerchantBatchHistoryKey(operationMerchantId, line.SkuId, NormalizeLot(line.LotNumber), line.ExpiryDate),
                            line.Quantity,
                            ToPhysicalPieces(line.Quantity, line.PiecesPerPackSnapshot),
                            line.LineTotal,
                            operation.ConfirmedAt ?? operation.CreatedAt,
                            line.PiecesPerPackSnapshot is not > 0);
                    }
                }

                foreach (var line in operation.OperationLines.Where(line => line.EntryMode == "Pieces"))
                {
                    if (line.PieceAllocations.Count == 0 || line.PieceAllocations.Sum(value => value.IssuedPieceQuantity) != line.Quantity)
                    {
                        AddSold(sold, new MerchantBatchHistoryKey(operationMerchantId, line.SkuId, NormalizeLot(line.LotNumber), line.ExpiryDate), 0, 0, 0m, operation.ConfirmedAt ?? operation.CreatedAt, true);
                        continue;
                    }

                    foreach (var allocation in line.PieceAllocations)
                    {
                        AddSold(
                            sold,
                            new MerchantBatchHistoryKey(operationMerchantId, line.SkuId, NormalizeLot(allocation.LotNumber), allocation.ExpiryDate),
                            0,
                            allocation.IssuedPieceQuantity,
                            line.LineTotal * allocation.IssuedPieceQuantity / Math.Max(1, line.Quantity),
                            operation.ConfirmedAt ?? operation.CreatedAt,
                            false);
                    }
                }
            }
            else if (operation.OperationType == "Return")
            {
                foreach (var line in operation.OperationLines)
                {
                    AddReturned(returned, new MerchantBatchHistoryKey(operationMerchantId, line.SkuId, NormalizeLot(line.LotNumber), line.ExpiryDate), line.EntryMode == "Packs" ? line.Quantity : 0, ToPhysicalPieces(line.Quantity, line.EntryMode == "Pieces" ? 1 : line.PiecesPerPackSnapshot), line.NormalValue + line.ExceptionValue, line.EntryMode != "Pieces" && line.PiecesPerPackSnapshot is not > 0);
                }
            }
            else if (operation.OperationType == "Change")
            {
                foreach (var line in operation.OperationLines.Where(line => line.Section == "ChangeOut"))
                {
                    AddReturned(returned, new MerchantBatchHistoryKey(operationMerchantId, line.SkuId, NormalizeLot(line.LotNumber), line.ExpiryDate), line.EntryMode == "Packs" ? line.Quantity : 0, ToPhysicalPieces(line.Quantity, line.EntryMode == "Pieces" ? 1 : line.PiecesPerPackSnapshot), line.NormalValue + line.ExceptionValue, line.EntryMode != "Pieces" && line.PiecesPerPackSnapshot is not > 0);
                }
            }
        }

        return sold.Keys.Concat(returned.Keys)
            .Distinct()
            .Select(key =>
            {
                sold.TryGetValue(key, out var sale);
                returned.TryGetValue(key, out var returnedState);
                return new MerchantBatchHistoryRow(
                    key,
                    sale?.Quantity ?? 0,
                    sale?.Value ?? 0m,
                    returnedState?.Quantity ?? 0,
                    returnedState?.Value ?? 0m,
                    sale?.LatestSaleAt,
                    sale?.PhysicalQuantity ?? 0,
                    returnedState?.PhysicalQuantity ?? 0,
                    (sale?.HasLegacyUnitUnknown ?? false) || (returnedState?.HasLegacyUnitUnknown ?? false));
            })
            .OrderBy(row => row.Key.MerchantId)
            .ThenBy(row => row.Key.SkuId)
            .ThenBy(row => row.Key.ExpiryDate)
            .ThenBy(row => row.Key.LotNumber)
            .ToList();
    }

    private static IReadOnlyList<TransferAllocationSnapshot> ReadAllocations(OperationLog operation)
    {
        foreach (var version in operation.OperationVersions.OrderByDescending(version => version.VersionNumber))
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<OperationSnapshot>(version.SnapshotData, JsonOptions);
                if (snapshot?.TransferAllocations is { Count: > 0 })
                {
                    return snapshot.TransferAllocations;
                }
            }
            catch (JsonException)
            {
                // Historical snapshots may use an older shape. Fall back to operation lines.
            }
        }

        return [];
    }

    private static int ToPhysicalPieces(int quantity, int? piecesPerPackSnapshot) =>
        piecesPerPackSnapshot is > 0 ? quantity * piecesPerPackSnapshot.Value : 0;

    private static void AddSold(Dictionary<MerchantBatchHistoryKey, QuantityState> values, MerchantBatchHistoryKey key, int quantity, int physicalQuantity, decimal value, DateTime saleAt, bool hasLegacyUnitUnknown)
    {
        if (!values.TryGetValue(key, out var state))
        {
            values[key] = new QuantityState(quantity, physicalQuantity, value, saleAt, hasLegacyUnitUnknown);
            return;
        }

        state.Quantity += quantity;
        state.PhysicalQuantity += physicalQuantity;
        state.Value += value;
        state.HasLegacyUnitUnknown |= hasLegacyUnitUnknown;
        if (saleAt > state.LatestSaleAt)
        {
            state.LatestSaleAt = saleAt;
        }
    }

    private static void AddReturned(Dictionary<MerchantBatchHistoryKey, ValueState> values, MerchantBatchHistoryKey key, int quantity, int physicalQuantity, decimal value, bool hasLegacyUnitUnknown)
    {
        if (!values.TryGetValue(key, out var current))
        {
            values[key] = new ValueState(quantity, physicalQuantity, value, hasLegacyUnitUnknown);
            return;
        }
        current.Quantity += quantity;
        current.PhysicalQuantity += physicalQuantity;
        current.Value += value;
        current.HasLegacyUnitUnknown |= hasLegacyUnitUnknown;
    }

    public static string? NormalizeLot(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private sealed class QuantityState
    {
        public QuantityState(int quantity, int physicalQuantity, decimal value, DateTime latestSaleAt, bool hasLegacyUnitUnknown)
        {
            Quantity = quantity;
            PhysicalQuantity = physicalQuantity;
            Value = value;
            LatestSaleAt = latestSaleAt;
            HasLegacyUnitUnknown = hasLegacyUnitUnknown;
        }

        public int Quantity { get; set; }
        public int PhysicalQuantity { get; set; }
        public decimal Value { get; set; }
        public DateTime LatestSaleAt { get; set; }
        public bool HasLegacyUnitUnknown { get; set; }
    }

    private sealed class ValueState
    {
        public ValueState(int quantity, int physicalQuantity, decimal value, bool hasLegacyUnitUnknown) { Quantity = quantity; PhysicalQuantity = physicalQuantity; Value = value; HasLegacyUnitUnknown = hasLegacyUnitUnknown; }
        public int Quantity { get; set; }
        public int PhysicalQuantity { get; set; }
        public decimal Value { get; set; }
        public bool HasLegacyUnitUnknown { get; set; }
    }

    private sealed record OperationSnapshot(IReadOnlyList<TransferAllocationSnapshot>? TransferAllocations);
    private sealed record TransferAllocationSnapshot(Guid SkuId, IReadOnlyList<BatchAllocationSnapshot> Allocations);
    private sealed record BatchAllocationSnapshot(int Quantity, string? LotNumber, DateOnly? ExpiryDate);
}

public sealed record MerchantBatchHistoryKey(Guid MerchantId, Guid SkuId, string? LotNumber, DateOnly? ExpiryDate);

public sealed record MerchantBatchHistoryRow(
    MerchantBatchHistoryKey Key,
    int SoldQuantity,
    decimal SoldValue,
    int ReturnedQuantity,
    decimal ReturnedValue,
    DateTime? LatestSaleAt,
    int SoldPhysicalPieces,
    int ReturnedPhysicalPieces,
    bool HasLegacyUnitUnknown)
{
    public int RecordedBalanceQuantity => Math.Max(SoldQuantity - ReturnedQuantity, 0);
    public int RecordedBalancePhysicalPieces => Math.Max(SoldPhysicalPieces - ReturnedPhysicalPieces, 0);
    public decimal RecordedBalanceValue => Math.Max(SoldValue - ReturnedValue, 0m);
}
