using Lensee.Host.Infrastructure;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.Modules.Reporting.Data;
using Lensee.Modules.Reporting.Application;
using Lensee.Modules.Reporting.Application.Abstractions;
using Lensee.Modules.Reporting.Application.Models;
using Lensee.Modules.Reporting.Infrastructure;
using Lensee.Modules.Reporting.Configuration;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Primitives;
using Lensee.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lensee.Host.Endpoints;

public static partial class ReportsEndpoints
{
    private const string Completed = "Completed";
    private const string Change = "Change";
    private const string ChangeOut = "ChangeOut";
    private const string ChangeIn = "ChangeIn";
    private const string CashReceived = "CashReceived";
    private static readonly HashSet<string> OperationTypes = new(StringComparer.Ordinal)
    {
        "InventoryReceipt", "WarehouseTransfer", "WholesaleSale", "RetailSale", "Reserve", "WriteOff", "StocktakeAdjustment", "Change", "Return"
    };
    private static readonly HashSet<string> SupplyStatuses = new(StringComparer.Ordinal)
    {
        "Draft", "Received", "Cancelled"
    };

    private static readonly HashSet<string> ExportReportTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "stock.csv",
        "operations.csv",
        "payments.csv",
        "merchant-balances.csv",
        "operation-bill",
        "operation-bill.pdf",
        "payment-receipt",
        "payment-receipt.pdf",
        "cash-receipt",
        "cash-receipt.pdf",
        "cash-receive-receipt.pdf",
        "supply-landed-cost",
        "supply-landed-cost.csv",
        "supply-landed-cost.pdf",
        "merchant-statement",
        "merchant-statement.pdf",
        "stocktake-summary",
        "stocktake-summary.pdf"
    };

    public static RouteGroupBuilder MapReportsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/reports").WithTags("Reports");

        group.MapGet("/financial-summary", GetFinancialSummaryAsync).RequireAuthorization("reports.read");
        group.MapGet("/catalog", GetReportCatalog).RequireAuthorization("reports.read");
        group.MapGet("/{key}/export", ExportReportAsync).RequireAuthorization("reports.read");
        group.MapGet("/stock", GetStockReportAsync).RequireAuthorization("reports.read");
        group.MapGet("/stock.csv", GetStockCsvAsync).RequireAuthorization("reports.read");
        group.MapGet("/operations", GetOperationsReportAsync).RequireAuthorization("reports.read");
        group.MapGet("/operations.csv", GetOperationsCsvAsync).RequireAuthorization("reports.read");
        group.MapGet("/operations/{id:guid}/bill.pdf", GetOperationBillPdfAsync).RequireAuthorization("reports.read");
        group.MapGet("/payments", GetPaymentsReportAsync).RequireAuthorization("reports.read");
        group.MapGet("/payments.csv", GetPaymentsCsvAsync).RequireAuthorization("reports.read");
        group.MapGet("/payments/{id:guid}/receipt.pdf", GetPaymentReceiptPdfAsync).RequireAuthorization("reports.read");
        group.MapGet("/payments/{id:guid}/cash-receipt.pdf", GetPaymentReceiptPdfAsync).RequireAuthorization("reports.read");
        group.MapGet("/supply", GetSupplyLandedCostReportAsync).RequireAuthorization("reports.read");
        group.MapGet("/supply.csv", GetSupplyLandedCostCsvAsync).RequireAuthorization("reports.read");
        group.MapGet("/supply/{id:guid}/landed-cost.pdf", GetSupplyLandedCostPdfAsync).RequireAuthorization("reports.read");
        group.MapGet("/merchant-balances", GetMerchantBalancesReportAsync).RequireAuthorization("reports.read");
        group.MapGet("/merchant-balances.csv", GetMerchantBalancesCsvAsync).RequireAuthorization("reports.read");
        group.MapGet("/merchants/{merchantId:guid}/statement.pdf", GetMerchantStatementPdfAsync).RequireAuthorization("reports.read");
        group.MapGet("/stocktakes/{id:guid}/summary.pdf", GetStocktakeSummaryPdfAsync).RequireAuthorization("reports.read");
        group.MapGet("/exports", ListExportLogsAsync).RequireAuthorization("reports.read");
        group.MapPost("/exports", CreateExportLogAsync).RequireAuthorization("reports.read");

        routes.MapGet("/api/v1/documents/{key}/{id:guid}", ExportDocumentAsync).RequireAuthorization("reports.read");

        return group;
    }

    private static IResult GetReportCatalog(IReportCatalog catalog) => Results.Ok(catalog.All.Select(descriptor => new
    {
        descriptor.Key,
        template = descriptor.Template.ToString().ToLowerInvariant(),
        formats = descriptor.Formats.Select(FormatValue).OrderBy(value => value).ToArray(),
        languages = descriptor.Languages.Select(LanguageValue).OrderBy(value => value).ToArray(),
        descriptor.Filters,
        descriptor.AuthorizationPolicy,
        descriptor.LocationScoped,
        descriptor.IsDocument
    }));

    private static async Task<IResult> ExportReportAsync(
        string key,
        string? format,
        string? language,
        Guid? locationId,
        DateTime? from,
        DateTime? to,
        string? operationType,
        string? status,
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        CrmDbContext crmDbContext,
        ReportingDbContext reportingDbContext,
        MerchantAccountService merchantAccountService,
        IReportCatalog reportCatalog,
        IDocumentExportService exportService,
        IOptions<ReportingOptions> reportingOptions,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!reportCatalog.TryGet(key, out var descriptor) || descriptor.IsDocument)
        {
            return Results.NotFound();
        }
        if (!ReportingServiceCollectionExtensions.TryParseFormat(format, out var exportFormat) || !descriptor.Formats.Contains(exportFormat))
        {
            return InvalidExportFormat();
        }
        if (!ReportingServiceCollectionExtensions.TryParseLanguage(language, out var documentLanguage) || !descriptor.Languages.Contains(documentLanguage))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["language"] = ["Language must be ar, en, or bi."] });
        }
        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = ["From must be earlier than or equal to To."] });
        }

        IResult queryResult = key.ToLowerInvariant() switch
        {
            "financial-summary" => await GetFinancialSummaryAsync(operationsDbContext, paymentsDbContext, cancellationToken),
            "stock" => await GetStockReportAsync(locationId, inventoryDbContext, catalogDbContext, currentUser, reportingOptions, cancellationToken),
            "operations" => await GetOperationsReportAsync(from, to, operationType, operationsDbContext, reportingOptions, cancellationToken),
            "payments" => await GetPaymentsReportAsync(operationsDbContext, paymentsDbContext, reportingOptions, cancellationToken),
            "supply" => await GetSupplyLandedCostReportAsync(from, to, status, operationsDbContext, reportingOptions, cancellationToken),
            "merchant-balances" => await GetMerchantBalancesReportAsync(crmDbContext, merchantAccountService, reportingOptions, cancellationToken),
            _ => Results.NotFound()
        };

        if (queryResult is IStatusCodeHttpResult { StatusCode: >= 400 })
        {
            return queryResult;
        }
        if (queryResult is not IValueHttpResult valueResult || valueResult.Value is null)
        {
            return queryResult;
        }

        var document = BuildAnalyticalDocument(key, valueResult.Value, documentLanguage, currentUser.Role, clock.UtcNow);
        if (document.Metadata.RowCount > reportingOptions.Value.MaxExportRows)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["rows"] = [$"This export contains more than {reportingOptions.Value.MaxExportRows} rows. Narrow the selected filters."]
            });
        }

        var exported = await exportService.ExportAsync(document, exportFormat, cancellationToken);
        var reportType = $"{key}.{FormatValue(exportFormat)}";
        await LogExportAsync(reportingDbContext, currentUser, clock, reportType, $"download://reports/{key}/export", cancellationToken);
        return Results.File(exported.Content, exported.ContentType, exported.FileName);
    }

    private static async Task<IResult> ExportDocumentAsync(
        string key,
        Guid id,
        string? format,
        string? language,
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        CrmDbContext crmDbContext,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        IdentityDbContext identityDbContext,
        ReportingDbContext reportingDbContext,
        MerchantAccountService merchantAccountService,
        IReportCatalog reportCatalog,
        IDocumentExportService exportService,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (!reportCatalog.TryGet(key, out var descriptor) || !descriptor.IsDocument)
        {
            return Results.NotFound();
        }
        if (!ReportingServiceCollectionExtensions.TryParseFormat(format, out var requestedFormat) || !descriptor.Formats.Contains(requestedFormat))
        {
            return InvalidExportFormat();
        }
        if (!ReportingServiceCollectionExtensions.TryParseLanguage(language, out var requestedLanguage) || !descriptor.Languages.Contains(requestedLanguage))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["language"] = ["Language must be ar, en, or bi."] });
        }

        return key.ToLowerInvariant() switch
        {
            "operation-bill" => await GetOperationBillPdfAsync(id, language, format, operationsDbContext, paymentsDbContext, crmDbContext, inventoryDbContext, identityDbContext, reportingDbContext, merchantAccountService, currentUser, clock, exportService, cancellationToken),
            "payment-receipt" or "cash-receipt" => await GetPaymentReceiptPdfAsync(id, language, format, paymentsDbContext, operationsDbContext, crmDbContext, identityDbContext, reportingDbContext, merchantAccountService, currentUser, clock, exportService, cancellationToken),
            "supply-landed-cost" => await GetSupplyLandedCostPdfAsync(id, language, format, operationsDbContext, identityDbContext, reportingDbContext, currentUser, clock, exportService, cancellationToken),
            "merchant-statement" => await GetMerchantStatementPdfAsync(id, language, format, null, null, crmDbContext, operationsDbContext, paymentsDbContext, identityDbContext, reportingDbContext, merchantAccountService, currentUser, clock, exportService, cancellationToken),
            "stocktake-summary" => await GetStocktakeSummaryPdfAsync(id, language, format, operationsDbContext, catalogDbContext, inventoryDbContext, identityDbContext, reportingDbContext, currentUser, clock, exportService, cancellationToken),
            _ => Results.NotFound()
        };
    }

    private static async Task<IResult> GetFinancialSummaryAsync(
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        CancellationToken cancellationToken)
    {
        var operationEffects = await operationsDbContext.OperationLogs.AsNoTracking()
            .Where(operation => !operation.IsDeleted &&
                ((operation.OperationType == "WholesaleSale" || operation.OperationType == "RetailSale") && operation.Status == Completed ||
                 (operation.OperationType == "Return" || operation.OperationType == "Change") && operation.Status == "Confirmed"))
            .Select(operation => new
            {
                operation.Id,
                Effect = (operation.RecordKind == "Reversal" ? -1m : 1m) *
                    (operation.OperationType == "Return"
                        ? -(operation.OperationLines.Sum(line => (decimal?)line.LineTotal) ?? 0m)
                        : operation.OperationType == "Change"
                            ? (operation.OperationLines.Where(line => line.Section == "ChangeIn").Sum(line => (decimal?)line.LineTotal) ?? 0m)
                                - (operation.OperationLines.Where(line => line.Section == "ChangeOut").Sum(line => (decimal?)line.LineTotal) ?? 0m)
                            : operation.OperationLines.Sum(line => (decimal?)line.LineTotal) ?? 0m)
            })
            .ToListAsync(cancellationToken);
        var effectiveOperationIds = operationEffects.Where(value => value.Effect != 0m).Select(value => value.Id).ToArray();
        var operationNet = operationEffects.Sum(value => value.Effect);

        var confirmedSubLogs = await paymentsDbContext.InstallmentSubLogs.AsNoTracking()
            .Where(value => !value.MainLog.IsDeleted && value.SubLogStatus == "Confirmed" && effectiveOperationIds.Contains(value.MainLog.OperationId))
            .SumAsync(value => (decimal?)value.Amount, cancellationToken) ?? 0m;
        var cashTotals = await paymentsDbContext.CashRecords.AsNoTracking()
            .Where(value => value.Status == Completed && effectiveOperationIds.Contains(value.OperationId))
            .GroupBy(value => value.PaymentType)
            .Select(group => new { PaymentType = group.Key, Amount = group.Sum(value => value.Amount) })
            .ToDictionaryAsync(value => value.PaymentType, value => value.Amount, cancellationToken);
        var adjustmentTotals = await paymentsDbContext.FinancialAdjustments.AsNoTracking()
            .Where(value => value.Status == Completed)
            .GroupBy(value => value.AdjustmentType)
            .Select(group => new { AdjustmentType = group.Key, Amount = group.Sum(value => value.Amount) })
            .ToDictionaryAsync(value => value.AdjustmentType, value => value.Amount, cancellationToken);

        var cashReceived = cashTotals.GetValueOrDefault("CashReceived");
        var cashRefunded = cashTotals.GetValueOrDefault("CashRefund");
        var additionalCharges = adjustmentTotals.GetValueOrDefault("AdditionalCharge") + adjustmentTotals.GetValueOrDefault("MerchantCredit");
        var balanceReductions = adjustmentTotals.GetValueOrDefault("BalanceReduction");
        var paymentsNet = confirmedSubLogs + cashReceived - cashRefunded;
        var balance = operationNet + additionalCharges - confirmedSubLogs - cashReceived + cashRefunded - balanceReductions;

        return Results.Ok(new FinancialSummaryResponse(operationNet, paymentsNet, balance));
    }
    private static async Task<IResult> GetStockReportAsync(
        Guid? locationId,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        ICurrentUser currentUser,
        IOptions<ReportingOptions> reportingOptions,
        CancellationToken cancellationToken)
    {
        if (string.Equals(currentUser.Role, LenseeRoles.Accountant, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Forbid();
        }

        if (string.Equals(currentUser.Role, LenseeRoles.WarehouseClerk, StringComparison.OrdinalIgnoreCase) &&
            (!currentUser.LocationId.HasValue || (locationId.HasValue && locationId != currentUser.LocationId)))
        {
            return Results.Forbid();
        }

        var effectiveLocationId = string.Equals(currentUser.Role, LenseeRoles.WarehouseClerk, StringComparison.OrdinalIgnoreCase)
            ? currentUser.LocationId
            : locationId;
        var query = inventoryDbContext.StockBalances
            .Include(balance => balance.Location)
            .AsNoTracking()
            .AsQueryable();

        if (effectiveLocationId.HasValue)
        {
            query = query.Where(balance => balance.LocationId == effectiveLocationId.Value);
        }

        var balances = await query
            .OrderBy(balance => balance.Location.Name)
            .ThenBy(balance => balance.SkuId)
            .Take(reportingOptions.Value.MaxExportRows + 1)
            .ToListAsync(cancellationToken);

        var skuIds = balances.Select(balance => balance.SkuId).Distinct().ToArray();
        var skus = await catalogDbContext.Skus
            .Include(sku => sku.Product)
            .Where(sku => skuIds.Contains(sku.Id))
            .ToDictionaryAsync(sku => sku.Id, cancellationToken);

        var rows = balances.Select(balance =>
        {
            skus.TryGetValue(balance.SkuId, out var sku);
            return new StockReportRow(
                balance.LocationId,
                balance.Location.Name,
                balance.Location.LocationType,
                balance.SkuId,
                sku?.SkuCode,
                sku?.Product.Name,
                balance.AvailableQty,
                balance.ReservedInWarehouseQty,
                balance.ReservedWithRepQty,
                balance.TargetQty,
                balance.LastUpdated);
        }).ToList();

        return Results.Ok(rows);
    }

    private static async Task<IResult> GetStockCsvAsync(
        Guid? locationId,
        string? language,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        ReportingDbContext reportingDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IDocumentExportService exportService,
        IOptions<ReportingOptions> reportingOptions,
        CancellationToken cancellationToken)
    {
        var result = await GetStockReportAsync(locationId, inventoryDbContext, catalogDbContext, currentUser, reportingOptions, cancellationToken);
        if (result is IValueHttpResult { Value: IEnumerable<StockReportRow> rows })
        {
            return await ExportLegacyCsvAsync("stock", rows.ToList(), language, reportingDbContext, currentUser, clock, exportService, reportingOptions, cancellationToken);
        }

        return result;
    }
    private static async Task<IResult> GetOperationsReportAsync(
        DateTime? from,
        DateTime? to,
        string? operationType,
        OperationsDbContext operationsDbContext,
        IOptions<ReportingOptions> reportingOptions,
        CancellationToken cancellationToken)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = ["From must be earlier than or equal to To."] });
        }
        if (!string.IsNullOrWhiteSpace(operationType) && !OperationTypes.Contains(operationType.Trim()))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["operationType"] = ["Operation type is not valid."] });
        }
        var query = operationsDbContext.OperationLogs
            .AsNoTracking()
            .Include(operation => operation.OperationLines)
            .Where(operation => !operation.IsDeleted)
            .AsQueryable();

        if (from.HasValue)
        {
            query = query.Where(operation => operation.CreatedAt >= from.Value);
        }
        if (to.HasValue)
        {
            query = query.Where(operation => operation.CreatedAt <= to.Value);
        }
        if (!string.IsNullOrWhiteSpace(operationType))
        {
            query = query.Where(operation => operation.OperationType == operationType.Trim());
        }

        var operations = await query
            .OrderByDescending(operation => operation.CreatedAt)
            .Take(reportingOptions.Value.MaxExportRows + 1)
            .ToListAsync(cancellationToken);

        var rows = operations.Select(operation => new OperationReportRow(
            operation.Id,
            operation.OperationNumber,
            operation.OperationType,
            operation.Status,
            operation.ClientId,
            operation.ClientName,
            operation.PaymentMethod,
            operation.OperationLines.Sum(line => line.Quantity),
            operation.OperationLines.Sum(line => line.BonusQuantity),
            operation.OperationLines.Sum(line => line.LineTotal),
            operation.CreatedAt,
            operation.ConfirmedAt)).ToList();

        return Results.Ok(rows);
    }

    private static async Task<IResult> GetOperationsCsvAsync(
        DateTime? from,
        DateTime? to,
        string? operationType,
        string? language,
        OperationsDbContext operationsDbContext,
        ReportingDbContext reportingDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IDocumentExportService exportService,
        IOptions<ReportingOptions> reportingOptions,
        CancellationToken cancellationToken)
    {
        var result = await GetOperationsReportAsync(from, to, operationType, operationsDbContext, reportingOptions, cancellationToken);
        if (result is IValueHttpResult { Value: IEnumerable<OperationReportRow> rows })
        {
            return await ExportLegacyCsvAsync("operations", rows.ToList(), language, reportingDbContext, currentUser, clock, exportService, reportingOptions, cancellationToken);
        }

        return result;
    }
    private static async Task<IResult> GetPaymentsReportAsync(
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        IOptions<ReportingOptions> reportingOptions,
        CancellationToken cancellationToken)
    {
        var logs = await paymentsDbContext.MainPaymentLogs
            .Include(log => log.InstallmentSubLogs)
            .Where(log => !log.IsDeleted)
            .OrderByDescending(log => log.LastModifiedAt)
            .Take(reportingOptions.Value.MaxExportRows + 1)
            .ToListAsync(cancellationToken);
        var operationIds = logs.Select(log => log.OperationId).Distinct().ToArray();
        var operationContexts = await operationsDbContext.OperationLogs
            .Where(operation => operationIds.Contains(operation.Id))
            .ToDictionaryAsync(operation => operation.Id, operation => new { operation.OperationNumber, operation.ClientId }, cancellationToken);
        var logIds = logs.Select(log => log.Id).ToArray();
        var cashByOperation = (await paymentsDbContext.CashRecords
            .Where(record => operationIds.Contains(record.OperationId))
            .ToListAsync(cancellationToken))
            .GroupBy(record => record.OperationId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<CashRecord>)group.ToList());
        var adjustmentsByLog = (await paymentsDbContext.FinancialAdjustments
            .Where(adjustment => adjustment.PaymentLogId.HasValue && logIds.Contains(adjustment.PaymentLogId.Value))
            .ToListAsync(cancellationToken))
            .GroupBy(adjustment => adjustment.PaymentLogId!.Value)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<FinancialAdjustment>)group.ToList());
        var rows = logs.Select(log =>
        {
            var balance = PaymentBalanceCalculator.Calculate(
                log,
                adjustmentsByLog.GetValueOrDefault(log.Id, []),
                cashByOperation.GetValueOrDefault(log.OperationId, []));
            return new PaymentReportRow(
                log.Id,
                log.OperationId,
                null,
                log.MerchantId ?? operationContexts.GetValueOrDefault(log.OperationId)?.ClientId,
                log.PaymentMethod,
                log.TotalAmount,
                balance.ConfirmedCollections,
                balance.RemainingAmount,
                log.Status,
                log.AssignedTo,
                log.LastModifiedAt,
                balance.RefundDue);
        }).ToList();

        rows = rows.Select(row => row with
        {
            OperationNumber = operationContexts.TryGetValue(row.OperationId, out var operation) ? operation.OperationNumber : null
        }).ToList();

        return Results.Ok(rows);
    }

    private static async Task<IResult> GetPaymentsCsvAsync(
        string? language,
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        ReportingDbContext reportingDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IDocumentExportService exportService,
        IOptions<ReportingOptions> reportingOptions,
        CancellationToken cancellationToken)
    {
        var result = await GetPaymentsReportAsync(operationsDbContext, paymentsDbContext, reportingOptions, cancellationToken);
        if (result is IValueHttpResult { Value: IEnumerable<PaymentReportRow> rows })
        {
            return await ExportLegacyCsvAsync("payments", rows.ToList(), language, reportingDbContext, currentUser, clock, exportService, reportingOptions, cancellationToken);
        }

        return result;
    }
    private static async Task<IResult> GetSupplyLandedCostReportAsync(
        DateTime? from,
        DateTime? to,
        string? status,
        OperationsDbContext operationsDbContext,
        IOptions<ReportingOptions> reportingOptions,
        CancellationToken cancellationToken)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = ["From must be earlier than or equal to To."] });
        }
        if (!string.IsNullOrWhiteSpace(status) && !SupplyStatuses.Contains(status.Trim()))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Supply status is not valid."] });
        }
        var query = operationsDbContext.SupplyShipments
            .Include(shipment => shipment.Lines)
            .Include(shipment => shipment.Costs)
            .Include(shipment => shipment.InventoryReceiptOperation)
            .AsNoTracking()
            .AsQueryable();

        if (from.HasValue)
        {
            query = query.Where(shipment => shipment.ShipmentDate >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(shipment => shipment.ShipmentDate <= to.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(shipment => shipment.Status == status.Trim());
        }

        var rows = await query
            .OrderByDescending(shipment => shipment.ShipmentDate)
            .Take(reportingOptions.Value.MaxExportRows + 1)
            .Select(shipment => new SupplyLandedCostReportRow(
                shipment.Id,
                shipment.ShipmentNumber,
                shipment.SupplierName,
                shipment.InvoiceNumber,
                shipment.ShipmentDate,
                shipment.Status,
                shipment.Lines.Sum(line => line.Quantity),
                shipment.ProductSubtotal,
                shipment.CostSubtotal,
                shipment.LandedTotal,
                shipment.InventoryReceiptOperationId,
                shipment.InventoryReceiptOperation == null ? null : shipment.InventoryReceiptOperation.OperationNumber))
            .ToListAsync(cancellationToken);

        return Results.Ok(rows);
    }

    private static async Task<IResult> GetSupplyLandedCostCsvAsync(
        DateTime? from,
        DateTime? to,
        string? status,
        string? language,
        OperationsDbContext operationsDbContext,
        ReportingDbContext reportingDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IDocumentExportService exportService,
        IOptions<ReportingOptions> reportingOptions,
        CancellationToken cancellationToken)
    {
        var result = await GetSupplyLandedCostReportAsync(from, to, status, operationsDbContext, reportingOptions, cancellationToken);
        if (result is IValueHttpResult { Value: IEnumerable<SupplyLandedCostReportRow> rows })
        {
            return await ExportLegacyCsvAsync("supply", rows.ToList(), language, reportingDbContext, currentUser, clock, exportService, reportingOptions, cancellationToken);
        }

        return result;
    }
    private static async Task<IResult> GetSupplyLandedCostPdfAsync(
        Guid id,
        string? language,
        string? format,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        ReportingDbContext reportingDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IDocumentExportService exportService,
        CancellationToken cancellationToken)
    {
        if (!TryResolveExportFormat(format, [ExportFormat.Pdf, ExportFormat.Excel], out var exportFormat)) return InvalidExportFormat();
        if (!ReportingServiceCollectionExtensions.TryParseLanguage(language, out _)) return InvalidDocumentLanguage();

        var shipment = await operationsDbContext.SupplyShipments
            .Include(value => value.Lines)
            .Include(value => value.Costs)
            .Include(value => value.HistoryLogs)
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (shipment is null)
        {
            return Results.NotFound();
        }

        var receiptOperationNumber = shipment.InventoryReceiptOperationId.HasValue
            ? await operationsDbContext.OperationLogs
                .Where(operation => operation.Id == shipment.InventoryReceiptOperationId.Value && !operation.IsDeleted)
                .Select(operation => operation.OperationNumber)
                .FirstOrDefaultAsync(cancellationToken)
            : null;
        var userLookup = await LoadUserLookupAsync(identityDbContext, shipment, cancellationToken);

        var summary = new List<PdfFact>
        {
            new("Shipment", shipment.ShipmentNumber),
            new("Supplier", shipment.SupplierName),
            new("Invoice", shipment.InvoiceNumber ?? "-"),
            new("Status", shipment.Status),
            new("Products", FormatMoney(shipment.ProductSubtotal)),
            new("Import costs", FormatMoney(shipment.CostSubtotal)),
            new("Landed total", FormatMoney(shipment.LandedTotal)),
            new("Receipt operation", receiptOperationNumber ?? "-")
        };

        var sections = new List<PdfSection>
        {
            new(
                "Shipment data",
                [
                    new PdfFact("Shipment date", FormatDateTime(shipment.ShipmentDate)),
                    new PdfFact("Created by", GetUserDisplayName(shipment.CreatedBy, userLookup)),
                    new PdfFact("Created at", FormatDateTime(shipment.CreatedAt)),
                    new PdfFact("Confirmed by", GetUserDisplayName(shipment.ConfirmedBy, userLookup)),
                    new PdfFact("Confirmed at", FormatDateTime(shipment.ConfirmedAt)),
                    new PdfFact("Notes", shipment.Notes ?? "-")
                ]),
            new(
                "Lines",
                Tables:
                [
                    new PdfTableSection(
                        "SKU landed costs",
                        ["SKU", "Product", "Qty", "Unit price", "Line", "Allocated", "Landed unit", "Lot", "Expiry"],
                        shipment.Lines.Select(line => (IReadOnlyList<string>)new[]
                        {
                            line.SkuCodeSnapshot,
                            line.ProductNameSnapshot,
                            line.Quantity.ToString(),
                            line.UnitPrice?.ToString("0.####") ?? "-",
                            FormatMoney(line.LineSubtotal),
                            FormatMoney(line.AllocatedCost),
                            FormatMoney(line.LandedUnitCost),
                            line.LotNumber ?? "-",
                            FormatDate(line.ExpiryDate)
                        }).ToList(),
                        "No supply lines were recorded.")
                ]),
            new(
                "Cost breakdown",
                Tables:
                [
                    new PdfTableSection(
                        "Import costs",
                        ["Type", "Description", "Amount"],
                        shipment.Costs.Select(cost => (IReadOnlyList<string>)new[]
                        {
                            cost.CostType,
                            cost.Description ?? "-",
                            FormatMoney(cost.Amount)
                        }).ToList(),
                        "No import costs were recorded.")
                ]),
            new(
                "History",
                Tables:
                [
                    new PdfTableSection(
                        "Supply history",
                        ["Action", "Time", "Summary"],
                        shipment.HistoryLogs.OrderByDescending(item => item.CreatedAt).Select(item => (IReadOnlyList<string>)new[]
                        {
                            item.Action,
                            FormatDateTime(item.CreatedAt),
                            item.Summary ?? "-"
                        }).ToList(),
                        "No history was recorded.")
                ])
        };

        var document = BuildEnterpriseDocument(
            "Supply landed cost",
            "Imported shipment, cost allocation, and inventory receipt",
            shipment.ShipmentNumber,
            summary,
            sections,
            language,
            GetUserDisplayName(currentUser.UserId, userLookup),
            clock.UtcNow);
        var pdf = await exportService.ExportAsync(document, exportFormat, cancellationToken);
        var exportExtension = exportFormat == ExportFormat.Excel ? "xlsx" : "pdf";
        await LogExportAsync(reportingDbContext, currentUser, clock, $"supply-landed-cost.{exportExtension}", $"download://reports/supply/{id}/landed-cost.{exportExtension}", cancellationToken);
        return Results.File(pdf.Content, pdf.ContentType, pdf.FileName);
    }

    private static async Task<IResult> GetMerchantBalancesReportAsync(
        CrmDbContext crmDbContext,
        MerchantAccountService merchantAccountService,
        IOptions<ReportingOptions> reportingOptions,
        CancellationToken cancellationToken)
    {
        var merchants = await crmDbContext.Merchants
            .AsNoTracking()
            .Where(merchant => !merchant.IsDeleted)
            .OrderBy(merchant => merchant.BusinessName)
            .Take(reportingOptions.Value.MaxExportRows + 1)
            .ToListAsync(cancellationToken);
        var breakdowns = await merchantAccountService.GetFinancialBreakdownsAsync(merchants.Select(merchant => merchant.Id).ToArray(), cancellationToken);
        var rows = new List<MerchantBalanceReportRow>(merchants.Count);
        foreach (var merchant in merchants)
        {
            breakdowns.TryGetValue(merchant.Id, out var accountBreakdown);
            rows.Add(new MerchantBalanceReportRow(
                merchant.Id,
                merchant.BusinessName,
                merchant.Status,
                accountBreakdown?.TotalSales ?? 0m,
                accountBreakdown?.NetCollected ?? 0m,
                accountBreakdown?.RemainingOwed ?? 0m,
                accountBreakdown?.Refunds ?? 0m,
                accountBreakdown?.AcceptedReturnValue ?? 0m,
                accountBreakdown?.AdditionalCharges ?? 0m,
                accountBreakdown?.AmountReductions ?? 0m));
        }

        return Results.Ok(rows);
    }
    private static async Task<IResult> GetMerchantBalancesCsvAsync(
        string? language,
        CrmDbContext crmDbContext,
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        ReportingDbContext reportingDbContext,
        MerchantAccountService merchantAccountService,
        ICurrentUser currentUser,
        IClock clock,
        IDocumentExportService exportService,
        IOptions<ReportingOptions> reportingOptions,
        CancellationToken cancellationToken)
    {
        var result = await GetMerchantBalancesReportAsync(crmDbContext, merchantAccountService, reportingOptions, cancellationToken);
        if (result is IValueHttpResult { Value: IEnumerable<MerchantBalanceReportRow> rows })
        {
            return await ExportLegacyCsvAsync("merchant-balances", rows.ToList(), language, reportingDbContext, currentUser, clock, exportService, reportingOptions, cancellationToken);
        }

        return result;
    }
    private static async Task<IResult> GetOperationBillPdfAsync(
        Guid id,
        string? language,
        string? format,
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        CrmDbContext crmDbContext,
        InventoryDbContext inventoryDbContext,
        IdentityDbContext identityDbContext,
        ReportingDbContext reportingDbContext,
        MerchantAccountService merchantAccountService,
        ICurrentUser currentUser,
        IClock clock,
        IDocumentExportService exportService,
        CancellationToken cancellationToken)
    {
        if (!TryResolveExportFormat(format, [ExportFormat.Pdf], out var exportFormat)) return InvalidExportFormat();
        if (!ReportingServiceCollectionExtensions.TryParseLanguage(language, out _)) return InvalidDocumentLanguage();

        var operation = await operationsDbContext.OperationLogs
            .Include(value => value.OperationLines)
            .Include(value => value.OperationVersions)
            .Include(value => value.InventoryReceiptHeader)
            .FirstOrDefaultAsync(value => value.Id == id && !value.IsDeleted, cancellationToken);
        if (operation is null)
        {
            return Results.NotFound();
        }

        var merchant = operation.ClientId.HasValue
            ? await crmDbContext.Merchants.FirstOrDefaultAsync(value => value.Id == operation.ClientId.Value && !value.IsDeleted, cancellationToken)
            : null;
        var paymentLog = await paymentsDbContext.MainPaymentLogs
            .Include(value => value.InstallmentSubLogs)
            .FirstOrDefaultAsync(value => value.OperationId == operation.Id && !value.IsDeleted, cancellationToken);
        var cashRecords = await paymentsDbContext.CashRecords
            .Where(value => value.OperationId == operation.Id)
            .OrderByDescending(value => value.PaymentDate)
            .ToListAsync(cancellationToken);
        var adjustments = await paymentsDbContext.FinancialAdjustments
            .Where(value => value.OperationId == operation.Id)
            .OrderByDescending(value => value.CreatedAt)
            .ToListAsync(cancellationToken);
        var locations = await inventoryDbContext.Locations
            .Where(value =>
                (operation.SourceLocationId.HasValue && value.Id == operation.SourceLocationId.Value) ||
                (operation.DestinationLocationId.HasValue && value.Id == operation.DestinationLocationId.Value))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var userLookup = await LoadUserLookupAsync(
            identityDbContext,
            operation,
            paymentLog,
            cashRecords,
            adjustments,
            cancellationToken);
        var balance = merchant is not null
            ? await merchantAccountService.GetSnapshotAsync(merchant.Id, cancellationToken)
            : null;
        var paymentBalance = paymentLog is null
            ? null
            : PaymentBalanceCalculator.Calculate(paymentLog, adjustments, cashRecords);
        var totalQty = operation.OperationLines.Sum(line => line.Quantity);
        var totalValue = operation.OperationLines.Sum(line => line.LineTotal);
        var isChangeOperation = string.Equals(operation.OperationType, Change, StringComparison.OrdinalIgnoreCase);
        var lineHeaders = isChangeOperation
            ? new[] { "SKU", "Product", "Side", "Qty", "Bonus", "Unit price", "Total" }
            : ["SKU", "Product", "Qty", "Bonus", "Unit price", "Total"];
        var lineRows = operation.OperationLines
            .OrderBy(value => value.ProductNameSnapshot)
            .Select(line => (IReadOnlyList<string>)(isChangeOperation
                ? [
                    line.SkuCodeSnapshot,
                    line.ProductNameSnapshot,
                    FormatOperationLineSection(line.Section),
                    line.Quantity.ToString(),
                    line.BonusQuantity.ToString(),
                    FormatMoney(line.UnitPrice),
                    FormatMoney(line.LineTotal)
                ]
                : [
                    line.SkuCodeSnapshot,
                    line.ProductNameSnapshot,
                    line.Quantity.ToString(),
                    line.BonusQuantity.ToString(),
                    FormatMoney(line.UnitPrice),
                    FormatMoney(line.LineTotal)
                ]))
            .ToList();

        var summary = new List<PdfFact>
        {
            new("Operation no.", operation.OperationNumber),
            new("Date", FormatDateTime(operation.ConfirmedAt ?? operation.CreatedAt)),
            new("Type", operation.OperationType),
            new("Status", operation.Status),
            new("Customer", operation.ClientName ?? merchant?.BusinessName ?? "-"),
            new("Payment method", DescribePaymentMethod(operation.PaymentMethod)),
            new("Total quantity", totalQty.ToString()),
            new("Document total", FormatMoney(totalValue))
        };

        if (operation.InventoryReceiptHeader is not null)
        {
            summary.Add(new("Supplier", operation.InventoryReceiptHeader.SupplierName ?? "-"));
            summary.Add(new("Invoice", operation.InventoryReceiptHeader.InvoiceNumber ?? "-"));
        }

        var sections = new List<PdfSection>
        {
            new(
                "Parties",
                [
                    new PdfFact("Merchant", merchant?.BusinessName ?? operation.ClientName ?? "-"),
                    new PdfFact("Representative", operation.OperationLines.Select(line => line.RepresentativeNameSnapshot).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "-"),
                    new PdfFact("Source", GetLocationName(operation.SourceLocationId, locations)),
                    new PdfFact("Destination", GetLocationName(operation.DestinationLocationId, locations))
                ]),
            new(
                "Payment Summary",
                [
                    new PdfFact("Operation total", FormatMoney(totalValue)),
                    new PdfFact("Payment method", paymentLog is null ? DescribePaymentMethod(operation.PaymentMethod) : DescribePaymentMethod(paymentLog.PaymentMethod)),
                    new PdfFact("Paid to date", paymentBalance is null ? FormatMoney(cashRecords.Where(value => value.PaymentType == CashReceived).Sum(value => value.Amount)) : FormatMoney(paymentBalance.ConfirmedCollections)),
                    new PdfFact("Remaining", paymentBalance is null ? "-" : FormatMoney(paymentBalance.RemainingAmount)),
                    new PdfFact("Refund due", paymentBalance is null ? "-" : FormatMoney(paymentBalance.RefundDue)),
                new PdfFact("Merchant balance", balance is null ? "-" : FormatMoney(balance.AmountDue))
                ]),
            new(
                "Lines",
                Tables:
                [
                    new PdfTableSection(
                        "Operation lines",
                        lineHeaders,
                        lineRows,
                        "No operation lines were recorded.")
                ]),
            new(
                "Timeline",
                Tables:
                [
                    new PdfTableSection(
                        "Actor timeline",
                        ["Step", "Actor", "At"],
                        BuildOperationActorTimeline(operation, userLookup),
                        "No workflow timeline is available.")
                ])
        };

        var document = BuildEnterpriseDocument(
            "Operation bill",
            "Official receipt-style operation document",
            operation.OperationNumber,
            summary,
            sections,
            language,
            GetUserDisplayName(currentUser.UserId, userLookup),
            clock.UtcNow);
        var pdf = await exportService.ExportAsync(document, exportFormat, cancellationToken);
        await LogExportAsync(reportingDbContext, currentUser, clock, "operation-bill.pdf", $"download://reports/operations/{id}/bill.pdf", cancellationToken);
        return Results.File(pdf.Content, pdf.ContentType, pdf.FileName);
    }

    private static async Task<IResult> GetPaymentReceiptPdfAsync(
        Guid id,
        string? language,
        string? format,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        CrmDbContext crmDbContext,
        IdentityDbContext identityDbContext,
        ReportingDbContext reportingDbContext,
        MerchantAccountService merchantAccountService,
        ICurrentUser currentUser,
        IClock clock,
        IDocumentExportService exportService,
        CancellationToken cancellationToken)
    {
        if (!TryResolveExportFormat(format, [ExportFormat.Pdf], out var exportFormat)) return InvalidExportFormat();
        if (!ReportingServiceCollectionExtensions.TryParseLanguage(language, out _)) return InvalidDocumentLanguage();

        var log = await paymentsDbContext.MainPaymentLogs
            .Include(value => value.InstallmentSubLogs)
            .FirstOrDefaultAsync(value => value.Id == id && !value.IsDeleted, cancellationToken);
        if (log is null)
        {
            return Results.NotFound();
        }

        var operation = await operationsDbContext.OperationLogs
            .Include(value => value.OperationLines)
            .FirstOrDefaultAsync(value => value.Id == log.OperationId && !value.IsDeleted, cancellationToken);
        var merchant = log.MerchantId.HasValue
            ? await crmDbContext.Merchants.FirstOrDefaultAsync(value => value.Id == log.MerchantId.Value && !value.IsDeleted, cancellationToken)
            : null;
        var cashRecords = await paymentsDbContext.CashRecords
            .Where(value => value.OperationId == log.OperationId)
            .OrderByDescending(value => value.PaymentDate)
            .ToListAsync(cancellationToken);
        var adjustments = await paymentsDbContext.FinancialAdjustments
            .Where(value => (log.MerchantId.HasValue && value.MerchantId == log.MerchantId.Value) || value.OperationId == log.OperationId)
            .OrderByDescending(value => value.CreatedAt)
            .ToListAsync(cancellationToken);
        var userLookup = await LoadUserLookupAsync(
            identityDbContext,
            operation,
            log,
            cashRecords,
            adjustments,
            cancellationToken);
        var balance = log.MerchantId.HasValue
            ? await merchantAccountService.GetSnapshotAsync(log.MerchantId.Value, cancellationToken)
            : null;
        var paymentBalance = PaymentBalanceCalculator.Calculate(log, adjustments, cashRecords);

        var summary = new List<PdfFact>
        {
            new("Receipt no.", DocumentRecordCode("PAY", log.Id)),
            new("Date", FormatDateTime(log.LastModifiedAt)),
            new("Merchant", merchant?.BusinessName ?? operation?.ClientName ?? "Anonymous buyer"),
            new("Method", DescribePaymentMethod(log.PaymentMethod)),
            new("Status", log.Status),
            new("Total", FormatMoney(log.TotalAmount)),
            new("Paid", FormatMoney(paymentBalance.ConfirmedCollections)),
            new("Remaining", FormatMoney(paymentBalance.RemainingAmount)),
            new("Refund due", FormatMoney(paymentBalance.RefundDue))
        };

        var paymentRows = log.InstallmentSubLogs
            .OrderBy(value => value.DraftedAt)
            .Select(sub => (IReadOnlyList<string>)new[]
            {
                FormatDate(sub.DateReceived),
                DescribePaymentMethod(sub.PaymentMethod),
                FormatMoney(sub.Amount),
                sub.SubLogStatus
            })
            .Concat(cashRecords.Select(record => (IReadOnlyList<string>)new[]
            {
                FormatDateTime(record.PaymentDate),
                record.PaymentType,
                FormatMoney(record.Amount),
                record.Status
            }))
            .ToList();

        var sections = new List<PdfSection>
        {
            new(
                "Merchant",
                [
                    new PdfFact("Merchant / buyer", merchant?.BusinessName ?? operation?.ClientName ?? "Anonymous buyer"),
                    new PdfFact("Contact person", merchant?.ContactPersonName ?? operation?.ClientName ?? "-"),
                    new PdfFact("Phone", merchant is null ? "-" : JoinValues(merchant.PhoneNumbers))
                ]),
            new(
                "Operation",
                [
                    new PdfFact("Operation", operation?.OperationNumber ?? log.OperationId.ToString("N")[..8]),
                    new PdfFact("Type", operation?.OperationType ?? "-"),
                    new PdfFact("Date", operation is null ? "-" : FormatDateTime(operation.CreatedAt))
                ]),
            new(
                "Payment",
                [
                    new PdfFact("Method", DescribePaymentMethod(log.PaymentMethod)),
                    new PdfFact("Total amount", FormatMoney(log.TotalAmount)),
                    new PdfFact("Adjusted amount", FormatMoney(paymentBalance.AdjustedAmount)),
                    new PdfFact("Paid amount", FormatMoney(paymentBalance.ConfirmedCollections)),
                    new PdfFact("Remaining amount", FormatMoney(paymentBalance.RemainingAmount)),
                    new PdfFact("Refund due", FormatMoney(paymentBalance.RefundDue)),
                    new PdfFact("Merchant balance", balance is null ? "-" : FormatMoney(balance.AmountDue))
                ]),
            new(
                "Payment entries",
                Tables:
                [
                    new PdfTableSection(
                        "Payments",
                        ["Date", "Method", "Amount", "Status"],
                        paymentRows,
                        "No payment entries were recorded.")
                ],
                Note: log.Notes is null ? null : $"Notes: {log.Notes}")
        };

        var isCashReceipt = string.Equals(log.PaymentMethod, "CashHandToHand", StringComparison.OrdinalIgnoreCase);
        var document = BuildEnterpriseDocument(
            isCashReceipt ? "Cash collection receipt" : "Payment receipt",
            isCashReceipt ? "Cash collection and accountant approval detail" : "Financial collection and review detail",
            DocumentRecordCode(isCashReceipt ? "CASH" : "PAY", log.Id),
            summary,
            sections,
            language,
            GetUserDisplayName(currentUser.UserId, userLookup),
            clock.UtcNow);
        var pdf = await exportService.ExportAsync(document, exportFormat, cancellationToken);
        var documentName = isCashReceipt ? "cash-receipt.pdf" : "payment-receipt.pdf";
        var documentPath = isCashReceipt ? $"download://reports/payments/{id}/cash-receipt.pdf" : $"download://reports/payments/{id}/receipt.pdf";
        await LogExportAsync(reportingDbContext, currentUser, clock, documentName, documentPath, cancellationToken);
        return Results.File(pdf.Content, pdf.ContentType, pdf.FileName);
    }

    private static async Task<IResult> GetMerchantStatementPdfAsync(
        Guid merchantId,
        string? language,
        string? format,
        DateTime? from,
        DateTime? to,
        CrmDbContext crmDbContext,
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        ReportingDbContext reportingDbContext,
        MerchantAccountService merchantAccountService,
        ICurrentUser currentUser,
        IClock clock,
        IDocumentExportService exportService,
        CancellationToken cancellationToken)
    {
        if (!TryResolveExportFormat(format, [ExportFormat.Pdf, ExportFormat.Excel], out var exportFormat)) return InvalidExportFormat();
        if (!ReportingServiceCollectionExtensions.TryParseLanguage(language, out _)) return InvalidDocumentLanguage();

        var merchant = await crmDbContext.Merchants.FirstOrDefaultAsync(value => value.Id == merchantId && !value.IsDeleted, cancellationToken);
        if (merchant is null)
        {
            return Results.NotFound();
        }

        if (!from.HasValue && !to.HasValue)
        {
            var today = clock.EgyptNow.Date;
            from = new DateTime(today.Year, today.Month, 1);
            to = from.Value.AddMonths(1).AddDays(-1);
        }

        var account = await merchantAccountService.GetSnapshotAsync(merchantId, cancellationToken);
        var accountBreakdown = await merchantAccountService.GetFinancialBreakdownAsync(merchantId, cancellationToken);
        var statement = await merchantAccountService.GetStatementAsync(merchantId, 500, cancellationToken, from, to);
        var openingBalance = await merchantAccountService.GetOpeningBalanceAsync(merchantId, from, cancellationToken);
        var closingBalance = await merchantAccountService.GetClosingBalanceAsync(merchantId, to, cancellationToken);
        var operations = await operationsDbContext.OperationLogs
            .Include(value => value.OperationLines)
            .Include(value => value.OperationVersions)
            .Where(value => value.ClientId == merchantId && !value.IsDeleted)
            .OrderByDescending(value => value.CreatedAt)
            .ToListAsync(cancellationToken);
        var merchantOperationIds = operations.Select(value => value.Id).ToArray();
        var notes = await crmDbContext.MerchantNotes
            .Where(value => value.MerchantId == merchantId)
            .OrderByDescending(value => value.CreatedAt)
            .ToListAsync(cancellationToken);
        var paymentLogs = await paymentsDbContext.MainPaymentLogs
            .Include(value => value.InstallmentSubLogs)
            .Where(value => !value.IsDeleted &&
                (value.MerchantId == merchantId ||
                 (value.MerchantId == null && merchantOperationIds.Contains(value.OperationId))))
            .OrderByDescending(value => value.LastModifiedAt)
            .ToListAsync(cancellationToken);
        var adjustments = await paymentsDbContext.FinancialAdjustments
            .Where(value => value.MerchantId == merchantId)
            .OrderByDescending(value => value.CreatedAt)
            .ToListAsync(cancellationToken);
        var operationIds = operations.Select(value => value.Id)
            .Concat(paymentLogs.Select(value => value.OperationId))
            .Distinct()
            .ToArray();
        var cashRecords = await paymentsDbContext.CashRecords
            .Where(value => operationIds.Contains(value.OperationId))
            .OrderByDescending(value => value.PaymentDate)
            .ToListAsync(cancellationToken);
        var cashByOperation = cashRecords
            .GroupBy(value => value.OperationId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<CashRecord>)group.ToList());
        var adjustmentsByLog = adjustments
            .Where(value => value.PaymentLogId.HasValue)
            .GroupBy(value => value.PaymentLogId!.Value)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<FinancialAdjustment>)group.ToList());
        var paymentBalances = paymentLogs.ToDictionary(
            log => log.Id,
            log => PaymentBalanceCalculator.Calculate(log, adjustmentsByLog.GetValueOrDefault(log.Id, []), cashByOperation.GetValueOrDefault(log.OperationId, [])));
        var userLookup = await LoadUserLookupAsync(identityDbContext, operations, paymentLogs, cashRecords, adjustments, notes, cancellationToken);
        var entryActorIds = statement.Select(value => value.PostedBy).Where(value => value != Guid.Empty && !userLookup.ContainsKey(value)).Distinct().ToArray();
        if (entryActorIds.Length > 0)
        {
            foreach (var user in await identityDbContext.Users.Where(value => entryActorIds.Contains(value.Id)).ToListAsync(cancellationToken))
            {
                userLookup[user.Id] = user;
            }
        }

        var summary = new List<PdfFact>
        {
            new("Merchant", merchant.BusinessName),
            new("Status", merchant.Status),
            new("Contact person", merchant.ContactPersonName),
            new("Phone", JoinValues(merchant.PhoneNumbers)),
            new("Statement period", from.HasValue || to.HasValue ? $"{from:yyyy-MM-dd} to {to:yyyy-MM-dd}" : "Current month"),
            new("Total sales", FormatMoney(accountBreakdown?.TotalSales ?? 0m)),
            new("Net collected", FormatMoney(accountBreakdown?.NetCollected ?? 0m)),
            new("Remaining owed", FormatMoney(accountBreakdown?.RemainingOwed ?? 0m)),
            new("Refunds", FormatMoney(accountBreakdown?.Refunds ?? 0m))
        };

        var sections = new List<PdfSection>
        {
            new(
                "Account summary",
                [
                    new PdfFact("Total sales", FormatMoney(accountBreakdown?.TotalSales ?? 0m)),
                    new PdfFact("Net collected", FormatMoney(accountBreakdown?.NetCollected ?? 0m)),
                    new PdfFact("Remaining owed", FormatMoney(accountBreakdown?.RemainingOwed ?? 0m)),
                    new PdfFact("Refunds", FormatMoney(accountBreakdown?.Refunds ?? 0m)),
                    new PdfFact("Accepted return value", FormatMoney(accountBreakdown?.AcceptedReturnValue ?? 0m)),
                    new PdfFact("Additional charges", FormatMoney(accountBreakdown?.AdditionalCharges ?? 0m)),
                    new PdfFact("Amount reductions", FormatMoney(accountBreakdown?.AmountReductions ?? 0m))
                ]),
            new(
                "Account activity",
                Tables:
                [
                    new PdfTableSection(
                        "Confirmed account activity",
                        ["When", "What happened", "Related record", "How", "Added", "Reduced", "Balance", "Recorded by"],
                        statement.Select(entry =>
                        {
                            var references = new List<string>(2);
                            if (entry.OperationId is { } operationId)
                            {
                                var operationNumber = operations.FirstOrDefault(operation => operation.Id == operationId)?.OperationNumber;
                                if (!string.IsNullOrWhiteSpace(operationNumber)) references.Add(operationNumber);
                            }
                            if (entry.PaymentId is { } paymentId) references.Add(DocumentRecordCode("PAY", paymentId));

                            return (IReadOnlyList<string>)new[]
                            {
                                FormatDateTime(entry.PostedAt),
                                DescribeMerchantAccountEntry(entry.EntryType),
                                references.Count > 0 ? string.Join(" · ", references) : entry.EntryType == "Collection" ? "Account collection" : "Related account activity",
                                DescribePaymentMethod(entry.PaymentMethod),
                                FormatMoney(entry.DebitAmount),
                                FormatMoney(entry.CreditAmount),
                                FormatMoney(entry.RunningBalance),
                                GetUserDisplayName(entry.PostedBy, userLookup)
                            };
                        }).ToList(),
                        "No confirmed account activity was recorded.")
                ]),
            new(
                "Operations history",
                Tables:
                [
                    new PdfTableSection(
                        "Operations",
                        ["Operation", "Type", "Status", "Payment", "Total", "Created", "Confirmed", "Selling clerk"],
                        operations.Select(operation => (IReadOnlyList<string>)new[]
                        {
                            operation.OperationNumber,
                            DescribeOperationType(operation.OperationType),
                            operation.Status,
                            DescribePaymentMethod(operation.PaymentMethod),
                            FormatMoney(operation.OperationLines.Sum(line => line.LineTotal)),
                            FormatDateTime(operation.CreatedAt),
                            FormatDateTime(operation.ConfirmedAt),
                            GetUserDisplayName(operation.CreatedBy, userLookup)
                        }).ToList(),
                        "No operations were recorded for this merchant.")
                ]),
            new(
                "Payment history",
                Tables:
                [
                    new PdfTableSection(
                        "Payment logs",
                        ["Payment log", "Operation", "Method", "Status", "Total", "Paid", "Remaining", "Initialized by"],
                        paymentLogs.Select(log => (IReadOnlyList<string>)new[]
                        {
                            DocumentRecordCode("PAY", log.Id),
                            operations.FirstOrDefault(operation => operation.Id == log.OperationId)?.OperationNumber ?? log.OperationId.ToString("N")[..8],
                            DescribePaymentMethod(log.PaymentMethod),
                            log.Status,
                            FormatMoney(log.TotalAmount),
                            FormatMoney(paymentBalances[log.Id].ConfirmedCollections),
                            FormatMoney(paymentBalances[log.Id].RemainingAmount),
                            GetUserDisplayName(log.InitializedBy, userLookup)
                        }).ToList(),
                        "No payment logs were recorded."),
                    new PdfTableSection(
                        "Cash records",
                        ["Date", "Operation", "Type", "Amount", "Created by", "Notes"],
                        cashRecords.Select(record => (IReadOnlyList<string>)new[]
                        {
                            FormatDateTime(record.PaymentDate),
                            operations.FirstOrDefault(operation => operation.Id == record.OperationId)?.OperationNumber ?? record.OperationId.ToString("N")[..8],
                            DescribeCashRecordType(record.PaymentType),
                            FormatMoney(record.Amount),
                            GetUserDisplayName(record.CreatedBy, userLookup),
                            record.Notes ?? "-"
                        }).ToList(),
                        "No cash records were recorded."),
                    new PdfTableSection(
                        "Refund payouts",
                        ["Date", "Operation", "Method", "Amount", "Created by", "Notes"],
                        cashRecords.Where(record => string.Equals(record.PaymentType, "CashRefund", StringComparison.OrdinalIgnoreCase))
                            .Select(record => (IReadOnlyList<string>)new[]
                            {
                                FormatDateTime(record.PaymentDate),
                                operations.FirstOrDefault(operation => operation.Id == record.OperationId)?.OperationNumber ?? "Related operation",
                                DescribePaymentMethod(record.SubType),
                                FormatMoney(record.Amount),
                                GetUserDisplayName(record.CreatedBy, userLookup),
                                record.Notes ?? "-"
                            }).ToList(),
                        "No refund payouts were recorded."),
                    new PdfTableSection(
                        "Adjustments",
                        ["Date", "Type", "Amount", "Status", "Created by", "Notes"],
                        adjustments.Select(adjustment => (IReadOnlyList<string>)new[]
                        {
                            FormatDateTime(adjustment.CreatedAt),
                            adjustment.AdjustmentType,
                            FormatMoney(adjustment.Amount),
                            adjustment.Status,
                            GetUserDisplayName(adjustment.CreatedBy, userLookup),
                            adjustment.Notes ?? "-"
                        }).ToList(),
                        "No adjustments were recorded.")
                ]),
            new(
                "Notes",
                Tables:
                [
                    new PdfTableSection(
                        "Merchant notes",
                        ["Created", "Added by", "Note"],
                        notes.Select(note => (IReadOnlyList<string>)new[]
                        {
                            FormatDateTime(note.CreatedAt),
                            GetUserDisplayName(note.AddedBy, userLookup),
                            note.Note
                        }).ToList(),
                        "No notes were recorded.")
                ])
        };

        var document = BuildEnterpriseDocument(
            "Merchant statement",
            "Commercial relationship and financial position",
            DocumentRecordCode("MER", merchant.Id),
            summary,
            sections,
            language,
            GetUserDisplayName(currentUser.UserId, userLookup),
            clock.UtcNow);
        var pdf = await exportService.ExportAsync(document, exportFormat, cancellationToken);
        var exportExtension = exportFormat == ExportFormat.Excel ? "xlsx" : "pdf";
        await LogExportAsync(reportingDbContext, currentUser, clock, $"merchant-statement.{exportExtension}", $"download://reports/merchants/{merchantId}/statement.{exportExtension}", cancellationToken);
        return Results.File(pdf.Content, pdf.ContentType, pdf.FileName);
    }

    private static async Task<IResult> GetStocktakeSummaryPdfAsync(
        Guid id,
        string? language,
        string? format,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        InventoryDbContext inventoryDbContext,
        IdentityDbContext identityDbContext,
        ReportingDbContext reportingDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IDocumentExportService exportService,
        CancellationToken cancellationToken)
    {
        if (!TryResolveExportFormat(format, [ExportFormat.Pdf, ExportFormat.Excel], out var exportFormat)) return InvalidExportFormat();
        if (!ReportingServiceCollectionExtensions.TryParseLanguage(language, out _)) return InvalidDocumentLanguage();

        var session = await operationsDbContext.StocktakeSessions
            .Include(value => value.StocktakeAdjustmentLines)
            .FirstOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (session is null)
        {
            return Results.NotFound();
        }

        var location = await inventoryDbContext.Locations.FirstOrDefaultAsync(value => value.Id == session.LocationId, cancellationToken);
        var skuIds = session.StocktakeAdjustmentLines.Select(value => value.SkuId).Distinct().ToArray();
        var skus = await catalogDbContext.Skus
            .Include(value => value.Product)
            .Where(value => skuIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, cancellationToken);
        var userLookup = await LoadUserLookupAsync(identityDbContext, session, cancellationToken);
        var summary = new List<PdfFact>
        {
            new("Session", DocumentRecordCode("STK", session.Id)),
            new("Location", location?.Name ?? session.LocationId.ToString("N")),
            new("Status", session.Status),
            new("Performed by", GetUserDisplayName(session.PerformedBy, userLookup)),
            new("Confirmed by", GetUserDisplayName(session.ConfirmedBy, userLookup)),
            new("Created at", FormatDateTime(session.CreatedAt)),
            new("Confirmed at", FormatDateTime(session.ConfirmedAt)),
            new("Counted lines", (session.ProductsCounted ?? 0).ToString()),
            new("Total discrepancy", (session.TotalDiscrepancyUnits ?? 0).ToString())
        };

        var sections = new List<PdfSection>
        {
            new(
                "Actors",
                [
                    new PdfFact("Performed by", GetUserDisplayName(session.PerformedBy, userLookup)),
                    new PdfFact("Confirmed by", GetUserDisplayName(session.ConfirmedBy, userLookup)),
                    new PdfFact("Session date", FormatDateTime(session.SessionDate)),
                    new PdfFact("Confirmed at", FormatDateTime(session.ConfirmedAt))
                ]),
            new(
                "Summary",
                [
                    new PdfFact("Total counted", (session.ProductsCounted ?? 0).ToString()),
                    new PdfFact("Discrepancies", (session.TotalDiscrepancyUnits ?? 0).ToString()),
                    new PdfFact("Adjustment count", session.StocktakeAdjustmentLines.Count.ToString())
                ]),
            new(
                "Notes",
                [
                    new PdfFact("Notes", session.Notes ?? "-")
                ]),
            new(
                "Adjustment Lines",
                Tables:
                [
                    new PdfTableSection(
                        "Stocktake lines",
                        ["SKU", "Product", "Lot", "Batch expiry", "System", "Physical", "Delta", "Note"],
                        session.StocktakeAdjustmentLines.Select(line =>
                        {
                            skus.TryGetValue(line.SkuId, out var sku);
                            return (IReadOnlyList<string>)new[]
                            {
                                sku?.SkuCode ?? line.SkuId.ToString("N")[..8],
                                sku?.Product.Name ?? "-",
                                line.LotNumber ?? "-",
                                FormatDate(line.ExpiryDate),
                                line.SystemQtyBefore.ToString(),
                                line.PhysicalCount.ToString(),
                                line.Delta.ToString(),
                                line.LineNote ?? "-"
                            };
                        }).ToList(),
                        "No stocktake lines were recorded.")
                ])
        };

        var document = BuildEnterpriseDocument(
            "Stocktake summary",
            "Physical count and discrepancy review",
            DocumentRecordCode("STK", session.Id),
            summary,
            sections,
            language,
            GetUserDisplayName(currentUser.UserId, userLookup),
            clock.UtcNow);
        var pdf = await exportService.ExportAsync(document, exportFormat, cancellationToken);
        var exportExtension = exportFormat == ExportFormat.Excel ? "xlsx" : "pdf";
        await LogExportAsync(reportingDbContext, currentUser, clock, $"stocktake-summary.{exportExtension}", $"download://reports/stocktakes/{id}/summary.{exportExtension}", cancellationToken);
        return Results.File(pdf.Content, pdf.ContentType, pdf.FileName);
    }

    private static async Task<IResult> ListExportLogsAsync(
        int? page,
        int? pageSize,
        ReportingDbContext reportingDbContext,
        IdentityDbContext identityDbContext,
        CancellationToken cancellationToken)
    {
        var request = new PageRequest(page ?? 1, pageSize ?? 25);
        var query = reportingDbContext.ExportLogs.OrderByDescending(log => log.CreatedAt);
        var total = await query.CountAsync(cancellationToken);
        var logs = await query
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var userIds = logs
            .Where(log => log.RequestedBy.HasValue)
            .Select(log => log.RequestedBy!.Value)
            .Distinct()
            .ToArray();
        var roles = await identityDbContext.Users
            .Where(user => userIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.Role, cancellationToken);

        var rows = logs.Select(log =>
        {
            var role = log.RequestedBy.HasValue && roles.TryGetValue(log.RequestedBy.Value, out var value)
                ? value
                : null;
            return new ExportLogResponse(log.Id, log.ReportType, log.RequestedBy, role, log.GeneratedUrl, log.CreatedAt);
        }).ToList();

        return Results.Ok(new PagedResult<ExportLogResponse>(rows, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> CreateExportLogAsync(
        CreateExportLogRequest request,
        ReportingDbContext reportingDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var reportType = NormalizeExportReportType(request.ReportType);
        if (reportType is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.ReportType)] = ["Report type must be a supported report export."] });
        }

        var export = new ExportLog
        {
            Id = Guid.NewGuid(),
            ReportType = reportType,
            RequestedBy = currentUser.UserId,
            GeneratedUrl = request.GeneratedUrl ?? $"demo://reports/{reportType}",
            CreatedAt = clock.EgyptNow
        };

        reportingDbContext.ExportLogs.Add(export);
        await reportingDbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/reports/exports/{export.Id}", new ExportLogResponse(export.Id, export.ReportType, export.RequestedBy, currentUser.Role, export.GeneratedUrl, export.CreatedAt));
    }

    private static string? NormalizeExportReportType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return ExportReportTypes.FirstOrDefault(type => string.Equals(type, trimmed, StringComparison.OrdinalIgnoreCase));
    }
    private static IReadOnlyList<IReadOnlyList<string>> BuildOperationActorTimeline(
        OperationLog operation,
        IReadOnlyDictionary<Guid, User> userLookup)
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[]
            {
                "Created",
                GetUserDisplayName(operation.CreatedBy, userLookup),
                FormatDateTime(operation.CreatedAt)
            }
        };

        if (operation.ConfirmedBy.HasValue || operation.ConfirmedAt.HasValue)
        {
            rows.Add(new[]
            {
                "Confirmed / last action",
                GetUserDisplayName(operation.ConfirmedBy, userLookup),
                FormatDateTime(operation.ConfirmedAt)
            });
        }

        foreach (var version in operation.OperationVersions.OrderBy(value => value.VersionNumber))
        {
            rows.Add(new[]
            {
                $"Version {version.VersionNumber}",
                GetUserDisplayName(version.EditedBy, userLookup),
                FormatDateTime(version.EditedAt)
            });
        }

        return rows;
    }

    private static string DescribePaymentMethod(string? value)
    {
        return value switch
        {
            null or "" => "-",
            "CashHandToHand" => "Cash hand to hand",
            "CashTransaction" => "Cash transaction",
            "BankTransfer" => "Bank transfer",
            "Wallet" => "Wallet",
            "MerchantAccount" => "Merchant account",
            "Installment" or "Installlaugment" => "Merchant account",
            _ => value
        };
    }

    private static string DescribeOperationType(string? value) => value switch
    {
        "WholesaleSale" => "Wholesale sale",
        "RetailSale" => "Retail sale",
        "Return" => "Return",
        "Change" => "Exchange",
        "InventoryReceipt" => "Inventory receipt",
        "WarehouseTransfer" => "Warehouse transfer",
        "Reserve" => "Representative reserve",
        "WriteOff" => "Write-off",
        _ => string.IsNullOrWhiteSpace(value) ? "-" : value
    };

    private static string DescribeCashRecordType(string? value) => value switch
    {
        "CashReceived" => "Money received",
        "CashRefund" => "Money refunded",
        _ => string.IsNullOrWhiteSpace(value) ? "-" : value
    };

    private static string DescribeMerchantAccountEntry(string entryType) => entryType switch
    {
        "SaleCharge" => "Sale added",
        "ReturnCredit" => "Return accepted",
        "ExchangeSurcharge" => "Exchange amount added",
        "ExchangeCredit" => "Exchange credit added",
        "Collection" => "Money received",
        "RefundPayout" => "Money refunded",
        "AdditionalCharge" => "Additional charge added",
        "BalanceReduction" => "Amount reduced",
        _ => "Account activity"
    };

    private static string FormatOperationLineSection(string? value)
    {
        return value switch
        {
            ChangeOut => "Returned",
            ChangeIn => "Replacement",
            null or "" => "-",
            _ => value
        };
    }

    private static string FormatMoney(decimal value) => value.ToString("0.####");

    private static string FormatDate(DateOnly? value) => value?.ToString("yyyy-MM-dd") ?? "-";

    private static string FormatDate(DateOnly value) => value.ToString("yyyy-MM-dd");

    private static string FormatDateTime(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm") ?? "-";

    private static string JoinValues(IEnumerable<string>? values)
    {
        var items = values?.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? [];
        return items.Length == 0 ? "-" : string.Join(", ", items);
    }

    private static string GetLocationName(Guid? locationId, IReadOnlyDictionary<Guid, Location> locations)
    {
        if (!locationId.HasValue)
        {
            return "-";
        }

        return locations.TryGetValue(locationId.Value, out var location)
            ? location.Name
            : locationId.Value.ToString("N");
    }

    private static string GetUserDisplayName(Guid? userId, IReadOnlyDictionary<Guid, User> users)
    {
        if (!userId.HasValue || userId.Value == Guid.Empty)
        {
            return "-";
        }

        return users.TryGetValue(userId.Value, out var user)
            ? (string.IsNullOrWhiteSpace(user.FullName) ? user.Username : user.FullName)
            : userId.Value.ToString("N");
    }

    private static async Task LogExportAsync(
        ReportingDbContext reportingDbContext,
        ICurrentUser currentUser,
        IClock clock,
        string reportType,
        string generatedUrl,
        CancellationToken cancellationToken)
    {
        reportingDbContext.ExportLogs.Add(new ExportLog
        {
            Id = Guid.NewGuid(),
            ReportType = reportType,
            RequestedBy = currentUser.UserId,
            GeneratedUrl = generatedUrl,
            CreatedAt = clock.EgyptNow
        });
        await reportingDbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<Dictionary<Guid, User>> LoadUserLookupAsync(
        IdentityDbContext identityDbContext,
        OperationLog? operation,
        MainPaymentLog? paymentLog,
        IEnumerable<CashRecord> cashRecords,
        IEnumerable<FinancialAdjustment> adjustments,
        CancellationToken cancellationToken)
    {
        var ids = new List<Guid>();

        if (operation is not null)
        {
            ids.Add(operation.CreatedBy);
            if (operation.ConfirmedBy.HasValue) ids.Add(operation.ConfirmedBy.Value);
            ids.AddRange(operation.OperationVersions.Select(version => version.EditedBy));
        }

        if (paymentLog is not null)
        {
            ids.Add(paymentLog.InitializedBy);
            if (paymentLog.AssignedTo.HasValue) ids.Add(paymentLog.AssignedTo.Value);
            if (paymentLog.LastModifiedBy.HasValue) ids.Add(paymentLog.LastModifiedBy.Value);
            ids.AddRange(paymentLog.InstallmentSubLogs.Select(value => value.DraftedBy));
            ids.AddRange(paymentLog.InstallmentSubLogs.Where(value => value.ConfirmedBy.HasValue).Select(value => value.ConfirmedBy!.Value));
        }

        ids.AddRange(cashRecords.Select(value => value.CreatedBy));
        ids.AddRange(adjustments.Select(value => value.CreatedBy));

        var distinctIds = ids.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (distinctIds.Length == 0)
        {
            return [];
        }

        return await identityDbContext.Users
            .Where(user => distinctIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, cancellationToken);
    }

    private static async Task<Dictionary<Guid, User>> LoadUserLookupAsync(
        IdentityDbContext identityDbContext,
        IEnumerable<OperationLog> operations,
        IEnumerable<MainPaymentLog> paymentLogs,
        IEnumerable<CashRecord> cashRecords,
        IEnumerable<FinancialAdjustment> adjustments,
        IEnumerable<MerchantNote> notes,
        CancellationToken cancellationToken)
    {
        var ids = operations.Select(value => value.CreatedBy)
            .Concat(operations.Where(value => value.ConfirmedBy.HasValue).Select(value => value.ConfirmedBy!.Value))
            .Concat(operations.SelectMany(value => value.OperationVersions.Select(version => version.EditedBy)))
            .Concat(paymentLogs.Select(value => value.InitializedBy))
            .Concat(paymentLogs.Where(value => value.AssignedTo.HasValue).Select(value => value.AssignedTo!.Value))
            .Concat(paymentLogs.Where(value => value.LastModifiedBy.HasValue).Select(value => value.LastModifiedBy!.Value))
            .Concat(paymentLogs.SelectMany(value => value.InstallmentSubLogs.Select(sub => sub.DraftedBy)))
            .Concat(paymentLogs.SelectMany(value => value.InstallmentSubLogs.Where(sub => sub.ConfirmedBy.HasValue).Select(sub => sub.ConfirmedBy!.Value)))
            .Concat(cashRecords.Select(value => value.CreatedBy))
            .Concat(adjustments.Select(value => value.CreatedBy))
            .Concat(notes.Select(value => value.AddedBy))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        return await identityDbContext.Users
            .Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, cancellationToken);
    }

    private static async Task<Dictionary<Guid, User>> LoadUserLookupAsync(
        IdentityDbContext identityDbContext,
        StocktakeSession session,
        CancellationToken cancellationToken)
    {
        var ids = new[] { session.PerformedBy, session.ConfirmedBy ?? Guid.Empty }
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        return await identityDbContext.Users
            .Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, cancellationToken);
    }

    private static async Task<Dictionary<Guid, User>> LoadUserLookupAsync(
        IdentityDbContext identityDbContext,
        SupplyShipment shipment,
        CancellationToken cancellationToken)
    {
        var ids = new[]
        {
            shipment.CreatedBy,
            shipment.UpdatedBy ?? Guid.Empty,
            shipment.ConfirmedBy ?? Guid.Empty,
            shipment.CancelledBy ?? Guid.Empty
        }
        .Concat(shipment.HistoryLogs.Select(history => history.ActorUserId))
        .Where(id => id != Guid.Empty)
        .Distinct()
        .ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        return await identityDbContext.Users
            .Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, cancellationToken);
    }

}

