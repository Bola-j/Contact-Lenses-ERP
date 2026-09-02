using System.Security.Cryptography;
using System.Text.Json;
using Lensee.Host.Infrastructure;
using Lensee.Host.Services;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Catalog.Services;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Inventory.Services;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Primitives;
using Lensee.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static class OperationsEndpoints
{
    private const string InventoryReceipt = "InventoryReceipt";
    private const string WarehouseTransfer = "WarehouseTransfer";
    private const string WholesaleSale = "WholesaleSale";
    private const string RetailSale = "RetailSale";
    private const string Reserve = "Reserve";
    private const string Return = "Return";
    private const string Change = "Change";
    private const string WriteOff = "WriteOff";
    private const string Standard = "Standard";
    private const string ChangeOut = "ChangeOut";
    private const string ChangeIn = "ChangeIn";
    private const string Draft = "Draft";
    private const string Reserved = "Reserved";
    private const string Shipped = "Shipped";
    private const string Received = "Received";
    private const string Confirmed = "Confirmed";
    private const string Completed = "Completed";
    private const string Cancelled = "Cancelled";
    private const string MainWarehouse = "MainWarehouse";
    private const string Retail = "Retail";
    private const string Online = "Online";

    private static readonly HashSet<string> PaymentMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "CashHandToHand",
        "CashTransaction",
        "Installment"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static RouteGroupBuilder MapOperationsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/operations").WithTags("Operations");

        group.MapGet("/replenishment", GetReplenishmentAsync).RequireAuthorization("operations.read");
        group.MapPost("/replenishment/reserve", ReserveReplenishmentAsync).RequireAuthorization("operations.write");
        group.MapPost("/replenishment/daily-reset", ReserveReplenishmentAsync).RequireAuthorization("operations.write");
        group.MapGet("/", ListOperationsAsync).RequireAuthorization("operations.read");
        group.MapGet("/{id:guid}", GetOperationAsync).RequireAuthorization("operations.read");
        group.MapPost("/", CreateOperationAsync).RequireAuthorization("operations.write");
        group.MapPut("/{id:guid}", UpdateOperationAsync).RequireAuthorization("operations.write");
        group.MapPut("/{id:guid}/shopify-allocation", UpdateShopifyAllocationAsync).RequireAuthorization("operations.write");
        group.MapPost("/{id:guid}/revise", ReviseOperationAsync).RequireAuthorization("operations.write");
        group.MapPost("/{id:guid}/confirm", ConfirmOperationAsync).RequireAuthorization("operations.write");
        group.MapPost("/{id:guid}/ship", ShipOperationAsync).RequireAuthorization("operations.write");
        group.MapPost("/{id:guid}/receive", ReceiveOperationAsync).RequireAuthorization("operations.write");
        group.MapPost("/{id:guid}/complete", ReceiveOperationAsync).RequireAuthorization("operations.write");
        group.MapPost("/{id:guid}/cancel", CancelOperationAsync).RequireAuthorization("operations.write");
        group.MapGet("/{id:guid}/corrections", GetCorrectionLineageAsync).RequireAuthorization("operations.read");
        group.MapPost("/{id:guid}/corrections", CreateCorrectionAsync).RequireAuthorization("operations.corrections.request");
        group.MapGet("/corrections/{proposalId:guid}", GetCorrectionAsync).RequireAuthorization("operations.read");
        group.MapPost("/corrections/{proposalId:guid}/settlement", SubmitCorrectionSettlementAsync).RequireAuthorization("operations.corrections.request");
        group.MapPost("/corrections/{proposalId:guid}/approve", ApproveCorrectionAsync).RequireAuthorization("operations.corrections.approve");
        group.MapPost("/corrections/{proposalId:guid}/reject", RejectCorrectionAsync).RequireAuthorization("operations.corrections.approve");

        return group;
    }

    private static async Task<IResult> CreateCorrectionAsync(
        Guid id,
        CreateOperationCorrectionCommand request,
        OperationCorrectionService service,
        ICurrentUser currentUser,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(id, request, currentUser.UserId ?? Guid.Empty, currentUser.Role, cancellationToken);
        return ToCorrectionResult(result, httpContext);
    }

    private static async Task<IResult> SubmitCorrectionSettlementAsync(
        Guid proposalId,
        SubmitOperationCorrectionSettlementCommand request,
        OperationCorrectionService service,
        ICurrentUser currentUser,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.SubmitSettlementAsync(proposalId, request, currentUser.UserId ?? Guid.Empty, currentUser.Role, cancellationToken);
        return ToCorrectionResult(result, httpContext);
    }

    private static async Task<IResult> ApproveCorrectionAsync(
        Guid proposalId,
        OperationCorrectionService service,
        ICurrentUser currentUser,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.ApproveAsync(proposalId, currentUser.UserId ?? Guid.Empty, currentUser.Role, cancellationToken);
        return ToCorrectionResult(result, httpContext);
    }

    private static async Task<IResult> RejectCorrectionAsync(
        Guid proposalId,
        RejectOperationCorrectionCommand request,
        OperationCorrectionService service,
        ICurrentUser currentUser,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.RejectAsync(proposalId, request, currentUser.UserId ?? Guid.Empty, currentUser.Role, cancellationToken);
        return ToCorrectionResult(result, httpContext);
    }

    private static async Task<IResult> GetCorrectionAsync(
        Guid proposalId,
        OperationCorrectionService service,
        OperationsDbContext operationsDbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var proposal = await service.GetAsync(proposalId, cancellationToken);
        if (proposal is null)
        {
            return Results.NotFound();
        }

        var operation = await operationsDbContext.OperationLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == proposal.OperationId && !value.IsDeleted, cancellationToken);
        return operation is null || !CanReadOperation(currentUser, operation)
            ? Results.NotFound()
            : Results.Ok(proposal);
    }

    private static async Task<IResult> GetCorrectionLineageAsync(
        Guid id,
        OperationCorrectionService service,
        OperationsDbContext operationsDbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var operation = await operationsDbContext.OperationLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == id && !value.IsDeleted, cancellationToken);
        if (operation is null || !CanReadOperation(currentUser, operation))
        {
            return Results.NotFound();
        }

        return Results.Ok(await service.GetLineageAsync(id, cancellationToken));
    }

    private static IResult ToCorrectionResult(CorrectionCommandResult result, HttpContext httpContext)
    {
        if (result.Value is not null)
        {
            httpContext.Items[AuditMutationMiddleware.AuditWrittenItemKey] = true;
            return result.StatusCode == StatusCodes.Status201Created
                ? Results.Created($"/api/v1/operations/corrections/{result.Value.Id}", result.Value)
                : Results.Ok(result.Value);
        }

        return result.StatusCode switch
        {
            StatusCodes.Status404NotFound => Results.NotFound(),
            StatusCodes.Status403Forbidden => Results.Forbid(),
            StatusCodes.Status409Conflict => Results.Conflict(new { code = result.Code ?? "transition-conflict", detail = result.Error }),
            _ => Results.ValidationProblem(
                new Dictionary<string, string[]> { [result.ErrorField ?? "correction"] = [result.Error ?? "The correction request is invalid."] },
                statusCode: result.StatusCode)
        };
    }

    private static async Task<IResult> GetReplenishmentAsync(
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        OperationsDbContext operationsDbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (IsWarehouseClerk(currentUser) && currentUser.LocationId is null)
        {
            return Results.Forbid();
        }

        var rows = await BuildReplenishmentRowsAsync(inventoryDbContext, catalogDbContext, operationsDbContext, currentUser, null, null, cancellationToken);
        return Results.Ok(rows);
    }

    private static async Task<IResult> ReserveReplenishmentAsync(
        ReplenishmentReserveRequest request,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        NotificationsDbContext notificationsDbContext,
        StockLedgerService ledgerService,
        TargetReplenishmentService replenishmentService,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!LenseeRoles.Normalize(currentUser.Role).Equals(LenseeRoles.Admin, StringComparison.OrdinalIgnoreCase) &&
            !LenseeRoles.Normalize(currentUser.Role).Equals(LenseeRoles.ERPAdmin, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Forbid();
        }

        // The Inventory button is an explicit manual run. It must remain usable even
        // when the midnight scheduled run has already completed for the same Cairo day.
        var serviceResult = await replenishmentService.RunAsync("Manual", request.LocationId, request.SkuId, cancellationToken);
        return Results.Ok(new ReplenishmentReserveResponse(serviceResult.CreatedOperations, serviceResult.UncoveredQuantity, [], []));

#pragma warning disable CS0162
        var mainLocation = await inventoryDbContext.Locations
            .FirstOrDefaultAsync(location => location.IsActive && location.LocationType == MainWarehouse, cancellationToken);
        if (mainLocation is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["mainWarehouse"] = ["MainWarehouse location is required."] });
        }

        Guid? locationFilter = request.LocationId is { } locationId && locationId != Guid.Empty ? locationId : null;
        Guid? skuFilter = request.SkuId is { } skuId && skuId != Guid.Empty ? skuId : null;
        var rows = await BuildReplenishmentRowsAsync(inventoryDbContext, catalogDbContext, operationsDbContext, currentUser, locationFilter, skuFilter, cancellationToken);
        var shortages = rows
            .Where(row => row.ShortagePacks > 0)
            .GroupBy(row => row.DestinationLocationId)
            .ToList();
        if (shortages.Count == 0)
        {
            return Results.Ok(new ReplenishmentReserveResponse(0, 0, [], []));
        }

        var mainBalances = await inventoryDbContext.StockBalances
            .Where(balance => balance.LocationId == mainLocation.Id)
            .ToDictionaryAsync(balance => balance.SkuId, cancellationToken);
        var remainingMainAvailable = mainBalances.ToDictionary(
            pair => pair.Key,
            pair => Math.Max(pair.Value.AvailableQty - (pair.Value.TargetQty ?? 0), 0));
        var now = clock.EgyptNow;
        var userId = currentUser.UserId ?? Guid.Empty;
        var created = new List<ReplenishmentOperationResponse>();
        var alerts = new List<ReplenishmentAlertResponse>();
        var unfilled = 0;

        foreach (var destinationGroup in shortages)
        {
            var draftLines = new List<ReplenishmentLineDraft>();
            foreach (var shortage in destinationGroup)
            {
                remainingMainAvailable.TryGetValue(shortage.SkuId, out var mainAvailable);
                var quantity = Math.Min(shortage.ShortagePacks, Math.Max(mainAvailable, 0));
                if (quantity <= 0)
                {
                    unfilled += shortage.ShortagePacks;
                    alerts.Add(ToReplenishmentAlert(shortage, "MainWarehouse cannot supply this SKU without falling below its target stock."));
                    continue;
                }

                remainingMainAvailable[shortage.SkuId] = mainAvailable - quantity;
                if (quantity < shortage.ShortagePacks)
                {
                    unfilled += shortage.ShortagePacks - quantity;
                    alerts.Add(ToReplenishmentAlert(shortage, $"MainWarehouse can reserve only {quantity} of {shortage.ShortagePacks} needed pack(s) without falling below target."));
                }

                draftLines.Add(new ReplenishmentLineDraft(shortage.SkuId, shortage.SkuCode, shortage.ProductName, quantity));
            }

            if (draftLines.Count == 0)
            {
                continue;
            }

            var draftOperationLines = draftLines
                .Select(line => new OperationLine
                {
                    Id = Guid.NewGuid(),
                    OperationId = Guid.Empty,
                    SkuId = line.SkuId,
                    ProductNameSnapshot = line.ProductName ?? line.SkuId.ToString(),
                    SkuCodeSnapshot = line.SkuCode ?? line.SkuId.ToString(),
                    Section = "Standard",
                    Quantity = line.PackQuantity,
                    EntryMode = "Packs",
                    BonusQuantity = 0,
                    UnitPrice = 0,
                    LineTotal = 0,
                    LineNotes = "Target-stock replenishment"
                })
                .ToList();

            var operation = new OperationLog
            {
                Id = Guid.NewGuid(),
                OperationNumber = $"OP-{now:yyyyMMddHHmmss}-{RandomNumberGenerator.GetInt32(100, 1000)}",
                OperationType = WarehouseTransfer,
                Status = Draft,
                SourceLocationId = mainLocation.Id,
                DestinationLocationId = destinationGroup.Key,
                Notes = "Target-stock replenishment",
                CreatedBy = userId,
                CreatedActorName = "System - Target replenishment",
                CreatedAt = now,
                AutomationType = "TargetReplenishment"
            };
            foreach (var line in draftOperationLines)
            {
                line.OperationId = operation.Id;
                operation.OperationLines.Add(line);
            }

            operationsDbContext.OperationLogs.Add(operation);
            await AddVersionAsync(operationsDbContext, operation, "Draft replenishment created", userId, CreateSnapshot(operation), now, cancellationToken);
            await operationsDbContext.SaveChangesAsync(cancellationToken);
            await AddReplenishmentNotificationsAsync(notificationsDbContext, operation, now, cancellationToken);

            created.Add(new ReplenishmentOperationResponse(operation.Id, operation.OperationNumber, operation.DestinationLocationId!.Value, draftLines.Sum(line => line.PackQuantity)));
        }

        if (alerts.Count > 0)
        {
            await WriteReplenishmentAlertsAsync(notificationsDbContext, alerts, now, cancellationToken);
        }

        return Results.Ok(new ReplenishmentReserveResponse(created.Count, unfilled, created, alerts));
