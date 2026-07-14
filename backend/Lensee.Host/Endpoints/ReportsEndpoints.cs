using System.Text;
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
            return Csv("stock.csv", ["Location", "Type", "SKU", "Product", "Available", "ReservedWarehouse", "ReservedRep", "Target", "Updated"], rows.Select(row => new[]
            {
                row.LocationName,
                row.LocationType,
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
            return Csv("operations.csv", ["Operation", "Type", "Status", "Client", "Payment", "Qty", "Bonus", "Total", "Created"], rows.Select(row => new[]
            {
                row.OperationNumber,
                row.OperationType,
                row.Status,
                row.ClientName ?? "",
                row.PaymentMethod ?? "",
                row.Quantity.ToString(),
                row.BonusQuantity.ToString(),
                row.Total.ToString("0.####"),
                row.CreatedAt.ToString("s")
            }));
        }

        return result;
    }

    private static async Task<IResult> GetPaymentsReportAsync(
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
                log.MerchantId,
                log.PaymentMethod,
                log.TotalAmount,
                log.AmountPaid,
                log.TotalAmount - log.AmountPaid,
                log.Status,
                log.AssignedTo,
                log.LastModifiedAt))
            .ToListAsync(cancellationToken);

        return Results.Ok(rows);
    }

    private static async Task<IResult> GetPaymentsCsvAsync(
        PaymentsDbContext paymentsDbContext,
        ReportingDbContext reportingDbContext,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var result = await GetPaymentsReportAsync(paymentsDbContext, cancellationToken);
        if (result is IValueHttpResult { Value: IEnumerable<PaymentReportRow> rows })
        {
            await LogExportAsync(reportingDbContext, currentUser, clock, "payments.csv", "download://reports/payments.csv", cancellationToken);
            return Csv("payments.csv", ["Payment", "Operation", "Merchant", "Method", "Total", "Paid", "Remaining", "Status"], rows.Select(row => new[]
            {
                row.Id.ToString(),
                row.OperationId.ToString(),
                row.MerchantId?.ToString() ?? string.Empty,
                row.PaymentMethod,
                row.TotalAmount.ToString("0.####"),
                row.AmountPaid.ToString("0.####"),
                row.RemainingAmount.ToString("0.####"),
                row.Status
            }));
        }

        return result;
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
            return Csv("merchant-balances.csv", ["Merchant", "Status", "Sales", "Returns", "ChangeNet", "Payments", "Refunds", "Credits", "RemainingReductions", "Remaining"], rows.Select(row => new[]
            {
                row.BusinessName,
                row.Status,
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

        var summary = new List<PdfFact>
        {
            new("Operation no.", operation.OperationNumber),
            new("Type", operation.OperationType),
            new("Status", operation.Status),
            new("Created at", FormatDateTime(operation.CreatedAt)),
            new("Created by", GetUserDisplayName(operation.CreatedBy, userLookup)),
            new("Confirmed at", FormatDateTime(operation.ConfirmedAt)),
            new("Confirmed by", GetUserDisplayName(operation.ConfirmedBy, userLookup)),
            new("Last revised by", operation.OperationVersions.Count == 0 ? "-" : GetUserDisplayName(operation.OperationVersions.OrderByDescending(value => value.VersionNumber).First().EditedBy, userLookup)),
            new("Version", (operation.CurrentVersion?.VersionNumber.ToString()) ?? operation.OperationVersions.OrderByDescending(value => value.VersionNumber).Select(value => (int?)value.VersionNumber).FirstOrDefault()?.ToString() ?? "1"),
            new("Source", GetLocationName(operation.SourceLocationId, locations)),
            new("Destination", GetLocationName(operation.DestinationLocationId, locations)),
            new("Payment method", DescribePaymentMethod(operation.PaymentMethod)),
            new("Line quantity", totalQty.ToString()),
            new("Bonus quantity", totalBonus.ToString()),
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
                "Customer and commercial context",
                [
                    new PdfFact("Customer", operation.ClientName ?? merchant?.BusinessName ?? "-"),
                    new PdfFact("Registered merchant", merchant?.BusinessName ?? "-"),
                    new PdfFact("Contact person", merchant?.ContactPersonName ?? "-"),
                    new PdfFact("Phone", merchant is null ? "-" : JoinValues(merchant.PhoneNumbers)),
                    new PdfFact("Representative", operation.OperationLines.Select(line => line.RepresentativeNameSnapshot).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "-"),
                    new PdfFact("Merchant status", merchant?.Status ?? "-"),
                    new PdfFact("Current remaining", balance is null ? "-" : FormatMoney(balance.Balance))
                ]),
            new(
                "Financial summary",
                [
                    new PdfFact("Operation total", FormatMoney(totalValue)),
                    new PdfFact("Payment log status", paymentLog?.Status ?? "No payment log"),
                    new PdfFact("Payment method", paymentLog is null ? DescribePaymentMethod(operation.PaymentMethod) : DescribePaymentMethod(paymentLog.PaymentMethod)),
                    new PdfFact("Paid to date", paymentLog is null ? FormatMoney(cashRecords.Where(value => value.PaymentType == CashReceived).Sum(value => value.Amount)) : FormatMoney(paymentLog.AmountPaid)),
                    new PdfFact("Remaining", paymentLog is null ? "-" : FormatMoney(Math.Max(paymentLog.TotalAmount - paymentLog.AmountPaid, 0))),
                    new PdfFact("Cash received", FormatMoney(cashRecords.Where(value => value.PaymentType == CashReceived).Sum(value => value.Amount))),
                    new PdfFact("Cash refunded", FormatMoney(cashRecords.Where(value => value.PaymentType == CashRefund).Sum(value => value.Amount))),
                    new PdfFact("Merchant credit", FormatMoney(adjustments.Where(value => value.AdjustmentType == MerchantCredit && value.Status == Completed).Sum(value => value.Amount))),
                    new PdfFact("Remaining reduction", FormatMoney(adjustments.Where(value => value.AdjustmentType == BalanceReduction && value.Status == Completed).Sum(value => value.Amount)))
                ]),
            new(
                "Line items",
                Tables:
                [
                    new PdfTableSection(
                        "Operation lines",
                        ["SKU", "Product", "Side", "Qty", "Mode", "Bonus", "Unit price", "Total", "Lot", "Batch expiry", "Notes"],
                        operation.OperationLines
                            .OrderBy(value => value.ProductNameSnapshot)
                            .Select(line => (IReadOnlyList<string>)new[]
                            {
                                line.SkuCodeSnapshot,
                                line.ProductNameSnapshot,
                                line.Section,
                                line.Quantity.ToString(),
                                line.EntryMode,
                                line.BonusQuantity.ToString(),
                                FormatMoney(line.UnitPrice),
                                FormatMoney(line.LineTotal),
                                line.LotNumber ?? "-",
                                FormatDate(line.ExpiryDate),
                                line.LineNotes ?? line.WriteOffReasonText ?? line.WriteOffReason ?? "-"
                            }).ToList(),
                        "No operation lines were recorded.")
                ]),
            new(
                "Workflow and revision history",
                Tables:
                [
                    new PdfTableSection(
                        "Version history",
                        ["Version", "Edited at", "Edited by", "Reason", "Current"],
                        operation.OperationVersions
                            .OrderBy(value => value.VersionNumber)
                            .Select(version => (IReadOnlyList<string>)new[]
                            {
                                version.VersionNumber.ToString(),
                                FormatDateTime(version.EditedAt),
                                GetUserDisplayName(version.EditedBy, userLookup),
                                version.Reason,
                                operation.CurrentVersionId == version.Id ? "Yes" : string.Empty
                            }).ToList(),
                        "No explicit version history was recorded."),
                    new PdfTableSection(
                        "Actor timeline",
                        ["Step", "Actor", "At"],
                        BuildOperationActorTimeline(operation, userLookup),
                        "No workflow timeline is available.")
                ]),
            new(
                "Payment workflow",
                Tables:
                [
                    new PdfTableSection(
                        "Installment sub-logs",
                        ["Date", "Method", "Amount", "Status", "Drafted by", "Confirmed by", "Notes"],
                        paymentLog?.InstallmentSubLogs
                            .OrderBy(value => value.DraftedAt)
                            .Select(sub => (IReadOnlyList<string>)new[]
                            {
                                FormatDate(sub.DateReceived),
                                DescribePaymentMethod(sub.PaymentMethod),
                                FormatMoney(sub.Amount),
                                sub.SubLogStatus,
                                GetUserDisplayName(sub.DraftedBy, userLookup),
                                GetUserDisplayName(sub.ConfirmedBy, userLookup),
                                sub.Notes ?? sub.RejectionReason ?? "-"
                            }).ToList() ?? [],
                        "No installment sub-logs were recorded."),
                    new PdfTableSection(
                        "Cash records",
                        ["Date", "Type", "Amount", "Status", "Recorded by", "Notes"],
                        cashRecords
                            .Select(record => (IReadOnlyList<string>)new[]
                            {
                                FormatDateTime(record.PaymentDate),
                                record.PaymentType,
                                FormatMoney(record.Amount),
                                record.Status,
                                GetUserDisplayName(record.CreatedBy, userLookup),
                                record.Notes ?? record.SubType ?? "-"
                            }).ToList(),
                        "No cash records were recorded."),
                    new PdfTableSection(
                        "Financial adjustments",
                        ["Date", "Type", "Amount", "Status", "Created by", "Notes"],
                        adjustments
                            .Select(adjustment => (IReadOnlyList<string>)new[]
                            {
                                FormatDateTime(adjustment.CreatedAt),
                                adjustment.AdjustmentType,
                                FormatMoney(adjustment.Amount),
                                adjustment.Status,
                                GetUserDisplayName(adjustment.CreatedBy, userLookup),
                                adjustment.Notes ?? "-"
                            }).ToList(),
                        "No financial adjustments were recorded.")
                ],
                Note: operation.Notes is null ? null : $"Operation notes: {operation.Notes}")
        };

        var pdf = BuildEnterprisePdf(
            "Operation document",
            "Commercial and stock execution detail",
            operation.OperationNumber,
            summary,
            sections,
            language);
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
            new("Payment log", log.Id.ToString("N")),
            new("Operation", operation?.OperationNumber ?? log.OperationId.ToString("N")),
            new("Merchant", merchant?.BusinessName ?? operation?.ClientName ?? "Anonymous buyer"),
            new("Method", DescribePaymentMethod(log.PaymentMethod)),
            new("Status", log.Status),
            new("Initialized by", GetUserDisplayName(log.InitializedBy, userLookup)),
            new("Initialized at", FormatDateTime(log.InitializedAt)),
            new("Assigned to", GetUserDisplayName(log.AssignedTo, userLookup)),
            new("Assigned at", FormatDateTime(log.AssignedAt)),
            new("Last modified by", GetUserDisplayName(log.LastModifiedBy, userLookup)),
            new("Last modified at", FormatDateTime(log.LastModifiedAt)),
            new("Total", FormatMoney(log.TotalAmount)),
            new("Paid", FormatMoney(log.AmountPaid)),
            new("Remaining", FormatMoney(Math.Max(log.TotalAmount - log.AmountPaid, 0))),
            new("Merchant remaining", balance is null ? "-" : FormatMoney(balance.Balance))
        };

        var sections = new List<PdfSection>
        {
            new(
                "Merchant and sale context",
                [
                    new PdfFact("Merchant / buyer", merchant?.BusinessName ?? operation?.ClientName ?? "Anonymous buyer"),
                    new PdfFact("Contact person", merchant?.ContactPersonName ?? operation?.ClientName ?? "-"),
                    new PdfFact("Phone", merchant is null ? "-" : JoinValues(merchant.PhoneNumbers)),
                    new PdfFact("Merchant status", merchant?.Status ?? "-"),
                    new PdfFact("Operation type", operation?.OperationType ?? "-"),
                    new PdfFact("Selling clerk", operation is null ? "-" : GetUserDisplayName(operation.CreatedBy, userLookup)),
                    new PdfFact("Operation total", operation is null ? "-" : FormatMoney(operation.OperationLines.Sum(line => line.LineTotal))),
                    new PdfFact("Operation payment method", operation is null ? "-" : DescribePaymentMethod(operation.PaymentMethod))
                ],
                Note: log.Notes is null ? null : $"Payment log notes: {log.Notes}"),
            new(
                "Installment sub-logs",
                Tables:
                [
                    new PdfTableSection(
                        "Recorded entries",
                        ["Date", "Method", "Amount", "Status", "Drafted by", "Confirmed by", "Notes"],
                        log.InstallmentSubLogs
                            .OrderBy(value => value.DraftedAt)
                            .Select(sub => (IReadOnlyList<string>)new[]
                            {
                                FormatDate(sub.DateReceived),
                                DescribePaymentMethod(sub.PaymentMethod),
                                FormatMoney(sub.Amount),
                                sub.SubLogStatus,
                                GetUserDisplayName(sub.DraftedBy, userLookup),
                                GetUserDisplayName(sub.ConfirmedBy, userLookup),
                                sub.Notes ?? sub.RejectionReason ?? "-"
                            }).ToList(),
                        "No installment sub-logs were recorded.")
                ]),
            new(
                "Cash and adjustments",
                Tables:
                [
                    new PdfTableSection(
                        "Cash records",
                        ["Date", "Type", "Amount", "Status", "Created by", "Notes"],
                        cashRecords.Select(record => (IReadOnlyList<string>)new[]
                        {
                            FormatDateTime(record.PaymentDate),
                            record.PaymentType,
                            FormatMoney(record.Amount),
                            record.Status,
                            GetUserDisplayName(record.CreatedBy, userLookup),
                            record.Notes ?? record.SubType ?? "-"
                        }).ToList(),
                        "No cash records were recorded."),
                    new PdfTableSection(
                        "Financial adjustments",
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
                ])
        };

        var isCashReceipt = string.Equals(log.PaymentMethod, "CashHandToHand", StringComparison.OrdinalIgnoreCase);
        var pdf = BuildEnterprisePdf(
            isCashReceipt ? "Cash receive receipt" : "Payment receipt",
            isCashReceipt ? "Cash collection and accountant approval detail" : "Financial collection and review detail",
            log.Id.ToString("N"),
            summary,
            sections,
            language);
        var documentName = isCashReceipt ? "cash-receive-receipt.pdf" : "payment-receipt.pdf";
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
                "Recent operations",
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
                "Latest notes",
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
            merchant.BusinessName,
            summary,
            sections,
            language);
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
            new("Session", session.Id.ToString("N")),
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
                "Session detail",
                [
                    new PdfFact("Notes", session.Notes ?? "-")
                ]),
            new(
                "Counted lines",
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
            session.Id.ToString("N"),
            summary,
            sections,
            language);
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
        if (string.IsNullOrWhiteSpace(request.ReportType))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.ReportType)] = ["Report type is required."] });
        }

        var export = new ExportLog
        {
            Id = Guid.NewGuid(),
            ReportType = request.ReportType.Trim(),
            RequestedBy = currentUser.UserId,
            GeneratedUrl = request.GeneratedUrl ?? $"demo://reports/{request.ReportType.Trim()}",
            CreatedAt = clock.EgyptNow
        };

        reportingDbContext.ExportLogs.Add(export);
        await reportingDbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/reports/exports/{export.Id}", new ExportLogResponse(export.Id, export.ReportType, export.RequestedBy, currentUser.Role, export.GeneratedUrl, export.CreatedAt));
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
        string? language = null)
    {
        var arabic = string.Equals(language, "ar", StringComparison.OrdinalIgnoreCase);
        var referenceLabel = arabic ? "المرجع" : "Reference";
        var generatedLabel = arabic ? "تاريخ الإنشاء" : "Generated";
        var internalDocumentLabel = arabic ? "مستند داخلي للشركة" : "Enterprise internal document";
        var summaryLabel = arabic ? "ملخص" : "Summary";
        if (arabic)
        {
            title = PdfText(title);
            subtitle = PdfText(subtitle);
            summaryFacts = summaryFacts.Select(fact => fact with { Label = PdfText(fact.Label) }).ToArray();
            sections = sections.Select(section => section with
            {
                Title = PdfText(section.Title),
                Facts = section.Facts?.Select(fact => fact with { Label = PdfText(fact.Label) }).ToArray(),
                Tables = section.Tables?.Select(table => table with
                {
                    Title = PdfText(table.Title),
                    Headers = table.Headers.Select(PdfText).ToArray(),
                    EmptyMessage = PdfText(table.EmptyMessage)
                }).ToArray(),
                Note = section.Note
            }).ToArray();
        }
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(24);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(text => text.FontSize(9).FontColor(Colors.Grey.Darken3));
                page.Header().Column(header =>
                {
                    header.Item().Row(row =>
                    {
                        row.RelativeItem().Column(column =>
                        {
                            column.Item().Text("Lensee").SemiBold().FontSize(20).FontColor(Colors.Blue.Darken2);
                            column.Item().Text(title).SemiBold().FontSize(14).FontColor(Colors.Grey.Darken4);
                            column.Item().Text(subtitle).FontSize(9).FontColor(Colors.Grey.Darken1);
                        });
                        row.ConstantItem(180).AlignRight().Column(column =>
                        {
                            column.Item().Text($"{referenceLabel}: {documentReference}").SemiBold().FontSize(9).FontColor(Colors.Grey.Darken4);
                            column.Item().Text($"{generatedLabel}: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC").FontSize(8).FontColor(Colors.Grey.Darken1);
                            column.Item().Text(internalDocumentLabel).FontSize(8).FontColor(Colors.Grey.Darken1);
                        });
                    });
                    header.Item().PaddingTop(10).LineHorizontal(1).LineColor(Colors.Blue.Lighten3);
                });

                page.Content().PaddingTop(12).Column(column =>
                {
                    if (summaryFacts.Count > 0)
                    {
                        column.Item().Text(summaryLabel).SemiBold().FontSize(11).FontColor(Colors.Grey.Darken4);
                        column.Item().PaddingTop(6).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            foreach (var fact in summaryFacts)
                            {
                                table.Cell().Padding(4).Border(1).BorderColor(Colors.Grey.Lighten3).Background(Colors.Grey.Lighten5).Padding(8).Column(item =>
                                {
                                    item.Item().Text(fact.Label).FontSize(7).FontColor(Colors.Grey.Darken1);
                                    item.Item().Text(fact.Value).SemiBold().FontSize(9).FontColor(Colors.Grey.Darken4);
                                });
                            }
                        });
                    }

                    foreach (var section in sections)
                    {
                        column.Item().PaddingTop(14).Text(section.Title).SemiBold().FontSize(11).FontColor(Colors.Grey.Darken4);

                        if (!string.IsNullOrWhiteSpace(section.Note))
                        {
                            column.Item().PaddingTop(4).Text(section.Note).FontSize(8).FontColor(Colors.Grey.Darken1);
                        }

                        if (section.Facts is { Count: > 0 })
                        {
                            column.Item().PaddingTop(6).Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(130);
                                    columns.RelativeColumn();
                                });

                                foreach (var fact in section.Facts)
                                {
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(4).Text(fact.Label).FontSize(8).FontColor(Colors.Grey.Darken1);
                                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).PaddingVertical(4).Text(fact.Value).SemiBold().FontSize(9).FontColor(Colors.Grey.Darken4);
                                }
                            });
                        }

                        foreach (var tableSection in section.Tables ?? [])
                        {
                            column.Item().PaddingTop(8).Text(tableSection.Title).SemiBold().FontSize(9).FontColor(Colors.Blue.Darken2);
                            column.Item().PaddingTop(4).Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    foreach (var _ in tableSection.Headers)
                                    {
                                        columns.RelativeColumn();
                                    }
                                });

                                foreach (var header in tableSection.Headers)
                                {
                                    table.Cell().Background(Colors.Blue.Darken2).Padding(5).Text(header).SemiBold().FontColor(Colors.White).FontSize(8);
                                }

                                if (tableSection.Rows.Count == 0)
                                {
                                    table.Cell().ColumnSpan((uint)tableSection.Headers.Count).BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(8).Text(tableSection.EmptyMessage).FontColor(Colors.Grey.Darken1);
                                }

                                for (var rowIndex = 0; rowIndex < tableSection.Rows.Count; rowIndex++)
                                {
                                    foreach (var cell in tableSection.Rows[rowIndex])
                                    {
                                        var tableCell = table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3);
                                        if (rowIndex % 2 == 1)
                                        {
                                            tableCell = tableCell.Background(Colors.Grey.Lighten5);
                                        }

                                        tableCell.Padding(5).Text(cell ?? string.Empty).FontSize(8);
                                    }
                                }
                            });
                        }
                    }
                });

                page.Footer().BorderTop(1).BorderColor(Colors.Grey.Lighten3).PaddingTop(8).Row(row =>
                {
                    row.RelativeItem().Text("Lensee confidential business document").FontSize(8).FontColor(Colors.Grey.Darken1);
                    row.ConstantItem(120).AlignRight().Text(text =>
                    {
                        text.Span("Page ").FontSize(8);
                        text.CurrentPageNumber().FontSize(8);
                        text.Span(" / ").FontSize(8);
                        text.TotalPages().FontSize(8);
                    });
                });
            });
        }).GeneratePdf();
    }

    private static string PdfText(string value)
    {
        var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Operation document"] = "مستند العملية", ["Payment receipt"] = "إيصال المدفوعة", ["Merchant statement"] = "كشف حساب التاجر",
            ["Stocktake summary"] = "ملخص الجرد", ["Commercial and stock execution detail"] = "تفاصيل التنفيذ التجاري والمخزني",
            ["Financial collection and review detail"] = "تفاصيل التحصيل والمراجعة المالية", ["Commercial relationship and financial position"] = "العلاقة التجارية والموقف المالي",
            ["Physical count and discrepancy review"] = "الجرد الفعلي ومراجعة الفروقات", ["Date"] = "التاريخ", ["Type"] = "النوع",
            ["Status"] = "الحالة", ["Amount"] = "المبلغ", ["Created by"] = "أنشأه", ["Notes"] = "ملاحظات", ["Quantity"] = "الكمية",
            ["Total"] = "الإجمالي", ["Merchant"] = "التاجر", ["Payment"] = "السداد", ["Location"] = "الموقع", ["SKU"] = "كود الصنف",
            ["Expiry"] = "الصلاحية", ["Lot"] = "التشغيلة", ["No records were recorded."] = "لم يتم تسجيل أي سجلات."
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

}

public sealed record StockReportRow(Guid LocationId, string LocationName, string LocationType, Guid SkuId, string? SkuCode, string? ProductName, int AvailableQty, int ReservedInWarehouseQty, int ReservedWithRepQty, int? TargetQty, DateTime LastUpdated);

public sealed record FinancialSummaryResponse(decimal TotalSales, decimal ActualCollected, decimal RemainingReceivable);

public sealed record OperationReportRow(Guid Id, string OperationNumber, string OperationType, string Status, Guid? MerchantId, string? ClientName, string? PaymentMethod, int Quantity, int BonusQuantity, decimal Total, DateTime CreatedAt, DateTime? ConfirmedAt);

public sealed record PaymentReportRow(Guid Id, Guid OperationId, Guid? MerchantId, string PaymentMethod, decimal TotalAmount, decimal AmountPaid, decimal RemainingAmount, string Status, Guid? AssignedTo, DateTime LastModifiedAt);

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