public sealed record StockReportRow(Guid LocationId, string LocationName, string LocationType, Guid SkuId, string? SkuCode, string? ProductName, int AvailableQty, int ReservedInWarehouseQty, int ReservedWithRepQty, int? TargetQty, DateTime LastUpdated);

public sealed record FinancialSummaryResponse(decimal TotalSales, decimal ActualCollected, decimal RemainingReceivable);

public sealed record OperationReportRow(Guid Id, string OperationNumber, string OperationType, string Status, Guid? MerchantId, string? ClientName, string? PaymentMethod, int Quantity, int BonusQuantity, decimal Total, DateTime CreatedAt, DateTime? ConfirmedAt);

public sealed record PaymentReportRow(Guid Id, Guid OperationId, string? OperationNumber, Guid? MerchantId, string PaymentMethod, decimal TotalAmount, decimal AmountPaid, decimal RemainingAmount, string Status, Guid? AssignedTo, DateTime LastModifiedAt, decimal RefundDue);

public sealed record SupplyLandedCostReportRow(Guid Id, string ShipmentNumber, string SupplierName, string? InvoiceNumber, DateTime ShipmentDate, string Status, int Quantity, decimal ProductSubtotal, decimal CostSubtotal, decimal LandedTotal, Guid? InventoryReceiptOperationId, string? InventoryReceiptOperationNumber);

public sealed record MerchantBalanceReportRow(Guid MerchantId, string BusinessName, string Status, decimal TotalSales, decimal NetCollected, decimal RemainingOwed, decimal Refunds, decimal AcceptedReturnValue, decimal AdditionalCharges, decimal AmountReductions);

public sealed record CreateExportLogRequest(string ReportType, string? GeneratedUrl);

public sealed record ExportLogResponse(Guid Id, string ReportType, Guid? RequestedBy, string? RequestedByRole, string? GeneratedUrl, DateTime CreatedAt);

internal sealed record PdfFact(string Label, string Value);

internal sealed record PdfTableSection(string Title, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, string EmptyMessage);

internal sealed record PdfSection(
    string Title,
    IReadOnlyList<PdfFact>? Facts = null,
    IReadOnlyList<PdfTableSection>? Tables = null,
    string? Note = null);
