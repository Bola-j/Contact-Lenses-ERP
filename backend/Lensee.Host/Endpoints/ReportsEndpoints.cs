using System.Text;
using System.Numerics;
using Lensee.Host.Infrastructure;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.Modules.Reporting.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Primitives;
using Lensee.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lensee.Host.Endpoints;

public static class ReportsEndpoints
{
    private const string Completed = "Completed";
    private const string Confirmed = "Confirmed";
    private const string WholesaleSale = "WholesaleSale";
    private const string RetailSale = "RetailSale";
    private const string Return = "Return";
    private const string Change = "Change";
    private const string ChangeOut = "ChangeOut";
    private const string ChangeIn = "ChangeIn";
    private const string CashReceived = "CashReceived";
    private const string CashRefund = "CashRefund";
    private const string MerchantCredit = "MerchantCredit";
    private const string BalanceReduction = "BalanceReduction";

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

        return group;
    }

    private static async Task<IResult> GetFinancialSummaryAsync(
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        CancellationToken cancellationToken)
    {
        var totalSales = await operationsDbContext.OperationLogs
            .Include(operation => operation.OperationLines)
            .Where(operation => !operation.IsDeleted && operation.Status == Completed && (operation.OperationType == WholesaleSale || operation.OperationType == RetailSale))
            .SumAsync(operation => operation.OperationLines.Sum(line => line.LineTotal), cancellationToken);

        var paymentLogs = await paymentsDbContext.MainPaymentLogs
            .Where(log => !log.IsDeleted)
            .Select(log => new { log.TotalAmount, log.AmountPaid })
            .ToListAsync(cancellationToken);

        var actualCollected = paymentLogs.Sum(log => log.AmountPaid);
        var remainingReceivable = paymentLogs.Sum(log => Math.Max(log.TotalAmount - log.AmountPaid, 0));

        return Results.Ok(new FinancialSummaryResponse(totalSales, actualCollected, remainingReceivable));
    }
    private static async Task<IResult> GetStockReportAsync(
        Guid? locationId,
        InventoryDbContext inventoryDbContext,
        CatalogDbContext catalogDbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        if (string.Equals(currentUser.Role, LenseeRoles.Accountant, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Forbid();
        }

        var query = inventoryDbContext.StockBalances
            .Include(balance => balance.Location)
            .AsQueryable();

        if (locationId.HasValue)
        {
            query = query.Where(balance => balance.LocationId == locationId.Value);
        }

        var balances = await query
            .OrderBy(balance => balance.Location.Name)
            .ThenBy(balance => balance.SkuId)
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
        CancellationToken cancellationToken)
    {
        var result = await GetStockReportAsync(locationId, inventoryDbContext, catalogDbContext, currentUser, cancellationToken);
        if (result is IValueHttpResult { Value: IEnumerable<StockReportRow> rows })
        {
            await LogExportAsync(reportingDbContext, currentUser, clock, "stock.csv", "download://reports/stock.csv", cancellationToken);
            return Csv("stock.csv", CsvHeaders(language, "Location", "Type", "SKU", "Product", "Available", "ReservedWarehouse", "ReservedRep", "Target", "Updated"), rows.Select(row => new[]
            {
                row.LocationName,
                ReportText(row.LocationType, language),
                row.SkuCode ?? row.SkuId.ToString(),
                row.ProductName ?? "",
                row.AvailableQty.ToString(),
                row.ReservedInWarehouseQty.ToString(),
                row.ReservedWithRepQty.ToString(),
                row.TargetQty?.ToString() ?? "",
                row.LastUpdated.ToString("s")
            }));
        }

        return result;
    }

    private static async Task<IResult> GetOperationsReportAsync(
        DateTime? from,
        DateTime? to,
        string? operationType,
        OperationsDbContext operationsDbContext,
        CancellationToken cancellationToken)
    {
        var query = operationsDbContext.OperationLogs
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
            .Take(500)
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
        CancellationToken cancellationToken)
    {
        var result = await GetOperationsReportAsync(from, to, operationType, operationsDbContext, cancellationToken);
        if (result is IValueHttpResult { Value: IEnumerable<OperationReportRow> rows })
        {
            await LogExportAsync(reportingDbContext, currentUser, clock, "operations.csv", "download://reports/operations.csv", cancellationToken);
            return Csv("operations.csv", CsvHeaders(language, "Operation", "Type", "Status", "Client", "Payment", "Qty", "Bonus", "Total", "Created"), rows.Select(row => new[]
            {
                row.OperationNumber,
                ReportText(row.OperationType, language),
                ReportText(row.Status, language),
                row.ClientName ?? "",
                ReportText(row.PaymentMethod ?? "", language),
                row.Quantity.ToString(),
                row.BonusQuantity.ToString(),
                row.Total.ToString("0.####"),
                row.CreatedAt.ToString("s")
            }));
        }

        return result;
    }

    private static async Task<IResult> GetPaymentsReportAsync(
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        CancellationToken cancellationToken)
    {
        var rows = await paymentsDbContext.MainPaymentLogs
            .Include(log => log.InstallmentSubLogs)
            .Where(log => !log.IsDeleted)
            .OrderByDescending(log => log.LastModifiedAt)
            .Take(500)
            .Select(log => new PaymentReportRow(
                log.Id,
                log.OperationId,
                null,
                log.MerchantId,
                log.PaymentMethod,
                log.TotalAmount,
                log.AmountPaid,
                log.TotalAmount - log.AmountPaid,
                log.Status,
                log.AssignedTo,
                log.LastModifiedAt))
            .ToListAsync(cancellationToken);

        var operationIds = rows.Select(row => row.OperationId).Distinct().ToArray();
        var operationNumbers = await operationsDbContext.OperationLogs
            .Where(operation => operationIds.Contains(operation.Id))
            .ToDictionaryAsync(operation => operation.Id, operation => operation.OperationNumber, cancellationToken);

        rows = rows.Select(row => row with
        {
            OperationNumber = operationNumbers.TryGetValue(row.OperationId, out var number) ? number : null
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
        CancellationToken cancellationToken)
    {
        var result = await GetPaymentsReportAsync(operationsDbContext, paymentsDbContext, cancellationToken);
        if (result is IValueHttpResult { Value: IEnumerable<PaymentReportRow> rows })
        {
            await LogExportAsync(reportingDbContext, currentUser, clock, "payments.csv", "download://reports/payments.csv", cancellationToken);
            return Csv("payments.csv", CsvHeaders(language, "Payment", "Operation", "Merchant", "Method", "Total", "Paid", "Remaining", "Status"), rows.Select(row => new[]
            {
                row.Id.ToString(),
                row.OperationNumber ?? row.OperationId.ToString(),
                row.MerchantId?.ToString() ?? string.Empty,
                ReportText(row.PaymentMethod, language),
                row.TotalAmount.ToString("0.####"),
                row.AmountPaid.ToString("0.####"),
                row.RemainingAmount.ToString("0.####"),
                ReportText(row.Status, language)
            }));
        }

        return result;
    }

    private static async Task<IResult> GetSupplyLandedCostReportAsync(
        DateTime? from,
        DateTime? to,
        string? status,
        OperationsDbContext operationsDbContext,
        CancellationToken cancellationToken)
    {
        var query = operationsDbContext.SupplyShipments
            .Include(shipment => shipment.Lines)
            .Include(shipment => shipment.Costs)
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
            .Take(500)
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
                shipment.InventoryReceiptOperationId))
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
        CancellationToken cancellationToken)
    {
        var result = await GetSupplyLandedCostReportAsync(from, to, status, operationsDbContext, cancellationToken);
        if (result is IValueHttpResult { Value: IEnumerable<SupplyLandedCostReportRow> rows })
        {
            await LogExportAsync(reportingDbContext, currentUser, clock, "supply-landed-cost.csv", "download://reports/supply.csv", cancellationToken);
            return Csv("supply-landed-cost.csv", CsvHeaders(language, "Shipment", "Supplier", "Invoice", "Status", "Qty", "Products", "Import costs", "Landed total", "Receipt operation", "Created"), rows.Select(row => new[]
            {
                row.ShipmentNumber,
                row.SupplierName,
                row.InvoiceNumber ?? "",
                ReportText(row.Status, language),
                row.Quantity.ToString(),
                row.ProductSubtotal.ToString("0.####"),
                row.CostSubtotal.ToString("0.####"),
                row.LandedTotal.ToString("0.####"),
                row.InventoryReceiptOperationId?.ToString("N") ?? "",
                row.ShipmentDate.ToString("s")
            }));
        }

        return result;
    }

    private static async Task<IResult> GetSupplyLandedCostPdfAsync(
        Guid id,
        string? language,
        OperationsDbContext operationsDbContext,
        IdentityDbContext identityDbContext,
        ReportingDbContext reportingDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
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

        var pdf = BuildEnterprisePdf(
            "Supply landed cost",
            "Imported shipment, cost allocation, and inventory receipt",
            shipment.ShipmentNumber,
            summary,
            sections,
            language,
            GetUserDisplayName(currentUser.UserId, userLookup));
        await LogExportAsync(reportingDbContext, currentUser, clock, "supply-landed-cost.pdf", $"download://reports/supply/{id}/landed-cost.pdf", cancellationToken);
        return Results.File(pdf, "application/pdf", $"supply-{shipment.ShipmentNumber}-landed-cost.pdf");
    }

    private static async Task<IResult> GetMerchantBalancesReportAsync(
        CrmDbContext crmDbContext,
        MerchantBalanceService merchantBalanceService,
        CancellationToken cancellationToken)
    {
        var merchants = await crmDbContext.Merchants
            .Where(merchant => !merchant.IsDeleted)
            .OrderBy(merchant => merchant.BusinessName)
            .ToListAsync(cancellationToken);

        var rows = new List<MerchantBalanceReportRow>();
        foreach (var merchant in merchants)
        {
            var balance = await merchantBalanceService.CalculateAsync(merchant.Id, cancellationToken);
            rows.Add(new MerchantBalanceReportRow(
                merchant.Id,
                merchant.BusinessName,
                merchant.Status,
                balance.SaleTotal,
                balance.ReturnTotal,
                balance.ChangeNet,
                balance.PaymentsReceived,
                balance.CashRefunded,
                balance.MerchantCredits,
                balance.BalanceReductions,
                balance.Balance));
        }

        return Results.Ok(rows);
    }

    private static async Task<IResult> GetMerchantBalancesCsvAsync(
        string? language,
        CrmDbContext crmDbContext,
        MerchantBalanceService merchantBalanceService,
        ReportingDbContext reportingDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var result = await GetMerchantBalancesReportAsync(crmDbContext, merchantBalanceService, cancellationToken);
        if (result is IValueHttpResult { Value: IEnumerable<MerchantBalanceReportRow> rows })
        {
            await LogExportAsync(reportingDbContext, currentUser, clock, "merchant-balances.csv", "download://reports/merchant-balances.csv", cancellationToken);
            return Csv("merchant-balances.csv", CsvHeaders(language, "Merchant", "Status", "Sales", "Returns", "ChangeNet", "Payments", "Refunds", "Credits", "RemainingReductions", "Remaining"), rows.Select(row => new[]
            {
                row.BusinessName,
                ReportText(row.Status, language),
                row.SaleTotal.ToString("0.####"),
                row.ReturnTotal.ToString("0.####"),
                row.ChangeNet.ToString("0.####"),
                row.PaymentsReceived.ToString("0.####"),
                row.CashRefunded.ToString("0.####"),
                row.MerchantCredits.ToString("0.####"),
                row.BalanceReductions.ToString("0.####"),
                row.Balance.ToString("0.####")
            }));
        }

        return result;
    }

    private static async Task<IResult> GetOperationBillPdfAsync(
        Guid id,
        string? language,
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        CrmDbContext crmDbContext,
        InventoryDbContext inventoryDbContext,
        IdentityDbContext identityDbContext,
        ReportingDbContext reportingDbContext,
        MerchantBalanceService merchantBalanceService,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
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
            ? await merchantBalanceService.CalculateAsync(merchant.Id, cancellationToken)
            : null;
        var totalQty = operation.OperationLines.Sum(line => line.Quantity);
        var totalBonus = operation.OperationLines.Sum(line => line.BonusQuantity);
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
                    new PdfFact("Paid to date", paymentLog is null ? FormatMoney(cashRecords.Where(value => value.PaymentType == CashReceived).Sum(value => value.Amount)) : FormatMoney(paymentLog.AmountPaid)),
                    new PdfFact("Remaining", paymentLog is null ? "-" : FormatMoney(Math.Max(paymentLog.TotalAmount - paymentLog.AmountPaid, 0))),
                    new PdfFact("Merchant balance", balance is null ? "-" : FormatMoney(balance.Balance))
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

        var pdf = BuildEnterprisePdf(
            "Operation bill",
            "Official receipt-style operation document",
            operation.OperationNumber,
            summary,
            sections,
            language,
            GetUserDisplayName(currentUser.UserId, userLookup));
        await LogExportAsync(reportingDbContext, currentUser, clock, "operation-bill.pdf", $"download://reports/operations/{id}/bill.pdf", cancellationToken);
        return Results.File(pdf, "application/pdf", $"operation-{operation.OperationNumber}.pdf");
    }

    private static async Task<IResult> GetPaymentReceiptPdfAsync(
        Guid id,
        string? language,
        PaymentsDbContext paymentsDbContext,
        OperationsDbContext operationsDbContext,
        CrmDbContext crmDbContext,
        IdentityDbContext identityDbContext,
        ReportingDbContext reportingDbContext,
        MerchantBalanceService merchantBalanceService,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
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
            ? await merchantBalanceService.CalculateAsync(log.MerchantId.Value, cancellationToken)
            : null;

        var summary = new List<PdfFact>
        {
            new("Receipt no.", DocumentRecordCode("PAY", log.Id)),
            new("Date", FormatDateTime(log.LastModifiedAt)),
            new("Merchant", merchant?.BusinessName ?? operation?.ClientName ?? "Anonymous buyer"),
            new("Method", DescribePaymentMethod(log.PaymentMethod)),
            new("Status", log.Status),
            new("Total", FormatMoney(log.TotalAmount)),
            new("Paid", FormatMoney(log.AmountPaid)),
            new("Remaining", FormatMoney(Math.Max(log.TotalAmount - log.AmountPaid, 0)))
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
                    new PdfFact("Paid amount", FormatMoney(log.AmountPaid)),
                    new PdfFact("Remaining amount", FormatMoney(Math.Max(log.TotalAmount - log.AmountPaid, 0))),
                    new PdfFact("Merchant balance", balance is null ? "-" : FormatMoney(balance.Balance))
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
        var pdf = BuildEnterprisePdf(
            isCashReceipt ? "Cash collection receipt" : "Payment receipt",
            isCashReceipt ? "Cash collection and accountant approval detail" : "Financial collection and review detail",
            DocumentRecordCode(isCashReceipt ? "CASH" : "PAY", log.Id),
            summary,
            sections,
            language,
            GetUserDisplayName(currentUser.UserId, userLookup));
        var documentName = isCashReceipt ? "cash-receipt.pdf" : "payment-receipt.pdf";
        var documentPath = isCashReceipt ? $"download://reports/payments/{id}/cash-receipt.pdf" : $"download://reports/payments/{id}/receipt.pdf";
        await LogExportAsync(reportingDbContext, currentUser, clock, documentName, documentPath, cancellationToken);
        return Results.File(pdf, "application/pdf", $"{(isCashReceipt ? "cash-receipt" : "payment")}-{log.Id:N}.pdf");
    }

    private static async Task<IResult> GetMerchantStatementPdfAsync(
        Guid merchantId,
        string? language,
        CrmDbContext crmDbContext,
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext,
        IdentityDbContext identityDbContext,
        ReportingDbContext reportingDbContext,
        MerchantBalanceService merchantBalanceService,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var merchant = await crmDbContext.Merchants.FirstOrDefaultAsync(value => value.Id == merchantId && !value.IsDeleted, cancellationToken);
        if (merchant is null)
        {
            return Results.NotFound();
        }

        var balance = await merchantBalanceService.CalculateAsync(merchantId, cancellationToken);
        var operations = await operationsDbContext.OperationLogs
            .Include(value => value.OperationLines)
            .Include(value => value.OperationVersions)
            .Where(value => value.ClientId == merchantId && !value.IsDeleted)
            .OrderByDescending(value => value.CreatedAt)
            .Take(25)
            .ToListAsync(cancellationToken);
        var notes = await crmDbContext.MerchantNotes
            .Where(value => value.MerchantId == merchantId)
            .OrderByDescending(value => value.CreatedAt)
            .Take(10)
            .ToListAsync(cancellationToken);
        var paymentLogs = await paymentsDbContext.MainPaymentLogs
            .Include(value => value.InstallmentSubLogs)
            .Where(value => value.MerchantId == merchantId && !value.IsDeleted)
            .OrderByDescending(value => value.LastModifiedAt)
            .Take(20)
            .ToListAsync(cancellationToken);
        var adjustments = await paymentsDbContext.FinancialAdjustments
            .Where(value => value.MerchantId == merchantId)
            .OrderByDescending(value => value.CreatedAt)
            .Take(20)
            .ToListAsync(cancellationToken);
        var operationIds = operations.Select(value => value.Id).ToArray();
        var cashRecords = await paymentsDbContext.CashRecords
            .Where(value => operationIds.Contains(value.OperationId))
            .OrderByDescending(value => value.PaymentDate)
            .Take(30)
            .ToListAsync(cancellationToken);
        var userLookup = await LoadUserLookupAsync(identityDbContext, operations, paymentLogs, cashRecords, adjustments, notes, cancellationToken);

        var summary = new List<PdfFact>
        {
            new("Merchant", merchant.BusinessName),
            new("Status", merchant.Status),
            new("Contact person", merchant.ContactPersonName),
            new("Phone", JoinValues(merchant.PhoneNumbers)),
            new("Email", merchant.Email ?? "-"),
            new("Address", merchant.Address ?? "-"),
            new("Business type", merchant.BusinessType),
            new("Sales total", FormatMoney(balance.SaleTotal)),
            new("Returns total", FormatMoney(balance.ReturnTotal)),
            new("Change net", FormatMoney(balance.ChangeNet)),
            new("Payments received", FormatMoney(balance.PaymentsReceived)),
            new("Cash refunded", FormatMoney(balance.CashRefunded)),
            new("Merchant credits", FormatMoney(balance.MerchantCredits)),
            new("Remaining reductions", FormatMoney(balance.BalanceReductions)),
            new("Current remaining", FormatMoney(balance.Balance))
        };

        var sections = new List<PdfSection>
        {
            new(
                "Merchant profile",
                [
                    new PdfFact("Business name", merchant.BusinessName),
                    new PdfFact("Contact person", merchant.ContactPersonName),
                    new PdfFact("Phone", JoinValues(merchant.PhoneNumbers)),
                    new PdfFact("Email", merchant.Email ?? "-"),
                    new PdfFact("Address", merchant.Address ?? "-"),
                    new PdfFact("Business type", merchant.BusinessType),
                    new PdfFact("Status", merchant.Status)
                ]),
            new(
                "Balance summary",
                [
                    new PdfFact("Sales total", FormatMoney(balance.SaleTotal)),
                    new PdfFact("Returns total", FormatMoney(balance.ReturnTotal)),
                    new PdfFact("Change net", FormatMoney(balance.ChangeNet)),
                    new PdfFact("Payments received", FormatMoney(balance.PaymentsReceived)),
                    new PdfFact("Cash refunded", FormatMoney(balance.CashRefunded)),
                    new PdfFact("Merchant credits", FormatMoney(balance.MerchantCredits)),
                    new PdfFact("Remaining reductions", FormatMoney(balance.BalanceReductions)),
                    new PdfFact("Remaining balance", FormatMoney(balance.Balance))
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
                            operation.OperationType,
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
                            log.Id.ToString("N")[..8],
                            operations.FirstOrDefault(operation => operation.Id == log.OperationId)?.OperationNumber ?? log.OperationId.ToString("N")[..8],
                            DescribePaymentMethod(log.PaymentMethod),
                            log.Status,
                            FormatMoney(log.TotalAmount),
                            FormatMoney(log.AmountPaid),
                            FormatMoney(Math.Max(log.TotalAmount - log.AmountPaid, 0)),
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
                            record.PaymentType,
                            FormatMoney(record.Amount),
                            GetUserDisplayName(record.CreatedBy, userLookup),
                            record.Notes ?? "-"
                        }).ToList(),
                        "No cash records were recorded."),
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

        var pdf = BuildEnterprisePdf(
            "Merchant statement",
            "Commercial relationship and financial position",
            DocumentRecordCode("MER", merchant.Id),
            summary,
            sections,
            language,
            GetUserDisplayName(currentUser.UserId, userLookup));
        await LogExportAsync(reportingDbContext, currentUser, clock, "merchant-statement.pdf", $"download://reports/merchants/{merchantId}/statement.pdf", cancellationToken);
        return Results.File(pdf, "application/pdf", $"merchant-{merchantId:N}-statement.pdf");
    }

    private static async Task<IResult> GetStocktakeSummaryPdfAsync(
        Guid id,
        string? language,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        InventoryDbContext inventoryDbContext,
        IdentityDbContext identityDbContext,
        ReportingDbContext reportingDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
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

        var pdf = BuildEnterprisePdf(
            "Stocktake summary",
            "Physical count and discrepancy review",
            DocumentRecordCode("STK", session.Id),
            summary,
            sections,
            language,
            GetUserDisplayName(currentUser.UserId, userLookup));
        await LogExportAsync(reportingDbContext, currentUser, clock, "stocktake-summary.pdf", $"download://reports/stocktakes/{id}/summary.pdf", cancellationToken);
        return Results.File(pdf, "application/pdf", $"stocktake-{session.Id:N}.pdf");
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

    private static IReadOnlyList<string> CsvHeaders(string? language, params string[] headers) =>
        headers.Select(header => ReportText(header, language)).ToArray();

    private static string ReportText(string value, string? language)
    {
        if (!string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return ArabicReportText(value);
    }

    private static IResult Csv(string fileName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", headers.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", row.Select(EscapeCsv)));
        }

        return Results.File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv; charset=utf-8", fileName);
    }

    private static string EscapeCsv(string value)
    {
        var safe = value ?? string.Empty;
        return safe.Contains(',') || safe.Contains('"') || safe.Contains('\n') || safe.Contains('\r')
            ? $"\"{safe.Replace("\"", "\"\"")}\""
            : safe;
    }

    private static byte[] BuildEnterprisePdf(
        string title,
        string subtitle,
        string documentReference,
        IReadOnlyList<PdfFact> summaryFacts,
        IReadOnlyList<PdfSection> sections,
        string? language = null,
        string? generatedBy = null)
    {
        return BuildTemplatePdf(title, subtitle, documentReference, summaryFacts, sections, language, generatedBy);

    }

    private static byte[] BuildTemplatePdf(
        string title,
        string subtitle,
        string documentReference,
        IReadOnlyList<PdfFact> summaryFacts,
        IReadOnlyList<PdfSection> sections,
        string? language,
        string? generatedBy)
    {
        var arabic = string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase);
        var kind = GetPdfDocumentKind(title);
        var fontFamily = arabic
            ? (OperatingSystem.IsWindows() ? "Tahoma" : "Noto Sans Arabic")
            : (OperatingSystem.IsWindows() ? "Arial" : "Noto Sans");

        sections = NormalizeTemplateSections(kind, sections);

        if (arabic)
        {
            title = ArabicReportText(title);
            subtitle = ArabicReportText(subtitle);
            summaryFacts = summaryFacts.Select(fact => fact with { Label = ArabicReportText(fact.Label), Value = ArabicReportText(fact.Value) }).ToArray();
            sections = sections.Select(section => section with
            {
                Title = ArabicReportText(section.Title),
                Facts = section.Facts?.Select(fact => fact with { Label = ArabicReportText(fact.Label), Value = ArabicReportText(fact.Value) }).ToArray(),
                Tables = section.Tables?.Select(table => table with
                {
                    Title = ArabicReportText(table.Title),
                    Headers = table.Headers.Select(ArabicReportText).ToArray(),
                    Rows = table.Rows.Select(row => row.Select(ArabicReportText).ToArray()).ToArray(),
                    EmptyMessage = ArabicReportText(table.EmptyMessage)
                }).ToArray(),
                Note = section.Note is null ? null : ArabicReportText(section.Note)
            }).ToArray();
        }

        var statusLabel = arabic ? ArabicReportText("Status") : "Status";
        var status = summaryFacts.FirstOrDefault(fact => string.Equals(fact.Label, statusLabel, StringComparison.OrdinalIgnoreCase))?.Value;
        var overviewCount = Math.Min(kind == PdfDocumentKind.MerchantStatement ? 6 : 5, summaryFacts.Count);
        var overviewFacts = summaryFacts.Take(overviewCount).ToArray();
        var metricFacts = summaryFacts.Skip(overviewCount).Take(4).ToArray();
        var landscape = kind is PdfDocumentKind.SupplyLandedCost or PdfDocumentKind.StocktakeSummary;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(18);
                page.DefaultTextStyle(text => text.FontFamily(fontFamily).FontSize(8.5f).FontColor(Colors.Grey.Darken3));
                page.Header().Element(item => RenderDocumentHeader(item, title, subtitle, documentReference, status, arabic));
                page.Content().PaddingTop(8).Column(column =>
                {
                    if (kind == PdfDocumentKind.CashReceipt)
                    {
                        var paidFact = summaryFacts.FirstOrDefault(fact => fact.Label.Contains(arabic ? "\u0627\u0644\u0645\u062f\u0641\u0648\u0639" : "Paid", StringComparison.OrdinalIgnoreCase));
                        var totalFact = summaryFacts.FirstOrDefault(fact => fact.Label.Contains(arabic ? "\u0627\u0644\u0625\u062c\u0645\u0627\u0644\u064a" : "Total", StringComparison.OrdinalIgnoreCase));
                        var amount = paidFact?.Value ?? totalFact?.Value;
                        if (!string.IsNullOrWhiteSpace(amount))
                        {
                            column.Item().Element(item => RenderCashAmount(item, amount, arabic));
                        }
                    }

                    if (overviewFacts.Length > 0)
                    {
                        column.Item().PaddingTop(4).Element(item => RenderSectionHeading(item, TemplateText("overview", arabic), arabic));
                        column.Item().PaddingTop(5).Element(item => RenderOverviewPanel(item, overviewFacts, arabic));
                    }

                    if (metricFacts.Length > 0)
                    {
                        column.Item().PaddingTop(8).Element(item => RenderFactGrid(item, metricFacts, arabic, Math.Min(4, metricFacts.Length), false));
                    }

                    if (kind == PdfDocumentKind.SupplyLandedCost)
                    {
                        var shipmentData = sections.FirstOrDefault(section => section.Title == "Shipment data");
                        var lines = sections.FirstOrDefault(section => section.Title == "Lines");
                        var costBreakdown = sections.FirstOrDefault(section => section.Title == "Cost breakdown");
                        var history = sections.FirstOrDefault(section => section.Title == "History");

                        if (shipmentData is not null) RenderPdfSection(column, shipmentData, arabic);
                        if (lines is not null) RenderPdfSection(column, lines, arabic);
                        if (costBreakdown is not null || history is not null)
                        {
                            column.Item().PaddingTop(10).Row(row =>
                            {
                                if (arabic)
                                {
                                    if (history is not null) row.RelativeItem().Column(item => RenderPdfSection(item, history, arabic));
                                    row.ConstantItem(12);
                                    if (costBreakdown is not null) row.RelativeItem().Column(item => RenderPdfSection(item, costBreakdown, arabic));
                                }
                                else
                                {
                                    if (costBreakdown is not null) row.RelativeItem().Column(item => RenderPdfSection(item, costBreakdown, arabic));
                                    row.ConstantItem(12);
                                    if (history is not null) row.RelativeItem().Column(item => RenderPdfSection(item, history, arabic));
                                }
                            });
                        }
                    }
                    else
                    {
                        foreach (var section in sections)
                        {
                            RenderPdfSection(column, section, arabic);
                        }
                    }

                    column.Item().PaddingTop(12).Element(item => RenderSignatureBlocks(item, kind, arabic));
                });
                page.Footer().Element(item => RenderDocumentFooter(item, documentReference, kind, arabic, generatedBy));
            });
        }).GeneratePdf();
    }

    private static void RenderPdfSection(ColumnDescriptor column, PdfSection section, bool arabic)
    {
        column.Item().PaddingTop(10).Element(item => RenderSectionHeading(item, section.Title, arabic));
        if (section.Facts is { Count: > 0 })
        {
            var compact = IsMetricSection(section.Title, arabic);
            column.Item().PaddingTop(5).Element(item =>
            {
                if (compact)
                {
                    RenderFactGrid(item, section.Facts, arabic, Math.Min(4, section.Facts.Count), false);
                }
                else
                {
                    RenderOverviewPanel(item, section.Facts, arabic);
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(section.Note))
        {
            column.Item().PaddingTop(5).Element(item => RenderNote(item, section.Note, arabic));
        }

        foreach (var tableSection in section.Tables ?? [])
        {
            column.Item().PaddingTop(6).Text(tableSection.Title).SemiBold().FontSize(8.5f).FontColor(Colors.Grey.Darken4);
            column.Item().PaddingTop(3).Element(item => RenderPdfTable(item, tableSection, arabic));
        }
    }

    private static PdfDocumentKind GetPdfDocumentKind(string title) => title switch
    {
        "Operation bill" => PdfDocumentKind.OperationBill,
        "Payment receipt" => PdfDocumentKind.PaymentReceipt,
        "Cash receive receipt" or "Cash collection receipt" => PdfDocumentKind.CashReceipt,
        "Supply landed cost" => PdfDocumentKind.SupplyLandedCost,
        "Merchant statement" => PdfDocumentKind.MerchantStatement,
        "Stocktake summary" => PdfDocumentKind.StocktakeSummary,
        _ => PdfDocumentKind.Generic
    };

    private static IReadOnlyList<PdfSection> NormalizeTemplateSections(PdfDocumentKind kind, IReadOnlyList<PdfSection> sections)
    {
        if (kind != PdfDocumentKind.CashReceipt)
        {
            return sections;
        }

        var merchant = sections.FirstOrDefault(section => section.Title == "Merchant");
        var operation = sections.FirstOrDefault(section => section.Title == "Operation");
        var payment = sections.FirstOrDefault(section => section.Title == "Payment");
        var entries = sections.FirstOrDefault(section => section.Title == "Payment entries");
        var custodyFacts = (merchant?.Facts ?? []).Concat(operation?.Facts ?? []).ToArray();
        var normalized = new List<PdfSection>();

        if (custodyFacts.Length > 0)
        {
            normalized.Add(new PdfSection("Cash custody details", custodyFacts));
        }

        if (payment is not null)
        {
            normalized.Add(payment with { Title = "Related account movement" });
        }

        if (entries is not null)
        {
            normalized.Add(entries with { Title = "Custody trail" });
        }

        return normalized;
    }

    // Stable display references keep printed records traceable without exposing a shortened GUID.
    private static string DocumentRecordCode(string prefix, Guid id)
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var value = new BigInteger(id.ToByteArray(), isUnsigned: true, isBigEndian: false);
        var characters = new char[26];
        for (var index = characters.Length - 1; index >= 0; index--)
        {
            characters[index] = alphabet[(int)(value & 31)];
            value >>= 5;
        }

        return $"{prefix}-{new string(characters)}";
    }

    private static void RenderDocumentHeader(IContainer container, string title, string subtitle, string reference, string? status, bool arabic)
    {
        container.BorderTop(4).BorderColor(Colors.Grey.Darken4).PaddingTop(14).Column(column =>
        {
            column.Item().Row(row =>
            {
                if (arabic)
                {
                    row.RelativeItem(1.4f).Element(item => RenderDocumentIdentity(item, title, subtitle, reference, status, false));
                    row.RelativeItem().AlignRight().Element(item => RenderBrand(item, true));
                }
                else
                {
                    row.RelativeItem().Element(item => RenderBrand(item, false));
                    row.RelativeItem(1.4f).Element(item => RenderDocumentIdentity(item, title, subtitle, reference, status, true));
                }
            });
            AlignByLanguage(column.Item().PaddingTop(9), arabic).Text(TemplateText("internal document", arabic)).SemiBold().FontSize(6.5f).FontColor(Colors.Grey.Darken1);
            column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
        });
    }

    private static void RenderBrand(IContainer container, bool arabic)
    {
        container.Column(column =>
        {
            AlignByLanguage(column.Item(), arabic).Text("LENSEE").SemiBold().FontSize(18).FontColor(Colors.Grey.Darken4);
            AlignByLanguage(column.Item(), arabic).Text(arabic ? "نظام إدارة العمليات" : "OPTICAL OPERATIONS").SemiBold().FontSize(6).FontColor(Colors.Grey.Darken1);
        });
    }

    private static void RenderDocumentIdentity(IContainer container, string title, string subtitle, string reference, string? status, bool alignRight)
    {
        container.Column(column =>
        {
            AlignByLanguage(column.Item(), alignRight).Text(title).SemiBold().FontSize(16).FontColor(Colors.Grey.Darken4);
            AlignByLanguage(column.Item(), alignRight).Text(subtitle).FontSize(8).FontColor(Colors.Grey.Darken1);
            column.Item().PaddingTop(8).Row(row =>
            {
                AlignByLanguage(row.ConstantItem(112).ScaleToFit(), alignRight).Text(reference).SemiBold().FontSize(reference.Length > 25 ? 7 : 9).FontColor(Colors.Grey.Darken4);
                if (!string.IsNullOrWhiteSpace(status))
                {
                    row.ConstantItem(88).Border(1).BorderColor(Colors.Grey.Lighten1).PaddingVertical(5).AlignCenter().Text(status).SemiBold().FontSize(6.5f).FontColor(Colors.Grey.Darken4);
                }
            });
        });
    }

    private static void RenderSectionHeading(IContainer container, string title, bool arabic)
    {
        container.Column(column =>
        {
            AlignByLanguage(column.Item(), arabic).Text(title).SemiBold().FontSize(9).FontColor(Colors.Grey.Darken4);
            column.Item().PaddingTop(3).Row(row =>
            {
                if (arabic)
                {
                    row.RelativeItem();
                    row.ConstantItem(30).LineHorizontal(2).LineColor(Colors.Grey.Darken4);
                }
                else
                {
                    row.ConstantItem(30).LineHorizontal(2).LineColor(Colors.Grey.Darken4);
                    row.RelativeItem();
                }
            });
        });
    }

    private static void RenderFactGrid(IContainer container, IReadOnlyList<PdfFact> facts, bool arabic, int columnsCount, bool accent)
    {
        var orderedFacts = arabic ? facts.Reverse().ToArray() : facts.ToArray();
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                for (var index = 0; index < columnsCount; index++)
                {
                    columns.RelativeColumn();
                }
            });

            foreach (var fact in orderedFacts)
            {
                var cell = table.Cell().Padding(2).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(7);
                if (accent)
                {
                    cell = arabic
                        ? cell.BorderRight(4).BorderColor(Colors.Grey.Darken3)
                        : cell.BorderLeft(4).BorderColor(Colors.Grey.Darken3);
                }

                cell.Column(column =>
                {
                    AlignByLanguage(column.Item(), arabic).Text(fact.Label).SemiBold().FontSize(6.5f).FontColor(Colors.Grey.Darken1);
                    AlignByLanguage(column.Item().PaddingTop(3), arabic).Text(fact.Value).SemiBold().FontSize(9.5f).FontColor(Colors.Grey.Darken4);
                });
            }

            for (var index = orderedFacts.Length; index % columnsCount != 0; index++)
            {
                table.Cell().Padding(2);
            }
        });
    }

    private static void RenderOverviewPanel(IContainer container, IReadOnlyList<PdfFact> facts, bool arabic)
    {
        var orderedFacts = arabic ? facts.Reverse().ToArray() : facts.ToArray();
        container.Border(1).BorderColor(Colors.Grey.Lighten2).Background(Colors.Grey.Lighten5).Row(row =>
        {
            if (arabic)
            {
                row.ConstantItem(4).Background(Colors.Grey.Darken3);
                row.RelativeItem().Padding(8).Table(table => RenderOverviewFacts(table, orderedFacts, arabic));
            }
            else
            {
                row.ConstantItem(4).Background(Colors.Grey.Darken3);
                row.RelativeItem().Padding(8).Table(table => RenderOverviewFacts(table, orderedFacts, arabic));
            }
        });
    }

    private static void RenderOverviewFacts(TableDescriptor table, IReadOnlyList<PdfFact> facts, bool arabic)
    {
        table.ColumnsDefinition(columns =>
        {
            columns.RelativeColumn();
            columns.RelativeColumn();
        });

        foreach (var fact in facts)
        {
            table.Cell().PaddingVertical(3).Column(column =>
            {
                AlignByLanguage(column.Item(), arabic).Text(fact.Label).SemiBold().FontSize(6.5f).FontColor(Colors.Grey.Darken1);
                AlignByLanguage(column.Item().PaddingTop(2), arabic).Text(fact.Value).SemiBold().FontSize(9).FontColor(Colors.Grey.Darken4);
            });
        }

        for (var index = facts.Count; index % 2 != 0; index++)
        {
            table.Cell();
        }
    }

    private static void RenderCashAmount(IContainer container, string amount, bool arabic)
    {
        container.Background(Colors.Grey.Lighten4).Padding(12).Row(row =>
        {
            if (arabic)
            {
                row.RelativeItem().Text(amount).SemiBold().FontSize(21).FontColor(Colors.Grey.Darken4);
                row.RelativeItem().AlignRight().Column(column =>
                {
                    column.Item().Text(TemplateText("payment type", true)).SemiBold().FontSize(7).FontColor(Colors.Grey.Darken1);
                    column.Item().Text(TemplateText("cash hand to hand", true)).SemiBold().FontSize(10).FontColor(Colors.Grey.Darken4);
                });
            }
            else
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text(TemplateText("payment type", false)).SemiBold().FontSize(7).FontColor(Colors.Grey.Darken1);
                    column.Item().Text(TemplateText("cash hand to hand", false)).SemiBold().FontSize(10).FontColor(Colors.Grey.Darken4);
                });
                row.RelativeItem().AlignRight().Text(amount).SemiBold().FontSize(21).FontColor(Colors.Grey.Darken4);
            }
        });
    }

    private static void RenderNote(IContainer container, string note, bool arabic)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten2).Background(Colors.Grey.Lighten5).Padding(8).Column(column =>
        {
            AlignByLanguage(column.Item(), arabic).Text(TemplateText("note", arabic)).SemiBold().FontSize(6.5f).FontColor(Colors.Grey.Darken1);
            AlignByLanguage(column.Item().PaddingTop(4), arabic).Text(note).FontSize(8).FontColor(Colors.Grey.Darken4);
        });
    }

    private static void RenderPdfTable(IContainer container, PdfTableSection tableSection, bool arabic)
    {
        var headers = arabic ? tableSection.Headers.Reverse().ToArray() : tableSection.Headers.ToArray();
        var rows = arabic
            ? tableSection.Rows.Select(row => (IReadOnlyList<string>)row.Reverse().ToArray()).ToArray()
            : tableSection.Rows.ToArray();

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                foreach (var _ in headers)
                {
                    columns.RelativeColumn();
                }
            });
            table.Header(header =>
            {
                foreach (var headerText in headers)
                {
                    AlignByLanguage(header.Cell().Background(Colors.Grey.Lighten3).Padding(5), arabic).Text(headerText).SemiBold().FontSize(6.5f).FontColor(Colors.Grey.Darken4);
                }
            });

            if (rows.Length == 0)
            {
                AlignByLanguage(table.Cell().ColumnSpan((uint)headers.Length).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(8), arabic).Text(tableSection.EmptyMessage).FontColor(Colors.Grey.Darken1);
            }

            for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                foreach (var value in rows[rowIndex])
                {
                    var cell = table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2);
                    if (rowIndex % 2 == 1)
                    {
                        cell = cell.Background(Colors.Grey.Lighten5);
                    }

                    AlignByLanguage(cell.Padding(5), arabic).Text(value ?? string.Empty).FontSize(7.2f);
                }
            }
        });
    }

    private static void RenderSignatureBlocks(IContainer container, PdfDocumentKind kind, bool arabic)
    {
        var labels = kind switch
        {
            PdfDocumentKind.OperationBill => new[] { "prepared by", "warehouse representative", "merchant customer", "approved by" },
            PdfDocumentKind.PaymentReceipt => new[] { "recorded by", "accountant approval", "merchant acknowledgment" },
            PdfDocumentKind.CashReceipt => new[] { "cash handed over by", "cash received by", "accountant approval" },
            PdfDocumentKind.SupplyLandedCost => Array.Empty<string>(),
            PdfDocumentKind.MerchantStatement => new[] { "prepared by", "merchant acknowledgment" },
            PdfDocumentKind.StocktakeSummary => new[] { "counted by", "reviewed by", "confirmed by" },
            _ => Array.Empty<string>()
        };

        if (labels.Length == 0)
        {
            return;
        }

        container.ShowEntire().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                foreach (var _ in labels)
                {
                    columns.RelativeColumn();
                }
            });

            foreach (var label in arabic ? labels.Reverse() : labels)
            {
                table.Cell().Padding(3).Border(1).BorderColor(Colors.Grey.Lighten1).Padding(9).Column(column =>
                {
                    AlignByLanguage(column.Item(), arabic).Text(TemplateText(label, arabic)).SemiBold().FontSize(6.5f).FontColor(Colors.Grey.Darken1);
                    column.Item().PaddingTop(34).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                    AlignByLanguage(column.Item().PaddingTop(3), arabic).Text(TemplateText("name signature date", arabic)).FontSize(6).FontColor(Colors.Grey.Darken1);
                });
            }
        });
    }

    private static void RenderDocumentFooter(IContainer container, string reference, PdfDocumentKind kind, bool arabic, string? generatedBy)
    {
        container.BorderTop(1).BorderColor(Colors.Grey.Lighten2).PaddingTop(7).Row(row =>
        {
            if (arabic)
            {
                row.RelativeItem().Text(TemplateText("internal document", true)).FontSize(6.5f).FontColor(Colors.Grey.Darken1).AlignRight();
                row.RelativeItem().AlignCenter().Text(reference).FontSize(6.5f).FontColor(Colors.Grey.Darken1);
            }
            else
            {
                row.RelativeItem().Text("Lensee ERP | Internal business document").FontSize(6.5f).FontColor(Colors.Grey.Darken1);
                row.RelativeItem().AlignCenter().Text($"Reference: {reference}").FontSize(6.5f).FontColor(Colors.Grey.Darken1);
            }

            row.RelativeItem().AlignRight().Text(text =>
            {
                text.Span(arabic ? "صفحة " : "Page ").FontSize(6.5f);
                text.CurrentPageNumber().FontSize(6.5f);
                text.Span(arabic ? " من " : " of ").FontSize(6.5f);
                text.TotalPages().FontSize(6.5f);
            });
        });
    }

    private static bool IsMetricSection(string title, bool arabic)
    {
        var normalized = title.ToLowerInvariant();
        return normalized.Contains(arabic ? "ملخص" : "summary") ||
            normalized.Contains(arabic ? "دفع" : "payment") ||
            normalized.Contains(arabic ? "رصيد" : "balance");
    }

    private static IContainer AlignByLanguage(IContainer container, bool alignRight) =>
        alignRight ? container.AlignRight() : container;

    private static string TemplateText(string key, bool arabic)
    {
        if (!arabic)
        {
            return key switch
            {
                "overview" => "OVERVIEW",
                "internal document" => "LENSEE ERP - INTERNAL BUSINESS DOCUMENT",
                "payment type" => "PAYMENT TYPE",
                "cash hand to hand" => "CASH HAND-TO-HAND",
                "note" => "NOTE",
                "prepared by" => "PREPARED BY",
                "warehouse representative" => "WAREHOUSE REPRESENTATIVE",
                "merchant customer" => "MERCHANT / CUSTOMER",
                "approved by" => "APPROVED BY",
                "recorded by" => "RECORDED BY",
                "accountant approval" => "ACCOUNTANT APPROVAL",
                "merchant acknowledgment" => "MERCHANT ACKNOWLEDGMENT",
                "cash handed over by" => "CASH HANDED OVER BY",
                "cash received by" => "CASH RECEIVED BY",
                "procurement review" => "PROCUREMENT REVIEW",
                "inventory posted by" => "INVENTORY POSTED BY",
                "counted by" => "COUNTED BY",
                "reviewed by" => "REVIEWED BY",
                "confirmed by" => "CONFIRMED BY",
                "name signature date" => "Name / signature / date",
                _ => key
            };
        }

        return key switch
        {
            "overview" => "بيانات المستند",
            "internal document" => "نظام لينسي - مستند أعمال داخلي",
            "payment type" => "نوع الدفع",
            "cash hand to hand" => "تسليم نقدي باليد",
            "note" => "ملاحظة",
            "prepared by" => "أعده",
            "warehouse representative" => "مسؤول المخزن",
            "merchant customer" => "التاجر / العميل",
            "approved by" => "اعتمده",
            "recorded by" => "سجله",
            "accountant approval" => "اعتماد المحاسب",
            "merchant acknowledgment" => "إقرار التاجر",
            "cash handed over by" => "مسلم النقدية",
            "cash received by" => "مستلم النقدية",
            "procurement review" => "مراجعة المشتريات",
            "inventory posted by" => "ترحيل المخزون",
            "counted by" => "قام بالجرد",
            "reviewed by" => "راجعه",
            "confirmed by" => "اعتمده",
            "name signature date" => "الاسم / التوقيع / التاريخ",
            _ => key
        };
    }

    private enum PdfDocumentKind
    {
        Generic,
        OperationBill,
        PaymentReceipt,
        CashReceipt,
        SupplyLandedCost,
        MerchantStatement,
        StocktakeSummary
    }

    private static string ArabicReportText(string value)
    {
        var receiptText = value switch
        {
            "Operation bill" => "\u0641\u0627\u062a\u0648\u0631\u0629 \u0639\u0645\u0644\u064a\u0629",
            "Official receipt-style operation document" => "\u0645\u0633\u062a\u0646\u062f \u0631\u0633\u0645\u064a \u0645\u062e\u062a\u0635\u0631 \u0644\u0644\u0639\u0645\u0644\u064a\u0629",
            "Payment receipt" => "\u0625\u064a\u0635\u0627\u0644 \u0633\u062f\u0627\u062f",
            "Cash receive receipt" => "\u0625\u064a\u0635\u0627\u0644 \u0627\u0633\u062a\u0644\u0627\u0645 \u0646\u0642\u062f\u064a\u0629",
            "Cash collection receipt" => "\u0625\u064a\u0635\u0627\u0644 \u062a\u062d\u0635\u064a\u0644 \u0646\u0642\u062f\u064a",
            "Cash custody details" => "\u0628\u064a\u0627\u0646\u0627\u062a \u062d\u064a\u0627\u0632\u0629 \u0627\u0644\u0646\u0642\u062f\u064a\u0629",
            "Related account movement" => "\u062d\u0631\u0643\u0629 \u0627\u0644\u062d\u0633\u0627\u0628 \u0627\u0644\u0645\u0631\u062a\u0628\u0637\u0629",
            "Custody trail" => "\u0645\u0633\u0627\u0631 \u062d\u064a\u0627\u0632\u0629 \u0627\u0644\u0646\u0642\u062f\u064a\u0629",
            "Supply landed cost" => "\u062a\u0643\u0644\u0641\u0629 \u0627\u0644\u062a\u0648\u0631\u064a\u062f \u0627\u0644\u0646\u0647\u0627\u0626\u064a\u0629",
            "Imported shipment, cost allocation, and inventory receipt" => "\u0634\u062d\u0646\u0629 \u0645\u0633\u062a\u0648\u0631\u062f\u0629 \u0648\u062a\u0648\u0632\u064a\u0639 \u062a\u0643\u0627\u0644\u064a\u0641 \u0648\u0625\u064a\u0635\u0627\u0644 \u0645\u062e\u0632\u0648\u0646",
            "Summary" => "\u0645\u0644\u062e\u0635",
            "Parties" => "\u0627\u0644\u0623\u0637\u0631\u0627\u0641",
            "Lines" => "\u0627\u0644\u0628\u0646\u0648\u062f",
            "Payment Summary" => "\u0645\u0644\u062e\u0635 \u0627\u0644\u0633\u062f\u0627\u062f",
            "Timeline" => "\u0627\u0644\u0645\u0633\u0627\u0631",
            "Merchant" => "\u0627\u0644\u062a\u0627\u062c\u0631",
            "Operation" => "\u0627\u0644\u0639\u0645\u0644\u064a\u0629",
            "Payment" => "\u0627\u0644\u0633\u062f\u0627\u062f",
            "Payment entries" => "\u0628\u0646\u0648\u062f \u0627\u0644\u0633\u062f\u0627\u062f",
            "Operation lines" => "\u0628\u0646\u0648\u062f \u0627\u0644\u0639\u0645\u0644\u064a\u0629",
            "Shipment" => "\u0627\u0644\u0634\u062d\u0646\u0629",
            "Shipment data" => "\u0628\u064a\u0627\u0646\u0627\u062a \u0627\u0644\u0634\u062d\u0646\u0629",
            "Shipment date" => "\u062a\u0627\u0631\u064a\u062e \u0627\u0644\u0634\u062d\u0646\u0629",
            "Supplier" => "\u0627\u0644\u0645\u0648\u0631\u062f",
            "Invoice" => "\u0627\u0644\u0641\u0627\u062a\u0648\u0631\u0629",
            "Products" => "\u0627\u0644\u0645\u0646\u062a\u062c\u0627\u062a",
            "Import costs" => "\u062a\u0643\u0627\u0644\u064a\u0641 \u0627\u0644\u0627\u0633\u062a\u064a\u0631\u0627\u062f",
            "Landed total" => "\u0627\u0644\u0625\u062c\u0645\u0627\u0644\u064a \u0628\u0639\u062f \u0627\u0644\u062a\u0643\u0644\u0641\u0629",
            "Receipt operation" => "\u0639\u0645\u0644\u064a\u0629 \u0625\u064a\u0635\u0627\u0644 \u0627\u0644\u0645\u062e\u0632\u0648\u0646",
            "SKU landed costs" => "\u062a\u0643\u0627\u0644\u064a\u0641 SKU \u0627\u0644\u0646\u0647\u0627\u0626\u064a\u0629",
            "Allocated" => "\u0627\u0644\u0645\u0648\u0632\u0639",
            "Landed unit" => "\u062a\u0643\u0644\u0641\u0629 \u0627\u0644\u0648\u062d\u062f\u0629 \u0627\u0644\u0646\u0647\u0627\u0626\u064a\u0629",
            "Cost breakdown" => "\u062a\u0641\u0635\u064a\u0644 \u0627\u0644\u062a\u0643\u0627\u0644\u064a\u0641",
            "Supply history" => "\u0633\u062c\u0644 \u0627\u0644\u062a\u0648\u0631\u064a\u062f",
            "Action" => "\u0627\u0644\u0625\u062c\u0631\u0627\u0621",
            "Date" => "\u0627\u0644\u062a\u0627\u0631\u064a\u062e",
            "Type" => "\u0627\u0644\u0646\u0648\u0639",
            "Status" => "\u0627\u0644\u062d\u0627\u0644\u0629",
            "Customer" => "\u0627\u0644\u0639\u0645\u064a\u0644",
            "Representative" => "\u0627\u0644\u0645\u0646\u062f\u0648\u0628",
            "Source" => "\u0627\u0644\u0645\u0635\u062f\u0631",
            "Destination" => "\u0627\u0644\u0648\u062c\u0647\u0629",
            "Payment method" => "\u0637\u0631\u064a\u0642\u0629 \u0627\u0644\u0633\u062f\u0627\u062f",
            "Total quantity" => "\u0625\u062c\u0645\u0627\u0644\u064a \u0627\u0644\u0643\u0645\u064a\u0629",
            "Document total" => "\u0625\u062c\u0645\u0627\u0644\u064a \u0627\u0644\u0645\u0633\u062a\u0646\u062f",
            "Operation total" => "\u0625\u062c\u0645\u0627\u0644\u064a \u0627\u0644\u0639\u0645\u0644\u064a\u0629",
            "Paid to date" => "\u0627\u0644\u0645\u062f\u0641\u0648\u0639",
            "Remaining" => "\u0627\u0644\u0645\u062a\u0628\u0642\u064a",
            "Merchant balance" => "\u0631\u0635\u064a\u062f \u0627\u0644\u062a\u0627\u062c\u0631",
            "SKU" => "\u0643\u0648\u062f \u0627\u0644\u0635\u0646\u0641",
            "Product" => "\u0627\u0644\u0645\u0646\u062a\u062c",
            "Side" => "\u0627\u0644\u062c\u0627\u0646\u0628",
            "Returned" => "\u0627\u0644\u0645\u0631\u062a\u062c\u0639",
            "Replacement" => "\u0627\u0644\u0628\u062f\u064a\u0644",
            "Qty" => "\u0627\u0644\u0643\u0645\u064a\u0629",
            "Bonus" => "\u0628\u0648\u0646\u0635",
            "Unit price" => "\u0633\u0639\u0631 \u0627\u0644\u0648\u062d\u062f\u0629",
            "Total" => "\u0627\u0644\u0625\u062c\u0645\u0627\u0644\u064a",
            "Step" => "\u0627\u0644\u062e\u0637\u0648\u0629",
            "Actor" => "\u0627\u0644\u0645\u0633\u0624\u0648\u0644",
            "At" => "\u0627\u0644\u0648\u0642\u062a",
            "Receipt no." => "\u0631\u0642\u0645 \u0627\u0644\u0625\u064a\u0635\u0627\u0644",
            "Created" => "\u062a\u0645 \u0627\u0644\u0625\u0646\u0634\u0627\u0621",
            "Confirmed / last action" => "\u0627\u0644\u062a\u0623\u0643\u064a\u062f / \u0622\u062e\u0631 \u0625\u062c\u0631\u0627\u0621",
            "Method" => "\u0627\u0644\u0637\u0631\u064a\u0642\u0629",
            "Paid" => "\u0627\u0644\u0645\u062f\u0641\u0648\u0639",
            "Merchant / buyer" => "\u0627\u0644\u062a\u0627\u062c\u0631 / \u0627\u0644\u0645\u0634\u062a\u0631\u064a",
            "Contact person" => "\u0645\u0633\u0624\u0648\u0644 \u0627\u0644\u062a\u0648\u0627\u0635\u0644",
            "Phone" => "\u0627\u0644\u0647\u0627\u062a\u0641",
            "Total amount" => "\u0625\u062c\u0645\u0627\u0644\u064a \u0627\u0644\u0645\u0628\u0644\u063a",
            "Paid amount" => "\u0627\u0644\u0645\u0628\u0644\u063a \u0627\u0644\u0645\u062f\u0641\u0648\u0639",
            "Remaining amount" => "\u0627\u0644\u0645\u0628\u0644\u063a \u0627\u0644\u0645\u062a\u0628\u0642\u064a",
            "Payments" => "\u0627\u0644\u0645\u062f\u0641\u0648\u0639\u0627\u062a",
            "Operation no." => "\u0631\u0642\u0645 \u0627\u0644\u0639\u0645\u0644\u064a\u0629",
            "Completed" => "\u0645\u0643\u062a\u0645\u0644",
            "Confirmed" => "\u0645\u0624\u0643\u062f",
            "PendingAdminReview" => "\u0628\u0627\u0646\u062a\u0638\u0627\u0631 \u0645\u0631\u0627\u062c\u0639\u0629 \u0627\u0644\u0645\u062f\u064a\u0631",
            "WholesaleSale" => "\u0628\u064a\u0639 \u062c\u0645\u0644\u0629",
            "RetailSale" => "\u0628\u064a\u0639 \u0642\u0637\u0627\u0639\u064a",
            "CashHandToHand" or "Cash hand to hand" => "\u0646\u0642\u062f\u064a \u064a\u062f \u0628\u064a\u062f",
            "CashTransaction" or "Cash transaction" => "\u062a\u062d\u0648\u064a\u0644 \u0646\u0642\u062f\u064a",
            "Installment" => "\u062a\u0642\u0633\u064a\u0637",
            _ => null
        };
        if (receiptText is not null)
        {
            return receiptText;
        }

        var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Operation document"] = "مستند العملية",
            ["Payment receipt"] = "إيصال المدفوعة",
            ["Cash receive receipt"] = "إيصال استلام نقدية",
            ["Merchant statement"] = "كشف حساب التاجر",
            ["Stocktake summary"] = "ملخص الجرد",
            ["Commercial and stock execution detail"] = "تفاصيل التنفيذ التجاري والمخزني",
            ["Financial collection and review detail"] = "تفاصيل التحصيل والمراجعة المالية",
            ["Cash collection and accountant approval detail"] = "تفاصيل استلام النقدية واعتماد المحاسب",
            ["Commercial relationship and financial position"] = "العلاقة التجارية والموقف المالي",
            ["Physical count and discrepancy review"] = "الجرد الفعلي ومراجعة الفروقات",
            ["Header"] = "الرأس",
            ["Parties"] = "الأطراف",
            ["Lines"] = "البنود",
            ["Payment Summary"] = "ملخص السداد",
            ["Payment Details"] = "تفاصيل السداد",
            ["Timeline"] = "الخط الزمني",
            ["Version History"] = "سجل الإصدارات",
            ["Merchant profile"] = "ملف التاجر",
            ["Balance summary"] = "ملخص الرصيد",
            ["Operations history"] = "سجل العمليات",
            ["Payment history"] = "سجل السداد",
            ["Installments"] = "الأقساط",
            ["Trail"] = "المسار",
            ["Actors"] = "المسؤولون",
            ["Adjustment Lines"] = "بنود التسوية",
            ["Date"] = "التاريخ",
            ["Type"] = "النوع",
            ["Status"] = "الحالة",
            ["Amount"] = "المبلغ",
            ["Created by"] = "أنشأه",
            ["Created"] = "تاريخ الإنشاء",
            ["Created at"] = "تاريخ الإنشاء",
            ["Confirmed at"] = "تاريخ التأكيد",
            ["Confirmed by"] = "أكده",
            ["Performed by"] = "قام به",
            ["Session date"] = "تاريخ الجلسة",
            ["Notes"] = "ملاحظات",
            ["Customer"] = "العميل",
            ["Registered merchant"] = "التاجر المسجل",
            ["Contact person"] = "مسؤول التواصل",
            ["Phone"] = "الهاتف",
            ["Email"] = "البريد الإلكتروني",
            ["Address"] = "العنوان",
            ["Business name"] = "اسم النشاط",
            ["Business type"] = "نوع النشاط",
            ["Merchant status"] = "حالة التاجر",
            ["Representative"] = "المندوب",
            ["Source"] = "المصدر",
            ["Destination"] = "الوجهة",
            ["Payment method"] = "طريقة السداد",
            ["Operation no."] = "رقم العملية",
            ["Operation type"] = "نوع العملية",
            ["Operation status"] = "حالة العملية",
            ["Operation date"] = "تاريخ العملية",
            ["Operation total"] = "إجمالي العملية",
            ["Operation payment method"] = "طريقة سداد العملية",
            ["Quantity"] = "الكمية",
            ["Qty"] = "الكمية",
            ["Bonus"] = "بونص",
            ["Line quantity"] = "كمية البنود",
            ["Bonus quantity"] = "كمية البونص",
            ["Document total"] = "إجمالي المستند",
            ["Total"] = "الإجمالي",
            ["Total amount"] = "إجمالي المبلغ",
            ["Paid amount"] = "المبلغ المدفوع",
            ["Remaining amount"] = "المبلغ المتبقي",
            ["Paid to date"] = "المدفوع حتى الآن",
            ["Cash received"] = "النقدية المستلمة",
            ["Cash refunded"] = "النقدية المرتدة",
            ["Merchant credit"] = "رصيد دائن للتاجر",
            ["Current remaining"] = "المتبقي الحالي",
            ["Remaining balance"] = "الرصيد المتبقي",
            ["Remaining reduction"] = "تخفيض المتبقي",
            ["Merchant"] = "التاجر",
            ["Payment"] = "السداد",
            ["Client"] = "العميل",
            ["Method"] = "طريقة السداد",
            ["Paid"] = "المدفوع",
            ["Remaining"] = "المتبقي",
            ["Location"] = "الموقع",
            ["SKU"] = "كود الصنف",
            ["Product"] = "المنتج",
            ["Available"] = "المتاح",
            ["ReservedWarehouse"] = "محجوز بالمخزن",
            ["ReservedRep"] = "محجوز مع المندوب",
            ["Target"] = "المستهدف",
            ["Updated"] = "آخر تحديث",
            ["Expiry"] = "الصلاحية",
            ["Batch expiry"] = "صلاحية التشغيلة",
            ["Lot"] = "التشغيلة",
            ["Side"] = "الجانب",
            ["Mode"] = "الوضع",
            ["Unit price"] = "سعر الوحدة",
            ["Step"] = "الخطوة",
            ["Actor"] = "المسؤول",
            ["At"] = "في",
            ["Version"] = "الإصدار",
            ["Edited at"] = "تاريخ التعديل",
            ["Edited by"] = "عدله",
            ["Reason"] = "السبب",
            ["Current"] = "الحالي",
            ["Drafted by"] = "سجله",
            ["Assigned to"] = "مسند إلى",
            ["Last modified by"] = "آخر تعديل بواسطة",
            ["Last modified at"] = "تاريخ آخر تعديل",
            ["Initialized by"] = "بدأه",
            ["Initialized at"] = "تاريخ البدء",
            ["Merchant / buyer"] = "التاجر / المشتري",
            ["Selling clerk"] = "موظف البيع",
            ["Payment log"] = "سجل السداد",
            ["Payment log status"] = "حالة سجل السداد",
            ["Merchant remaining"] = "متبقي التاجر",
            ["Sales"] = "المبيعات",
            ["Sales total"] = "إجمالي المبيعات",
            ["Returns"] = "المرتجعات",
            ["Returns total"] = "إجمالي المرتجعات",
            ["Change net"] = "صافي الاستبدال",
            ["ChangeNet"] = "صافي الاستبدال",
            ["Payments"] = "المدفوعات",
            ["Payments received"] = "المدفوعات المستلمة",
            ["Refunds"] = "المبالغ المرتدة",
            ["Credits"] = "الأرصدة الدائنة",
            ["Merchant credits"] = "الأرصدة الدائنة للتاجر",
            ["Remaining reductions"] = "تخفيضات المتبقي",
            ["RemainingReductions"] = "تخفيضات المتبقي",
            ["Session"] = "الجلسة",
            ["Counted lines"] = "البنود المحسوبة",
            ["Total counted"] = "إجمالي المحسوب",
            ["Total discrepancy"] = "إجمالي الفرق",
            ["Discrepancies"] = "الفروقات",
            ["Adjustment count"] = "عدد التسويات",
            ["System"] = "النظام",
            ["Physical"] = "الفعلي",
            ["Delta"] = "الفرق",
            ["Note"] = "ملاحظة",
            ["No records were recorded."] = "لم يتم تسجيل أي سجلات.",
            ["Active"] = "نشط",
            ["Inactive"] = "غير نشط",
            ["Suspended"] = "موقوف",
            ["Completed"] = "مكتمل",
            ["Confirmed"] = "مؤكد",
            ["Draft"] = "مسودة",
            ["Reserved"] = "محجوز",
            ["Cancelled"] = "ملغي",
            ["PendingAdmin"] = "بانتظار المدير",
            ["PendingAccountant"] = "بانتظار المحاسب",
            ["PendingAdminReview"] = "بانتظار مراجعة المدير",
            ["Rejected"] = "مرفوض",
            ["WholesaleSale"] = "بيع جملة",
            ["RetailSale"] = "بيع قطاعي",
            ["OnlineSale"] = "بيع أونلاين",
            ["Change"] = "استبدال",
            ["Reserve"] = "حجز",
            ["Supply"] = "توريد",
            ["InventoryReceipt"] = "استلام مخزون",
            ["WriteOff"] = "إعدام",
            ["StocktakeAdjustment"] = "تسوية جرد",
            ["CashHandToHand"] = "نقدي يد بيد",
            ["CashTransaction"] = "تحويل نقدي",
            ["Installment"] = "تقسيط",
            ["MainWarehouse"] = "مخزن رئيسي",
            ["SubWarehouse"] = "مخزن فرعي",
            ["Online"] = "أونلاين"
        };
        return translations.TryGetValue(value, out var translated) ? translated : value;
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
            "Installment" => "Installment",
            _ => value
        };
    }

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