#pragma warning restore CS0162
    }

    private static async Task<IResult> ListOperationsAsync(
        int? page,
        int? pageSize,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var request = new PageRequest(page ?? 1, pageSize ?? 25);
        var query = operationsDbContext.OperationLogs
            .Include(operation => operation.OperationLines)
            .Include(operation => operation.ShopifyOrderLink)
            .Where(operation => !operation.IsDeleted)
            .AsQueryable();

        if (IsWarehouseClerk(currentUser))
        {
            if (currentUser.LocationId is not { } locationId)
            {
                return Results.Forbid();
            }

            query = query.Where(operation => operation.SourceLocationId == locationId || operation.DestinationLocationId == locationId);
        }

        var total = await query.CountAsync(cancellationToken);
        var operations = await query
            .OrderByDescending(operation => operation.CreatedAt)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);
        var locationLookup = await LoadLocationLookupAsync(inventoryDbContext, operations, cancellationToken);
        var userLookup = await LoadUserLookupAsync(identityDbContext, operations, cancellationToken);

        return Results.Ok(new PagedResult<OperationListResponse>(
            operations.Select(operation => ToListResponse(operation, locationLookup, userLookup)).ToList(),
            request.Page,
            request.PageSize,
            total));
    }

    private static async Task<IResult> GetOperationAsync(
        Guid id,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        InventoryDbContext inventoryDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var operation = await LoadOperationAsync(operationsDbContext, id, cancellationToken);
        if (operation is null)
        {
            return Results.NotFound();
        }

        if (!CanReadOperation(currentUser, operation))
        {
            return Results.Forbid();
        }

        var locationLookup = await LoadLocationLookupAsync(inventoryDbContext, [operation], cancellationToken);
        var userLookup = await LoadUserLookupAsync(identityDbContext, [operation], cancellationToken);
        var wearCycles = await LoadWearCyclesBySkuAsync(catalogDbContext, operation.OperationLines, cancellationToken);
        return Results.Ok(ToDetailResponse(operation, locationLookup, userLookup, wearCycles));
    }

    private static async Task<IResult> CreateOperationAsync(
        OperationRequest request,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        CrmDbContext crmDbContext,
        InventoryDbContext inventoryDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateDraftAsync(request, catalogDbContext, crmDbContext, inventoryDbContext, operationsDbContext, currentUser, isCreate: true, cancellationToken);
        if (validation.Errors.Count > 0)
        {
            return Results.ValidationProblem(validation.Errors);
        }

        if (!CanCreateDraft(currentUser, request, validation.SourceLocation, validation.DestinationLocation))
        {
            return Results.Forbid();
        }

        var now = clock.EgyptNow;
        var operation = new OperationLog
        {
            Id = Guid.NewGuid(),
            OperationNumber = $"OP-{now:yyyyMMddHHmmss}-{RandomNumberGenerator.GetInt32(100, 1000)}",
            OperationType = NormalizeOperationType(request.OperationType),
            Status = Draft,
            SourceLocationId = request.SourceLocationId,
            DestinationLocationId = request.DestinationLocationId,
            ClientId = validation.Merchant?.Id,
            ClientName = validation.Merchant?.BusinessName ?? TrimToNull(request.BuyerName),
            BuyerPhone = TrimToNull(request.BuyerPhone),
            RepresentativeId = validation.Representative?.Id,
            PaymentMethod = NormalizePaymentMethod(request.PaymentMethod),
            Notes = request.Notes,
            CreatedBy = currentUser.UserId ?? Guid.Empty,
            CreatedAt = now
        };

        await SharedDbTransaction.ExecuteAsync(operationsDbContext, async () =>
        {
            operationsDbContext.OperationLogs.Add(operation);
            AddLines(operation, validation.SkusById, request.Lines, validation.Merchant, validation.Representative);
            if (operation.OperationType == InventoryReceipt)
            {
                operationsDbContext.InventoryReceiptHeaders.Add(new InventoryReceiptHeader
                {
                    Id = Guid.NewGuid(),
                    OperationId = operation.Id,
                    SupplierName = request.Receipt?.SupplierName?.Trim() ?? "Supplier",
                    InvoiceNumber = TrimToNull(request.Receipt?.InvoiceNumber),
                    ReceiptDate = now
                });
            }

            await operationsDbContext.SaveChangesAsync(cancellationToken);
            await AddVersionAsync(operationsDbContext, operation, "Initial", currentUser.UserId ?? Guid.Empty, CreateSnapshot(operation), now, cancellationToken);
            await operationsDbContext.SaveChangesAsync(cancellationToken);
            await auditLogWriter.WriteAsync(
                "Operation",
                operation.Id,
                "Create",
                new { operation.OperationNumber, operation.OperationType, operation.Status },
                cancellationToken: cancellationToken);
        }, cancellationToken, identityDbContext);
        var created = await LoadOperationAsync(operationsDbContext, operation.Id, cancellationToken);
        var locationLookup = await LoadLocationLookupAsync(inventoryDbContext, [created!], cancellationToken);
        var userLookup = await LoadUserLookupAsync(identityDbContext, [created!], cancellationToken);
        var wearCycles = await LoadWearCyclesBySkuAsync(catalogDbContext, created!.OperationLines, cancellationToken);

        return Results.Created($"/api/v1/operations/{operation.Id}", ToDetailResponse(created!, locationLookup, userLookup, wearCycles));
    }

    private static async Task<IResult> UpdateOperationAsync(
        Guid id,
        OperationRequest request,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        CrmDbContext crmDbContext,
        InventoryDbContext inventoryDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var operation = await LoadOperationForDraftUpdateAsync(operationsDbContext, id, cancellationToken);
        if (operation is null)
        {
            return Results.NotFound();
        }
        if (operation.Status != Draft)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.Status)] = ["Only draft operations can be edited."] });
        }
        if (operationsDbContext.Database.IsRelational() && request.ExpectedVersion is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.ExpectedVersion)] = ["expectedVersion is required when editing a draft operation."] });
        }

        var validation = await ValidateDraftAsync(request, catalogDbContext, crmDbContext, inventoryDbContext, operationsDbContext, currentUser, isCreate: false, cancellationToken);
        if (validation.Errors.Count > 0)
        {
            return Results.ValidationProblem(validation.Errors);
        }
        if (!CanCreateDraft(currentUser, request, validation.SourceLocation, validation.DestinationLocation))
        {
            return Results.Forbid();
        }
        if (operation.SalesChannel == "Shopify")
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request)] = ["Shopify commercial data is read-only. Use the Shopify allocation endpoint to select batch and expiry."]
            });
        }

        IResult? conflictResult = null;
        try
        {
            await SharedDbTransaction.ExecuteAsync(operationsDbContext, async () =>
            {
                await LockOperationAsync(operationsDbContext, operation.Id, cancellationToken);
                await operationsDbContext.Entry(operation).ReloadAsync(cancellationToken);
                if (operation.Status != Draft)
                {
                    conflictResult = Results.Conflict(new { code = "transition-conflict", detail = "The operation is no longer a draft." });
                    return;
                }
                if (request.ExpectedVersion.HasValue && operation.ConcurrencyVersion != request.ExpectedVersion.Value)
                {
                    conflictResult = Results.Conflict(new { code = "stale-version", detail = "The operation has been changed by another request." });
                    return;
                }

                operation.OperationType = NormalizeOperationType(request.OperationType);
                operation.SourceLocationId = request.SourceLocationId;
                operation.DestinationLocationId = request.DestinationLocationId;
                operation.ClientId = validation.Merchant?.Id;
                operation.ClientName = validation.Merchant?.BusinessName ?? TrimToNull(request.BuyerName);
                operation.BuyerPhone = TrimToNull(request.BuyerPhone);
                operation.RepresentativeId = validation.Representative?.Id;
                operation.PaymentMethod = NormalizePaymentMethod(request.PaymentMethod);
                operation.Notes = request.Notes;
                await ReplaceOperationLinesAsync(operationsDbContext, operation, cancellationToken);
                AddLines(operation, validation.SkusById, request.Lines, validation.Merchant, validation.Representative);
                operationsDbContext.OperationLines.AddRange(operation.OperationLines);
                await SaveChangesIgnoringStaleDeletedOperationLinesAsync(operationsDbContext, cancellationToken);
                await AddVersionAsync(operationsDbContext, operation, "Draft update", currentUser.UserId ?? Guid.Empty, CreateSnapshot(operation), clock.EgyptNow, cancellationToken);
                await SaveChangesIgnoringStaleDeletedOperationLinesAsync(operationsDbContext, cancellationToken);
                await auditLogWriter.WriteAsync(
                    "Operation",
                    operation.Id,
                    "Update",
                    new { operation.OperationType, operation.Status },
                    cancellationToken: cancellationToken);
            }, cancellationToken, identityDbContext);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { code = "stale-version", detail = "The operation has been changed by another request." });
        }
        if (conflictResult is not null) return conflictResult;
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateShopifyAllocationAsync(
        Guid id,
        ShopifyAllocationRequest request,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        IdentityDbContext identityDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var operation = await LoadOperationForDraftUpdateAsync(operationsDbContext, id, cancellationToken);
        if (operation is null) return Results.NotFound();
        if (operationsDbContext.Database.IsRelational() && request.ExpectedVersion is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.ExpectedVersion)] = ["expectedVersion is required when editing a Shopify allocation."] });
        }
        if (operation.Status != Draft || operation.SalesChannel != "Shopify" || operation.OperationType != RetailSale)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(id)] = ["Only draft Shopify retail sales can receive batch allocation."] });
        }
        if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "confirm", cancellationToken))
        {
            return Results.Forbid();
        }

        var lines = request.Lines ?? [];
        var errors = new Dictionary<string, string[]>();
        if (lines.Count != operation.OperationLines.Count || lines.Select(line => line.OperationLineId).Distinct().Count() != lines.Count || lines.Any(line => !operation.OperationLines.Any(operationLine => operationLine.Id == line.OperationLineId)))
        {
            errors[nameof(request.Lines)] = ["Allocation must include each Shopify operation line exactly once."];
        }
        if (lines.Any(line => line.ExpiryDate is null))
        {
            errors[nameof(request.Lines)] = ["Each Shopify line requires a batch expiry date."];
        }

        var lineById = operation.OperationLines.ToDictionary(line => line.Id);
        foreach (var line in lines.Where(line => lineById.ContainsKey(line.OperationLineId) && line.ExpiryDate is not null))
        {
            var operationLine = lineById[line.OperationLineId];
            var lotNumber = NormalizeBlank(line.LotNumber);
            var exists = await inventoryDbContext.InventoryBatches.AnyAsync(batch =>
                batch.LocationId == operation.SourceLocationId &&
                batch.SkuId == operationLine.SkuId &&
                batch.LotNumber == lotNumber &&
                batch.ExpiryDate == line.ExpiryDate,
                cancellationToken);
            if (!exists)
            {
                errors[$"lines[{line.OperationLineId}]"] = [$"The selected batch does not exist for {operationLine.SkuCodeSnapshot} at the Online location."];
            }
        }
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        IResult? allocationConflict = null;
        try
        {
            await SharedDbTransaction.ExecuteAsync(operationsDbContext, async () =>
            {
                await LockOperationAsync(operationsDbContext, operation.Id, cancellationToken);
                await operationsDbContext.Entry(operation).ReloadAsync(cancellationToken);
                if (operation.Status != Draft || (request.ExpectedVersion.HasValue && operation.ConcurrencyVersion != request.ExpectedVersion.Value))
                {
                    allocationConflict = Results.Conflict(new { code = operation.Status == Draft ? "stale-version" : "transition-conflict", detail = "The Shopify operation changed before allocation could be saved." });
                    return;
                }
                foreach (var line in lines)
                {
                    var operationLine = lineById[line.OperationLineId];
                    operationLine.LotNumber = NormalizeBlank(line.LotNumber);
                    operationLine.ExpiryDate = line.ExpiryDate;
                }
                await operationsDbContext.SaveChangesAsync(cancellationToken);
                await AddVersionAsync(operationsDbContext, operation, "Shopify batch allocation updated", currentUser.UserId ?? Guid.Empty, CreateSnapshot(operation), clock.EgyptNow, cancellationToken);
                await operationsDbContext.SaveChangesAsync(cancellationToken);
                await auditLogWriter.WriteAsync(
                    "Operation",
                    operation.Id,
                    "UpdateAllocation",
                    new { operation.OperationType, operation.Status },
                    cancellationToken: cancellationToken);
            }, cancellationToken, identityDbContext);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { code = "stale-version", detail = "The Shopify operation changed before allocation could be saved." });
        }
        if (allocationConflict is not null) return allocationConflict;
        return Results.NoContent();
    }

    private static async Task AddReplenishmentNotificationsAsync(
        NotificationsDbContext notifications,
        OperationLog operation,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (var role in new[] { LenseeRoles.Admin, LenseeRoles.ERPAdmin, LenseeRoles.WarehouseClerk })
        {
            var exists = await notifications.NotificationLogs.AnyAsync(value =>
                value.AlertType == "Replenishment" && value.ReferenceId == operation.Id && value.TargetRole == role && !value.IsRead,
                cancellationToken);
            if (exists) continue;
            var id = Guid.NewGuid();
            notifications.NotificationLogs.Add(new NotificationLog
            {
                Id = id,
                AlertType = "Replenishment",
                Message = $"Replenishment {operation.OperationNumber} was created as a Draft. Review and confirm the warehouse transfer.",
                ReferenceId = operation.Id,
                ReferenceType = "Operation",
                ReferenceCode = operation.OperationNumber,
                ReferenceTitle = operation.OperationNumber,
                TargetRole = role,
                Channel = "InApp",
                CreatedAt = now,
                NotificationNumber = $"NOT-{id:N}".ToUpperInvariant()
            });
        }
        await notifications.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureReplenishmentStageNotificationAsync(NotificationsDbContext notifications, OperationLog operation, DateTime now, CancellationToken cancellationToken)
    {
        var old = await notifications.NotificationLogs.Where(value => value.AlertType == "Replenishment" && value.ReferenceId == operation.Id && !value.IsRead).ToListAsync(cancellationToken);
        foreach (var item in old) item.IsRead = true;
        var stageMessage = operation.Status switch
        {
            Reserved => $"Replenishment {operation.OperationNumber} is Reserved. Source clerk should ship it.",
            Shipped => $"Replenishment {operation.OperationNumber} is Shipped. Destination clerk should receive it.",
            Received => $"Replenishment {operation.OperationNumber} was Received.",
            _ => $"Replenishment {operation.OperationNumber} is {operation.Status}. Review the warehouse transfer."
        };
        foreach (var role in new[] { LenseeRoles.Admin, LenseeRoles.ERPAdmin, LenseeRoles.WarehouseClerk })
        {
            var id = Guid.NewGuid();
            notifications.NotificationLogs.Add(new NotificationLog { Id = id, AlertType = "Replenishment", Message = stageMessage, ReferenceId = operation.Id, ReferenceType = "Operation", ReferenceCode = operation.OperationNumber, ReferenceTitle = operation.OperationNumber, ReferenceContextJson = System.Text.Json.JsonSerializer.Serialize(new { operation.Status }), TargetRole = role, Channel = "InApp", CreatedAt = now, NotificationNumber = $"NOT-{id:N}".ToUpperInvariant() });
        }
        await notifications.SaveChangesAsync(cancellationToken);
    }

    private static async Task<IResult> ReviseOperationAsync(
        Guid id,
        OperationRevisionRequest request,
        HttpContext httpContext,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        CrmDbContext crmDbContext,
        InventoryDbContext inventoryDbContext,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        StockLedgerService ledgerService,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var operation = await LoadOperationAsync(operationsDbContext, id, cancellationToken);
        if (operation is null)
        {
            return Results.NotFound();
        }

        if (!string.Equals(currentUser.Role, LenseeRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Forbid();
        }
        if (operationsDbContext.Database.IsRelational() && request.Operation.ExpectedVersion is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Operation.ExpectedVersion)] = ["expectedVersion is required when revising an operation."] });
        }

        // Compare before requiring a reason or validating the request. An unchanged
        // revision is a true no-op (including no fallback audit event).
        if (string.Equals(BuildRevisionFingerprint(operation), BuildRevisionFingerprint(request.Operation), StringComparison.Ordinal))
        {
            httpContext.Items[AuditMutationMiddleware.AuditWrittenItemKey] = true;
            return Results.NoContent();
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Reason)] = ["Revision reason is required."]
            });
        }

        if (operation.Status == Cancelled)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(operation.Status)] = ["Cancelled operations cannot be revised."]
            });
        }
        if (IsFinalizedForCorrection(operation.Status))
        {
            return Results.Conflict(new
            {
                detail = "Finalized operations are immutable. Create a correction proposal instead.",
                correctionRoute = $"/api/v1/operations/{operation.Id}/corrections"
            });
        }
        if (operation.SalesChannel == "Shopify")
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(operation.SalesChannel)] = ["Shopify commercial data cannot be revised. Use the Shopify cancellation/refund exception workflow for commercial changes."]
            });
        }

        if (!string.Equals(NormalizeOperationType(request.Operation.OperationType), operation.OperationType, StringComparison.OrdinalIgnoreCase))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Operation.OperationType)] = ["Operation type cannot change during revision."]
            });
        }

        var revisionBlock = await GetRevisionBlockReasonAsync(operation, paymentsDbContext, cancellationToken);
        if (revisionBlock is not null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(operation.Status)] = [revisionBlock]
            });
        }

        var validation = await ValidateDraftAsync(request.Operation, catalogDbContext, crmDbContext, inventoryDbContext, operationsDbContext, currentUser, isCreate: false, cancellationToken);
        if (validation.Errors.Count > 0)
        {
            return Results.ValidationProblem(validation.Errors);
        }

        var now = clock.EgyptNow;
        var userId = currentUser.UserId ?? Guid.Empty;
        var originalStatus = operation.Status;
        var originalAllocations = ReadTransferAllocations(operation);
        IResult? revisionConflict = null;
        try
        {
            await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, paymentsDbContext, crmDbContext, identityDbContext, async () =>
            {
                await LockOperationAsync(operationsDbContext, operation.Id, cancellationToken);
                await operationsDbContext.Entry(operation).ReloadAsync(cancellationToken);
                if (request.Operation.ExpectedVersion.HasValue && operation.ConcurrencyVersion != request.Operation.ExpectedVersion.Value)
                {
                    revisionConflict = Results.Conflict(new { code = "stale-version", detail = "The operation changed before revision could be applied." });
                    return;
                }
                await ReverseOperationEffectsAsync(operation, originalAllocations, inventoryDbContext, paymentsDbContext, ledgerService, userId, cancellationToken);

                operation.SourceLocationId = request.Operation.SourceLocationId;
                operation.DestinationLocationId = request.Operation.DestinationLocationId;
                operation.ClientId = validation.Merchant?.Id;
                operation.ClientName = validation.Merchant?.BusinessName ?? TrimToNull(request.Operation.BuyerName);
                operation.RepresentativeId = validation.Representative?.Id;
                operation.PaymentMethod = NormalizePaymentMethod(request.Operation.PaymentMethod);
                operation.Notes = request.Operation.Notes;

                await ReplaceOperationLinesAsync(operationsDbContext, operation, cancellationToken);
                AddLines(operation, validation.SkusById, request.Operation.Lines, validation.Merchant, validation.Representative);
                operationsDbContext.OperationLines.AddRange(operation.OperationLines);

                if (operation.InventoryReceiptHeader is not null)
                {
                    if (operation.OperationType == InventoryReceipt)
                    {
                        operation.InventoryReceiptHeader.SupplierName = request.Operation.Receipt?.SupplierName?.Trim() ?? "Supplier";
                        operation.InventoryReceiptHeader.InvoiceNumber = TrimToNull(request.Operation.Receipt?.InvoiceNumber);
                        operation.InventoryReceiptHeader.ReceiptDate = now;
                    }
                    else
                    {
                        operationsDbContext.InventoryReceiptHeaders.Remove(operation.InventoryReceiptHeader);
                        operation.InventoryReceiptHeader = null;
                    }
                }
                else if (operation.OperationType == InventoryReceipt)
                {
                    operation.InventoryReceiptHeader = new InventoryReceiptHeader
                    {
                        Id = Guid.NewGuid(),
                        OperationId = operation.Id,
                        SupplierName = request.Operation.Receipt?.SupplierName?.Trim() ?? "Supplier",
                        InvoiceNumber = TrimToNull(request.Operation.Receipt?.InvoiceNumber),
                        ReceiptDate = now
                    };
                    operationsDbContext.InventoryReceiptHeaders.Add(operation.InventoryReceiptHeader);
                }

                operation.ConfirmedBy = ShouldSetConfirmedActorAfterRevision(originalStatus) ? userId : null;
                operation.ConfirmedAt = ShouldSetConfirmedActorAfterRevision(originalStatus) ? now : null;
                operation.Status = Draft;

                var revisedAllocations = await ReapplyOperationToStatusAsync(
                    operation,
                    originalStatus,
                    operationsDbContext,
                    inventoryDbContext,
                    catalogDbContext,
                    crmDbContext,
                    paymentsDbContext,
                    ledgerService,
                    userId,
                    now,
                    cancellationToken);

                await AddVersionAsync(
                    operationsDbContext,
                    operation,
                    request.Reason.Trim(),
                    userId,
                    CreateSnapshot(operation, revisedAllocations),
                    now,
                    cancellationToken);
                await SaveChangesIgnoringStaleDeletedOperationLinesAsync(operationsDbContext, cancellationToken);
                await auditLogWriter.WriteAsync(
                    "Operation",
                    operation.Id,
                    "Revise",
                    new { operation.OperationType, operation.Status, Reason = request.Reason.Trim() },
                    cancellationToken: cancellationToken);
            }, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { code = "stale-version", detail = "The operation changed before revision could be applied." });
        }
        if (revisionConflict is not null) return revisionConflict;

        var revised = await LoadOperationAsync(operationsDbContext, operation.Id, cancellationToken);
        var locationLookup = await LoadLocationLookupAsync(inventoryDbContext, [revised!], cancellationToken);
        var userLookup = await LoadUserLookupAsync(identityDbContext, [revised!], cancellationToken);
        var wearCycles = await LoadWearCyclesBySkuAsync(catalogDbContext, revised!.OperationLines, cancellationToken);
        return Results.Ok(ToDetailResponse(revised!, locationLookup, userLookup, wearCycles));
    }

    private static async Task<IResult> ConfirmOperationAsync(
        Guid id,
        HttpContext httpContext,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        StockLedgerService ledgerService,
        NotificationsDbContext notificationsDbContext,
        MerchantBatchHistoryService batchHistoryService,
        MerchantExpiryRecallService recallService,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var operation = await LoadOperationAsync(operationsDbContext, id, cancellationToken);
        if (operation is null)
        {
            return Results.NotFound();
        }
        if (operation.Status != Draft)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.Status)] = ["Only draft operations can be confirmed."] });
        }
        if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "confirm", cancellationToken))
        {
            return Results.Forbid();
        }

        // The route branch is selected from this preflight read. The aggregate is
        // locked and loaded again inside the shared transaction before any stock
        // mutation, so a concurrent draft edit cannot make this branch operate on
        // obsolete lines or a different operation type.
        var requestedOperationType = operation.OperationType;
        IResult? confirmationConflict = null;
        async Task<bool> LockDraftForConfirmationAsync()
        {
            var lockedOperation = await LockAndLoadOperationAsync(operationsDbContext, id, cancellationToken);
            if (lockedOperation is null ||
                lockedOperation.Status != Draft ||
                !string.Equals(lockedOperation.OperationType, requestedOperationType, StringComparison.Ordinal))
            {
                confirmationConflict = Results.Conflict(new
                {
                    code = "transition-conflict",
                    detail = "The operation changed before confirmation could be applied. Reload and retry."
                });
                return false;
            }

            operation = lockedOperation;
            if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "confirm", cancellationToken))
            {
                confirmationConflict = Results.Forbid();
                return false;
            }

            return true;
        }

        OperationConfirmationRequest confirmationRequest;
        try
        {
            confirmationRequest = await ReadConfirmationRequestAsync(httpContext.Request, cancellationToken);
        }
        catch (JsonException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["Confirmation request is not valid JSON."] });
        }

        var canBypassSalesVariance = CanBypassMerchantSalesVariance(currentUser);
        var bypassReason = TrimToNull(confirmationRequest.SalesVarianceReason);
        if (bypassReason?.Length > 500)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(confirmationRequest.SalesVarianceReason)] = ["Exception reason cannot exceed 500 characters."] });
        }

        IReadOnlyList<MerchantSalesVarianceWarning> salesVarianceWarnings = [];
        if (operation.OperationType is Return or Change)
        {
            salesVarianceWarnings = await BuildMerchantSalesVarianceWarningsAsync(operation, batchHistoryService, cancellationToken);
            if (salesVarianceWarnings.Count > 0 && confirmationRequest.AcknowledgeSalesVariance != true)
            {
                return Results.Conflict(CreateMerchantSalesVarianceGate(salesVarianceWarnings, canBypassSalesVariance));
            }
            if (salesVarianceWarnings.Count > 0 && !canBypassSalesVariance)
            {
                return Results.Forbid();
            }
            if (salesVarianceWarnings.Count > 0 && bypassReason is null)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(confirmationRequest.SalesVarianceReason)] = ["An exception reason is required to confirm this return."] });
            }
        }

        var now = clock.EgyptNow;
        var userId = currentUser.UserId ?? Guid.Empty;
        var salesVarianceBypassed = false;
        Task WriteConfirmAuditAsync() => auditLogWriter.WriteAsync(
            "Operation",
            operation.Id,
            "Confirm",
            new { operation.OperationType, operation.Status, SalesVarianceBypassed = salesVarianceBypassed, SalesVarianceReason = salesVarianceBypassed ? bypassReason : null },
            cancellationToken: cancellationToken);
        if (operation.OperationType == InventoryReceipt)
        {
            await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, identityDbContext, async () =>
            {
                if (!await LockDraftForConfirmationAsync()) return;
                foreach (var line in operation.OperationLines)
                {
                    await ledgerService.ReceiveAsync(
                        operation.DestinationLocationId!.Value,
                        line.SkuId,
                        line.Quantity,
                        userId,
                        line.LotNumber,
                        line.ExpiryDate,
                        line.LineNotes,
                        operation.Id,
                        cancellationToken);
                }

                operation.Status = Received;
                operation.ConfirmedAt = now;
                operation.ConfirmedBy = userId;
                await AddVersionAsync(operationsDbContext, operation, "Received", userId, CreateSnapshot(operation), now, cancellationToken);
                await operationsDbContext.SaveChangesAsync(cancellationToken);
                await WriteConfirmAuditAsync();
            }, cancellationToken);
        }
        else if (operation.OperationType == WarehouseTransfer)
        {
            var allocations = new List<TransferAllocationSnapshot>();
            foreach (var line in operation.OperationLines)
            {
                try
                {
                    await ledgerService.PlanReserveInWarehouseFefoAsync(
                        operation.SourceLocationId!.Value,
                        line.SkuId,
                        line.Quantity,
                        null,
                        cancellationToken);
                }
                catch (InvalidOperationException exception)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        [line.SkuCodeSnapshot] = [exception.Message]
                    });
                }
            }

            await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, identityDbContext, async () =>
            {
                if (!await LockDraftForConfirmationAsync()) return;
                foreach (var line in operation.OperationLines)
                {
                    var lineAllocations = await ledgerService.ReserveInWarehouseFefoAsync(
                        operation.SourceLocationId!.Value,
                        line.SkuId,
                        line.Quantity,
                        userId,
                        operation.Id,
                        null,
                        cancellationToken);

                    allocations.Add(new TransferAllocationSnapshot(line.SkuId, lineAllocations.ToList()));
                }

                operation.Status = Reserved;
                operation.ConfirmedAt = now;
                operation.ConfirmedBy = userId;
                await AddVersionAsync(operationsDbContext, operation, "Reserved", userId, CreateSnapshot(operation, allocations), now, cancellationToken);
                await operationsDbContext.SaveChangesAsync(cancellationToken);
                await WriteConfirmAuditAsync();
            }, cancellationToken);
        }
        else if (operation.OperationType is WholesaleSale or RetailSale)
        {
            IReadOnlyList<TransferAllocationSnapshot> allocations = [];
            try
            {
                await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, identityDbContext, async () =>
                {
                    if (!await LockDraftForConfirmationAsync()) return;
                    allocations = await BuildSelectedSalePackAllocationsAsync(operation, inventoryDbContext, operationsDbContext, clock, cancellationToken);
                    foreach (var allocation in allocations)
                    {
                        await ledgerService.ReserveSelectedBatchInWarehouseAsync(
                            operation.SourceLocationId!.Value,
                            allocation.SkuId,
                            allocation.Allocations.Sum(batch => batch.Quantity),
                            allocation.Allocations[0].LotNumber,
                            allocation.Allocations[0].ExpiryDate,
                            userId,
                            operation.Id,
                            cancellationToken);
                    }

                    operation.Status = Reserved;
                    operation.ConfirmedAt = now;
                    operation.ConfirmedBy = userId;
                    await AddVersionAsync(operationsDbContext, operation, "Reserved sale", userId, CreateSnapshot(operation, allocations), now, cancellationToken);
                    await operationsDbContext.SaveChangesAsync(cancellationToken);
                    await WriteConfirmAuditAsync();
                }, cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.OperationLines)] = [exception.Message] });
            }
        }
        else if (operation.OperationType == Return)
        {
            try
            {
                await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, identityDbContext, async () =>
                {
                    if (!await LockDraftForConfirmationAsync()) return;
                    await AcquireMerchantReturnLocksAsync(operation, operationsDbContext, cancellationToken);
                    var lockedWarnings = await BuildMerchantSalesVarianceWarningsAsync(operation, batchHistoryService, cancellationToken);
                    if (lockedWarnings.Count > 0 && !(confirmationRequest.AcknowledgeSalesVariance == true && canBypassSalesVariance && bypassReason is not null))
                    {
                        throw new MerchantSalesVarianceException(lockedWarnings);
                    }
                    salesVarianceBypassed = lockedWarnings.Count > 0;
                    foreach (var line in operation.OperationLines)
                    {
                        if (line.ExpiryDate is { } expiry && expiry < DateOnly.FromDateTime(now))
                        {
                            line.WriteOffReason = "ExpiredMerchantReturn";
                            line.WriteOffReasonText = "Expired merchant return was received and written off in the same operation.";
                            await ledgerService.ReceiveExpiredReturnAndWriteOffAsync(
                                operation.SourceLocationId!.Value,
                                line.SkuId,
                                line.Quantity,
                                userId,
                                line.LotNumber,
                                line.ExpiryDate,
                                line.LineNotes,
                                operation.Id,
                                cancellationToken);
                        }
                        else
                        {
                            await ledgerService.ReceiveReturnAsync(
                                operation.SourceLocationId!.Value,
                                line.SkuId,
                                line.Quantity,
                                userId,
                                line.LotNumber,
                                line.ExpiryDate,
                                line.LineNotes,
                                operation.Id,
                                cancellationToken);
                        }
                    }

                    operation.Status = Confirmed;
                    operation.ConfirmedAt = now;
                    operation.ConfirmedBy = userId;
                    await recallService.ApplyConfirmedReturnAsync(operation, cancellationToken);
                    var versionReason = salesVarianceBypassed
                        ? $"Confirmed return with recorded sales exception: {bypassReason}"
                        : "Confirmed return";
                    await AddVersionAsync(operationsDbContext, operation, versionReason, userId, CreateSnapshot(operation), now, cancellationToken);
                    await operationsDbContext.SaveChangesAsync(cancellationToken);
                    await WriteConfirmAuditAsync();
                }, cancellationToken);
            }
            catch (MerchantSalesVarianceException exception)
            {
                return Results.Conflict(CreateMerchantSalesVarianceGate(exception.Warnings, canBypassSalesVariance));
            }
            if (operation.MerchantExpiryRecallId is { } recallId)
            {
                await recallService.SynchronizeResolvedNotificationAsync(recallId, cancellationToken);
            }
        }
        else if (operation.OperationType == Change)
        {
            var allocations = new List<TransferAllocationSnapshot>();
            try
            {
                await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, identityDbContext, async () =>
                {
                    if (!await LockDraftForConfirmationAsync()) return;
                    await AcquireMerchantReturnLocksAsync(operation, operationsDbContext, cancellationToken);
                    var lockedWarnings = await BuildMerchantSalesVarianceWarningsAsync(operation, batchHistoryService, cancellationToken);
                    if (lockedWarnings.Count > 0 && !(confirmationRequest.AcknowledgeSalesVariance == true && canBypassSalesVariance && bypassReason is not null))
                    {
                        throw new MerchantSalesVarianceException(lockedWarnings);
                    }
                    salesVarianceBypassed = lockedWarnings.Count > 0;
                    foreach (var line in operation.OperationLines.Where(line => line.Section == ChangeOut))
                    {
                        await ledgerService.ReceiveChangeOutAsync(
                            operation.SourceLocationId!.Value,
                            line.SkuId,
                            line.Quantity,
                            userId,
                            line.LotNumber,
                            line.ExpiryDate,
                            line.LineNotes,
                            operation.Id,
                            cancellationToken);
                    }

                    foreach (var line in operation.OperationLines.Where(line => line.Section == ChangeIn))
                    {
                        var lineAllocations = await ledgerService.IssueFefoAsync(
                            operation.SourceLocationId!.Value,
                            line.SkuId,
                            line.Quantity,
                            InventoryTransactionTypes.ChangeIn,
                            userId,
                            operation.Id,
                            null,
                            cancellationToken);
                        allocations.Add(new TransferAllocationSnapshot(line.SkuId, lineAllocations.ToList()));
                    }

                    operation.Status = Confirmed;
                    operation.ConfirmedAt = now;
                    operation.ConfirmedBy = userId;
                    var versionReason = salesVarianceBypassed
                        ? $"Confirmed change with recorded sales exception: {bypassReason}"
                        : "Confirmed change";
                    await AddVersionAsync(operationsDbContext, operation, versionReason, userId, CreateSnapshot(operation, allocations), now, cancellationToken);
                    await operationsDbContext.SaveChangesAsync(cancellationToken);
                    await WriteConfirmAuditAsync();
                }, cancellationToken);
            }
            catch (MerchantSalesVarianceException exception)
            {
                return Results.Conflict(CreateMerchantSalesVarianceGate(exception.Warnings, canBypassSalesVariance));
            }
        }
        else if (operation.OperationType == WriteOff)
        {
            var allocations = new List<TransferAllocationSnapshot>();
            await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, identityDbContext, async () =>
            {
                if (!await LockDraftForConfirmationAsync()) return;
                foreach (var line in operation.OperationLines)
                {
                    var lineAllocations = await ledgerService.IssueFefoAsync(
                        operation.SourceLocationId!.Value,
                        line.SkuId,
                        line.Quantity,
                        InventoryTransactionTypes.WriteOff,
                        userId,
                        operation.Id,
                        null,
                        cancellationToken);
                    allocations.Add(new TransferAllocationSnapshot(line.SkuId, lineAllocations.ToList()));
                }

                operation.Status = Confirmed;
                operation.ConfirmedAt = now;
                operation.ConfirmedBy = userId;
                await AddVersionAsync(operationsDbContext, operation, "Confirmed write-off", userId, CreateSnapshot(operation, allocations), now, cancellationToken);
                await operationsDbContext.SaveChangesAsync(cancellationToken);
                await WriteConfirmAuditAsync();
            }, cancellationToken);
        }
        else if (operation.OperationType == Reserve)
        {
            var allocations = new List<TransferAllocationSnapshot>();
            await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, identityDbContext, async () =>
            {
                if (!await LockDraftForConfirmationAsync()) return;
                foreach (var group in operation.OperationLines.GroupBy(line => line.SkuId))
                {
                    var lineAllocations = await ledgerService.ReserveInWarehouseFefoAsync(
                        operation.SourceLocationId!.Value,
                        group.Key,
                        group.Sum(line => line.Quantity),
                        userId,
                        operation.Id,
                        null,
                        cancellationToken);

                    allocations.Add(new TransferAllocationSnapshot(group.Key, lineAllocations.ToList()));
                }

                operation.Status = Reserved;
                operation.ConfirmedAt = now;
                operation.ConfirmedBy = userId;
                await AddVersionAsync(operationsDbContext, operation, "Reserved for representative shipment", userId, CreateSnapshot(operation, allocations), now, cancellationToken);
                await operationsDbContext.SaveChangesAsync(cancellationToken);
                await WriteConfirmAuditAsync();
            }, cancellationToken);
        }
        if (confirmationConflict is not null) return confirmationConflict;
        if (operation.AutomationType == "TargetReplenishment") await EnsureReplenishmentStageNotificationAsync(notificationsDbContext, operation, clock.EgyptNow, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ShipOperationAsync(
        Guid id,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        IdentityDbContext identityDbContext,
        StockLedgerService ledgerService,
        NotificationsDbContext notificationsDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var operation = await LoadOperationAsync(operationsDbContext, id, cancellationToken);
        if (operation is null)
        {
            return Results.NotFound();
        }
        if (operation.OperationType is not (WarehouseTransfer or WholesaleSale or RetailSale or Reserve) || operation.Status != Reserved)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.Status)] = ["Only reserved transfers, sales, or representative reserves can be shipped."] });
        }
        if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "ship", cancellationToken))
        {
            return Results.Forbid();
        }

        var userId = currentUser.UserId ?? Guid.Empty;
        IReadOnlyList<TransferAllocationSnapshot> allocations = ReadTransferAllocations(operation);
        if (LinesRequiringAllocation(operation).Any(line => allocations.All(value => value.SkuId != line.SkuId)))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.OperationLines)] = ["Transfer allocation snapshot is missing."] });
        }

        IResult? shipConflict = null;

        await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, identityDbContext, async () =>
        {
            var lockedOperation = await LockAndLoadOperationAsync(operationsDbContext, id, cancellationToken);
            if (lockedOperation is null ||
                lockedOperation.OperationType is not (WarehouseTransfer or WholesaleSale or RetailSale or Reserve) ||
                lockedOperation.Status != Reserved)
            {
                shipConflict = Results.Conflict(new
                {
                    code = "transition-conflict",
                    detail = "The operation changed before shipping could be applied. Reload and retry."
                });
                return;
            }

            operation = lockedOperation;
            if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "ship", cancellationToken))
            {
                shipConflict = Results.Forbid();
                return;
            }

            allocations = ReadTransferAllocations(operation);
            if (LinesRequiringAllocation(operation).Any(line => allocations.All(value => value.SkuId != line.SkuId)))
            {
                shipConflict = Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.OperationLines)] = ["Transfer allocation snapshot is missing."] });
                return;
            }

            if (operation.OperationType == WarehouseTransfer)
            {
                await CommitTransferOutAsync(operation, allocations, ledgerService, userId, InventoryTransactionTypes.SupplyOut, cancellationToken);
            }
            else if (operation.OperationType is WholesaleSale or RetailSale)
            {
                await ShipSaleOutAsync(operation, allocations, catalogDbContext, ledgerService, userId, clock.EgyptNow, cancellationToken);
            }
            else if (operation.OperationType == Reserve)
            {
                await ShipRepresentativeReserveAsync(operation, allocations, ledgerService, userId, cancellationToken);
            }

            operation.Status = Shipped;
            await AddVersionAsync(operationsDbContext, operation, "Shipped", userId, CreateSnapshot(operation, allocations), clock.EgyptNow, cancellationToken);
            await operationsDbContext.SaveChangesAsync(cancellationToken);
            await auditLogWriter.WriteAsync(
                "Operation",
                operation.Id,
                "Ship",
                new { operation.OperationType, operation.Status },
                cancellationToken: cancellationToken);
        }, cancellationToken);

        if (shipConflict is not null) return shipConflict;

        if (operation.AutomationType == "TargetReplenishment") await EnsureReplenishmentStageNotificationAsync(notificationsDbContext, operation, clock.EgyptNow, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> ReceiveOperationAsync(
        Guid id,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        CrmDbContext crmDbContext,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        StockLedgerService ledgerService,
        NotificationsDbContext notificationsDbContext,
        IAppEventPublisher eventPublisher,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var operation = await LoadOperationAsync(operationsDbContext, id, cancellationToken);
        if (operation is null)
        {
            return Results.NotFound();
        }
        if (operation.OperationType is not (WarehouseTransfer or WholesaleSale or RetailSale or Reserve) || operation.Status is not (Reserved or Shipped))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.Status)] = ["Only reserved or shipped transfers, sales, or representative reserves can be received."] });
        }
        if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "receive", cancellationToken))
        {
            return Results.Forbid();
        }

        var userId = currentUser.UserId ?? Guid.Empty;
        IReadOnlyList<TransferAllocationSnapshot> allocations = ReadTransferAllocations(operation);
        if (LinesRequiringAllocation(operation).Any(line => allocations.All(value => value.SkuId != line.SkuId)))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.OperationLines)] = ["Transfer allocation snapshot is missing."] });
        }

        IResult? receiveConflict = null;

        await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, paymentsDbContext, crmDbContext, identityDbContext, async () =>
        {
            var lockedOperation = await LockAndLoadOperationAsync(operationsDbContext, id, cancellationToken);
            if (lockedOperation is null ||
                lockedOperation.OperationType is not (WarehouseTransfer or WholesaleSale or RetailSale or Reserve) ||
                lockedOperation.Status is not (Reserved or Shipped))
            {
                receiveConflict = Results.Conflict(new
                {
                    code = "transition-conflict",
                    detail = "The operation changed before receiving could be applied. Reload and retry."
                });
                return;
            }

            operation = lockedOperation;
            if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "receive", cancellationToken))
            {
                receiveConflict = Results.Forbid();
                return;
            }

            allocations = ReadTransferAllocations(operation);
            if (LinesRequiringAllocation(operation).Any(line => allocations.All(value => value.SkuId != line.SkuId)))
            {
                receiveConflict = Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.OperationLines)] = ["Transfer allocation snapshot is missing."] });
                return;
            }

            if (operation.Status == Reserved)
            {
                if (operation.OperationType == WarehouseTransfer)
                {
                    await CommitTransferOutAsync(operation, allocations, ledgerService, userId, InventoryTransactionTypes.SupplyOut, cancellationToken);
                }
                else if (operation.OperationType is WholesaleSale or RetailSale)
                {
                    await ShipSaleOutAsync(operation, allocations, catalogDbContext, ledgerService, userId, clock.EgyptNow, cancellationToken);
                }
                else if (operation.OperationType == Reserve)
                {
                    await ShipRepresentativeReserveAsync(operation, allocations, ledgerService, userId, cancellationToken);
                }
            }

            if (operation.OperationType == WarehouseTransfer)
            {
                var allocationLookup = BuildAllocationLookupBySku(allocations);
                foreach (var line in operation.OperationLines)
                {
                    if (!allocationLookup.TryGetValue(line.SkuId, out var lineAllocations))
                    {
                        throw new InvalidOperationException("Transfer allocation snapshot is missing.");
                    }

                    var lineLotNumber = NormalizeBlank(line.LotNumber);
                    var matchingAllocations = lineAllocations
                        .Where(allocation =>
                            (lineLotNumber is null || NormalizeBlank(allocation.LotNumber) == lineLotNumber) &&
                            (!line.ExpiryDate.HasValue || allocation.ExpiryDate == line.ExpiryDate))
                        .ToList();
                    if (matchingAllocations.Count == 0)
                    {
                        throw new InvalidOperationException("Transfer allocation does not match the revised line batch.");
                    }

                    foreach (var allocation in matchingAllocations)
                    {
                        await ledgerService.ReceiveSupplyAsync(
                            operation.DestinationLocationId!.Value,
                            line.SkuId,
                            allocation.Quantity,
                            userId,
                            allocation.LotNumber,
                            allocation.ExpiryDate,
                            line.LineNotes,
                            operation.Id,
                            cancellationToken);
                    }
                }

                operation.Status = Received;
                await AddVersionAsync(operationsDbContext, operation, "Received", userId, CreateSnapshot(operation, allocations), clock.EgyptNow, cancellationToken);
            }
            else
            {
                operation.Status = operation.OperationType is WholesaleSale or RetailSale ? Completed : Confirmed;
                operation.ConfirmedAt = clock.EgyptNow;
                operation.ConfirmedBy = userId;
                await EnsureAnonymousRetailCashMerchantAsync(operation, crmDbContext, clock.EgyptNow, cancellationToken);
                await AddVersionAsync(operationsDbContext, operation, operation.OperationType == Reserve ? "Representative received stock" : "Customer completed sale", userId, CreateSnapshot(operation, allocations), clock.EgyptNow, cancellationToken);
            }

            await crmDbContext.SaveChangesAsync(cancellationToken);
            await operationsDbContext.SaveChangesAsync(cancellationToken);
            if (operation.OperationType is WholesaleSale or RetailSale && operation.Status == Completed)
            {
                await PaymentsEndpoints.CreatePaymentArtifactsForCompletedSaleAsync(operation, paymentsDbContext, userId, clock.EgyptNow, cancellationToken);
            }
            await auditLogWriter.WriteAsync(
                "Operation",
                operation.Id,
                "Receive",
                new { operation.OperationType, operation.Status },
                cancellationToken: cancellationToken);
        }, cancellationToken);

        if (receiveConflict is not null) return receiveConflict;

        if (operation.OperationType is WholesaleSale or RetailSale &&
            string.Equals(operation.PaymentMethod, "CashHandToHand", StringComparison.OrdinalIgnoreCase))
        {
            var paymentLog = await paymentsDbContext.MainPaymentLogs
                .FirstOrDefaultAsync(log => log.OperationId == operation.Id && !log.IsDeleted, cancellationToken);
            if (paymentLog is not null)
            {
                await eventPublisher.PublishAsync(new PaymentWorkflowChangedEvent(
                    paymentLog.Id,
                    paymentLog.MerchantId,
                    operation.Id,
                    "CashReviewRequired",
                    $"Cash sale {operation.OperationNumber} is awaiting accountant review.",
                    null,
                    LenseeRoles.Accountant,
                    clock.EgyptNow), cancellationToken);
            }
        }

        if (operation.AutomationType == "TargetReplenishment") await EnsureReplenishmentStageNotificationAsync(notificationsDbContext, operation, clock.EgyptNow, cancellationToken);

        return Results.NoContent();
    }

    private static async Task<IResult> CancelOperationAsync(
        Guid id,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        IdentityDbContext identityDbContext,
        StockLedgerService ledgerService,
        ICurrentUser currentUser,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var operation = await LoadOperationAsync(operationsDbContext, id, cancellationToken);
        if (operation is null)
        {
            return Results.NotFound();
        }
        if (IsFinalizedForCorrection(operation.Status))
        {
            return Results.Conflict(new
            {
                detail = "Finalized operations are immutable. Create a correction proposal instead.",
                correctionRoute = $"/api/v1/operations/{operation.Id}/corrections"
            });
        }
        if (operation.Status == Shipped)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(operation.Status)] = ["Shipped transfers cannot be cancelled because stock has already left the main warehouse. Receive it, then use a return/transfer correction if needed."] });
        }
        if (operation.Status == Cancelled)
        {
            return Results.NoContent();
        }
        if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "cancel", cancellationToken))
        {
            return Results.Forbid();
        }

        var userId = currentUser.UserId ?? Guid.Empty;
        IResult? cancelConflict = null;
        await ExecuteInventoryOperationTransactionAsync(inventoryDbContext, operationsDbContext, identityDbContext, async () =>
        {
            var lockedOperation = await LockAndLoadOperationAsync(operationsDbContext, id, cancellationToken);
            if (lockedOperation is null)
            {
                cancelConflict = Results.Conflict(new
                {
                    code = "transition-conflict",
                    detail = "The operation changed before cancellation could be applied. Reload and retry."
                });
                return;
            }

            operation = lockedOperation;
            if (IsFinalizedForCorrection(operation.Status))
            {
                cancelConflict = Results.Conflict(new
                {
                    code = "transition-conflict",
                    detail = "Finalized operations are immutable. Create a correction proposal instead.",
                    correctionRoute = $"/api/v1/operations/{operation.Id}/corrections"
                });
                return;
            }
            if (operation.Status == Shipped)
            {
                cancelConflict = Results.Conflict(new
                {
                    code = "transition-conflict",
                    detail = "The operation was shipped before cancellation could be applied."
                });
                return;
            }
            if (operation.Status == Cancelled)
            {
                return;
            }
            if (!await CanMutateOperationAsync(currentUser, operation, inventoryDbContext, "cancel", cancellationToken))
            {
                cancelConflict = Results.Forbid();
                return;
            }

            if (operation.OperationType == WarehouseTransfer && operation.Status is Reserved or Shipped)
            {
                foreach (var group in operation.OperationLines.GroupBy(line => line.SkuId))
                {
                    await ledgerService.ReleaseInWarehouseAsync(operation.SourceLocationId!.Value, group.Key, group.Sum(line => line.Quantity), userId, operation.Id, cancellationToken);
                }
            }
            if (operation.OperationType is WholesaleSale or RetailSale or Reserve && operation.Status == Reserved)
            {
                foreach (var group in operation.OperationLines.Where(line => line.EntryMode == "Packs").GroupBy(line => line.SkuId))
                {
                    await ledgerService.ReleaseInWarehouseAsync(operation.SourceLocationId!.Value, group.Key, group.Sum(line => line.Quantity), userId, operation.Id, cancellationToken);
                }
            }

            operation.Status = Cancelled;
            await AddVersionAsync(operationsDbContext, operation, "Cancelled", userId, CreateSnapshot(operation, ReadTransferAllocations(operation)), clock.EgyptNow, cancellationToken);
            await operationsDbContext.SaveChangesAsync(cancellationToken);
            await auditLogWriter.WriteAsync(
                "Operation",
                operation.Id,
                "Cancel",
                new { operation.OperationType, operation.Status },
                cancellationToken: cancellationToken);
        }, cancellationToken);
        if (cancelConflict is not null) return cancelConflict;
        return Results.NoContent();
    }

    private static async Task<IReadOnlyList<ReplenishmentRowResponse>> BuildReplenishmentRowsAsync(
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        OperationsDbContext operationsDbContext,
        ICurrentUser currentUser,
        Guid? locationFilter,
        Guid? skuFilter,
        CancellationToken cancellationToken)
    {
        var locationsQuery = inventoryDbContext.Locations
            .Where(location => location.IsActive && location.LocationType != MainWarehouse);
        if (IsWarehouseClerk(currentUser))
        {
            var clerkLocationId = currentUser.LocationId!.Value;
            locationsQuery = locationsQuery.Where(location => location.Id == clerkLocationId);
        }
        if (locationFilter.HasValue)
        {
            locationsQuery = locationsQuery.Where(location => location.Id == locationFilter.Value);
        }

        var locations = await locationsQuery
            .OrderBy(location => location.Name)
            .ToListAsync(cancellationToken);
        var locationIds = locations.Select(location => location.Id).ToArray();
        if (locationIds.Length == 0)
        {
            return [];
        }

        var balancesQuery = inventoryDbContext.StockBalances
            .Where(balance => locationIds.Contains(balance.LocationId) && balance.TargetQty != null);
        if (skuFilter.HasValue)
        {
            balancesQuery = balancesQuery.Where(balance => balance.SkuId == skuFilter.Value);
        }

        var balances = await balancesQuery.ToListAsync(cancellationToken);
        var skuIds = balances.Select(balance => balance.SkuId).Distinct().ToArray();
        if (skuIds.Length == 0)
        {
            return [];
        }

        var skus = await catalogDbContext.Skus
            .Include(sku => sku.Product)
            .Where(sku => skuIds.Contains(sku.Id))
            .ToDictionaryAsync(
                sku => sku.Id,
                sku => new ReplenishmentSkuLookup(sku.SkuCode, sku.Product.Name, sku.Product.PiecesPerPack),
                cancellationToken);

        var incoming = await operationsDbContext.OperationLogs
            .Include(operation => operation.OperationLines)
            .Where(operation =>
                !operation.IsDeleted &&
                operation.OperationType == WarehouseTransfer &&
                operation.AutomationType == "TargetReplenishment" &&
                (operation.Status == Draft || operation.Status == Reserved || operation.Status == Shipped) &&
                operation.DestinationLocationId.HasValue &&
                locationIds.Contains(operation.DestinationLocationId.Value))
            .SelectMany(
                operation => operation.OperationLines,
                (operation, line) => new { DestinationLocationId = operation.DestinationLocationId!.Value, line.SkuId, line.Quantity })
            .GroupBy(value => new { value.DestinationLocationId, value.SkuId })
            .Select(group => new { group.Key.DestinationLocationId, group.Key.SkuId, Quantity = group.Sum(value => value.Quantity) })
            .ToDictionaryAsync(value => (value.DestinationLocationId, value.SkuId), value => value.Quantity, cancellationToken);

        var mainLocationId = await inventoryDbContext.Locations
            .Where(location => location.IsActive && location.LocationType == MainWarehouse)
            .Select(location => (Guid?)location.Id)
            .FirstOrDefaultAsync(cancellationToken);
        var mainStock = mainLocationId.HasValue
            ? await inventoryDbContext.StockBalances
                .Where(balance => balance.LocationId == mainLocationId.Value && skuIds.Contains(balance.SkuId))
                .ToDictionaryAsync(balance => balance.SkuId, balance => balance.AvailableQty, cancellationToken)
            : [];

        return balances
            .Select(balance =>
            {
                var location = locations.First(location => location.Id == balance.LocationId);
                skus.TryGetValue(balance.SkuId, out var sku);
                incoming.TryGetValue((balance.LocationId, balance.SkuId), out var incomingPacks);
                mainStock.TryGetValue(balance.SkuId, out var mainAvailable);
                var target = balance.TargetQty ?? 0;
                var shortage = Math.Max(target - balance.AvailableQty - incomingPacks, 0);
                return new ReplenishmentRowResponse(
                    balance.LocationId,
                    location.Name,
                    location.LocationType,
                    balance.SkuId,
                    sku?.SkuCode,
                    sku?.ProductName,
                    sku?.PiecesPerPack,
                    balance.AvailableQty,
                    ToPieces(balance.AvailableQty, sku?.PiecesPerPack, location.LocationType),
                    incomingPacks,
                    ToPieces(incomingPacks, sku?.PiecesPerPack, location.LocationType),
                    target,
                    ToPieces(target, sku?.PiecesPerPack, location.LocationType),
                    shortage,
                    ToPieces(shortage, sku?.PiecesPerPack, location.LocationType),
                    mainAvailable);
            })
            .OrderByDescending(row => row.ShortagePacks)
            .ThenBy(row => row.DestinationLocationName)
            .ThenBy(row => row.SkuCode ?? row.SkuId.ToString())
            .ToList();
    }

    private static async Task<Dictionary<Guid, int>> LoadPiecesPerPackBySkuAsync(
        CatalogDbContext catalogDbContext,
        IEnumerable<OperationLine> lines,
        CancellationToken cancellationToken)
    {
        var skuIds = lines.Select(line => line.SkuId).Distinct().ToArray();
        if (skuIds.Length == 0)
        {
            return [];
        }

        return await catalogDbContext.Skus
            .Include(sku => sku.Product)
            .Where(sku => skuIds.Contains(sku.Id) && sku.Product.PiecesPerPack != null)
            .ToDictionaryAsync(sku => sku.Id, sku => sku.Product.PiecesPerPack!.Value, cancellationToken);
    }

    private static async Task<IReadOnlyList<TransferAllocationSnapshot>> BuildSelectedSalePackAllocationsAsync(
        OperationLog operation,
        InventoryDbContext inventoryDbContext,
        OperationsDbContext operationsDbContext,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var packLines = operation.OperationLines.Where(line => line.EntryMode == "Packs").ToList();
        if (packLines.Count == 0)
        {
            return [];
        }

        var sourceLocationId = operation.SourceLocationId ?? throw new InvalidOperationException("Sale source location is required.");
        var today = DateOnly.FromDateTime(clock.EgyptNow);
        var skuIds = packLines.Select(line => line.SkuId).Distinct().ToArray();
        var batches = await inventoryDbContext.InventoryBatches
            .Where(batch =>
                batch.LocationId == sourceLocationId &&
                skuIds.Contains(batch.SkuId) &&
                batch.Quantity > 0 &&
                (batch.ExpiryDate == null || batch.ExpiryDate >= today))
            .ToListAsync(cancellationToken);
        var reservedByBatch = await LoadReservedBatchQuantitiesAsync(operationsDbContext, sourceLocationId, operation.Id, cancellationToken);
        var plannedByBatch = new Dictionary<Guid, int>();
        var allocations = new List<TransferAllocationSnapshot>();

        foreach (var line in packLines)
        {
            if (line.ExpiryDate is null)
            {
                throw new InvalidOperationException($"{line.SkuCodeSnapshot} requires a selected batch expiry.");
            }

            var batch = batches.FirstOrDefault(value =>
                value.SkuId == line.SkuId &&
                NormalizeBlank(value.LotNumber) == NormalizeBlank(line.LotNumber) &&
                value.ExpiryDate == line.ExpiryDate);
            if (batch is null)
            {
                throw new InvalidOperationException($"{line.SkuCodeSnapshot} selected batch was not found or is expired.");
            }

            reservedByBatch.TryGetValue(batch.Id, out var reservedQuantity);
            plannedByBatch.TryGetValue(batch.Id, out var plannedQuantity);
            var availableQuantity = batch.Quantity - reservedQuantity - plannedQuantity;
            if (availableQuantity < line.Quantity)
            {
                throw new InvalidOperationException($"{line.SkuCodeSnapshot} selected batch has only {Math.Max(availableQuantity, 0)} available pack(s).");
            }

            plannedByBatch[batch.Id] = plannedQuantity + line.Quantity;
            allocations.Add(new TransferAllocationSnapshot(line.SkuId, [new BatchAllocation(batch.Id, line.Quantity, batch.LotNumber, batch.ExpiryDate)]));
        }

        return allocations;
    }

    private static async Task<Dictionary<Guid, int>> LoadReservedBatchQuantitiesAsync(
        OperationsDbContext dbContext,
        Guid locationId,
        Guid excludeOperationId,
        CancellationToken cancellationToken)
    {
        var operations = await dbContext.OperationLogs
            .Include(operation => operation.OperationVersions)
            .Where(operation =>
                operation.Id != excludeOperationId &&
                !operation.IsDeleted &&
                operation.SourceLocationId == locationId &&
                operation.Status == Reserved)
            .ToListAsync(cancellationToken);

        var reserved = new Dictionary<Guid, int>();
        foreach (var operation in operations)
        {
            foreach (var allocation in ReadTransferAllocations(operation))
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

    private static async Task<OperationLog?> LoadOperationAsync(OperationsDbContext dbContext, Guid id, CancellationToken cancellationToken) =>
        await dbContext.OperationLogs
            .Include(operation => operation.OperationLines)
            .Include(operation => operation.InventoryReceiptHeader)
            .Include(operation => operation.OperationVersions)
            .Include(operation => operation.ShopifyOrderLink)
            .FirstOrDefaultAsync(operation => operation.Id == id && !operation.IsDeleted, cancellationToken);

    private static async Task<OperationLog?> LoadOperationForDraftUpdateAsync(OperationsDbContext dbContext, Guid id, CancellationToken cancellationToken) =>
        await dbContext.OperationLogs
            .Include(operation => operation.OperationLines)
            .Include(operation => operation.InventoryReceiptHeader)
            .Include(operation => operation.OperationVersions)
            .Include(operation => operation.ShopifyOrderLink)
            .FirstOrDefaultAsync(operation => operation.Id == id && !operation.IsDeleted, cancellationToken);

    private static async Task LockOperationAsync(OperationsDbContext dbContext, Guid id, CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational()) return;
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"select 1 from operations.operation_logs where id = {id} for update",
            cancellationToken);
    }

    /// <summary>
    /// Claims the operation row before tracking its aggregate.  Transition commands
    /// must use this after their inexpensive preflight reads so concurrent draft
    /// edits and terminal transitions cannot apply stock effects from stale lines.
    /// </summary>
    private static async Task<OperationLog?> LockAndLoadOperationAsync(OperationsDbContext dbContext, Guid id, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        await LockOperationAsync(dbContext, id, cancellationToken);
        return await LoadOperationAsync(dbContext, id, cancellationToken);
    }

    private static async Task<DraftValidationResult> ValidateDraftAsync(
        OperationRequest request,
        CatalogDbContext catalogDbContext,
        CrmDbContext crmDbContext,
        InventoryDbContext inventoryDbContext,
        OperationsDbContext operationsDbContext,
        ICurrentUser currentUser,
        bool isCreate,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var operationType = NormalizeOperationType(request.OperationType);
        if (operationType is not (InventoryReceipt or WarehouseTransfer or WholesaleSale or RetailSale or Reserve or Return or Change or WriteOff))
        {
            errors[nameof(request.OperationType)] = ["Operation type must be InventoryReceipt, WarehouseTransfer, WholesaleSale, RetailSale, Reserve, Return, Change, or WriteOff."];
        }
        if (request.Lines.Count == 0)
        {
            errors[nameof(request.Lines)] = ["At least one line is required."];
        }
        if (request.Lines.Any(line => GetLineQuantity(operationType, line) <= 0))
        {
            errors[nameof(request.Lines)] = ["Line quantity must be greater than zero."];
        }
        if (request.Lines.Select(line => GetLineUniquenessKey(operationType, line)).Distinct().Count() != request.Lines.Count)
        {
            errors[nameof(request.Lines)] = ["Duplicate SKU lines must differ by side, sale bonus flag, entry mode, lot, or batch expiry."];
        }
        if (NormalizePaymentMethod(request.PaymentMethod) is null && !string.IsNullOrWhiteSpace(request.PaymentMethod))
        {
            errors[nameof(request.PaymentMethod)] = ["Payment method must be CashHandToHand, CashTransaction, or Installment."];
        }
        if (operationType is WholesaleSale or RetailSale)
        {
            var invalidPriceLine = request.Lines.FirstOrDefault(line => line.IsBonus != true && (line.UnitPrice ?? 0) <= 0);
            if (invalidPriceLine is not null)
            {
                errors[nameof(request.Lines)] = ["Sale line unit price must be greater than zero unless the line is marked as bonus."];
            }
            if (request.Lines.Any(line => line.ExpiryDate is null))
            {
                errors[nameof(request.Lines)] = ["Sale lines must select an available batch expiry from the source warehouse."];
            }
        }
        if ((operationType is WarehouseTransfer or WholesaleSale or RetailSale or Reserve or WriteOff) &&
            request.Lines.Any(line => line.ExpiryDate is null))
        {
            errors[nameof(request.Lines)] = ["Stock-consuming lines must include the selected batch expiry."];
        }
        if (operationType is not (WholesaleSale or RetailSale) && request.Lines.Any(line => line.IsBonus == true))
        {
            errors[nameof(request.Lines)] = ["Bonus lines are allowed only for sale operations."];
        }

        var source = request.SourceLocationId.HasValue
            ? await inventoryDbContext.Locations.FirstOrDefaultAsync(location => location.Id == request.SourceLocationId && location.IsActive, cancellationToken)
            : null;
        var destination = request.DestinationLocationId.HasValue
            ? await inventoryDbContext.Locations.FirstOrDefaultAsync(location => location.Id == request.DestinationLocationId && location.IsActive, cancellationToken)
            : null;

        if (operationType == InventoryReceipt)
        {
            if (destination is null || !IsMainWarehouse(destination))
            {
                errors[nameof(request.DestinationLocationId)] = ["Inventory receipt destination must be the main warehouse."];
            }
        }
        if (operationType == WarehouseTransfer)
        {
            if (source is null || !IsMainWarehouse(source))
            {
                errors[nameof(request.SourceLocationId)] = ["Warehouse transfer source must be the main warehouse."];
            }
            if (destination is null || IsMainWarehouse(destination))
            {
                errors[nameof(request.DestinationLocationId)] = ["Warehouse transfer destination must be a non-main warehouse."];
            }
        }
        if (operationType == WholesaleSale)
        {
            if (source is null)
            {
                errors[nameof(request.SourceLocationId)] = ["Wholesale sale source location is required."];
            }
            if (!request.MerchantId.HasValue)
            {
                errors[nameof(request.MerchantId)] = ["Wholesale sale requires an active merchant."];
            }
            if (request.Lines.Any(line => NormalizeEntryMode(line.EntryMode) != "Packs"))
            {
                errors[nameof(request.Lines)] = ["Wholesale sale is pack-based only."];
            }
        }
        if (operationType == RetailSale)
        {
            if (source is null || !IsRetailSaleLocation(source))
            {
                errors[nameof(request.SourceLocationId)] = ["Retail sale source must be a retail or online location."];
            }
        }
        if (operationType == WholesaleSale &&
            NormalizePaymentMethod(request.PaymentMethod) is "CashTransaction" or "Installment" &&
            !request.MerchantId.HasValue)
        {
            errors[nameof(request.MerchantId)] = ["Cash transaction and installment sales require a registered merchant."];
        }
        if (operationType == RetailSale &&
            NormalizePaymentMethod(request.PaymentMethod) is "Installment" &&
            !request.MerchantId.HasValue)
        {
            errors[nameof(request.MerchantId)] = ["Installment retail sales require a registered merchant."];
        }
        if (operationType == Reserve)
        {
            if (source is null)
            {
                errors[nameof(request.SourceLocationId)] = ["Reserve source location is required."];
            }
            if (!request.RepresentativeId.HasValue)
            {
                errors[nameof(request.RepresentativeId)] = ["Reserve requires an active representative."];
            }
        }
        if (operationType == Return)
        {
            if (source is null)
            {
                errors[nameof(request.SourceLocationId)] = ["Return receiving location is required."];
            }
            if (!request.MerchantId.HasValue)
            {
                errors[nameof(request.MerchantId)] = ["Return requires an active merchant."];
            }
            if (request.Lines.Any(line => NormalizeEntryMode(line.EntryMode) != "Packs"))
            {
                errors[nameof(request.Lines)] = ["Return is pack-based in this milestone."];
            }
            if (request.Lines.Any(line => line.ExpiryDate is null))
            {
                errors[nameof(request.Lines)] = ["Return lines must include the batch expiry date received by the merchant."];
            }
        }
        if (operationType == Change)
        {
            if (source is null)
            {
                errors[nameof(request.SourceLocationId)] = ["Change location is required."];
            }
            if (!request.MerchantId.HasValue)
            {
                errors[nameof(request.MerchantId)] = ["Change requires an active merchant."];
            }
            if (!request.Lines.Any(line => NormalizeLineSection(operationType, line.Section) == ChangeOut))
            {
                errors[nameof(request.Lines)] = ["Change requires at least one returned line."];
            }
            if (!request.Lines.Any(line => NormalizeLineSection(operationType, line.Section) == ChangeIn))
            {
                errors[nameof(request.Lines)] = ["Change requires at least one replacement line."];
            }
            if (request.Lines.Any(line => NormalizeEntryMode(line.EntryMode) != "Packs"))
            {
                errors[nameof(request.Lines)] = ["Change is pack-based in this milestone."];
            }
            if (request.Lines.Any(line => NormalizeLineSection(operationType, line.Section) == ChangeOut && line.ExpiryDate is null))
            {
                errors[nameof(request.Lines)] = ["Returned change lines must include the batch expiry date received by the merchant."];
            }
        }
        if (operationType == WriteOff)
        {
            if (source is null)
            {
                errors[nameof(request.SourceLocationId)] = ["Write-off source location is required."];
            }
            if (!string.Equals(currentUser.Role, LenseeRoles.Admin, StringComparison.OrdinalIgnoreCase))
            {
                errors[nameof(request.OperationType)] = ["Write-off is Admin-only."];
            }
            if (request.Lines.Any(line => NormalizeEntryMode(line.EntryMode) != "Packs"))
            {
                errors[nameof(request.Lines)] = ["Write-off is pack-based in this milestone."];
            }
        }

        Merchant? merchant = null;
        if (request.MerchantId.HasValue)
        {
            merchant = await crmDbContext.Merchants.FirstOrDefaultAsync(value => value.Id == request.MerchantId && !value.IsDeleted && value.Status == "Active", cancellationToken);
            if (merchant is null)
            {
                errors[nameof(request.MerchantId)] = ["Merchant must exist and be active."];
            }
        }

        Representative? representative = null;
        if (request.RepresentativeId.HasValue)
        {
            representative = await crmDbContext.Representatives.FirstOrDefaultAsync(value => value.Id == request.RepresentativeId && !value.IsDeleted && value.Status == "Active", cancellationToken);
            if (representative is null)
            {
                errors[nameof(request.RepresentativeId)] = ["Representative must exist and be active."];
            }
        }

        var skuIds = request.Lines.Select(line => line.SkuId).Distinct().ToArray();
        var skus = await catalogDbContext.Skus
            .Include(sku => sku.Product)
            .Where(sku => skuIds.Contains(sku.Id) && sku.IsActive && sku.DeletedAt == null && sku.Product.IsActive && sku.Product.DeletedAt == null)
            .ToDictionaryAsync(sku => sku.Id, cancellationToken);
        if (skus.Count != skuIds.Length)
        {
            errors[nameof(request.Lines)] = ["All operation SKUs must exist and be active."];
        }
        else if (operationType == InventoryReceipt && request.Lines.Any(line =>
                     string.Equals(skus[line.SkuId].Product.ExpiryType, "Batch", StringComparison.OrdinalIgnoreCase) &&
                     line.ExpiryDate is null))
        {
            errors[nameof(request.Lines)] = ["Batch expiry is required for products with batch expiry tracking."];
        }

        return new DraftValidationResult(errors, source, destination, skus, merchant, representative);
    }

    private static async Task<IReadOnlyList<MerchantSalesVarianceWarning>> BuildMerchantSalesVarianceWarningsAsync(
        OperationLog operation,
        MerchantBatchHistoryService historyService,
        CancellationToken cancellationToken)
    {
        var warnings = new List<MerchantSalesVarianceWarning>();
        if (operation.ClientId is null || operation.OperationType is not (Return or Change))
        {
            return warnings;
        }

        var history = await historyService.LoadAsync(operation.ClientId.Value, operation.Id, cancellationToken);
        var facts = history.ToDictionary(
            row => new MerchantReturnKey(row.Key.SkuId, MerchantBatchHistoryService.NormalizeLot(row.Key.LotNumber), row.Key.ExpiryDate));
        var groups = operation.OperationLines
            .Where(line => operation.OperationType == Return || line.Section == ChangeOut)
            .GroupBy(line => new MerchantReturnKey(line.SkuId, MerchantBatchHistoryService.NormalizeLot(line.LotNumber), line.ExpiryDate));
        foreach (var group in groups)
        {
            var requested = group.Sum(line => line.Quantity);
            facts.TryGetValue(group.Key, out var fact);
            var recordedBalance = fact?.RecordedBalanceQuantity ?? 0;
            if (requested > recordedBalance)
            {
                var line = group.First();
                var soldQuantity = fact?.SoldQuantity ?? 0;
                var returnedQuantity = fact?.ReturnedQuantity ?? 0;
                var excessQuantity = Math.Max(returnedQuantity + requested - soldQuantity, 0);
                warnings.Add(new MerchantSalesVarianceWarning(
                    line.SkuId,
                    line.SkuCodeSnapshot,
                    line.ProductNameSnapshot,
                    line.LotNumber,
                    line.ExpiryDate,
                    requested,
                    soldQuantity,
                    returnedQuantity,
                    excessQuantity,
                    $"Confirmed returns would be {excessQuantity} pack(s) above recorded sales for this merchant batch."));
            }
        }
        return warnings;
    }

    private static MerchantSalesVarianceGateResponse CreateMerchantSalesVarianceGate(
        IReadOnlyList<MerchantSalesVarianceWarning> warnings,
        bool canBypass) =>
        new(
            "MerchantSalesVariance",
            "Recorded sales warning",
            "One or more returned batch quantities are above the recorded sales balance. Review the batch facts before continuing.",
            canBypass,
            canBypass,
            warnings);

    private static bool CanBypassMerchantSalesVariance(ICurrentUser currentUser) =>
        string.Equals(currentUser.Role, LenseeRoles.Admin, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(currentUser.Role, LenseeRoles.ERPAdmin, StringComparison.OrdinalIgnoreCase);

    private static async Task<OperationConfirmationRequest> ReadConfirmationRequestAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength is null or 0)
        {
            return new OperationConfirmationRequest(null, null);
        }

        return await JsonSerializer.DeserializeAsync<OperationConfirmationRequest>(request.Body, JsonOptions, cancellationToken)
            ?? new OperationConfirmationRequest(null, null);
    }

    private static async Task AcquireMerchantReturnLocksAsync(
        OperationLog operation,
        OperationsDbContext operationsDbContext,
        CancellationToken cancellationToken)
    {
        if (!operationsDbContext.Database.IsRelational() || operation.ClientId is null)
        {
            return;
        }

        var keys = operation.OperationLines
            .Where(line => operation.OperationType == Return || line.Section == ChangeOut)
            .Select(line => $"{operation.ClientId:N}|{line.SkuId:N}|{MerchantBatchHistoryService.NormalizeLot(line.LotNumber) ?? string.Empty}|{line.ExpiryDate:yyyy-MM-dd}")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        foreach (var key in keys)
        {
            await operationsDbContext.Database.ExecuteSqlInterpolatedAsync(
                $"select pg_advisory_xact_lock(hashtextextended({key}, 0))",
                cancellationToken);
        }
    }

    private static bool CanReadOperation(ICurrentUser currentUser, OperationLog operation) =>
        !IsWarehouseClerk(currentUser) ||
        (currentUser.LocationId.HasValue &&
            (operation.SourceLocationId == currentUser.LocationId || operation.DestinationLocationId == currentUser.LocationId));

    private static bool CanCreateDraft(ICurrentUser currentUser, OperationRequest request, Location? source, Location? destination)
    {
        if (string.Equals(currentUser.Role, LenseeRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (string.Equals(currentUser.Role, LenseeRoles.ERPAdmin, StringComparison.OrdinalIgnoreCase) &&
            NormalizeOperationType(request.OperationType) is Return or Change)
        {
            return true;
        }
        if (!IsWarehouseClerk(currentUser) || currentUser.LocationId is not { } clerkLocationId)
        {
            return false;
        }

        var operationType = NormalizeOperationType(request.OperationType);
        if (operationType == InventoryReceipt)
        {
            return destination?.Id == clerkLocationId && destination is not null && IsMainWarehouse(destination);
        }
        if (operationType == WarehouseTransfer)
        {
            return source?.Id == clerkLocationId && source is not null && IsMainWarehouse(source);
        }
        if (operationType == WriteOff)
        {
            return false;
        }

        return source?.Id == clerkLocationId && source is not null;
    }

    private static async Task<bool> CanMutateOperationAsync(ICurrentUser currentUser, OperationLog operation, InventoryDbContext dbContext, string action, CancellationToken cancellationToken)
    {
        if (string.Equals(currentUser.Role, LenseeRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (string.Equals(currentUser.Role, LenseeRoles.ERPAdmin, StringComparison.OrdinalIgnoreCase) &&
            (operation.OperationType is Return or Change || operation.AutomationType == "TargetReplenishment"))
        {
            return true;
        }
        if (!IsWarehouseClerk(currentUser) || currentUser.LocationId is not { } clerkLocationId)
        {
            return false;
        }

        var source = operation.SourceLocationId.HasValue
            ? await dbContext.Locations.FindAsync([operation.SourceLocationId.Value], cancellationToken)
            : null;
        if (operation.OperationType == InventoryReceipt)
        {
            return action is "confirm" or "cancel" && operation.DestinationLocationId == clerkLocationId && operation.DestinationLocationId.HasValue;
        }
        if (operation.OperationType == WarehouseTransfer && action is ("confirm" or "ship" or "cancel"))
        {
            return operation.SourceLocationId == clerkLocationId && source is not null && IsMainWarehouse(source);
        }
        if (operation.OperationType == WriteOff)
        {
            return false;
        }
        if (operation.OperationType is WholesaleSale or RetailSale or Reserve)
        {
            return action is "confirm" or "ship" or "receive" or "cancel" && operation.SourceLocationId == clerkLocationId;
        }
        if (operation.OperationType is Return or Change)
        {
            return action is "confirm" or "cancel" && operation.SourceLocationId == clerkLocationId;
        }
        if (action == "receive")
        {
            return operation.DestinationLocationId == clerkLocationId;
        }

        return false;
    }

    private static async Task<Dictionary<Guid, Location>> LoadLocationLookupAsync(InventoryDbContext dbContext, IReadOnlyCollection<OperationLog> operations, CancellationToken cancellationToken)
    {
        var ids = operations.SelectMany(operation => new[] { operation.SourceLocationId, operation.DestinationLocationId })
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await dbContext.Locations.Where(location => ids.Contains(location.Id)).ToDictionaryAsync(location => location.Id, cancellationToken);
    }

    private static async Task<Dictionary<Guid, User>> LoadUserLookupAsync(IdentityDbContext dbContext, IReadOnlyCollection<OperationLog> operations, CancellationToken cancellationToken)
    {
        var ids = operations
            .SelectMany(operation => new Guid?[] { operation.CreatedBy, operation.ConfirmedBy }
                .Concat(operation.OperationVersions.Select(version => (Guid?)version.EditedBy)))
            .Where(id => id.HasValue && id.Value != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        return await dbContext.Users
            .Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, cancellationToken);
    }

    private static string? GetUserDisplayName(Guid? userId, IReadOnlyDictionary<Guid, User> userLookup)
    {
        if (!userId.HasValue || userId.Value == Guid.Empty)
        {
            return null;
        }

        return userLookup.TryGetValue(userId.Value, out var user)
            ? (string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName)
            : userId.Value.ToString();
    }

    private static string? GetActorDisplayName(string? actorName, Guid? userId, IReadOnlyDictionary<Guid, User> userLookup) =>
        !string.IsNullOrWhiteSpace(actorName) ? actorName : GetUserDisplayName(userId, userLookup);

    private static void AddLines(OperationLog operation, IReadOnlyDictionary<Guid, Sku> skusById, IReadOnlyList<OperationLineRequest> lines, Merchant? merchant, Representative? representative)
    {
        foreach (var line in lines)
        {
            var sku = skusById[line.SkuId];
            var entryMode = NormalizeEntryMode(line.EntryMode);
            var quantity = GetLineQuantity(operation.OperationType, line);
            var isBonus = operation.OperationType is WholesaleSale or RetailSale && line.IsBonus == true;
            var unitPrice = isBonus ? 0 : line.UnitPrice ?? 0;
            var section = NormalizeLineSection(operation.OperationType, line.Section);
            operation.OperationLines.Add(new OperationLine
            {
                Id = Guid.NewGuid(),
                OperationId = operation.Id,
                SkuId = line.SkuId,
                ProductNameSnapshot = sku.Product.Name,
                SkuCodeSnapshot = sku.SkuCode,
                MerchantNameSnapshot = merchant?.BusinessName,
                RepresentativeNameSnapshot = representative?.Name,
                Section = section,
                Quantity = quantity,
                EntryMode = entryMode,
                BonusQuantity = isBonus ? quantity : 0,
                UnitPrice = unitPrice,
                LineTotal = unitPrice * quantity,
                LotNumber = TrimToNull(line.LotNumber),
                ExpiryDate = line.ExpiryDate,
                LineNotes = TrimToNull(line.Notes)
            });
        }
    }

    private static int GetLineQuantity(string operationType, OperationLineRequest line) =>
        operationType == RetailSale && NormalizeEntryMode(line.EntryMode) == "Pieces"
            ? line.PieceQuantity ?? line.PackQuantity
            : line.PackQuantity;

    private static string NormalizeEntryMode(string? value) =>
        string.Equals(value, "Pieces", StringComparison.OrdinalIgnoreCase) ? "Pieces" : "Packs";

    private static string NormalizeLineSection(string operationType, string? value)
    {
        if (operationType == Change)
        {
            return string.Equals(value, ChangeIn, StringComparison.OrdinalIgnoreCase) ? ChangeIn : ChangeOut;
        }

        return Standard;
    }

    private static (Guid SkuId, string Section, bool IsBonus, string EntryMode, string? LotNumber, DateOnly? ExpiryDate) GetLineUniquenessKey(string operationType, OperationLineRequest line) =>
        (
            line.SkuId,
            NormalizeLineSection(operationType, line.Section),
            operationType is WholesaleSale or RetailSale && line.IsBonus == true,
            NormalizeEntryMode(line.EntryMode),
            NormalizeBlank(line.LotNumber),
            line.ExpiryDate);

    private static async Task ReplaceOperationLinesAsync(
        OperationsDbContext dbContext,
        OperationLog operation,
        CancellationToken cancellationToken)
    {
        var trackedLines = operation.OperationLines.ToList();

        if (dbContext.Database.IsRelational())
        {
            await dbContext.OperationLines
                .Where(line => line.OperationId == operation.Id)
                .ExecuteDeleteAsync(cancellationToken);

            foreach (var entry in dbContext.ChangeTracker.Entries<OperationLine>()
                .Where(entry => entry.Entity.OperationId == operation.Id)
                .ToList())
            {
                entry.State = EntityState.Detached;
            }
        }
        else
        {
            var existingLines = await dbContext.OperationLines
                .Where(line => line.OperationId == operation.Id)
                .ToListAsync(cancellationToken);
            dbContext.OperationLines.RemoveRange(existingLines);
            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var line in trackedLines.Concat(existingLines).DistinctBy(line => line.Id))
            {
                dbContext.Entry(line).State = EntityState.Detached;
            }
        }

        operation.OperationLines = new List<OperationLine>();
    }

    private static async Task SaveChangesIgnoringStaleDeletedOperationLinesAsync(
        OperationsDbContext dbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception) when (IsOnlyStaleReplacedOperationLines(exception))
        {
            foreach (var entry in exception.Entries)
            {
                entry.State = EntityState.Detached;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private static bool IsOnlyStaleReplacedOperationLines(DbUpdateConcurrencyException exception) =>
        exception.Entries.Count > 0 &&
        exception.Entries.All(entry => entry.Entity is OperationLine && entry.State != EntityState.Added);

    private static Task AddVersionAsync(OperationsDbContext dbContext, OperationLog operation, string reason, Guid userId, OperationSnapshot snapshot, DateTime now, CancellationToken cancellationToken)
    {
        var version = new OperationVersion
        {
            Id = Guid.NewGuid(),
            OperationId = operation.Id,
            VersionNumber = operation.OperationVersions.Count == 0
                ? 1
                : operation.OperationVersions.Max(value => value.VersionNumber) + 1,
            SnapshotData = JsonSerializer.Serialize(snapshot, JsonOptions),
            Reason = reason,
            EditedBy = userId,
            EditedAt = now
        };
        operation.OperationVersions.Add(version);
        dbContext.OperationVersions.Add(version);
        operation.CurrentVersionId = version.Id;
        return Task.CompletedTask;
    }

    private static OperationSnapshot CreateSnapshot(OperationLog operation, IReadOnlyList<TransferAllocationSnapshot>? allocations = null) =>
        new(
            operation.OperationType,
            operation.Status,
            operation.SourceLocationId,
            operation.DestinationLocationId,
            operation.ClientId,
            operation.ClientName,
            operation.RepresentativeId,
            operation.PaymentMethod,
            operation.Notes,
            operation.OperationLines
                .Select(line => new OperationLineSnapshot(line.SkuId, line.SkuCodeSnapshot, line.ProductNameSnapshot, line.Section, line.Quantity, line.EntryMode, line.BonusQuantity, line.UnitPrice, line.LineTotal, line.LotNumber, line.ExpiryDate, line.LineNotes, line.ShopifyLineItemId, line.ShopifyVariantId, line.ShopifySkuSnapshot, line.ShopifyTitleSnapshot, line.ShopifyVariantTitleSnapshot, line.ShopifyPropertiesSnapshot))
                .ToList(),
            allocations ?? []);

    private static string BuildRevisionFingerprint(OperationLog operation) =>
        JsonSerializer.Serialize(new
        {
            operation.OperationType,
            operation.SourceLocationId,
            operation.DestinationLocationId,
            ClientId = operation.ClientId,
            BuyerName = operation.ClientId.HasValue ? null : TrimToNull(operation.ClientName),
            operation.RepresentativeId,
            PaymentMethod = NormalizePaymentMethod(operation.PaymentMethod),
            BuyerPhone = TrimToNull(operation.BuyerPhone),
            Notes = TrimToNull(operation.Notes),
            Receipt = operation.InventoryReceiptHeader is null ? null : new
            {
                SupplierName = TrimToNull(operation.InventoryReceiptHeader.SupplierName) ?? "Supplier",
                InvoiceNumber = TrimToNull(operation.InventoryReceiptHeader.InvoiceNumber)
            },
            Lines = operation.OperationLines.Select(line => new
            {
                line.SkuId,
                Section = NormalizeLineSection(operation.OperationType, line.Section),
                EntryMode = NormalizeEntryMode(line.EntryMode),
                Quantity = line.Quantity,
                BonusQuantity = line.BonusQuantity,
                UnitPrice = line.UnitPrice,
                LotNumber = NormalizeBlank(line.LotNumber),
                line.ExpiryDate,
                Notes = TrimToNull(line.LineNotes)
            }).OrderBy(line => line.SkuId).ThenBy(line => line.Section).ThenBy(line => line.EntryMode).ThenBy(line => line.Quantity).ThenBy(line => line.LotNumber).ToArray()
        }, JsonOptions);

    private static string BuildRevisionFingerprint(OperationRequest request) =>
        JsonSerializer.Serialize(new
        {
            OperationType = NormalizeOperationType(request.OperationType),
            request.SourceLocationId,
            request.DestinationLocationId,
            ClientId = request.MerchantId,
            BuyerName = request.MerchantId.HasValue ? null : TrimToNull(request.BuyerName),
            request.RepresentativeId,
            PaymentMethod = NormalizePaymentMethod(request.PaymentMethod),
            BuyerPhone = TrimToNull(request.BuyerPhone),
            Notes = TrimToNull(request.Notes),
            Receipt = request.Receipt is null ? null : new
            {
                SupplierName = TrimToNull(request.Receipt.SupplierName) ?? "Supplier",
                InvoiceNumber = TrimToNull(request.Receipt.InvoiceNumber)
            },
            Lines = request.Lines.Select(line =>
            {
                var entryMode = NormalizeEntryMode(line.EntryMode);
                var quantity = GetLineQuantity(NormalizeOperationType(request.OperationType), line);
                var isBonus = NormalizeOperationType(request.OperationType) is WholesaleSale or RetailSale && line.IsBonus == true;
                return new
                {
                    line.SkuId,
                    Section = NormalizeLineSection(NormalizeOperationType(request.OperationType), line.Section),
                    EntryMode = entryMode,
                    Quantity = quantity,
                    BonusQuantity = isBonus ? quantity : 0,
                    UnitPrice = isBonus ? 0 : line.UnitPrice ?? 0,
                    LotNumber = NormalizeBlank(line.LotNumber),
                    line.ExpiryDate,
                    Notes = TrimToNull(line.Notes)
                };
            }).OrderBy(line => line.SkuId).ThenBy(line => line.Section).ThenBy(line => line.EntryMode).ThenBy(line => line.Quantity).ThenBy(line => line.LotNumber).ToArray()
        }, JsonOptions);

    private static Dictionary<Guid, List<BatchAllocation>> BuildAllocationLookupBySku(IReadOnlyList<TransferAllocationSnapshot> allocations) =>
        allocations
            .GroupBy(value => value.SkuId)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(value => value.Allocations).ToList());

    private static IReadOnlyList<TransferAllocationSnapshot> ReadTransferAllocations(OperationLog operation)
    {
        var snapshot = operation.OperationVersions
            .OrderByDescending(version => version.VersionNumber)
            .Select(version =>
            {
                try
                {
                    return JsonSerializer.Deserialize<OperationSnapshot>(version.SnapshotData, JsonOptions);
                }
                catch
                {
                    return null;
                }
            })
            .FirstOrDefault(value => value?.TransferAllocations?.Count > 0);

        return snapshot?.TransferAllocations ?? [];
    }

    private static async Task CommitTransferOutAsync(
        OperationLog operation,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        StockLedgerService ledgerService,
        Guid userId,
        string transactionType,
        CancellationToken cancellationToken)
    {
        var allocationLookup = BuildAllocationLookupBySku(allocations);
        foreach (var line in operation.OperationLines.GroupBy(line => line.SkuId).Select(group => new { SkuId = group.Key }))
        {
            if (!allocationLookup.TryGetValue(line.SkuId, out var lineAllocations))
            {
                throw new InvalidOperationException("Transfer allocation snapshot is missing.");
            }

            await ledgerService.CommitReservedInWarehouseOutAsync(
                operation.SourceLocationId!.Value,
                line.SkuId,
                lineAllocations,
                userId,
                operation.Id,
                transactionType,
                cancellationToken);
        }
    }

    private static async Task ShipSaleOutAsync(
        OperationLog operation,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        CatalogDbContext catalogDbContext,
        StockLedgerService ledgerService,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (var allocation in allocations)
        {
            await ledgerService.CommitReservedInWarehouseOutAsync(
                operation.SourceLocationId!.Value,
                allocation.SkuId,
                allocation.Allocations,
                userId,
                operation.Id,
                InventoryTransactionTypes.Sale,
                cancellationToken);
        }

        var pieceLines = operation.OperationLines.Where(line => line.EntryMode == "Pieces").ToList();
        if (pieceLines.Count == 0)
        {
            return;
        }

        var piecesPerPack = await LoadPiecesPerPackBySkuAsync(catalogDbContext, pieceLines, cancellationToken);
        foreach (var line in pieceLines)
        {
            if (!piecesPerPack.TryGetValue(line.SkuId, out var packPieces) || packPieces <= 0)
            {
                throw new InvalidOperationException("SKU pieces per pack is required for piece sales.");
            }
            if (line.ExpiryDate is null)
            {
                throw new InvalidOperationException($"{line.SkuCodeSnapshot} requires a selected batch expiry.");
            }

            await ledgerService.IssuePiecesFromSelectionAsync(
                operation.SourceLocationId!.Value,
                line.SkuId,
                line.Quantity,
                packPieces,
                line.LotNumber,
                line.ExpiryDate,
                userId,
                operation.Id,
                cancellationToken);
        }
    }

    private static async Task ShipRepresentativeReserveAsync(
        OperationLog operation,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        StockLedgerService ledgerService,
        Guid userId,
        CancellationToken cancellationToken)
    {
        foreach (var allocation in allocations)
        {
            await ledgerService.MoveReservedInWarehouseToRepresentativeAsync(
                operation.SourceLocationId!.Value,
                allocation.SkuId,
                allocation.Allocations,
                userId,
                operation.Id,
                cancellationToken);
        }
    }

    private static IEnumerable<OperationLine> LinesRequiringAllocation(OperationLog operation) =>
        operation.OperationType switch
        {
            WarehouseTransfer or Reserve => operation.OperationLines,
            WholesaleSale or RetailSale => operation.OperationLines.Where(line => line.EntryMode == "Packs"),
            _ => []
        };

    private static bool IsShopifyAllocationOnlyUpdate(OperationLog operation, OperationRequest request)
    {
        if (NormalizeOperationType(request.OperationType) != operation.OperationType ||
            request.SourceLocationId != operation.SourceLocationId ||
            request.DestinationLocationId != operation.DestinationLocationId ||
            request.MerchantId != operation.ClientId ||
            request.RepresentativeId != operation.RepresentativeId ||
            NormalizePaymentMethod(request.PaymentMethod) != operation.PaymentMethod ||
            TrimToNull(request.BuyerName) != operation.ClientName ||
            TrimToNull(request.BuyerPhone) != operation.BuyerPhone ||
            TrimToNull(request.Notes) != operation.Notes ||
            request.Lines.Count != operation.OperationLines.Count)
        {
            return false;
        }

        var existing = operation.OperationLines
            .GroupBy(line => (line.SkuId, line.Section, IsBonus: line.BonusQuantity > 0, line.EntryMode, line.Quantity, line.UnitPrice))
            .ToDictionary(group => group.Key, group => group.Count());
        var requested = request.Lines
            .GroupBy(line => (line.SkuId, Section: NormalizeLineSection(operation.OperationType, line.Section), IsBonus: line.IsBonus == true, EntryMode: NormalizeEntryMode(line.EntryMode), Quantity: GetLineQuantity(operation.OperationType, line), UnitPrice: line.IsBonus == true ? 0m : line.UnitPrice ?? 0m))
            .ToDictionary(group => group.Key, group => group.Count());
        return existing.Count == requested.Count && existing.All(item => requested.TryGetValue(item.Key, out var count) && count == item.Value);
    }

    private static bool IsAllocationPending(OperationLog operation) =>
        operation.SalesChannel == "Shopify" && operation.OperationLines.Any(line => line.ExpiryDate is null);

    private static OperationListResponse ToListResponse(OperationLog operation, IReadOnlyDictionary<Guid, Location> locationLookup, IReadOnlyDictionary<Guid, User> userLookup) =>
        new(
            operation.Id,
            operation.OperationNumber,
            operation.OperationType,
            operation.Status,
            operation.SourceLocationId,
            GetLocationName(operation.SourceLocationId, locationLookup),
            operation.DestinationLocationId,
            GetLocationName(operation.DestinationLocationId, locationLookup),
            operation.ClientId,
            operation.ClientName,
            operation.RepresentativeId,
            operation.PaymentMethod,
            operation.CreatedAt,
            operation.ConfirmedAt,
            GetActorDisplayName(operation.CreatedActorName, operation.CreatedBy, userLookup),
            GetUserDisplayName(operation.ConfirmedBy, userLookup),
            operation.OperationVersions.OrderByDescending(version => version.VersionNumber).Select(version => GetActorDisplayName(version.EditedActorName, version.EditedBy, userLookup)).FirstOrDefault(),
            operation.Status == Draft,
            false,
            null,
            operation.SalesChannel,
            operation.BuyerPhone,
            operation.BuyerEmail,
            operation.ShippingAddress,
            operation.ShopifyOrderLink?.ShopifyOrderId,
            operation.ShopifyOrderLink?.ShopifyOrderNumber,
            IsAllocationPending(operation));

    private static OperationDetailResponse ToDetailResponse(
        OperationLog operation,
        IReadOnlyDictionary<Guid, Location> locationLookup,
        IReadOnlyDictionary<Guid, User> userLookup,
        IReadOnlyDictionary<Guid, WearCycleInfo> wearCycles)
    {
        var lines = operation.OperationLines
            .Select(line =>
            {
                wearCycles.TryGetValue(line.SkuId, out var wearCycle);
                return new OperationLineResponse(line.Id, line.SkuId, line.SkuCodeSnapshot, line.ProductNameSnapshot, line.Section, line.Quantity, line.EntryMode, line.BonusQuantity, line.UnitPrice, line.LineTotal, line.LotNumber, line.ExpiryDate, line.MerchantNameSnapshot, line.RepresentativeNameSnapshot, line.LineNotes, wearCycle?.Cycle, wearCycle?.Duration, line.ShopifyLineItemId, line.ShopifyVariantId, line.ShopifySkuSnapshot, line.ShopifyTitleSnapshot, line.ShopifyVariantTitleSnapshot, line.ShopifyPropertiesSnapshot);
            })
            .ToList();
        var allocations = ReadTransferAllocations(operation)
            .SelectMany(allocation => allocation.Allocations.Select(batch => new OperationAllocationResponse(
                allocation.SkuId,
                operation.OperationLines.FirstOrDefault(line => line.SkuId == allocation.SkuId)?.SkuCodeSnapshot,
                operation.OperationLines.FirstOrDefault(line => line.SkuId == allocation.SkuId)?.ProductNameSnapshot,
                batch.BatchId,
                batch.Quantity,
                batch.LotNumber,
                batch.ExpiryDate)))
            .ToList();
        var versions = BuildVersionResponses(operation, userLookup);

        return new(
            operation.Id,
            operation.OperationNumber,
            operation.OperationType,
            operation.Status,
            operation.SourceLocationId,
            GetLocationName(operation.SourceLocationId, locationLookup),
            operation.DestinationLocationId,
            GetLocationName(operation.DestinationLocationId, locationLookup),
            operation.ClientId,
            operation.ClientName,
            operation.RepresentativeId,
            operation.OperationLines.Select(line => line.RepresentativeNameSnapshot).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
            operation.PaymentMethod,
            operation.Notes,
            operation.CreatedAt,
            operation.ConfirmedAt,
            GetActorDisplayName(operation.CreatedActorName, operation.CreatedBy, userLookup),
            GetUserDisplayName(operation.ConfirmedBy, userLookup),
            operation.OperationVersions.OrderByDescending(version => version.VersionNumber).Select(version => GetActorDisplayName(version.EditedActorName, version.EditedBy, userLookup)).FirstOrDefault(),
            operation.CurrentVersion?.VersionNumber ?? operation.OperationVersions.OrderByDescending(version => version.VersionNumber).Select(version => (int?)version.VersionNumber).FirstOrDefault(),
            operation.ConcurrencyVersion,
            operation.Status == Draft,
            false,
            null,
            operation.InventoryReceiptHeader is null ? null : new ReceiptResponse(operation.InventoryReceiptHeader.SupplierName, operation.InventoryReceiptHeader.InvoiceNumber),
            lines,
            allocations,
            versions,
            operation.SalesChannel,
            operation.BuyerPhone,
            operation.BuyerEmail,
            operation.ShippingAddress,
            operation.ShopifyOrderLink?.ShopifyOrderId,
            operation.ShopifyOrderLink?.ShopifyOrderNumber,
            IsAllocationPending(operation));
    }

    private static async Task<IReadOnlyDictionary<Guid, WearCycleInfo>> LoadWearCyclesBySkuAsync(
        CatalogDbContext catalogDbContext,
        IEnumerable<OperationLine> lines,
        CancellationToken cancellationToken)
    {
        var skuIds = lines.Select(line => line.SkuId).Distinct().ToArray();
        if (skuIds.Length == 0) return new Dictionary<Guid, WearCycleInfo>();

        var skus = await catalogDbContext.Skus.AsNoTracking()
            .Where(sku => skuIds.Contains(sku.Id))
            .Select(sku => new WearCycleLookup(sku.Id, sku.Product.ProductType, sku.Product.OpenedExpiryRate, sku.Product.OpenedExpiryDuration))
            .ToListAsync(cancellationToken);
        return skus.ToDictionary(sku => sku.SkuId, sku => ToWearCycleInfo(sku.ProductType, sku.OpenedExpiryRate, sku.OpenedExpiryDuration));
    }

    private static WearCycleInfo ToWearCycleInfo(string productType, string? openedExpiryRate, string? openedExpiryDuration)
    {
        if (!CatalogValidation.IsLensProduct(productType)) return new WearCycleInfo("NotApplicable", null);
        return CatalogValidation.HasValidOpenedExpiryRate(openedExpiryRate)
            ? new WearCycleInfo(openedExpiryRate, openedExpiryDuration)
            : new WearCycleInfo(null, null);
    }

    private static IReadOnlyList<OperationVersionResponse> BuildVersionResponses(
        OperationLog operation,
        IReadOnlyDictionary<Guid, User> userLookup)
    {
        var snapshots = operation.OperationVersions
            .OrderBy(version => version.VersionNumber)
            .Select(version => new
            {
                Version = version,
                Snapshot = SafeReadSnapshot(version.SnapshotData)
            })
            .ToList();

        var currentVersionId = operation.CurrentVersionId;
        var responses = new List<OperationVersionResponse>(snapshots.Count);
        for (var index = 0; index < snapshots.Count; index++)
        {
            var current = snapshots[index];
            var previous = index > 0 ? snapshots[index - 1].Snapshot : null;
            var snapshot = current.Snapshot;
            responses.Add(new OperationVersionResponse(
                current.Version.Id,
                current.Version.VersionNumber,
                current.Version.Reason,
                current.Version.EditedAt,
                GetActorDisplayName(current.Version.EditedActorName, current.Version.EditedBy, userLookup),
                currentVersionId == current.Version.Id,
                previous is not null && snapshot is not null && (previous.SourceLocationId != snapshot.SourceLocationId || previous.DestinationLocationId != snapshot.DestinationLocationId),
                previous is not null && snapshot is not null && (previous.ClientId != snapshot.ClientId || previous.ClientName != snapshot.ClientName || previous.RepresentativeId != snapshot.RepresentativeId),
                previous is not null && snapshot is not null && (previous.PaymentMethod != snapshot.PaymentMethod || !SameFinancialLines(previous.Lines, snapshot.Lines)),
                previous is not null && snapshot is not null && !SameLines(previous.Lines, snapshot.Lines)));
        }

        return responses;
    }

    private static OperationSnapshot? SafeReadSnapshot(string snapshotData)
    {
        try
        {
            return JsonSerializer.Deserialize<OperationSnapshot>(snapshotData, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static bool SameLines(IReadOnlyList<OperationLineSnapshot> left, IReadOnlyList<OperationLineSnapshot> right)
    {
        left ??= [];
        right ??= [];
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!Equals(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SameFinancialLines(IReadOnlyList<OperationLineSnapshot> left, IReadOnlyList<OperationLineSnapshot> right)
    {
        left ??= [];
        right ??= [];
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index].UnitPrice != right[index].UnitPrice ||
                left[index].LineTotal != right[index].LineTotal ||
                left[index].BonusQuantity != right[index].BonusQuantity)
            {
                return false;
            }
        }

        return true;
    }

    private static string? GetLocationName(Guid? locationId, IReadOnlyDictionary<Guid, Location> lookup) =>
        locationId.HasValue && lookup.TryGetValue(locationId.Value, out var location) ? location.Name : null;

    private static string NormalizeOperationType(string value) =>
        string.Equals(value, InventoryReceipt, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "\u0627\u0633\u062a\u0644\u0627\u0645 \u0645\u062e\u0632\u0648\u0646", StringComparison.OrdinalIgnoreCase)
            ? InventoryReceipt
            : string.Equals(value, WarehouseTransfer, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Supply", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "\u062a\u062d\u0648\u064a\u0644 \u0645\u062e\u0632\u0648\u0646", StringComparison.OrdinalIgnoreCase)
                ? WarehouseTransfer
                : string.Equals(value, WholesaleSale, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "\u0628\u064a\u0639 \u062c\u0645\u0644\u0629", StringComparison.OrdinalIgnoreCase)
                    ? WholesaleSale
                    : string.Equals(value, RetailSale, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "\u0628\u064a\u0639 \u0642\u0637\u0627\u0639\u064a / \u0623\u0648\u0646\u0644\u0627\u064a\u0646", StringComparison.OrdinalIgnoreCase)
                        ? RetailSale
                        : string.Equals(value, Reserve, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "\u062d\u062c\u0632 \u0644\u0644\u0645\u0646\u062f\u0648\u0628", StringComparison.OrdinalIgnoreCase)
                            ? Reserve
                            : string.Equals(value, Return, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "\u0645\u0631\u062a\u062c\u0639", StringComparison.OrdinalIgnoreCase)
                                ? Return
                                : string.Equals(value, Change, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "\u0627\u0633\u062a\u0628\u062f\u0627\u0644", StringComparison.OrdinalIgnoreCase)
                                    ? Change
                                    : string.Equals(value, WriteOff, StringComparison.OrdinalIgnoreCase) || string.Equals(value, "\u0625\u0639\u062f\u0627\u0645 / \u062a\u0633\u0648\u064a\u0629 \u0645\u062e\u0632\u0648\u0646", StringComparison.OrdinalIgnoreCase)
                                        ? WriteOff
                                        : value.Trim();

    private static bool IsMainWarehouse(Location location) =>
        string.Equals(location.LocationType, MainWarehouse, StringComparison.OrdinalIgnoreCase);

    private static bool IsRetailSaleLocation(Location location) =>
        !IsMainWarehouse(location) &&
        (string.Equals(location.LocationType, Retail, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(location.LocationType, Online, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(location.LocationType, "SubWarehouse", StringComparison.OrdinalIgnoreCase) ||
            location.Name.Contains("Retail", StringComparison.OrdinalIgnoreCase) ||
            location.Name.Contains("Online", StringComparison.OrdinalIgnoreCase));

    private static bool IsWarehouseClerk(ICurrentUser currentUser) =>
        string.Equals(currentUser.Role, LenseeRoles.WarehouseClerk, StringComparison.OrdinalIgnoreCase);

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizePaymentMethod(string? value)
    {
        var trimmed = TrimToNull(value);
        if (trimmed is null)
        {
            return null;
        }

        if (string.Equals(trimmed, "Cash", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, "\u0646\u0642\u062f\u064a \u0645\u0628\u0627\u0634\u0631", StringComparison.OrdinalIgnoreCase))
        {
            return "CashHandToHand";
        }
        if (string.Equals(trimmed, "Card", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "Wallet", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "BankTransaction", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, "\u062a\u062d\u0648\u064a\u0644 \u0623\u0648 \u0625\u064a\u062f\u0627\u0639 \u0646\u0642\u062f\u064a", StringComparison.OrdinalIgnoreCase))
        {
            return "CashTransaction";
        }

        if (string.Equals(trimmed, "\u062a\u0642\u0633\u064a\u0637", StringComparison.OrdinalIgnoreCase))
        {
            return "Installment";
        }

        return PaymentMethods.FirstOrDefault(method => string.Equals(method, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static int? ToPieces(int packs, int? piecesPerPack, string locationType) =>
        !string.Equals(locationType, MainWarehouse, StringComparison.OrdinalIgnoreCase) && piecesPerPack is > 0
            ? packs * piecesPerPack.Value
            : null;

    private static ReplenishmentAlertResponse ToReplenishmentAlert(ReplenishmentRowResponse shortage, string message) =>
        new(
            shortage.DestinationLocationId,
            shortage.DestinationLocationName,
            shortage.SkuId,
            shortage.SkuCode,
            shortage.ProductName,
            shortage.ShortagePacks,
            shortage.MainAvailablePacks,
            message);

    private static async Task WriteReplenishmentAlertsAsync(
        NotificationsDbContext notificationsDbContext,
        IReadOnlyCollection<ReplenishmentAlertResponse> alerts,
        DateTime now,
        CancellationToken cancellationToken)
    {
        foreach (var alert in alerts)
        {
            var message = $"{alert.DestinationLocationName}: {alert.SkuCode ?? alert.SkuId.ToString()} needs {alert.ShortagePacks} pack(s). {alert.Message}";
            foreach (var role in new[] { LenseeRoles.Admin, LenseeRoles.CLevel })
            {
                var alreadyExists = await notificationsDbContext.NotificationLogs.AnyAsync(notification =>
                    notification.AlertType == "TargetReplenishmentLowMainStock" &&
                    notification.TargetRole == role &&
                    notification.ReferenceId == alert.SkuId &&
                    notification.ReferenceType == "Sku" &&
                    notification.Message == message &&
                    notification.CreatedAt.Date == now.Date,
                    cancellationToken);
                if (alreadyExists)
                {
                    continue;
                }

                notificationsDbContext.NotificationLogs.Add(new NotificationLog
                {
                    Id = Guid.NewGuid(),
                    AlertType = "TargetReplenishmentLowMainStock",
                    Message = message,
                    ReferenceId = alert.SkuId,
                    ReferenceType = "Sku",
                    TargetRole = role,
                    Channel = "InApp",
                    IsRead = false,
                    CreatedAt = now
                });
            }
        }

        await notificationsDbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<string?> GetRevisionBlockReasonAsync(
        OperationLog operation,
        PaymentsDbContext paymentsDbContext,
        CancellationToken cancellationToken)
    {
        if (operation.OperationType == Return &&
            operation.Status == Confirmed &&
            operation.OperationLines.Any(line => line.WriteOffReason == "ExpiredMerchantReturn"))
        {
            return "Expired merchant returns cannot be revised because receipt and write-off were posted together.";
        }

        if (operation.OperationType is RetailSale &&
            operation.Status is Shipped or Completed &&
            operation.OperationLines.Any(line => line.EntryMode == "Pieces"))
        {
            return "Retail piece sales cannot be revised after shipment because exact loose-piece reversal is not yet supported.";
        }

        if (operation.OperationType is Change or WriteOff &&
            operation.Status == Confirmed &&
            ReadTransferAllocations(operation).Count == 0 &&
            operation.OperationLines.Any(line => line.Section == ChangeIn || operation.OperationType == WriteOff))
        {
            return "This operation was confirmed before allocation snapshots were captured and cannot be safely revised.";
        }

        if (operation.OperationType is WholesaleSale or RetailSale && operation.Status == Completed)
        {
            var paymentBlock = await PaymentsEndpoints.GetRevisionBlockReasonForCompletedSaleAsync(operation.Id, paymentsDbContext, cancellationToken);
            if (paymentBlock is not null)
            {
                return paymentBlock;
            }
        }

        return null;
    }

    private static bool IsFinalizedForCorrection(string status) => status is Confirmed or Completed or Received;

    private static async Task EnsureAnonymousRetailCashMerchantAsync(
        OperationLog operation,
        CrmDbContext crmDbContext,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (operation.OperationType != RetailSale ||
            operation.ClientId.HasValue ||
            (!string.Equals(operation.PaymentMethod, "CashHandToHand", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(operation.PaymentMethod, "CashTransaction", StringComparison.OrdinalIgnoreCase)) ||
            string.IsNullOrWhiteSpace(operation.ClientName))
        {
            return;
        }

        var buyerName = operation.ClientName.Trim();
        var merchant = await crmDbContext.Merchants
            .FirstOrDefaultAsync(value =>
                !value.IsDeleted &&
                value.BusinessType == "Other" &&
                value.BusinessName == buyerName,
                cancellationToken);

        if (merchant is null)
        {
            merchant = new Merchant
            {
                Id = Guid.NewGuid(),
                BusinessName = buyerName,
                ContactPersonName = buyerName,
                PhoneNumbers = [],
                BusinessType = "Other",
                Status = "Active",
                Notes = "Auto-created from anonymous cash sale.",
                CreatedAt = now,
                UpdatedAt = now
            };
            crmDbContext.Merchants.Add(merchant);
        }

        operation.ClientId = merchant.Id;
        operation.ClientName = merchant.BusinessName;
    }

    private static bool ShouldSetConfirmedActorAfterRevision(string status) =>
        status is not Draft and not Cancelled;

    private static async Task ReverseOperationEffectsAsync(
        OperationLog operation,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        InventoryDbContext inventoryDbContext,
        PaymentsDbContext paymentsDbContext,
        StockLedgerService ledgerService,
        Guid userId,
        CancellationToken cancellationToken)
    {
        switch (operation.Status)
        {
            case Draft:
            case Cancelled:
                return;
            case Reserved:
                await ReverseReservedOperationAsync(operation, ledgerService, userId, cancellationToken);
                break;
            case Shipped:
                await ReverseShippedOperationAsync(operation, allocations, ledgerService, userId, cancellationToken);
                break;
            case Received:
                await ReverseReceivedOperationAsync(operation, allocations, inventoryDbContext, ledgerService, userId, cancellationToken);
                break;
            case Completed:
                await ReverseCompletedSaleAsync(operation, allocations, paymentsDbContext, inventoryDbContext, ledgerService, userId, cancellationToken);
                break;
            case Confirmed:
                await ReverseConfirmedOperationAsync(operation, allocations, inventoryDbContext, ledgerService, userId, cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Revision is not supported for status {operation.Status}.");
        }
    }

    private static async Task ReverseReservedOperationAsync(
        OperationLog operation,
        StockLedgerService ledgerService,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (operation.OperationType is WarehouseTransfer or WholesaleSale or RetailSale or Reserve)
        {
            foreach (var group in operation.OperationLines.Where(line => line.EntryMode != "Pieces").GroupBy(line => line.SkuId))
            {
                await ledgerService.ReleaseInWarehouseUpToAsync(operation.SourceLocationId!.Value, group.Key, group.Sum(line => line.Quantity), userId, operation.Id, cancellationToken);
            }
        }
    }

    private static async Task ReverseShippedOperationAsync(
        OperationLog operation,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        StockLedgerService ledgerService,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (operation.OperationType == WarehouseTransfer)
        {
            await RestoreBatchAllocationsAsync(operation.SourceLocationId!.Value, allocations, ledgerService, userId, operation.Id, cancellationToken);
            return;
        }

        if (operation.OperationType == Reserve)
        {
            foreach (var allocation in allocations)
            {
                await ledgerService.ReleaseWithRepUpToAsync(operation.SourceLocationId!.Value, allocation.SkuId, allocation.Allocations.Sum(batch => batch.Quantity), userId, operation.Id, cancellationToken);
                await RestoreBatchAllocationsAsync(operation.SourceLocationId!.Value, [allocation], ledgerService, userId, operation.Id, cancellationToken);
            }
            return;
        }

        if (operation.OperationType is WholesaleSale or RetailSale)
        {
            await RestoreBatchAllocationsAsync(operation.SourceLocationId!.Value, allocations, ledgerService, userId, operation.Id, cancellationToken);
        }
    }

    private static async Task ReverseReceivedOperationAsync(
        OperationLog operation,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        InventoryDbContext inventoryDbContext,
        StockLedgerService ledgerService,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (operation.OperationType != WarehouseTransfer)
        {
            throw new InvalidOperationException("Only received warehouse transfers can be reversed from received status.");
        }

        await RemoveBatchAllocationsAsync(operation.DestinationLocationId!.Value, allocations, ledgerService, userId, operation.Id, cancellationToken);
        await RestoreBatchAllocationsAsync(operation.SourceLocationId!.Value, allocations, ledgerService, userId, operation.Id, cancellationToken);
    }

    private static async Task ReverseCompletedSaleAsync(
        OperationLog operation,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        PaymentsDbContext paymentsDbContext,
        InventoryDbContext inventoryDbContext,
        StockLedgerService ledgerService,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await PaymentsEndpoints.RemovePaymentArtifactsForSaleRevisionAsync(operation.Id, paymentsDbContext, cancellationToken);
        await ReverseShippedOperationAsync(operation, allocations, ledgerService, userId, cancellationToken);
    }

    private static async Task ReverseConfirmedOperationAsync(
        OperationLog operation,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        InventoryDbContext inventoryDbContext,
        StockLedgerService ledgerService,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (operation.OperationType == InventoryReceipt)
        {
            await RemoveLineBatchesAsync(operation.SourceLocationId ?? operation.DestinationLocationId!.Value, operation.OperationLines, ledgerService, userId, operation.Id, cancellationToken);
            return;
        }

        if (operation.OperationType == Return)
        {
            await RemoveLineBatchesAsync(operation.SourceLocationId!.Value, operation.OperationLines, ledgerService, userId, operation.Id, cancellationToken);
            return;
        }

        if (operation.OperationType == Change)
        {
            await RemoveLineBatchesAsync(operation.SourceLocationId!.Value, operation.OperationLines.Where(line => line.Section == ChangeOut), ledgerService, userId, operation.Id, cancellationToken);
            await RestoreBatchAllocationsAsync(operation.SourceLocationId!.Value, allocations, ledgerService, userId, operation.Id, cancellationToken);
            return;
        }

        if (operation.OperationType == WriteOff)
        {
            await RestoreBatchAllocationsAsync(operation.SourceLocationId!.Value, allocations, ledgerService, userId, operation.Id, cancellationToken);
            return;
        }

        if (operation.OperationType == Reserve)
        {
            foreach (var group in operation.OperationLines.GroupBy(line => line.SkuId))
            {
                await ledgerService.ReleaseWithRepUpToAsync(operation.SourceLocationId!.Value, group.Key, group.Sum(line => line.Quantity), userId, operation.Id, cancellationToken);
            }
        }
    }

    private static async Task<IReadOnlyList<TransferAllocationSnapshot>> ReapplyOperationToStatusAsync(
        OperationLog operation,
        string originalStatus,
        OperationsDbContext operationsDbContext,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        CrmDbContext crmDbContext,
        PaymentsDbContext paymentsDbContext,
        StockLedgerService ledgerService,
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var allocations = new List<TransferAllocationSnapshot>();

        if (originalStatus == Draft)
        {
            operation.Status = Draft;
            return allocations;
        }

        if (operation.OperationType == InventoryReceipt)
        {
            foreach (var line in operation.OperationLines)
            {
                await ledgerService.ReceiveAsync(
                    operation.DestinationLocationId!.Value,
                    line.SkuId,
                    line.Quantity,
                    userId,
                    line.LotNumber,
                    line.ExpiryDate,
                    line.LineNotes,
                    operation.Id,
                    cancellationToken);
            }
            operation.Status = Received;
            operation.ConfirmedAt = now;
            operation.ConfirmedBy = userId;
            return allocations;
        }

        if (operation.OperationType == WarehouseTransfer)
        {
            foreach (var line in operation.OperationLines)
            {
                var lineAllocation = await ReserveSelectedOrFefoAsync(operation.SourceLocationId!.Value, line, ledgerService, userId, operation.Id, cancellationToken);
                allocations.Add(new TransferAllocationSnapshot(line.SkuId, [lineAllocation]));
            }
            operation.Status = Reserved;
            operation.ConfirmedAt = now;
            operation.ConfirmedBy = userId;
            return allocations;
        }

        if (operation.OperationType is WholesaleSale or RetailSale)
        {
            allocations.AddRange(await BuildSelectedSalePackAllocationsAsync(operation, inventoryDbContext, operationsDbContext, new ClockStub(now), cancellationToken));
            foreach (var allocation in allocations)
            {
                await ledgerService.ReserveSelectedBatchInWarehouseAsync(operation.SourceLocationId!.Value, allocation.SkuId, allocation.Allocations.Sum(batch => batch.Quantity), allocation.Allocations[0].LotNumber, allocation.Allocations[0].ExpiryDate, userId, operation.Id, cancellationToken);
            }
            operation.Status = Reserved;
            operation.ConfirmedAt = now;
            operation.ConfirmedBy = userId;
            if (originalStatus is Shipped or Completed)
            {
                await ShipSaleOutAsync(operation, allocations, catalogDbContext, ledgerService, userId, now, cancellationToken);
                operation.Status = Shipped;
            }
            if (originalStatus == Completed)
            {
                operation.Status = Completed;
                await PaymentsEndpoints.CreatePaymentArtifactsForCompletedSaleAsync(operation, paymentsDbContext, userId, now, cancellationToken);
            }
            return allocations;
        }

        if (operation.OperationType == Return)
        {
            foreach (var line in operation.OperationLines)
            {
                await ledgerService.ReceiveReturnAsync(operation.SourceLocationId!.Value, line.SkuId, line.Quantity, userId, line.LotNumber, line.ExpiryDate, line.LineNotes, operation.Id, cancellationToken);
            }
            operation.Status = Confirmed;
            operation.ConfirmedAt = now;
            operation.ConfirmedBy = userId;
            return allocations;
        }

        if (operation.OperationType == Change)
        {
            foreach (var line in operation.OperationLines.Where(line => line.Section == ChangeOut))
            {
                await ledgerService.ReceiveChangeOutAsync(operation.SourceLocationId!.Value, line.SkuId, line.Quantity, userId, line.LotNumber, line.ExpiryDate, line.LineNotes, operation.Id, cancellationToken);
            }
            foreach (var line in operation.OperationLines.Where(line => line.Section == ChangeIn))
            {
                var lineAllocation = await IssueSelectedOrFefoAsync(operation.SourceLocationId!.Value, line, InventoryTransactionTypes.ChangeIn, ledgerService, userId, operation.Id, cancellationToken);
                allocations.Add(new TransferAllocationSnapshot(line.SkuId, [lineAllocation]));
            }
            operation.Status = Confirmed;
            operation.ConfirmedAt = now;
            operation.ConfirmedBy = userId;
            return allocations;
        }

        if (operation.OperationType == WriteOff)
        {
            foreach (var line in operation.OperationLines)
            {
                var lineAllocation = await IssueSelectedOrFefoAsync(operation.SourceLocationId!.Value, line, InventoryTransactionTypes.WriteOff, ledgerService, userId, operation.Id, cancellationToken);
                allocations.Add(new TransferAllocationSnapshot(line.SkuId, [lineAllocation]));
            }
            operation.Status = Confirmed;
            operation.ConfirmedAt = now;
            operation.ConfirmedBy = userId;
            return allocations;
        }

        if (operation.OperationType == Reserve)
        {
            foreach (var group in operation.OperationLines.GroupBy(line => line.SkuId))
            {
                foreach (var line in group)
                {
                    var lineAllocation = await ReserveSelectedOrFefoAsync(operation.SourceLocationId!.Value, line, ledgerService, userId, operation.Id, cancellationToken);
                    allocations.Add(new TransferAllocationSnapshot(group.Key, [lineAllocation]));
                }
            }
            operation.Status = Reserved;
            operation.ConfirmedAt = now;
            operation.ConfirmedBy = userId;
            if (originalStatus is Shipped or Confirmed)
            {
                await ShipRepresentativeReserveAsync(operation, allocations, ledgerService, userId, cancellationToken);
                operation.Status = Shipped;
            }
            if (originalStatus == Confirmed)
            {
                operation.Status = Confirmed;
            }
            return allocations;
        }

        throw new InvalidOperationException($"Reapply is not supported for operation type {operation.OperationType}.");
    }

    private static async Task<BatchAllocation> ReserveSelectedOrFefoAsync(
        Guid locationId,
        OperationLine line,
        StockLedgerService ledgerService,
        Guid userId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (line.ExpiryDate is not null)
        {
            return await ledgerService.ReserveSelectedBatchInWarehouseAsync(
                locationId,
                line.SkuId,
                line.Quantity,
                line.LotNumber,
                line.ExpiryDate,
                userId,
                operationId,
                cancellationToken);
        }

        var allocations = await ledgerService.ReserveInWarehouseFefoAsync(locationId, line.SkuId, line.Quantity, userId, operationId, null, cancellationToken);
        return allocations.Single();
    }

    private static async Task<BatchAllocation> IssueSelectedOrFefoAsync(
        Guid locationId,
        OperationLine line,
        string transactionType,
        StockLedgerService ledgerService,
        Guid userId,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        if (line.ExpiryDate is not null)
        {
            return await ledgerService.IssueSelectedBatchAsync(
                locationId,
                line.SkuId,
                line.Quantity,
                transactionType,
                line.LotNumber,
                line.ExpiryDate,
                userId,
                operationId,
                cancellationToken);
        }

        var allocations = await ledgerService.IssueFefoAsync(locationId, line.SkuId, line.Quantity, transactionType, userId, operationId, null, cancellationToken);
        return allocations.Single();
    }

    private static async Task RestoreBatchAllocationsAsync(
        Guid locationId,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        StockLedgerService ledgerService,
        Guid userId,
        Guid referenceOperationId,
        CancellationToken cancellationToken)
    {
        foreach (var allocation in allocations)
        {
            foreach (var batch in allocation.Allocations)
            {
                await ledgerService.AdjustStocktakeBatchAsync(locationId, allocation.SkuId, batch.LotNumber, batch.ExpiryDate, batch.Quantity, userId, referenceOperationId, cancellationToken);
            }
        }
    }

    private static async Task RemoveBatchAllocationsAsync(
        Guid locationId,
        IReadOnlyList<TransferAllocationSnapshot> allocations,
        StockLedgerService ledgerService,
        Guid userId,
        Guid referenceOperationId,
        CancellationToken cancellationToken)
    {
        foreach (var allocation in allocations)
        {
            foreach (var batch in allocation.Allocations)
            {
                await ledgerService.AdjustStocktakeBatchAsync(locationId, allocation.SkuId, batch.LotNumber, batch.ExpiryDate, -batch.Quantity, userId, referenceOperationId, cancellationToken);
            }
        }
    }

    private static async Task RemoveLineBatchesAsync(
        Guid locationId,
        IEnumerable<OperationLine> lines,
        StockLedgerService ledgerService,
        Guid userId,
        Guid referenceOperationId,
        CancellationToken cancellationToken)
    {
        foreach (var line in lines)
        {
            await ledgerService.AdjustStocktakeBatchAsync(locationId, line.SkuId, line.LotNumber, line.ExpiryDate, -line.Quantity, userId, referenceOperationId, cancellationToken);
        }
    }

    private static async Task ExecuteInventoryOperationTransactionAsync(
        InventoryDbContext inventoryDbContext,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        await SharedDbTransaction.ExecuteAsync(inventoryDbContext, action, cancellationToken, operationsDbContext, identityDbContext);
    }

    private static async Task ExecuteInventoryOperationTransactionAsync(
        InventoryDbContext inventoryDbContext,
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        CrmDbContext crmDbContext,
        IdentityDbContext identityDbContext,
        Func<Task> action,
        CancellationToken cancellationToken)
    {
        await SharedDbTransaction.ExecuteAsync(inventoryDbContext, action, cancellationToken, operationsDbContext, paymentsDbContext, crmDbContext, identityDbContext);
    }

    private sealed record DraftValidationResult(Dictionary<string, string[]> Errors, Location? SourceLocation, Location? DestinationLocation, Dictionary<Guid, Sku> SkusById, Merchant? Merchant, Representative? Representative);

    private sealed record ReplenishmentLineDraft(Guid SkuId, string? SkuCode, string? ProductName, int PackQuantity);

    private sealed record ReplenishmentSkuLookup(string SkuCode, string ProductName, int? PiecesPerPack);

    private sealed record MerchantReturnKey(Guid SkuId, string? LotNumber, DateOnly? ExpiryDate);

    private sealed class MerchantSalesVarianceException : Exception
    {
        public MerchantSalesVarianceException(IReadOnlyList<MerchantSalesVarianceWarning> warnings)
            : base("Merchant return quantities changed while the operation was being confirmed.")
        {
            Warnings = warnings;
        }

        public IReadOnlyList<MerchantSalesVarianceWarning> Warnings { get; }
    }

    private sealed record OperationSnapshot(
        string OperationType,
        string Status,
        Guid? SourceLocationId,
        Guid? DestinationLocationId,
        Guid? ClientId,
        string? ClientName,
        Guid? RepresentativeId,
        string? PaymentMethod,
        string? Notes,
        IReadOnlyList<OperationLineSnapshot> Lines,
        IReadOnlyList<TransferAllocationSnapshot> TransferAllocations);

    private sealed record OperationLineSnapshot(Guid SkuId, string SkuCode, string ProductName, string Section, int Quantity, string EntryMode, int BonusQuantity, decimal UnitPrice, decimal LineTotal, string? LotNumber, DateOnly? ExpiryDate, string? Notes, string? ShopifyLineItemId, string? ShopifyVariantId, string? ShopifySku, string? ShopifyTitle, string? ShopifyVariantTitle, string? ShopifyProperties);

    private sealed record WearCycleInfo(string? Cycle, string? Duration);

    private sealed record WearCycleLookup(Guid SkuId, string ProductType, string? OpenedExpiryRate, string? OpenedExpiryDuration);

    private sealed record TransferAllocationSnapshot(Guid SkuId, IReadOnlyList<BatchAllocation> Allocations);

    private sealed class ClockStub : IClock
    {
        private readonly DateTime _egyptNow;

        public ClockStub(DateTime egyptNow)
        {
            _egyptNow = egyptNow;
        }

        public DateTime UtcNow => _egyptNow.ToUniversalTime();

        public DateTime EgyptNow => _egyptNow;
    }
}

public sealed record OperationRequest(
    string OperationType,
    Guid? SourceLocationId,
    Guid? DestinationLocationId,
    Guid? MerchantId,
    Guid? RepresentativeId,
    string? BuyerName,
    string? BuyerPhone,
    string? PaymentMethod,
    string? Notes,
    ReceiptRequest? Receipt,
    IReadOnlyList<OperationLineRequest> Lines,
    uint? ExpectedVersion = null);

public sealed record OperationRevisionRequest(OperationRequest Operation, string Reason);

public sealed record OperationConfirmationRequest(bool? AcknowledgeSalesVariance, string? SalesVarianceReason);

public sealed record MerchantSalesVarianceGateResponse(
    string Code,
    string Title,
    string Detail,
    bool CanBypass,
    bool ReasonRequired,
    IReadOnlyList<MerchantSalesVarianceWarning> Warnings);

public sealed record MerchantSalesVarianceWarning(
    Guid SkuId,
    string SkuCode,
    string ProductName,
    string? LotNumber,
    DateOnly? ExpiryDate,
    int RequestedQuantity,
    int SoldQuantity,
    int ReturnedQuantity,
    int ExcessQuantity,
    string Message);

public sealed record ReceiptRequest(string? SupplierName, string? InvoiceNumber);

public sealed record ReceiptResponse(string? SupplierName, string? InvoiceNumber);

public sealed record OperationLineRequest(Guid SkuId, int PackQuantity, int? PieceQuantity, string? EntryMode, string? Section, decimal? UnitPrice, bool? IsBonus, string? LotNumber, DateOnly? ExpiryDate, string? Notes);

public sealed record OperationListResponse(
    Guid Id,
    string OperationNumber,
    string OperationType,
    string Status,
    Guid? SourceLocationId,
    string? SourceLocationName,
    Guid? DestinationLocationId,
    string? DestinationLocationName,
    Guid? ClientId,
    string? ClientName,
    Guid? RepresentativeId,
    string? PaymentMethod,
    DateTime CreatedAt,
    DateTime? ConfirmedAt,
    string? CreatedByName,
    string? ConfirmedByName,
    string? LastEditedByName,
    bool CanEditDraft,
    bool CanRevise,
    string? RevisionBlockReason,
    string SalesChannel,
    string? BuyerPhone,
    string? BuyerEmail,
    string? ShippingAddress,
    string? ShopifyOrderId,
    string? ShopifyOrderNumber,
    bool AllocationPending);

public sealed record OperationDetailResponse(
    Guid Id,
    string OperationNumber,
    string OperationType,
    string Status,
    Guid? SourceLocationId,
    string? SourceLocationName,
    Guid? DestinationLocationId,
    string? DestinationLocationName,
    Guid? ClientId,
    string? ClientName,
    Guid? RepresentativeId,
    string? RepresentativeName,
    string? PaymentMethod,
    string? Notes,
    DateTime CreatedAt,
    DateTime? ConfirmedAt,
    string? CreatedByName,
    string? ConfirmedByName,
    string? LastEditedByName,
    int? CurrentVersionNumber,
    uint ConcurrencyVersion,
    bool CanEditDraft,
    bool CanRevise,
    string? RevisionBlockReason,
    ReceiptResponse? Receipt,
    IReadOnlyList<OperationLineResponse> Lines,
    IReadOnlyList<OperationAllocationResponse> Allocations,
    IReadOnlyList<OperationVersionResponse> Versions,
    string SalesChannel,
    string? BuyerPhone,
    string? BuyerEmail,
    string? ShippingAddress,
    string? ShopifyOrderId,
    string? ShopifyOrderNumber,
    bool AllocationPending);

public sealed record OperationLineResponse(Guid Id, Guid SkuId, string SkuCode, string ProductName, string Section, int Quantity, string EntryMode, int BonusQuantity, decimal UnitPrice, decimal LineTotal, string? LotNumber, DateOnly? ExpiryDate, string? MerchantNameSnapshot, string? RepresentativeNameSnapshot, string? Notes, string? WearCycle, string? WearDuration, string? ShopifyLineItemId, string? ShopifyVariantId, string? ShopifySku, string? ShopifyTitle, string? ShopifyVariantTitle, string? ShopifyProperties);
public sealed record ShopifyAllocationRequest(IReadOnlyList<ShopifyAllocationLineRequest>? Lines, uint? ExpectedVersion = null);
public sealed record ShopifyAllocationLineRequest(Guid OperationLineId, string? LotNumber, DateOnly? ExpiryDate);

public sealed record OperationAllocationResponse(Guid SkuId, string? SkuCode, string? ProductName, Guid BatchId, int Quantity, string? LotNumber, DateOnly? ExpiryDate);

public sealed record OperationVersionResponse(Guid Id, int VersionNumber, string Reason, DateTime EditedAt, string? EditedByName, bool IsCurrent, bool ChangedRoute, bool ChangedParty, bool ChangedFinancial, bool ChangedLines);

public sealed record ReplenishmentReserveRequest(Guid? LocationId, Guid? SkuId);

public sealed record ReplenishmentRowResponse(
    Guid DestinationLocationId,
    string DestinationLocationName,
    string DestinationLocationType,
    Guid SkuId,
    string? SkuCode,
    string? ProductName,
    int? PiecesPerPack,
    int AvailablePacks,
    int? AvailablePieces,
    int IncomingPacks,
    int? IncomingPieces,
    int TargetPacks,
    int? TargetPieces,
    int ShortagePacks,
    int? ShortagePieces,
    int MainAvailablePacks);

public sealed record ReplenishmentReserveResponse(
    int CreatedOperations,
    int UnfilledPacks,
    IReadOnlyList<ReplenishmentOperationResponse> Operations,
    IReadOnlyList<ReplenishmentAlertResponse> Alerts);

public sealed record ReplenishmentOperationResponse(Guid Id, string OperationNumber, Guid DestinationLocationId, int ReservedPacks);

public sealed record ReplenishmentAlertResponse(
    Guid DestinationLocationId,
    string DestinationLocationName,
    Guid SkuId,
    string? SkuCode,
    string? ProductName,
    int ShortagePacks,
    int MainAvailablePacks,
    string Message);
