using System.Text.Json;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Inventory.Services;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Primitives;
using Lensee.SharedKernel.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static class InventoryEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapInventoryEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/inventory").WithTags("Inventory");

        group.MapGet("/locations", ListLocationsAsync).RequireAuthorization("inventory.read");
        group.MapGet("/stock-balances", ListStockBalancesAsync).RequireAuthorization("inventory.read");
        group.MapGet("/product-totals", ListProductTotalsAsync).RequireAuthorization("inventory.read");
        group.MapGet("/stock-balances/{locationId:guid}/{skuId:guid}", GetStockBalanceAsync).RequireAuthorization("inventory.read");
        group.MapGet("/stock-options", ListStockOptionsAsync).RequireAuthorization("inventory.read");
        group.MapPut("/stock-balances/{locationId:guid}/{skuId:guid}/target", SetTargetQuantityAsync).RequireAuthorization("inventory.write");
        group.MapGet("/batches", ListBatchesAsync).RequireAuthorization("inventory.read");
        group.MapGet("/transfer-blocked-batches", ListTransferBlockedBatchesAsync).RequireAuthorization("inventory.read");
        group.MapGet("/transactions", ListTransactionsAsync).RequireAuthorization("inventory.read");
        group.MapPost("/receipts", CreateReceiptAsync).RequireAuthorization("inventory.write");

        return group;
    }

    private static async Task<IResult> ListLocationsAsync(
        InventoryDbContext dbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Locations.AsQueryable();
        if (IsWarehouseClerk(currentUser))
        {
            if (currentUser.LocationId is not { } clerkLocationId)
            {
                return Results.Forbid();
            }

            query = query.Where(location => location.Id == clerkLocationId);
        }

        var locations = await query
            .OrderBy(location => location.Name)
            .Select(location => new LocationResponse(location.Id, location.Name, location.LocationType, location.IsActive))
            .ToListAsync(cancellationToken);

        return Results.Ok(locations);
    }

    private static async Task<IResult> ListStockBalancesAsync(
        Guid? locationId,
        Guid? skuId,
        bool? includeZeroStock,
        int? page,
        int? pageSize,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!TryResolveLocationScope(currentUser, locationId, out var scopedLocationId, out var forbidden))
        {
            return forbidden;
        }

        var request = new PageRequest(page ?? 1, pageSize ?? 25);
        var query = inventoryDbContext.StockBalances.Include(balance => balance.Location).AsQueryable();
        if (scopedLocationId.HasValue)
        {
            query = query.Where(balance => balance.LocationId == scopedLocationId.Value);
        }
        if (skuId.HasValue)
        {
            query = query.Where(balance => balance.SkuId == skuId.Value);
        }

        var rows = await query
            .OrderBy(balance => balance.Location.Name)
            .ThenBy(balance => balance.SkuId)
            .ToListAsync(cancellationToken);
        var skuLookup = await LoadSkuLookupAsync(catalogDbContext, rows.Select(row => row.SkuId), cancellationToken);
        var loosePieces = await LoadLoosePiecesAsync(inventoryDbContext, rows.Select(row => (row.LocationId, row.SkuId)), cancellationToken);
        var response = rows
            .Select(balance => ToResponse(balance, skuLookup, loosePieces.GetValueOrDefault((balance.LocationId, balance.SkuId))))
            .ToList();

        if (includeZeroStock == true)
        {
            response.AddRange(await BuildZeroStockRowsAsync(
                inventoryDbContext,
                catalogDbContext,
                scopedLocationId,
                skuId,
                rows,
                cancellationToken));
        }

        var ordered = response
            .OrderBy(balance => balance.LocationName)
            .ThenBy(balance => balance.SkuCode ?? balance.SkuId.ToString())
            .ToList();
        var pageItems = ordered
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToList();

        return Results.Ok(new PagedResult<StockBalanceResponse>(pageItems, request.Page, request.PageSize, ordered.Count));
    }

    private static async Task<IResult> GetStockBalanceAsync(
        Guid locationId,
        Guid skuId,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!CanAccessLocation(currentUser, locationId))
        {
            return Results.Forbid();
        }

        var balance = await inventoryDbContext.StockBalances
            .Include(value => value.Location)
            .FirstOrDefaultAsync(value => value.LocationId == locationId && value.SkuId == skuId, cancellationToken);
        if (balance is null)
        {
            return Results.NotFound();
        }

        var skuLookup = await LoadSkuLookupAsync(catalogDbContext, [skuId], cancellationToken);
        var loosePieces = await LoadLoosePiecesAsync(inventoryDbContext, [(locationId, skuId)], cancellationToken);
        return Results.Ok(ToResponse(balance, skuLookup, loosePieces.GetValueOrDefault((locationId, skuId))));
    }

    private static async Task<IResult> ListProductTotalsAsync(
        Guid? locationId,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!TryResolveLocationScope(currentUser, locationId, out var scopedLocationId, out var forbidden))
        {
            return forbidden;
        }

        var query = inventoryDbContext.StockBalances
            .Include(balance => balance.Location)
            .Where(balance => balance.AvailableQty > 0)
            .AsQueryable();
        if (scopedLocationId.HasValue)
        {
            query = query.Where(balance => balance.LocationId == scopedLocationId.Value);
        }

        var balances = await query.ToListAsync(cancellationToken);
        var skuLookup = await LoadSkuLookupAsync(catalogDbContext, balances.Select(balance => balance.SkuId), cancellationToken);
        var loosePieces = await LoadLoosePiecesAsync(inventoryDbContext, balances.Select(balance => (balance.LocationId, balance.SkuId)), cancellationToken);

        var rows = balances
            .Select(balance =>
            {
                skuLookup.TryGetValue(balance.SkuId, out var sku);
                return new
                {
                    Balance = balance,
                    Sku = sku,
                    AvailablePieces = ToTotalPieces(balance.AvailableQty, sku, loosePieces.GetValueOrDefault((balance.LocationId, balance.SkuId)))
                };
            })
            .Where(row => row.Sku is not null)
            .GroupBy(row => new { row.Sku!.ProductId, row.Sku.ProductName })
            .Select(group => new InventoryProductTotalResponse(
                group.Key.ProductId,
                group.Key.ProductName,
                group.Select(row => row.Balance.SkuId).Distinct().Count(),
                group.Sum(row => row.Balance.AvailableQty),
                group.Any(row => row.AvailablePieces.HasValue) ? group.Sum(row => row.AvailablePieces ?? 0) : null))
            .OrderBy(row => row.ProductName)
            .ToList();

        return Results.Ok(rows);
    }
    private static async Task<IResult> ListStockOptionsAsync(
        Guid locationId,
        Guid skuId,
        string? entryMode,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        OperationsDbContext operationsDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!CanAccessLocation(currentUser, locationId))
        {
            return Results.Forbid();
        }

        var location = await inventoryDbContext.Locations.FirstOrDefaultAsync(value => value.Id == locationId && value.IsActive, cancellationToken);
        if (location is null)
        {
            return Results.NotFound();
        }

        var sku = await catalogDbContext.Skus
            .Include(value => value.Product)
            .FirstOrDefaultAsync(value => value.Id == skuId && value.IsActive && value.DeletedAt == null, cancellationToken);
        if (sku is null)
        {
            return Results.NotFound();
        }

        var today = DateOnly.FromDateTime(clock.EgyptNow);
        var reservedByBatch = await LoadReservedBatchQuantitiesAsync(operationsDbContext, locationId, cancellationToken);
        var batches = await inventoryDbContext.InventoryBatches
            .Where(batch =>
                batch.LocationId == locationId &&
                batch.SkuId == skuId &&
                batch.Quantity > 0 &&
                batch.ExpiryDate != null &&
                batch.ExpiryDate >= today)
            .OrderBy(batch => batch.ExpiryDate == null)
            .ThenBy(batch => batch.ExpiryDate)
            .ThenBy(batch => batch.LotNumber)
            .ToListAsync(cancellationToken);

        var packGroups = batches
            .Select(batch => new
            {
                batch.LotNumber,
                batch.ExpiryDate,
                Quantity = Math.Max(batch.Quantity - reservedByBatch.GetValueOrDefault(batch.Id), 0)
            })
            .Where(batch => batch.Quantity > 0)
            .GroupBy(batch => (LotNumber: NormalizeBlank(batch.LotNumber), batch.ExpiryDate))
            .ToDictionary(group => group.Key, group => group.Sum(batch => batch.Quantity));

        var looseGroups = await inventoryDbContext.OpenedPieceLots
            .Where(lot =>
                lot.LocationId == locationId &&
                lot.SkuId == skuId &&
                lot.LoosePieceQuantity > 0 &&
                lot.PieceExpiryDate != null &&
                lot.PieceExpiryDate >= today)
            .GroupBy(lot => new { LotNumber = lot.LotNumber, ExpiryDate = lot.BatchExpiryDate })
            .Select(group => new
            {
                group.Key.LotNumber,
                group.Key.ExpiryDate,
                Quantity = group.Sum(lot => lot.LoosePieceQuantity)
            })
            .ToListAsync(cancellationToken);
        var looseByKey = looseGroups.ToDictionary(
            group => (LotNumber: NormalizeBlank(group.LotNumber), group.ExpiryDate),
            group => group.Quantity);

        var allowsPieces = AllowsPieceDisplay(location.LocationType);
        var piecesPerPack = sku.Product.PiecesPerPack.GetValueOrDefault();
        var requestedPieces = string.Equals(entryMode, "Pieces", StringComparison.OrdinalIgnoreCase);
        var keys = packGroups.Keys.Concat(looseByKey.Keys)
            .Distinct()
            .OrderBy(key => key.ExpiryDate == null)
            .ThenBy(key => key.ExpiryDate)
            .ThenBy(key => key.LotNumber)
            .ToList();

        var options = keys
            .Select(key =>
            {
                packGroups.TryGetValue(key, out var packQuantity);
                looseByKey.TryGetValue(key, out var loosePieceQuantity);
                int? pieceQuantity = allowsPieces && piecesPerPack > 0
                    ? packQuantity * piecesPerPack + loosePieceQuantity
                    : null;
                return new StockOptionResponse(
                    locationId,
                    skuId,
                    key.LotNumber,
                    key.ExpiryDate,
                    packQuantity,
                    pieceQuantity,
                    loosePieceQuantity,
                    BuildStockOptionLabel(key.LotNumber, key.ExpiryDate, packQuantity, pieceQuantity, requestedPieces));
            })
            .Where(option => requestedPieces ? option.PieceQuantity is > 0 : option.PackQuantity > 0)
            .ToList();

        return Results.Ok(options);
    }

    private static async Task<IResult> SetTargetQuantityAsync(
        Guid locationId,
        Guid skuId,
        TargetQuantityRequest request,
        InventoryDbContext dbContext,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        if (request.TargetPacks < 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.TargetPacks)] = ["Target packs must be zero or greater."]
            });
        }

        var balance = await dbContext.StockBalances.FirstOrDefaultAsync(value => value.LocationId == locationId && value.SkuId == skuId, cancellationToken);
        if (balance is null)
        {
            return Results.NotFound();
        }

        balance.TargetQty = request.TargetPacks;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogWriter.WriteAsync("StockBalance", balance.Id, "SetTarget", new { balance.LocationId, balance.SkuId, balance.TargetQty }, cancellationToken: cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> ListBatchesAsync(
        Guid? locationId,
        Guid? skuId,
        bool? includeEmpty,
        int? page,
        int? pageSize,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!TryResolveLocationScope(currentUser, locationId, out var scopedLocationId, out var forbidden))
        {
            return forbidden;
        }

        var request = new PageRequest(page ?? 1, pageSize ?? 25);
        var query = inventoryDbContext.InventoryBatches.Include(batch => batch.Location).AsQueryable();
        if (scopedLocationId.HasValue)
        {
            query = query.Where(batch => batch.LocationId == scopedLocationId.Value);
        }
        if (skuId.HasValue)
        {
            query = query.Where(batch => batch.SkuId == skuId.Value);
        }
        if (includeEmpty != true)
        {
            query = query.Where(batch => batch.Quantity > 0);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(batch => batch.ExpiryDate == null)
            .ThenBy(batch => batch.ExpiryDate)
            .ThenBy(batch => batch.Location.Name)
            .ThenBy(batch => batch.LotNumber)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var skuLookup = await LoadSkuLookupAsync(catalogDbContext, rows.Select(row => row.SkuId), cancellationToken);
        var response = rows.Select(batch => ToResponse(batch, skuLookup)).ToList();

        return Results.Ok(new PagedResult<InventoryBatchResponse>(response, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> ListTransactionsAsync(
        Guid? locationId,
        Guid? skuId,
        int? page,
        int? pageSize,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (!TryResolveLocationScope(currentUser, locationId, out var scopedLocationId, out var forbidden))
        {
            return forbidden;
        }

        var request = new PageRequest(page ?? 1, pageSize ?? 25);
        var query = inventoryDbContext.StockTransactions.Include(transaction => transaction.Location).AsQueryable();
        if (scopedLocationId.HasValue)
        {
            query = query.Where(transaction => transaction.LocationId == scopedLocationId.Value);
        }
        if (skuId.HasValue)
        {
            query = query.Where(transaction => transaction.SkuId == skuId.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(transaction => transaction.CreatedAt)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var skuLookup = await LoadSkuLookupAsync(catalogDbContext, rows.Select(row => row.SkuId), cancellationToken);
        var response = rows.Select(transaction => ToResponse(transaction, skuLookup)).ToList();

        return Results.Ok(new PagedResult<StockTransactionResponse>(response, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> ListTransferBlockedBatchesAsync(
        Guid? locationId,
        Guid? skuId,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!TryResolveLocationScope(currentUser, locationId, out var scopedLocationId, out var forbidden))
        {
            return forbidden;
        }

        var query = inventoryDbContext.InventoryBatches
            .Include(batch => batch.Location)
            .Where(batch => batch.Quantity > 0 && batch.ExpiryDate != null)
            .AsQueryable();
        if (scopedLocationId.HasValue)
        {
            query = query.Where(batch => batch.LocationId == scopedLocationId.Value);
        }
        if (skuId.HasValue)
        {
            query = query.Where(batch => batch.SkuId == skuId.Value);
        }

        var batches = await query
            .OrderBy(batch => batch.ExpiryDate)
            .ThenBy(batch => batch.Location.Name)
            .ThenBy(batch => batch.LotNumber)
            .ToListAsync(cancellationToken);
        var skuIds = batches.Select(batch => batch.SkuId).Distinct().ToArray();
        var skuLookup = await catalogDbContext.Skus
            .Include(sku => sku.Product)
            .Where(sku => skuIds.Contains(sku.Id))
            .Select(sku => new
            {
                sku.Id,
                sku.ProductId,
                sku.SkuCode,
                ProductName = sku.Product.Name,
                sku.Product.PiecesPerPack,
                sku.Product.SellMode
            })
            .ToDictionaryAsync(sku => sku.Id, cancellationToken);

        var today = DateOnly.FromDateTime(clock.EgyptNow);
        var rows = new List<TransferBlockedBatchResponse>();
        foreach (var batch in batches)
        {
            if (!skuLookup.TryGetValue(batch.SkuId, out var sku))
            {
                continue;
            }

            var isExpired = batch.ExpiryDate < today;
            if (!isExpired)
            {
                continue;
            }

            rows.Add(new TransferBlockedBatchResponse(
                batch.Id,
                batch.LocationId,
                batch.Location.Name,
                batch.Location.LocationType,
                batch.SkuId,
                sku.SkuCode,
                sku.ProductName,
                batch.LotNumber,
                batch.ExpiryDate,
                batch.Quantity,
                ToPieces(batch.Quantity, new SkuLookup(sku.Id, sku.ProductId, sku.SkuCode, sku.ProductName, sku.PiecesPerPack, sku.SellMode, true), batch.Location.LocationType),
                null,
                null,
                "Expired"));
        }

        return Results.Ok(rows);
    }

    private static async Task<IResult> CreateReceiptAsync(
        InventoryReceiptRequest request,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        StockLedgerService ledgerService,
        ICurrentUser currentUser,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var errors = ValidateReceipt(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (!await inventoryDbContext.Locations.AnyAsync(location => location.Id == request.LocationId && location.IsActive, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.LocationId)] = ["Location must exist and be active."]
            });
        }

        if (!await catalogDbContext.Skus.AnyAsync(sku => sku.Id == request.SkuId && sku.IsActive && sku.DeletedAt == null && sku.Product.IsActive && sku.Product.DeletedAt == null, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.SkuId)] = ["SKU must exist and be active."]
            });
        }

        var userId = currentUser.UserId ?? Guid.Empty;
        var batch = await ledgerService.ReceiveAsync(
            request.LocationId,
            request.SkuId,
            request.PackQuantity,
            userId,
            request.LotNumber,
            request.ExpiryDate,
            request.Notes,
            cancellationToken: cancellationToken);
        await auditLogWriter.WriteAsync("InventoryReceipt", batch.Id, "Create", new { request.LocationId, request.SkuId, request.PackQuantity, request.LotNumber, request.ExpiryDate }, request.PackQuantity, cancellationToken);

        return Results.Created($"/api/v1/inventory/batches/{batch.Id}", new InventoryReceiptResponse(batch.Id, batch.LocationId, batch.SkuId, batch.Quantity));
    }

    private static Dictionary<string, string[]> ValidateReceipt(InventoryReceiptRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.LocationId == Guid.Empty)
        {
            errors[nameof(request.LocationId)] = ["Location is required."];
        }
        if (request.SkuId == Guid.Empty)
        {
            errors[nameof(request.SkuId)] = ["SKU is required."];
        }
        if (request.PackQuantity <= 0)
        {
            errors[nameof(request.PackQuantity)] = ["Pack quantity must be greater than zero."];
        }
        if (request.LotNumber?.Length > 100)
        {
            errors[nameof(request.LotNumber)] = ["Lot number must be 100 characters or fewer."];
        }

        return errors;
    }

    private static bool TryResolveLocationScope(ICurrentUser currentUser, Guid? requestedLocationId, out Guid? scopedLocationId, out IResult forbidden)
    {
        scopedLocationId = requestedLocationId;
        forbidden = Results.Forbid();
        if (!IsWarehouseClerk(currentUser))
        {
            return true;
        }

        if (currentUser.LocationId is not { } clerkLocationId)
        {
            return false;
        }

        if (requestedLocationId.HasValue && requestedLocationId.Value != clerkLocationId)
        {
            return false;
        }

        scopedLocationId = clerkLocationId;
        return true;
    }

    private static bool CanAccessLocation(ICurrentUser currentUser, Guid locationId) =>
        !IsWarehouseClerk(currentUser) || currentUser.LocationId == locationId;

    private static bool IsWarehouseClerk(ICurrentUser currentUser) =>
        string.Equals(currentUser.Role, LenseeRoles.WarehouseClerk, StringComparison.OrdinalIgnoreCase);

    private static async Task<Dictionary<Guid, SkuLookup>> LoadSkuLookupAsync(CatalogDbContext dbContext, IEnumerable<Guid> skuIds, CancellationToken cancellationToken)
    {
        var ids = skuIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await dbContext.Skus
            .Include(sku => sku.Product)
            .Where(sku => ids.Contains(sku.Id))
            .Select(sku => new SkuLookup(sku.Id, sku.ProductId, sku.SkuCode, sku.Product.Name, sku.Product.PiecesPerPack, sku.Product.SellMode, sku.IsActive && sku.DeletedAt == null && sku.Product.IsActive && sku.Product.DeletedAt == null))
            .ToDictionaryAsync(sku => sku.Id, cancellationToken);
    }

    private static async Task<IReadOnlyList<StockBalanceResponse>> BuildZeroStockRowsAsync(
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        Guid? locationId,
        Guid? skuId,
        IReadOnlyCollection<StockBalance> existingBalances,
        CancellationToken cancellationToken)
    {
        var existingKeys = existingBalances
            .Select(balance => (balance.LocationId, balance.SkuId))
            .ToHashSet();
        var locationsQuery = inventoryDbContext.Locations.Where(location => location.IsActive).AsQueryable();
        if (locationId.HasValue)
        {
            locationsQuery = locationsQuery.Where(location => location.Id == locationId.Value);
        }

        var locations = await locationsQuery.OrderBy(location => location.Name).ToListAsync(cancellationToken);
        var skuQuery = catalogDbContext.Skus
            .Include(sku => sku.Product)
            .Where(sku => sku.IsActive && sku.DeletedAt == null && sku.Product.IsActive && sku.Product.DeletedAt == null)
            .AsQueryable();
        if (skuId.HasValue)
        {
            skuQuery = skuQuery.Where(sku => sku.Id == skuId.Value);
        }

        var skus = await skuQuery
            .OrderBy(sku => sku.SkuCode)
            .Select(sku => new SkuLookup(sku.Id, sku.ProductId, sku.SkuCode, sku.Product.Name, sku.Product.PiecesPerPack, sku.Product.SellMode, true))
            .ToListAsync(cancellationToken);
        var rows = new List<StockBalanceResponse>();
        foreach (var location in locations)
        {
            foreach (var sku in skus)
            {
                if (existingKeys.Contains((location.Id, sku.Id)))
                {
                    continue;
                }

                rows.Add(ToZeroStockResponse(location, sku));
            }
        }

        return rows;
    }

    private static StockBalanceResponse ToZeroStockResponse(Location location, SkuLookup sku) =>
        new(
            location.Id,
            location.Name,
            location.LocationType,
            sku.Id,
            sku.SkuCode,
            sku.ProductName,
            sku.IsActive,
            sku.PiecesPerPack,
            sku.SellMode,
            0,
            ToPieces(0, sku, location.LocationType),
            0,
            ToPieces(0, sku, location.LocationType),
            0,
            ToPieces(0, sku, location.LocationType),
            null,
            null,
            0,
            null);

    private static StockBalanceResponse ToResponse(StockBalance balance, IReadOnlyDictionary<Guid, SkuLookup> skuLookup, int loosePieces)
    {
        skuLookup.TryGetValue(balance.SkuId, out var sku);
        var availablePieces = ToPieces(balance.AvailableQty, sku, balance.Location.LocationType);
        return new StockBalanceResponse(
            balance.LocationId,
            balance.Location.Name,
            balance.Location.LocationType,
            balance.SkuId,
            sku?.SkuCode,
            sku?.ProductName,
            sku?.IsActive,
            sku?.PiecesPerPack,
            sku?.SellMode,
            balance.AvailableQty,
            availablePieces.HasValue ? availablePieces + loosePieces : null,
            balance.ReservedInWarehouseQty,
            ToPieces(balance.ReservedInWarehouseQty, sku, balance.Location.LocationType),
            balance.ReservedWithRepQty,
            ToPieces(balance.ReservedWithRepQty, sku, balance.Location.LocationType),
            balance.TargetQty,
            ToPieces(balance.TargetQty, sku, balance.Location.LocationType),
            balance.RowVersion,
            balance.LastUpdated);
    }

    private static InventoryBatchResponse ToResponse(InventoryBatch batch, IReadOnlyDictionary<Guid, SkuLookup> skuLookup)
    {
        skuLookup.TryGetValue(batch.SkuId, out var sku);
        return new InventoryBatchResponse(
            batch.Id,
            batch.LocationId,
            batch.Location.Name,
            batch.Location.LocationType,
            batch.SkuId,
            sku?.SkuCode,
            sku?.ProductName,
            sku?.IsActive,
            batch.LotNumber,
            batch.ExpiryDate,
            batch.Quantity,
            ToPieces(batch.Quantity, sku, batch.Location.LocationType),
            batch.Notes,
            batch.CreatedAt);
    }

    private static StockTransactionResponse ToResponse(StockTransaction transaction, IReadOnlyDictionary<Guid, SkuLookup> skuLookup)
    {
        skuLookup.TryGetValue(transaction.SkuId, out var sku);
        return new StockTransactionResponse(
            transaction.Id,
            transaction.LocationId,
            transaction.Location.Name,
            transaction.Location.LocationType,
            transaction.SkuId,
            sku?.SkuCode,
            sku?.ProductName,
            sku?.IsActive,
            transaction.TransactionType,
            transaction.QuantityChange,
            ToPieces(transaction.QuantityChange, sku, transaction.Location.LocationType),
            transaction.ReferenceOperationId,
            transaction.CreatedAt);
    }

    private static int? ToPieces(int? packs, SkuLookup? sku, string locationType) =>
        packs.HasValue && AllowsPieceDisplay(locationType) && sku?.PiecesPerPack is > 0 ? packs.Value * sku.PiecesPerPack.Value : null;

    private static int? ToTotalPieces(int packs, SkuLookup? sku, int loosePieces) =>
        sku?.PiecesPerPack is > 0 ? (packs * sku.PiecesPerPack.Value) + loosePieces : null;

    private static async Task<Dictionary<(Guid LocationId, Guid SkuId), int>> LoadLoosePiecesAsync(
        InventoryDbContext dbContext,
        IEnumerable<(Guid LocationId, Guid SkuId)> keys,
        CancellationToken cancellationToken)
    {
        var keyList = keys.Distinct().ToList();
        if (keyList.Count == 0)
        {
            return [];
        }

        var locationIds = keyList.Select(key => key.LocationId).Distinct().ToArray();
        var skuIds = keyList.Select(key => key.SkuId).Distinct().ToArray();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var lots = await dbContext.OpenedPieceLots
            .Where(lot => locationIds.Contains(lot.LocationId) && skuIds.Contains(lot.SkuId) && lot.LoosePieceQuantity > 0 && (lot.PieceExpiryDate == null || lot.PieceExpiryDate >= today))
            .ToListAsync(cancellationToken);

        return lots
            .GroupBy(lot => (lot.LocationId, lot.SkuId))
            .ToDictionary(group => group.Key, group => group.Sum(lot => lot.LoosePieceQuantity));
    }

    private static async Task<Dictionary<Guid, int>> LoadReservedBatchQuantitiesAsync(
        OperationsDbContext dbContext,
        Guid locationId,
        CancellationToken cancellationToken)
    {
        var operations = await dbContext.OperationLogs
            .Include(operation => operation.OperationVersions)
            .Where(operation =>
                !operation.IsDeleted &&
                operation.SourceLocationId == locationId &&
                operation.Status == "Reserved")
            .ToListAsync(cancellationToken);

        var reserved = new Dictionary<Guid, int>();
        foreach (var operation in operations)
        {
            foreach (var allocation in ReadOperationAllocations(operation))
            {
                foreach (var batch in allocation.Allocations)
                {
                    reserved.TryGetValue(batch.BatchId, out var current);
                    reserved[batch.BatchId] = current + batch.Quantity;
                }
            }
        }

        return reserved;
    }

    private static IReadOnlyList<InventoryTransferAllocationSnapshot> ReadOperationAllocations(OperationLog operation)
    {
        var snapshot = operation.OperationVersions
            .OrderByDescending(version => version.VersionNumber)
            .Select(version =>
            {
                try
                {
                    return JsonSerializer.Deserialize<InventoryOperationSnapshot>(version.SnapshotData, JsonOptions);
                }
                catch
                {
                    return null;
                }
            })
            .FirstOrDefault(value => value?.TransferAllocations.Count > 0);

        return snapshot?.TransferAllocations ?? [];
    }

    private static string BuildStockOptionLabel(string? lotNumber, DateOnly? expiryDate, int packQuantity, int? pieceQuantity, bool requestedPieces)
    {
        var expiry = expiryDate?.ToString("yyyy-MM-dd") ?? "No expiry";
        var lot = string.IsNullOrWhiteSpace(lotNumber) ? "No lot" : lotNumber;
        var quantity = requestedPieces && pieceQuantity.HasValue
            ? $"{pieceQuantity.Value} piece(s)"
            : $"{packQuantity} pack(s)";
        return $"{expiry} / {lot} / {quantity}";
    }

    private static string? NormalizeBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool AllowsPieceDisplay(string locationType) =>
        !string.Equals(locationType, "MainWarehouse", StringComparison.OrdinalIgnoreCase);

}

public sealed record LocationResponse(Guid Id, string Name, string LocationType, bool IsActive);

public sealed record StockBalanceResponse(
    Guid LocationId,
    string LocationName,
    string LocationType,
    Guid SkuId,
    string? SkuCode,
    string? ProductName,
    bool? SkuIsActive,
    int? PiecesPerPack,
    string? SellMode,
    int AvailablePacks,
    int? AvailablePieces,
    int ReservedInWarehousePacks,
    int? ReservedInWarehousePieces,
    int ReservedWithRepPacks,
    int? ReservedWithRepPieces,
    int? TargetPacks,
    int? TargetPieces,
    int RowVersion,
    DateTime? LastUpdated);

public sealed record InventoryProductTotalResponse(Guid ProductId, string ProductName, int SkuCount, int TotalPacks, int? TotalPieces);

public sealed record InventoryBatchResponse(
    Guid Id,
    Guid LocationId,
    string LocationName,
    string LocationType,
    Guid SkuId,
    string? SkuCode,
    string? ProductName,
    bool? SkuIsActive,
    string? LotNumber,
    DateOnly? ExpiryDate,
    int PackQuantity,
    int? PieceQuantity,
    string? Notes,
    DateTime CreatedAt);

public sealed record StockOptionResponse(
    Guid LocationId,
    Guid SkuId,
    string? LotNumber,
    DateOnly? ExpiryDate,
    int PackQuantity,
    int? PieceQuantity,
    int LoosePieceQuantity,
    string Label);

public sealed record StockTransactionResponse(
    Guid Id,
    Guid LocationId,
    string LocationName,
    string LocationType,
    Guid SkuId,
    string? SkuCode,
    string? ProductName,
    bool? SkuIsActive,
    string TransactionType,
    int PackChange,
    int? PieceChange,
    Guid? ReferenceOperationId,
    DateTime CreatedAt);

public sealed record TransferBlockedBatchResponse(
    Guid Id,
    Guid LocationId,
    string LocationName,
    string LocationType,
    Guid SkuId,
    string? SkuCode,
    string? ProductName,
    string? LotNumber,
    DateOnly? ExpiryDate,
    int PackQuantity,
    int? PieceQuantity,
    string? OpenedExpiryDuration,
    DateOnly? MinimumTransferExpiryDate,
    string Reason);

public sealed record TargetQuantityRequest(int? TargetPacks);

public sealed record InventoryReceiptRequest(Guid LocationId, Guid SkuId, int PackQuantity, string? LotNumber, DateOnly? ExpiryDate, string? Notes);

public sealed record InventoryReceiptResponse(Guid BatchId, Guid LocationId, Guid SkuId, int BatchPackQuantity);

internal sealed record SkuLookup(Guid Id, Guid ProductId, string SkuCode, string ProductName, int? PiecesPerPack, string? SellMode, bool IsActive);

internal sealed record InventoryOperationSnapshot(
    string OperationType,
    string Status,
    Guid? SourceLocationId,
    Guid? DestinationLocationId,
    IReadOnlyList<InventoryOperationLineSnapshot> Lines,
    IReadOnlyList<InventoryTransferAllocationSnapshot> TransferAllocations);

internal sealed record InventoryOperationLineSnapshot(Guid SkuId, string SkuCode, string ProductName, string Section, int PackQuantity, string? LotNumber, DateOnly? ExpiryDate);

internal sealed record InventoryTransferAllocationSnapshot(Guid SkuId, IReadOnlyList<BatchAllocation> Allocations);