public sealed record PaymentReportRow(Guid Id, Guid OperationId, string? OperationNumber, Guid? MerchantId, string PaymentMethod, decimal TotalAmount, decimal AmountPaid, decimal RemainingAmount, string Status, Guid? AssignedTo, DateTime LastModifiedAt);

public sealed record SupplyLandedCostReportRow(Guid Id, string ShipmentNumber, string SupplierName, string? InvoiceNumber, DateTime ShipmentDate, string Status, int Quantity, decimal ProductSubtotal, decimal CostSubtotal, decimal LandedTotal, Guid? InventoryReceiptOperationId);

public sealed record MerchantBalanceReportRow(Guid MerchantId, string BusinessName, string Status, decimal SaleTotal, decimal ReturnTotal, decimal ChangeNet, decimal PaymentsReceived, decimal CashRefunded, decimal MerchantCredits, decimal BalanceReductions, decimal Balance);

public sealed record CreateExportLogRequest(string ReportType, string? GeneratedUrl);

public sealed record ExportLogResponse(Guid Id, string ReportType, Guid? RequestedBy, string? RequestedByRole, string? GeneratedUrl, DateTime CreatedAt);

internal sealed record PdfFact(string Label, string Value);

internal sealed record PdfTableSection(string Title, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows, string EmptyMessage);

internal sealed record PdfSection(
    string Title,
    IReadOnlyList<PdfFact>? Facts = null,
    IReadOnlyList<PdfTableSection>? Tables = null,
    string? Note = null);

