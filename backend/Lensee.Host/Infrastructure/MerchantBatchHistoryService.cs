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
        var returned = new Dictionary<MerchantBatchHistoryKey, int>();

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
                                operation.ConfirmedAt ?? operation.CreatedAt);
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
                            operation.ConfirmedAt ?? operation.CreatedAt);
                    }
                }
            }
            else if (operation.OperationType == "Return")
            {
                foreach (var line in operation.OperationLines)
                {
                    AddReturned(returned, new MerchantBatchHistoryKey(operationMerchantId, line.SkuId, NormalizeLot(line.LotNumber), line.ExpiryDate), line.Quantity);
                }
            }
            else if (operation.OperationType == "Change")
            {
                foreach (var line in operation.OperationLines.Where(line => line.Section == "ChangeOut"))
                {
                    AddReturned(returned, new MerchantBatchHistoryKey(operationMerchantId, line.SkuId, NormalizeLot(line.LotNumber), line.ExpiryDate), line.Quantity);
                }
            }
        }

        return sold.Keys.Concat(returned.Keys)
            .Distinct()
            .Select(key =>
            {
                sold.TryGetValue(key, out var sale);
                returned.TryGetValue(key, out var returnedQuantity);
                return new MerchantBatchHistoryRow(
                    key,
                    sale?.Quantity ?? 0,
                    returnedQuantity,
                    sale?.LatestSaleAt);
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

    private static void AddSold(Dictionary<MerchantBatchHistoryKey, QuantityState> values, MerchantBatchHistoryKey key, int quantity, DateTime saleAt)
    {
        if (!values.TryGetValue(key, out var state))
        {
            values[key] = new QuantityState(quantity, saleAt);
            return;
        }

        state.Quantity += quantity;
        if (saleAt > state.LatestSaleAt)
        {
            state.LatestSaleAt = saleAt;
        }
    }

    private static void AddReturned(Dictionary<MerchantBatchHistoryKey, int> values, MerchantBatchHistoryKey key, int quantity)
    {
        values.TryGetValue(key, out var current);
        values[key] = current + quantity;
    }

    public static string? NormalizeLot(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private sealed class QuantityState
    {
        public QuantityState(int quantity, DateTime latestSaleAt)
        {
            Quantity = quantity;
            LatestSaleAt = latestSaleAt;
        }

        public int Quantity { get; set; }
        public DateTime LatestSaleAt { get; set; }
    }

    private sealed record OperationSnapshot(IReadOnlyList<TransferAllocationSnapshot>? TransferAllocations);
    private sealed record TransferAllocationSnapshot(Guid SkuId, IReadOnlyList<BatchAllocationSnapshot> Allocations);
    private sealed record BatchAllocationSnapshot(int Quantity, string? LotNumber, DateOnly? ExpiryDate);
}

public sealed record MerchantBatchHistoryKey(Guid MerchantId, Guid SkuId, string? LotNumber, DateOnly? ExpiryDate);

public sealed record MerchantBatchHistoryRow(
    MerchantBatchHistoryKey Key,
    int SoldQuantity,
    int ReturnedQuantity,
    DateTime? LatestSaleAt)
{
    public int RecordedBalanceQuantity => Math.Max(SoldQuantity - ReturnedQuantity, 0);
}
