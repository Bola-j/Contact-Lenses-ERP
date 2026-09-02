using System.Text.Json;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Inventory.Services;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

public sealed class InventoryReceiptCommandService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly InventoryDbContext _inventoryDbContext;
    private readonly CatalogDbContext _catalogDbContext;
    private readonly IdentityDbContext _identityDbContext;
    private readonly StockLedgerService _ledgerService;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditLogWriter _auditLogWriter;

    public InventoryReceiptCommandService(
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        IdentityDbContext identityDbContext,
        StockLedgerService ledgerService,
        ICurrentUser currentUser,
        IAuditLogWriter auditLogWriter)
    {
        _inventoryDbContext = inventoryDbContext;
        _catalogDbContext = catalogDbContext;
        _identityDbContext = identityDbContext;
        _ledgerService = ledgerService;
        _currentUser = currentUser;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<InventoryReceiptExecutionResult> ExecuteAsync(
        Guid commandKey,
        string requestHash,
        Guid locationId,
        Guid skuId,
        int packQuantity,
        string? lotNumber,
        DateOnly? expiryDate,
        string? notes,
        CancellationToken cancellationToken)
    {
        InventoryReceiptExecutionResult? result = null;
        await SharedDbTransaction.ExecuteAsync(_inventoryDbContext, async () =>
        {
            var invalidReference = await LockAndValidateActiveReferencesAsync(locationId, skuId, cancellationToken);
            if (invalidReference is not null)
            {
                result = InventoryReceiptExecutionResult.InvalidTarget(invalidReference);
                return;
            }

            var command = await ClaimAsync(commandKey, requestHash, cancellationToken);
            if (!string.Equals(command.RequestHash, requestHash, StringComparison.Ordinal))
            {
                result = InventoryReceiptExecutionResult.KeyReused();
                return;
            }

            command.LastSeenAt = DateTime.UtcNow;
            if (string.Equals(command.Status, "Completed", StringComparison.Ordinal))
            {
                var storedResponse = JsonSerializer.Deserialize<InventoryReceiptExecutionResponse>(command.ResponseBody!, JsonOptions)
                    ?? throw new InvalidOperationException("The completed receipt command has no replayable response.");
                await _inventoryDbContext.SaveChangesAsync(cancellationToken);
                result = InventoryReceiptExecutionResult.Completed(storedResponse, true);
                return;
            }

            var receipt = await _ledgerService.ReceiveReceiptWithLedgerAsync(
                locationId,
                skuId,
                packQuantity,
                _currentUser.UserId ?? Guid.Empty,
                lotNumber,
                expiryDate,
                notes,
                cancellationToken);
            var response = new InventoryReceiptExecutionResponse(
                receipt.Batch.Id,
                receipt.Batch.LocationId,
                receipt.Batch.SkuId,
                receipt.Batch.Quantity);
            command.BatchId = receipt.Batch.Id;
            command.StockTransactionId = receipt.StockTransactionId;
            command.ResponseBatchQuantity = response.BatchPackQuantity;
            command.ResponseStatusCode = StatusCodes.Status201Created;
            command.ResponseBody = JsonSerializer.Serialize(response, JsonOptions);
            command.Status = "Completed";
            await _auditLogWriter.WriteAsync(
                "InventoryReceipt",
                receipt.Batch.Id,
                "Create",
                new { locationId, skuId, packQuantity, lotNumber, expiryDate },
                packQuantity,
                cancellationToken);
            await _inventoryDbContext.SaveChangesAsync(cancellationToken);
            result = InventoryReceiptExecutionResult.Completed(response, false);
        }, cancellationToken, _identityDbContext, _catalogDbContext);

        return result ?? throw new InvalidOperationException("The receipt command did not produce a result.");
    }

    /// <summary>
    /// Resolves a completed command before callers validate mutable business state.
    /// A replay must remain available even when the referenced SKU or location was
    /// later deactivated; new commands still go through the normal validation path.
    /// </summary>
    public async Task<InventoryReceiptReplayResult> TryReplayAsync(
        Guid commandKey,
        string requestHash,
        CancellationToken cancellationToken)
    {
        var command = await _inventoryDbContext.InventoryReceiptCommands
            .AsNoTracking()
            .SingleOrDefaultAsync(value => value.Key == commandKey, cancellationToken);
        if (command is null)
        {
            return InventoryReceiptReplayResult.Missing();
        }

        if (!string.Equals(command.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return InventoryReceiptReplayResult.KeyReused();
        }

        if (!string.Equals(command.Status, "Completed", StringComparison.Ordinal))
        {
            return InventoryReceiptReplayResult.Pending();
        }

        var response = JsonSerializer.Deserialize<InventoryReceiptExecutionResponse>(command.ResponseBody!, JsonOptions)
            ?? throw new InvalidOperationException("The completed receipt command has no replayable response.");
        return InventoryReceiptReplayResult.Completed(response);
    }

    private async Task<InventoryReceiptCommand> ClaimAsync(Guid key, string requestHash, CancellationToken cancellationToken)
    {
        if (!_inventoryDbContext.Database.IsRelational())
        {
            var existing = await _inventoryDbContext.InventoryReceiptCommands
                .SingleOrDefaultAsync(value => value.Key == key, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var created = NewCommand(key, requestHash);
            _inventoryDbContext.InventoryReceiptCommands.Add(created);
            return created;
        }

        await _inventoryDbContext.Database.ExecuteSqlInterpolatedAsync($"""
            insert into inventory.inventory_receipt_commands
                (id, key, request_hash, status, created_at, last_seen_at)
            values ({Guid.NewGuid()}, {key}, {requestHash}, {"Pending"}, {DateTime.UtcNow}, {DateTime.UtcNow})
            on conflict (key) do nothing
            """, cancellationToken);
        return await _inventoryDbContext.InventoryReceiptCommands
            .FromSqlInterpolated($"select * from inventory.inventory_receipt_commands where key = {key} for update")
            .SingleAsync(cancellationToken);
    }

    private async Task<string?> LockAndValidateActiveReferencesAsync(
        Guid locationId,
        Guid skuId,
        CancellationToken cancellationToken)
    {
        if (!_inventoryDbContext.Database.IsRelational())
        {
            var location = await _inventoryDbContext.Locations.SingleOrDefaultAsync(value => value.Id == locationId, cancellationToken);
            if (location is null || !location.IsActive)
            {
                return "LocationId";
            }

            var sku = await _catalogDbContext.Skus.SingleOrDefaultAsync(value => value.Id == skuId, cancellationToken);
            if (sku is null || !sku.IsActive || sku.DeletedAt is not null)
            {
                return "SkuId";
            }

            var product = await _catalogDbContext.Products.SingleOrDefaultAsync(value => value.Id == sku.ProductId, cancellationToken);
            return product is null || !product.IsActive || product.DeletedAt is not null ? "SkuId" : null;
        }

        var lockedLocation = await _inventoryDbContext.Locations
            .FromSqlInterpolated($"select * from inventory.locations where id = {locationId} for update")
            .SingleOrDefaultAsync(cancellationToken);
        if (lockedLocation is null || !lockedLocation.IsActive)
        {
            return "LocationId";
        }

        var lockedSku = await _catalogDbContext.Skus
            .FromSqlInterpolated($"select * from catalog.skus where id = {skuId} for update")
            .SingleOrDefaultAsync(cancellationToken);
        if (lockedSku is null || !lockedSku.IsActive || lockedSku.DeletedAt is not null)
        {
            return "SkuId";
        }

        var lockedProduct = await _catalogDbContext.Products
            .FromSqlInterpolated($"select * from catalog.products where id = {lockedSku.ProductId} for update")
            .SingleOrDefaultAsync(cancellationToken);
        return lockedProduct is null || !lockedProduct.IsActive || lockedProduct.DeletedAt is not null ? "SkuId" : null;
    }

    private static InventoryReceiptCommand NewCommand(Guid key, string requestHash) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        RequestHash = requestHash,
        Status = "Pending",
        CreatedAt = DateTime.UtcNow,
        LastSeenAt = DateTime.UtcNow
    };
}

public sealed record InventoryReceiptExecutionResponse(
    Guid BatchId,
    Guid LocationId,
    Guid SkuId,
    int BatchPackQuantity);

public sealed record InventoryReceiptExecutionResult(
    bool IsKeyReused,
    bool IsReplay,
    InventoryReceiptExecutionResponse? Response,
    string? InvalidReference)
{
    public static InventoryReceiptExecutionResult KeyReused() => new(true, false, null, null);

    public static InventoryReceiptExecutionResult InvalidTarget(string field) => new(false, false, null, field);

    public static InventoryReceiptExecutionResult Completed(InventoryReceiptExecutionResponse response, bool replay) =>
        new(false, replay, response, null);
}

public sealed record InventoryReceiptReplayResult(
    bool IsKeyReused,
    InventoryReceiptExecutionResponse? Response)
{
    public static InventoryReceiptReplayResult Missing() => new(false, null);

    public static InventoryReceiptReplayResult Pending() => new(false, null);

    public static InventoryReceiptReplayResult KeyReused() => new(true, null);

    public static InventoryReceiptReplayResult Completed(InventoryReceiptExecutionResponse response) => new(false, response);
}
