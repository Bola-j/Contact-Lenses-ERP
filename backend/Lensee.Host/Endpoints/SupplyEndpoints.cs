using System.Text.Json;
using Lensee.Host.Infrastructure;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Inventory.Services;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static class SupplyEndpoints
{
    private const string Draft = "Draft";
    private const string Received = "Received";
    private const string Cancelled = "Cancelled";
    private const string InventoryReceipt = "InventoryReceipt";
    private static readonly string[] AllowedCostTypes = ["Customs", "Freight", "Clearance", "Handling", "Insurance", "Other"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapSupplyEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/supply/shipments")
            .WithTags("Supply")
            .RequireAuthorization();

        group.MapGet("/", ListShipmentsAsync).RequireAuthorization("supply.read").WithName("ListSupplyShipments");
        group.MapGet("/{id:guid}", GetShipmentAsync).RequireAuthorization("supply.read").WithName("GetSupplyShipment");
        group.MapGet("/{id:guid}/history", GetHistoryAsync).RequireAuthorization("supply.read").WithName("GetSupplyShipmentHistory");
        group.MapPost("/", CreateShipmentAsync).RequireAuthorization("supply.write").WithName("CreateSupplyShipment");
        group.MapPut("/{id:guid}", UpdateShipmentAsync).RequireAuthorization("supply.write").WithName("UpdateSupplyShipment");
        group.MapPost("/{id:guid}/confirm", ConfirmShipmentAsync).RequireAuthorization("supply.write").WithName("ConfirmSupplyShipment");
        group.MapPost("/{id:guid}/cancel", CancelShipmentAsync).RequireAuthorization("supply.write").WithName("CancelSupplyShipment");

        return group;
    }

    private static async Task<IResult> ListShipmentsAsync(
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        string? search,
        string? status,
        CancellationToken cancellationToken)
    {
        var query = operationsDbContext.SupplyShipments
            .Include(value => value.Lines)
            .Include(value => value.Costs)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(value =>
                value.ShipmentNumber.ToLower().Contains(term) ||
                value.SupplierName.ToLower().Contains(term) ||
                (value.InvoiceNumber != null && value.InvoiceNumber.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(value => value.Status == NormalizeStatus(status));
        }

        var shipments = await query
            .OrderByDescending(value => value.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        var locationLookup = await LoadLocationLookupAsync(inventoryDbContext, shipments.Select(value => value.DestinationLocationId), cancellationToken);

        return Results.Ok(shipments.Select(value => ToListResponse(value, locationLookup)).ToList());
    }

    private static async Task<IResult> GetShipmentAsync(Guid id, OperationsDbContext operationsDbContext, InventoryDbContext inventoryDbContext, CancellationToken cancellationToken)
    {
        var shipment = await LoadShipmentAsync(operationsDbContext, id, cancellationToken);
        if (shipment is null)
        {
            return Results.NotFound();
        }

        var locationLookup = await LoadLocationLookupAsync(inventoryDbContext, [shipment.DestinationLocationId], cancellationToken);
        return Results.Ok(ToDetailResponse(shipment, locationLookup));
    }

    private static async Task<IResult> GetHistoryAsync(Guid id, OperationsDbContext operationsDbContext, CancellationToken cancellationToken)
    {
        if (!await operationsDbContext.SupplyShipments.AnyAsync(value => value.Id == id, cancellationToken))
        {
            return Results.NotFound();
        }

        var history = await operationsDbContext.SupplyShipmentHistoryLogs
            .Where(value => value.ShipmentId == id)
            .OrderByDescending(value => value.CreatedAt)
            .Select(value => new SupplyHistoryResponse(value.Id, value.Action, value.ActorUserId, value.CreatedAt, value.Summary))
            .ToListAsync(cancellationToken);

        return Results.Ok(history);
    }

    private static async Task<IResult> CreateShipmentAsync(
        SupplyShipmentRequest request,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        InventoryDbContext inventoryDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var built = await BuildShipmentPartsAsync(request, catalogDbContext, inventoryDbContext, cancellationToken);
        if (built.Errors.Count > 0)
        {
            return Results.ValidationProblem(built.Errors);
        }

        var now = clock.EgyptNow;
        var shipment = new SupplyShipment
        {
            Id = Guid.NewGuid(),
            ShipmentNumber = $"SUP-{now:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}",
            SupplierName = request.SupplierName!.Trim(),
            InvoiceNumber = TrimToNull(request.InvoiceNumber),
            ShipmentDate = request.ShipmentDate ?? now,
            DestinationLocationId = request.DestinationLocationId,
            Status = Draft,
            Notes = TrimToNull(request.Notes),
            CreatedBy = currentUser.UserId ?? Guid.Empty,
            CreatedAt = now
        };

        ApplyParts(shipment, built);
        operationsDbContext.SupplyShipments.Add(shipment);
        AddHistory(operationsDbContext, shipment, "Create", currentUser.UserId ?? Guid.Empty, now, "Shipment created.");
        await operationsDbContext.SaveChangesAsync(cancellationToken);

        return Results.Created($"/api/v1/supply/shipments/{shipment.Id}", ToDetailResponse(shipment, built.LocationLookup));
    }

    private static async Task<IResult> UpdateShipmentAsync(
        Guid id,
        SupplyShipmentRequest request,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        InventoryDbContext inventoryDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var shipment = await operationsDbContext.SupplyShipments.FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (shipment is null)
        {
            return Results.NotFound();
        }

        if (shipment.Status != Draft)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(shipment.Status)] = ["Only draft supply shipments can be edited."] });
        }

        var built = await BuildShipmentPartsAsync(request, catalogDbContext, inventoryDbContext, cancellationToken);
        if (built.Errors.Count > 0)
        {
            return Results.ValidationProblem(built.Errors);
        }

        await using var transaction = operationsDbContext.Database.IsRelational()
            ? await operationsDbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var oldLines = await operationsDbContext.SupplyShipmentLines.Where(value => value.ShipmentId == shipment.Id).ToListAsync(cancellationToken);
        var oldCosts = await operationsDbContext.SupplyShipmentCosts.Where(value => value.ShipmentId == shipment.Id).ToListAsync(cancellationToken);
        operationsDbContext.SupplyShipmentLines.RemoveRange(oldLines);
        operationsDbContext.SupplyShipmentCosts.RemoveRange(oldCosts);
        await operationsDbContext.SaveChangesAsync(cancellationToken);

        var now = clock.EgyptNow;
        shipment.SupplierName = request.SupplierName!.Trim();
        shipment.InvoiceNumber = TrimToNull(request.InvoiceNumber);
        shipment.ShipmentDate = request.ShipmentDate ?? now;
        shipment.DestinationLocationId = request.DestinationLocationId;
        shipment.Notes = TrimToNull(request.Notes);
        shipment.UpdatedBy = currentUser.UserId ?? Guid.Empty;
        shipment.UpdatedAt = now;
        shipment.ProductSubtotal = built.Lines.Sum(value => value.LineSubtotal);
        shipment.CostSubtotal = built.Costs.Sum(value => value.Amount);
        shipment.LandedTotal = shipment.ProductSubtotal + shipment.CostSubtotal;
        foreach (var line in built.Lines)
        {
            line.ShipmentId = shipment.Id;
        }

        foreach (var cost in built.Costs)
        {
            cost.ShipmentId = shipment.Id;
        }

        operationsDbContext.SupplyShipmentLines.AddRange(built.Lines);
        operationsDbContext.SupplyShipmentCosts.AddRange(built.Costs);
        operationsDbContext.SupplyShipmentHistoryLogs.Add(new SupplyShipmentHistory
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipment.Id,
            Action = "Update",
            ActorUserId = currentUser.UserId ?? Guid.Empty,
            CreatedAt = now,
            Summary = "Draft shipment updated.",
            SnapshotData = JsonSerializer.Serialize(new
            {
                shipment.ShipmentNumber,
                shipment.SupplierName,
                shipment.InvoiceNumber,
                shipment.ShipmentDate,
                shipment.DestinationLocationId,
                shipment.Status,
                shipment.ProductSubtotal,
                shipment.CostSubtotal,
                shipment.LandedTotal,
                Lines = built.Lines.Select(line => new { line.SkuId, line.SkuCodeSnapshot, line.Quantity, line.UnitPrice, line.LineSubtotal, line.AllocatedCost, line.LandedUnitCost }),
                Costs = built.Costs.Select(cost => new { cost.CostType, cost.Amount })
            }, JsonOptions)
        });
        await operationsDbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> ConfirmShipmentAsync(
        Guid id,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        InventoryDbContext inventoryDbContext,
        StockLedgerService ledgerService,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var shipment = await LoadShipmentAsync(operationsDbContext, id, cancellationToken);
        if (shipment is null)
        {
            return Results.NotFound();
        }

        if (shipment.Status != Draft)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(shipment.Status)] = ["Only draft supply shipments can be confirmed."] });
        }

        var confirmErrors = await ValidateShipmentForConfirmationAsync(shipment, inventoryDbContext, catalogDbContext, cancellationToken);
        if (confirmErrors.Count > 0)
        {
            return Results.ValidationProblem(confirmErrors);
        }

        var now = clock.EgyptNow;
        var userId = currentUser.UserId ?? Guid.Empty;
        AllocateCosts(shipment.Lines.ToList(), shipment.Costs.Sum(value => value.Amount));
        await SharedDbTransaction.ExecuteAsync(inventoryDbContext, async () =>
        {
            var operation = new OperationLog
            {
                Id = Guid.NewGuid(),
                OperationNumber = $"OP-{now:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}",
                OperationType = InventoryReceipt,
                Status = Received,
                DestinationLocationId = shipment.DestinationLocationId,
                Notes = $"Supply {shipment.ShipmentNumber}. {shipment.Notes}".Trim(),
                CreatedBy = userId,
                CreatedAt = now,
                ConfirmedBy = userId,
                ConfirmedAt = now
            };

            foreach (var line in shipment.Lines)
            {
                operation.OperationLines.Add(new OperationLine
                {
                    Id = Guid.NewGuid(),
                    OperationId = operation.Id,
                    SkuId = line.SkuId,
                    ProductNameSnapshot = line.ProductNameSnapshot,
                    SkuCodeSnapshot = line.SkuCodeSnapshot,
                    Section = "Standard",
                    Quantity = line.Quantity,
                    EntryMode = "Packs",
                    BonusQuantity = 0,
                    UnitPrice = line.UnitPrice.GetValueOrDefault(),
                    UnitCost = line.LandedUnitCost,
                    LineTotal = line.LineSubtotal,
                    LotNumber = line.LotNumber,
                    ExpiryDate = line.ExpiryDate,
                    LineNotes = line.Notes
                });

                await ledgerService.ReceiveSupplyAsync(
                    shipment.DestinationLocationId,
                    line.SkuId,
                    line.Quantity,
                    userId,
                    line.LotNumber,
                    line.ExpiryDate,
                    line.Notes,
                    operation.Id,
                    cancellationToken);
            }

            operation.InventoryReceiptHeader = new InventoryReceiptHeader
            {
                Id = Guid.NewGuid(),
                OperationId = operation.Id,
                SupplierName = shipment.SupplierName,
                InvoiceNumber = shipment.InvoiceNumber,
                ReceiptDate = now
            };

            var version = new OperationVersion
            {
                Id = Guid.NewGuid(),
                OperationId = operation.Id,
                VersionNumber = 1,
                SnapshotData = JsonSerializer.Serialize(new { operation.OperationType, operation.Status, operation.DestinationLocationId, Lines = operation.OperationLines.Select(ToOperationLineSnapshot) }, JsonOptions),
                Reason = "Supply received",
                EditedBy = userId,
                EditedAt = now
            };
            operation.CurrentVersionId = version.Id;
            operation.OperationVersions.Add(version);

            shipment.Status = Received;
            shipment.ConfirmedAt = now;
            shipment.ConfirmedBy = userId;
            shipment.InventoryReceiptOperationId = operation.Id;

            operationsDbContext.OperationLogs.Add(operation);
            AddHistory(operationsDbContext, shipment, "Confirm", userId, now, $"Shipment received through operation {operation.OperationNumber}.");
            await operationsDbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken, operationsDbContext);

        return Results.NoContent();
    }

    private static async Task<IResult> CancelShipmentAsync(
        Guid id,
        OperationsDbContext operationsDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var shipment = await LoadShipmentAsync(operationsDbContext, id, cancellationToken);
        if (shipment is null)
        {
            return Results.NotFound();
        }

        if (shipment.Status != Draft)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(shipment.Status)] = ["Only draft supply shipments can be cancelled."] });
        }

        var now = clock.EgyptNow;
        shipment.Status = Cancelled;
        shipment.CancelledAt = now;
        shipment.CancelledBy = currentUser.UserId ?? Guid.Empty;
        AddHistory(operationsDbContext, shipment, "Cancel", currentUser.UserId ?? Guid.Empty, now, "Draft shipment cancelled.");
        await operationsDbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<SupplyBuildResult> BuildShipmentPartsAsync(SupplyShipmentRequest request, CatalogDbContext catalogDbContext, InventoryDbContext inventoryDbContext, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var lines = request.Lines ?? [];
        var costs = request.Costs ?? [];

        if (string.IsNullOrWhiteSpace(request.SupplierName))
        {
            errors[nameof(request.SupplierName)] = ["Supplier name is required."];
        }
        else if (request.SupplierName.Trim().Length > 255)
        {
            errors[nameof(request.SupplierName)] = ["Supplier name cannot exceed 255 characters."];
        }

        if (request.InvoiceNumber is { Length: > 100 })
        {
            errors[nameof(request.InvoiceNumber)] = ["Invoice number cannot exceed 100 characters."];
        }

        if (request.Notes is { Length: > 4000 })
        {
            errors[nameof(request.Notes)] = ["Notes cannot exceed 4000 characters."];
        }

        if (request.DestinationLocationId == Guid.Empty)
        {
            errors[nameof(request.DestinationLocationId)] = ["Active destination warehouse is required."];
        }

        if (lines.Count == 0)
        {
            errors[nameof(request.Lines)] = ["At least one SKU line is required."];
        }

        var location = await inventoryDbContext.Locations.AsNoTracking().FirstOrDefaultAsync(value => value.Id == request.DestinationLocationId && value.IsActive, cancellationToken);
        if (location is null)
        {
            errors[nameof(request.DestinationLocationId)] = ["Active destination warehouse is required."];
        }

        var skuIds = lines.Select(value => value.SkuId).Distinct().ToArray();
        var skus = await catalogDbContext.Skus
            .Include(value => value.Product)
            .AsNoTracking()
            .Where(value => skuIds.Contains(value.Id) && value.IsActive && value.Product.IsActive)
            .ToDictionaryAsync(value => value.Id, cancellationToken);

        var lineDrafts = new List<SupplyShipmentLine>();
        var duplicateLineKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (!skus.TryGetValue(line.SkuId, out var sku))
            {
                errors[$"{nameof(request.Lines)}[{index}].{nameof(line.SkuId)}"] = ["Active SKU is required."];
                continue;
            }

            if (line.Quantity <= 0)
            {
                errors[$"{nameof(request.Lines)}[{index}].{nameof(line.Quantity)}"] = ["Quantity must be greater than zero."];
            }

            if (line.UnitPrice.HasValue && line.UnitPrice.Value <= 0)
            {
                errors[$"{nameof(request.Lines)}[{index}].{nameof(line.UnitPrice)}"] = ["Unit price must be greater than zero when provided."];
            }

            if (line.LotNumber is { Length: > 100 })
            {
                errors[$"{nameof(request.Lines)}[{index}].{nameof(line.LotNumber)}"] = ["Lot number cannot exceed 100 characters."];
            }

            if (line.Notes is { Length: > 1000 })
            {
                errors[$"{nameof(request.Lines)}[{index}].{nameof(line.Notes)}"] = ["Line notes cannot exceed 1000 characters."];
            }

            var duplicateKey = $"{line.SkuId:N}|{TrimToNull(line.LotNumber)?.ToUpperInvariant() ?? ""}|{line.ExpiryDate?.ToString("O") ?? ""}";
            if (!duplicateLineKeys.Add(duplicateKey))
            {
                errors[$"{nameof(request.Lines)}[{index}]"] = ["Duplicate SKU, lot, and expiry lines must be combined."];
            }

            var quantity = Math.Max(0, line.Quantity);
            var unitPrice = line.UnitPrice.HasValue ? Math.Max(0, line.UnitPrice.Value) : (decimal?)null;
            lineDrafts.Add(new SupplyShipmentLine
            {
                Id = Guid.NewGuid(),
                SkuId = line.SkuId,
                ProductNameSnapshot = sku.Product.Name,
                SkuCodeSnapshot = sku.SkuCode,
                Quantity = quantity,
                UnitPrice = unitPrice,
                LineSubtotal = quantity * (unitPrice ?? 0),
                LotNumber = TrimToNull(line.LotNumber),
                ExpiryDate = line.ExpiryDate,
                Notes = TrimToNull(line.Notes)
            });
        }

        var costDrafts = new List<SupplyShipmentCost>();
        for (var index = 0; index < costs.Count; index++)
        {
            var cost = costs[index];
            if (string.IsNullOrWhiteSpace(cost.CostType))
            {
                errors[$"{nameof(request.Costs)}[{index}].{nameof(cost.CostType)}"] = ["Cost type is required."];
            }
            else if (NormalizeCostType(cost.CostType) is null)
            {
                errors[$"{nameof(request.Costs)}[{index}].{nameof(cost.CostType)}"] = ["Cost type must be Customs, Freight, Clearance, Handling, Insurance, or Other."];
            }

            if (cost.Amount < 0)
            {
                errors[$"{nameof(request.Costs)}[{index}].{nameof(cost.Amount)}"] = ["Cost amount cannot be negative."];
            }

            if (cost.Description is { Length: > 255 })
            {
                errors[$"{nameof(request.Costs)}[{index}].{nameof(cost.Description)}"] = ["Cost description cannot exceed 255 characters."];
            }

            costDrafts.Add(new SupplyShipmentCost
            {
                Id = Guid.NewGuid(),
                CostType = NormalizeCostType(cost.CostType) ?? "Other",
                Description = TrimToNull(cost.Description),
                Amount = Math.Max(0, cost.Amount)
            });
        }

        if (lineDrafts.All(value => value.UnitPrice.HasValue))
        {
            AllocateCosts(lineDrafts, costDrafts.Sum(value => value.Amount));
        }

        var locationLookup = location is null ? new Dictionary<Guid, Location>() : new Dictionary<Guid, Location> { [location.Id] = location };
        return new SupplyBuildResult(errors, lineDrafts, costDrafts, locationLookup);
    }

    private static async Task<Dictionary<string, string[]>> ValidateShipmentForConfirmationAsync(
        SupplyShipment shipment,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (shipment.Lines.Count == 0)
        {
            errors[nameof(shipment.Lines)] = ["At least one SKU line is required."];
        }

        if (shipment.Lines.Any(line => line.Quantity <= 0))
        {
            errors[nameof(shipment.Lines)] = ["Every SKU line quantity must be greater than zero."];
        }

        if (shipment.Lines.Any(line => line.UnitPrice is null or <= 0))
        {
            errors[nameof(shipment.Lines)] = ["Every SKU line needs a unit price greater than zero before confirmation."];
        }

        if (!await inventoryDbContext.Locations.AsNoTracking().AnyAsync(value => value.Id == shipment.DestinationLocationId && value.IsActive, cancellationToken))
        {
            errors[nameof(shipment.DestinationLocationId)] = ["Active destination warehouse is required."];
        }

        var skuIds = shipment.Lines.Select(value => value.SkuId).Distinct().ToArray();
        var activeSkuIds = await catalogDbContext.Skus
            .AsNoTracking()
            .Include(value => value.Product)
            .Where(value => skuIds.Contains(value.Id) && value.IsActive && value.Product.IsActive)
            .Select(value => value.Id)
            .ToArrayAsync(cancellationToken);

        if (activeSkuIds.Length != skuIds.Length)
        {
            errors[nameof(shipment.Lines)] = ["Every SKU line must reference an active SKU before confirmation."];
        }

        return errors;
    }

    private static void ApplyParts(SupplyShipment shipment, SupplyBuildResult built)
    {
        shipment.Lines = built.Lines;
        shipment.Costs = built.Costs;
        shipment.ProductSubtotal = built.Lines.Sum(value => value.LineSubtotal);
        shipment.CostSubtotal = built.Costs.Sum(value => value.Amount);
        shipment.LandedTotal = shipment.ProductSubtotal + shipment.CostSubtotal;
    }

    private static void AllocateCosts(List<SupplyShipmentLine> lines, decimal costTotal)
    {
        var productTotal = lines.Sum(value => value.LineSubtotal);
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var allocated = productTotal == 0
                ? (lines.Count == 0 ? 0 : Math.Round(costTotal / lines.Count, 4))
                : Math.Round(costTotal * (line.LineSubtotal / productTotal), 4);

            if (index == lines.Count - 1)
            {
                allocated = costTotal - lines.Take(index).Sum(value => value.AllocatedCost);
            }

            line.AllocatedCost = allocated;
            line.LandedUnitCost = line.Quantity == 0 ? 0 : Math.Round((line.LineSubtotal + allocated) / line.Quantity, 4);
        }
    }

    private static async Task<SupplyShipment?> LoadShipmentAsync(OperationsDbContext dbContext, Guid id, CancellationToken cancellationToken) =>
        await dbContext.SupplyShipments
            .Include(value => value.Lines)
            .Include(value => value.Costs)
            .Include(value => value.HistoryLogs)
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);

    private static async Task<IReadOnlyDictionary<Guid, Location>> LoadLocationLookupAsync(InventoryDbContext dbContext, IEnumerable<Guid> locationIds, CancellationToken cancellationToken)
    {
        var ids = locationIds.Distinct().ToArray();
        return await dbContext.Locations
            .AsNoTracking()
            .Where(value => ids.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
    }

    private static void AddHistory(OperationsDbContext dbContext, SupplyShipment shipment, string action, Guid actorUserId, DateTime now, string summary)
    {
        var history = new SupplyShipmentHistory
        {
            Id = Guid.NewGuid(),
            ShipmentId = shipment.Id,
            Action = action,
            ActorUserId = actorUserId,
            CreatedAt = now,
            Summary = summary,
            SnapshotData = JsonSerializer.Serialize(ToSnapshot(shipment), JsonOptions)
        };
        shipment.HistoryLogs.Add(history);
        dbContext.SupplyShipmentHistoryLogs.Add(history);
    }

    private static SupplyShipmentListResponse ToListResponse(SupplyShipment shipment, IReadOnlyDictionary<Guid, Location> locationLookup) =>
        new(
            shipment.Id,
            shipment.ShipmentNumber,
            shipment.SupplierName,
            shipment.InvoiceNumber,
            shipment.ShipmentDate,
            shipment.Status,
            shipment.DestinationLocationId,
            locationLookup.TryGetValue(shipment.DestinationLocationId, out var location) ? location.Name : null,
            shipment.Lines.Sum(value => value.Quantity),
            shipment.ProductSubtotal,
            shipment.CostSubtotal,
            shipment.LandedTotal,
            shipment.InventoryReceiptOperationId,
            shipment.CreatedAt);

    private static SupplyShipmentDetailResponse ToDetailResponse(SupplyShipment shipment, IReadOnlyDictionary<Guid, Location> locationLookup) =>
        new(
            shipment.Id,
            shipment.ShipmentNumber,
            shipment.SupplierName,
            shipment.InvoiceNumber,
            shipment.ShipmentDate,
            shipment.Status,
            shipment.DestinationLocationId,
            locationLookup.TryGetValue(shipment.DestinationLocationId, out var location) ? location.Name : null,
            shipment.Notes,
            shipment.ProductSubtotal,
            shipment.CostSubtotal,
            shipment.LandedTotal,
            shipment.InventoryReceiptOperationId,
            shipment.CreatedBy,
            shipment.CreatedAt,
            shipment.ConfirmedBy,
            shipment.ConfirmedAt,
            shipment.CancelledBy,
            shipment.CancelledAt,
            shipment.Lines.Select(line => new SupplyLineResponse(line.Id, line.SkuId, line.SkuCodeSnapshot, line.ProductNameSnapshot, line.Quantity, line.UnitPrice, line.LineSubtotal, line.AllocatedCost, line.LandedUnitCost, line.LotNumber, line.ExpiryDate, line.Notes)).ToList(),
            shipment.Costs.Select(cost => new SupplyCostResponse(cost.Id, cost.CostType, cost.Description, cost.Amount)).ToList(),
            shipment.HistoryLogs.OrderByDescending(value => value.CreatedAt).Select(value => new SupplyHistoryResponse(value.Id, value.Action, value.ActorUserId, value.CreatedAt, value.Summary)).ToList());

    private static object ToSnapshot(SupplyShipment shipment) => new
    {
        shipment.ShipmentNumber,
        shipment.SupplierName,
        shipment.InvoiceNumber,
        shipment.ShipmentDate,
        shipment.DestinationLocationId,
        shipment.Status,
        shipment.ProductSubtotal,
        shipment.CostSubtotal,
        shipment.LandedTotal,
        Lines = shipment.Lines.Select(line => new { line.SkuId, line.SkuCodeSnapshot, line.Quantity, line.UnitPrice, line.LineSubtotal, line.AllocatedCost, line.LandedUnitCost }),
        Costs = shipment.Costs.Select(cost => new { cost.CostType, cost.Amount })
    };

    private static object ToOperationLineSnapshot(OperationLine line) => new
    {
        line.SkuId,
        line.SkuCodeSnapshot,
        line.ProductNameSnapshot,
        line.Quantity,
        line.UnitPrice,
        line.UnitCost,
        line.LineTotal,
        line.LotNumber,
        line.ExpiryDate,
        line.LineNotes
    };

    private static string NormalizeStatus(string value) =>
        string.Equals(value, Received, StringComparison.OrdinalIgnoreCase) ? Received :
        string.Equals(value, Cancelled, StringComparison.OrdinalIgnoreCase) ? Cancelled :
        Draft;

    private static string? NormalizeCostType(string? value) =>
        AllowedCostTypes.FirstOrDefault(costType => string.Equals(costType, value?.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record SupplyBuildResult(Dictionary<string, string[]> Errors, List<SupplyShipmentLine> Lines, List<SupplyShipmentCost> Costs, IReadOnlyDictionary<Guid, Location> LocationLookup);
}

public sealed record SupplyShipmentRequest(
    string? SupplierName,
    string? InvoiceNumber,
    DateTime? ShipmentDate,
    Guid DestinationLocationId,
    string? Notes,
    IReadOnlyList<SupplyShipmentLineRequest>? Lines,
    IReadOnlyList<SupplyShipmentCostRequest>? Costs);

public sealed record SupplyShipmentLineRequest(Guid SkuId, int Quantity, decimal? UnitPrice, string? LotNumber, DateOnly? ExpiryDate, string? Notes);

public sealed record SupplyShipmentCostRequest(string? CostType, string? Description, decimal Amount);

public sealed record SupplyShipmentListResponse(
    Guid Id,
    string ShipmentNumber,
    string SupplierName,
    string? InvoiceNumber,
    DateTime ShipmentDate,
    string Status,
    Guid DestinationLocationId,
    string? DestinationLocationName,
    int Quantity,
    decimal ProductSubtotal,
    decimal CostSubtotal,
    decimal LandedTotal,
    Guid? InventoryReceiptOperationId,
    DateTime CreatedAt);

public sealed record SupplyShipmentDetailResponse(
    Guid Id,
    string ShipmentNumber,
    string SupplierName,
    string? InvoiceNumber,
    DateTime ShipmentDate,
    string Status,
    Guid DestinationLocationId,
    string? DestinationLocationName,
    string? Notes,
    decimal ProductSubtotal,
    decimal CostSubtotal,
    decimal LandedTotal,
    Guid? InventoryReceiptOperationId,
    Guid CreatedBy,
    DateTime CreatedAt,
    Guid? ConfirmedBy,
    DateTime? ConfirmedAt,
    Guid? CancelledBy,
    DateTime? CancelledAt,
    IReadOnlyList<SupplyLineResponse> Lines,
    IReadOnlyList<SupplyCostResponse> Costs,
    IReadOnlyList<SupplyHistoryResponse> History);

public sealed record SupplyLineResponse(Guid Id, Guid SkuId, string SkuCode, string ProductName, int Quantity, decimal? UnitPrice, decimal LineSubtotal, decimal AllocatedCost, decimal LandedUnitCost, string? LotNumber, DateOnly? ExpiryDate, string? Notes);

public sealed record SupplyCostResponse(Guid Id, string CostType, string? Description, decimal Amount);

public sealed record SupplyHistoryResponse(Guid Id, string Action, Guid ActorUserId, DateTime CreatedAt, string? Summary);
