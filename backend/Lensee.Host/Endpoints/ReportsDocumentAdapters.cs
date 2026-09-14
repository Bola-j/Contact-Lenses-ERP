using System.Numerics;
using System.Globalization;
using Lensee.Modules.Reporting.Application;
using Lensee.Modules.Reporting.Application.Abstractions;
using Lensee.Modules.Reporting.Application.Models;
using Lensee.Modules.Reporting.Configuration;
using Lensee.Modules.Reporting.Data;
using Lensee.Modules.Reporting.Infrastructure;
using Lensee.SharedKernel.Abstractions;
using Microsoft.Extensions.Options;

namespace Lensee.Host.Endpoints;

public static partial class ReportsEndpoints
{
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

    private static DocumentModel BuildAnalyticalDocument(
        string key,
        object value,
        DocumentLanguage language,
        string? generatedBy,
        DateTime generatedAtUtc) => key.ToLowerInvariant() switch
        {
            "financial-summary" => BuildFinancialSummaryDocument((FinancialSummaryResponse)value, language, generatedBy, generatedAtUtc),
            "stock" => BuildTableDocument(key, "Inventory report", language, generatedBy, generatedAtUtc,
                [Column("Location"), Column("Type"), Column("SKU", ColumnDataType.Code), Column("Product"), Column("Available", ColumnDataType.Integer), Column("Reserved warehouse", ColumnDataType.Integer), Column("Reserved rep", ColumnDataType.Integer), Column("Target", ColumnDataType.Integer), Column("Updated", ColumnDataType.DateTime)],
                ((IEnumerable<StockReportRow>)value).Select(row => new TableRow([
                    Text(row.LocationName), Text(row.LocationType), new DocumentCell.Code(row.SkuCode ?? row.SkuId.ToString("N")), Text(row.ProductName),
                    new DocumentCell.Integer(row.AvailableQty), new DocumentCell.Integer(row.ReservedInWarehouseQty), new DocumentCell.Integer(row.ReservedWithRepQty),
                    row.TargetQty.HasValue ? new DocumentCell.Integer(row.TargetQty.Value) : Text(string.Empty), new DocumentCell.DateTimeValue(row.LastUpdated)])).ToList()),
            "operations" => BuildTableDocument(key, "Operations report", language, generatedBy, generatedAtUtc,
                [Column("Operation", ColumnDataType.Code), Column("Type"), Column("Status"), Column("Client"), Column("Payment"), Column("Quantity", ColumnDataType.Integer), Column("Bonus", ColumnDataType.Integer), Column("Total", ColumnDataType.Money), Column("Created", ColumnDataType.DateTime)],
                ((IEnumerable<OperationReportRow>)value).Select(row => new TableRow([
                    new DocumentCell.Code(row.OperationNumber), Text(row.OperationType), Text(row.Status), Text(row.ClientName), Text(row.PaymentMethod),
                    new DocumentCell.Integer(row.Quantity), new DocumentCell.Integer(row.BonusQuantity), new DocumentCell.Money(row.Total), new DocumentCell.DateTimeValue(row.CreatedAt)])).ToList()),
            "payments" => BuildTableDocument(key, "Payments report", language, generatedBy, generatedAtUtc,
                [Column("Payment", ColumnDataType.Code), Column("Operation", ColumnDataType.Code), Column("Merchant", ColumnDataType.Code), Column("Method"), Column("Total", ColumnDataType.Money), Column("Paid", ColumnDataType.Money), Column("Remaining", ColumnDataType.Money), Column("Refund due", ColumnDataType.Money), Column("Status")],
                ((IEnumerable<PaymentReportRow>)value).Select(row => new TableRow([
                    new DocumentCell.Code(DocumentRecordCode("PAY", row.Id)), new DocumentCell.Code(row.OperationNumber ?? DocumentRecordCode("OP", row.OperationId)),
                    new DocumentCell.Code(row.MerchantId.HasValue ? DocumentRecordCode("MER", row.MerchantId.Value) : string.Empty), Text(row.PaymentMethod),
                    new DocumentCell.Money(row.TotalAmount), new DocumentCell.Money(row.AmountPaid), new DocumentCell.Money(row.RemainingAmount), new DocumentCell.Money(row.RefundDue), Text(row.Status)])).ToList()),
            "supply" => BuildTableDocument(key, "Supply landed cost report", language, generatedBy, generatedAtUtc,
                [Column("Shipment", ColumnDataType.Code), Column("Supplier"), Column("Invoice", ColumnDataType.Code), Column("Status"), Column("Quantity", ColumnDataType.Integer), Column("Products", ColumnDataType.Money), Column("Import costs", ColumnDataType.Money), Column("Landed total", ColumnDataType.Money), Column("Receipt operation", ColumnDataType.Code), Column("Created", ColumnDataType.DateTime)],
                ((IEnumerable<SupplyLandedCostReportRow>)value).Select(row => new TableRow([
                    new DocumentCell.Code(row.ShipmentNumber), Text(row.SupplierName), new DocumentCell.Code(row.InvoiceNumber ?? string.Empty), Text(row.Status), new DocumentCell.Integer(row.Quantity),
                    new DocumentCell.Money(row.ProductSubtotal), new DocumentCell.Money(row.CostSubtotal), new DocumentCell.Money(row.LandedTotal), new DocumentCell.Code(row.InventoryReceiptOperationNumber ?? string.Empty), new DocumentCell.DateTimeValue(row.ShipmentDate)])).ToList()),
            "merchant-balances" => BuildTableDocument(key, "Merchant balances report", language, generatedBy, generatedAtUtc,
                [Column("Merchant"), Column("Status"), Column("Sales", ColumnDataType.Money), Column("Returns", ColumnDataType.Money), Column("Change net", ColumnDataType.Money), Column("Payments", ColumnDataType.Money), Column("Refunds", ColumnDataType.Money), Column("Additional charges", ColumnDataType.Money), Column("Remaining reductions", ColumnDataType.Money), Column("Remaining", ColumnDataType.Money)],
                ((IEnumerable<MerchantBalanceReportRow>)value).Select(row => new TableRow([
                    Text(row.BusinessName), Text(row.Status), new DocumentCell.Money(row.SaleTotal), new DocumentCell.Money(row.ReturnTotal), new DocumentCell.Money(row.ChangeNet),
                    new DocumentCell.Money(row.PaymentsReceived), new DocumentCell.Money(row.CashRefunded), new DocumentCell.Money(row.AdditionalCharges), new DocumentCell.Money(row.BalanceReductions), new DocumentCell.Money(row.Balance)])).ToList()),
            _ => throw new NotSupportedException($"Unknown report '{key}'.")
        };

    private static DocumentModel BuildFinancialSummaryDocument(FinancialSummaryResponse summary, DocumentLanguage language, string? generatedBy, DateTime generatedAtUtc) => new(
        new Lensee.Modules.Reporting.Application.Models.DocumentMetadata("financial-summary", Localized("Financial summary", language), DocumentTemplateKind.Analytical, language, null, generatedAtUtc, generatedBy),
        new DocumentHeader(Localized("Financial summary", language)),
        [new InfoSection(Localized("Report metadata", language),
        [
            new InfoField(Localized("Generated at", language), generatedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
            new InfoField(Localized("Generated by", language), generatedBy ?? "-")
        ])],
        [
            new SummaryMetric(Localized("Total sales", language), FormatMoney(summary.TotalSales)),
            new SummaryMetric(Localized("Actual collected", language), FormatMoney(summary.ActualCollected)),
            new SummaryMetric(Localized("Remaining receivable", language), FormatMoney(summary.RemainingReceivable), summary.RemainingReceivable < 0 ? "critical" : null)
        ],
        [], null, [], null, new DocumentFooter());

    private static DocumentModel BuildTableDocument(
        string key,
        string title,
        DocumentLanguage language,
        string? generatedBy,
        DateTime generatedAtUtc,
        IReadOnlyList<TableColumn> columns,
        IReadOnlyList<TableRow> rows)
    {
        var localizedColumns = columns.Select(column => column with { Header = Localized(column.Header, language) }).ToList();
        return new DocumentModel(
            new Lensee.Modules.Reporting.Application.Models.DocumentMetadata(key, Localized(title, language), DocumentTemplateKind.Analytical, language, null, generatedAtUtc, generatedBy, columns.Count > 7, rows.Count),
            new DocumentHeader(Localized(title, language)),
            [new InfoSection(Localized("Report metadata", language),
            [
                new InfoField(Localized("Generated at", language), generatedAtUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)),
                new InfoField(Localized("Generated by", language), generatedBy ?? "-")
            ])], [],
            [new TableSection(null, localizedColumns, rows, Localized("No data found for the selected criteria.", language))],
            null, [], null, new DocumentFooter());
    }

    private static TableColumn Column(string header, ColumnDataType dataType = ColumnDataType.Text) => new(
        header.Replace(" ", string.Empty, StringComparison.Ordinal), header, dataType,
        dataType is ColumnDataType.Integer or ColumnDataType.Decimal or ColumnDataType.Money or ColumnDataType.Percentage ? DocumentTextAlignment.End : DocumentTextAlignment.Start,
        NumberFormat: dataType is ColumnDataType.Money or ColumnDataType.Decimal ? "#,##0.0000" : null,
        Direction: dataType == ColumnDataType.Code ? DocumentTextDirection.LeftToRight : DocumentTextDirection.Auto);

    private static DocumentCell Text(string? value, DocumentTextDirection direction = DocumentTextDirection.Auto) => DocumentCell.FromText(value, direction);

    private static DocumentModel BuildEnterpriseDocument(
        string title,
        string subtitle,
        string documentReference,
        IReadOnlyList<PdfFact> summaryFacts,
        IReadOnlyList<PdfSection> sections,
        string? language,
        string? generatedBy,
        DateTime generatedAtUtc)
    {
        ReportingServiceCollectionExtensions.TryParseLanguage(language, out var documentLanguage);
        var key = title switch
        {
            "Operation bill" => "operation-bill",
            "Payment receipt" => "payment-receipt",
            "Cash collection receipt" or "Cash receive receipt" => "cash-receipt",
            "Supply landed cost" => "supply-landed-cost",
            "Merchant statement" => "merchant-statement",
            "Stocktake summary" => "stocktake-summary",
            _ => title.Replace(" ", "-", StringComparison.Ordinal).ToLowerInvariant()
        };
        var template = key switch
        {
            "stocktake-summary" => DocumentTemplateKind.Operational,
            "supply-landed-cost" or "merchant-statement" => DocumentTemplateKind.Analytical,
            _ => DocumentTemplateKind.Transactional
        };
        var overviewCount = key == "merchant-statement" ? 8 : 5;
        var overviewFacts = summaryFacts.Take(overviewCount).ToArray();
        var metricFacts = summaryFacts.Skip(overviewCount).Take(4).ToArray();
        var infoSections = new List<InfoSection>
        {
            new(Localized("Overview", documentLanguage), overviewFacts.Select(fact => new InfoField(Localized(fact.Label, documentLanguage), LocalizedValue(fact.Value, documentLanguage), InferFieldDirection(fact.Label))).ToList())
        };
        var tables = new List<TableSection>();
        var notes = new List<string>();
        foreach (var section in sections)
        {
            if (section.Facts is { Count: > 0 })
            {
                infoSections.Add(new InfoSection(Localized(section.Title, documentLanguage), section.Facts.Select(fact => new InfoField(Localized(fact.Label, documentLanguage), LocalizedValue(fact.Value, documentLanguage), InferFieldDirection(fact.Label))).ToList()));
            }
            if (section.Tables is { Count: > 0 })
            {
                foreach (var source in section.Tables)
                {
                    var columns = source.Headers.Select(header => Column(Localized(header, documentLanguage), InferColumnType(header))).ToList();
                    var rows = source.Rows.Select(row => new TableRow(row.Select((cell, index) =>
                        ConvertCell(
                            index < source.Headers.Count ? source.Headers[index] : string.Empty,
                            LocalizedValue(cell, documentLanguage))).ToList())).ToList();
                    tables.Add(new TableSection(Localized(source.Title, documentLanguage), columns, rows, Localized(source.EmptyMessage, documentLanguage)));
                }
            }
            if (!string.IsNullOrWhiteSpace(section.Note)) notes.Add(Localized(section.Note, documentLanguage));
        }

        string[] signatureLabels = key switch
        {
            "operation-bill" => ["Prepared by", "Warehouse representative", "Merchant / customer", "Approved by"],
            "payment-receipt" => ["Recorded by", "Accountant approval", "Merchant acknowledgment"],
            "cash-receipt" => ["Cash handed over by", "Cash received by", "Accountant approval"],
            "merchant-statement" => ["Prepared by", "Reviewed by", "Merchant acknowledgment"],
            "stocktake-summary" => ["Counted by", "Reviewed by", "Confirmed by"],
            _ => []
        };
        var signatures = signatureLabels.Length == 0 ? null : new SignatureSection(signatureLabels.Select(label => Localized(label, documentLanguage)).ToList());
        var rowCount = tables.Sum(table => table.Rows.Count);
        return new DocumentModel(
            new Lensee.Modules.Reporting.Application.Models.DocumentMetadata(key, Localized(title, documentLanguage), template, documentLanguage, documentReference, generatedAtUtc, generatedBy, template == DocumentTemplateKind.Operational || tables.Exists(table => table.Columns.Count > 7), rowCount),
            new DocumentHeader(Localized(title, documentLanguage), Localized(subtitle, documentLanguage), LocalizedValue(summaryFacts.FirstOrDefault(fact => fact.Label == "Status")?.Value ?? string.Empty, documentLanguage)),
            infoSections,
            metricFacts.Select(fact => new SummaryMetric(Localized(fact.Label, documentLanguage), LocalizedValue(fact.Value, documentLanguage))).ToList(),
            tables, null, notes, signatures, new DocumentFooter());
    }

    private static DocumentTextDirection InferFieldDirection(string label)
    {
        var normalized = label.ToLowerInvariant();
        return normalized.Contains("sku", StringComparison.Ordinal) ||
            normalized.Contains("code", StringComparison.Ordinal) ||
            normalized.Contains("reference", StringComparison.Ordinal) ||
            normalized.Contains("invoice", StringComparison.Ordinal) ||
            normalized.Contains("operation", StringComparison.Ordinal) ||
            normalized.Contains("phone", StringComparison.Ordinal) ||
            normalized.Contains("email", StringComparison.Ordinal) ||
            normalized.Contains("date", StringComparison.Ordinal) ||
            normalized.Contains("time", StringComparison.Ordinal) ||
            normalized.Contains("التاريخ", StringComparison.Ordinal) ||
            normalized.Contains("المرجع", StringComparison.Ordinal) ||
            normalized.Contains("الهاتف", StringComparison.Ordinal) ||
            normalized.Contains("البريد", StringComparison.Ordinal)
            ? DocumentTextDirection.LeftToRight
            : DocumentTextDirection.Auto;
    }

    private static ColumnDataType InferColumnType(string header)
    {
        var normalized = header.ToLowerInvariant();
        if (normalized.Contains("date") || normalized is "at" or "time" or "created" or "confirmed") return ColumnDataType.DateTime;
        if (normalized.Contains("qty") || normalized.Contains("quantity") || normalized.Contains("count")) return ColumnDataType.Integer;
        if (normalized.Contains("amount") || normalized.Contains("total") || normalized.Contains("price") || normalized.Contains("cost") || normalized.Contains("paid") || normalized.Contains("remaining") || normalized.Contains("balance")) return ColumnDataType.Money;
        if (normalized.Contains("sku") || normalized.Contains("code") || normalized.Contains("reference") || normalized.Contains("invoice") || normalized.Contains("operation")) return ColumnDataType.Code;
        return ColumnDataType.Text;
    }

    private static DocumentCell ConvertCell(string header, string? value)
    {
        var text = value ?? string.Empty;
        return InferColumnType(header) switch
        {
            ColumnDataType.Integer when long.TryParse(text, out var integer) => new DocumentCell.Integer(integer),
            ColumnDataType.Money when decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number) => new DocumentCell.Money(number),
            ColumnDataType.Percentage when decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var percentage) => new DocumentCell.Percentage(percentage),
            ColumnDataType.Decimal when decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var decimalValue) => new DocumentCell.Decimal(decimalValue),
            ColumnDataType.Code => new DocumentCell.Code(text),
            ColumnDataType.DateTime when System.DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var dateTime) => new DocumentCell.DateTimeValue(dateTime),
            _ => Text(text)
        };
    }

    private static string LocalizedValue(string value, DocumentLanguage language)
    {
        if (language == DocumentLanguage.English) return value;
        // Date ranges are built as values rather than labels. Keep the range
        // readable in Arabic without translating arbitrary user-entered notes.
        var normalized = value.Replace(" to ", " إلى ", StringComparison.Ordinal);
        return Localized(normalized, language);
    }

    private static string Localized(string value, DocumentLanguage language)
    {
        if (language == DocumentLanguage.English) return value;
        var arabic = DocumentTranslations.Arabic(value);
        return language == DocumentLanguage.Bilingual && !string.Equals(arabic, value, StringComparison.Ordinal) ? $"{arabic} / {value}" : arabic;
    }

    private static bool TryResolveExportFormat(string? value, ExportFormat[] supported, out ExportFormat format) =>
        ReportingServiceCollectionExtensions.TryParseFormat(value, out format) && supported.Contains(format);

    private static IResult InvalidExportFormat() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["format"] = ["The requested export format is not supported for this report."]
    });

    private static IResult InvalidDocumentLanguage() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["language"] = ["Language must be ar, en, or bi."]
    });

    private static async Task<IResult> ExportLegacyCsvAsync(
        string key,
        object data,
        string? language,
        ReportingDbContext reportingDbContext,
        ICurrentUser currentUser,
        IClock clock,
        IDocumentExportService exportService,
        IOptions<ReportingOptions> reportingOptions,
        CancellationToken cancellationToken)
    {
        if (!ReportingServiceCollectionExtensions.TryParseLanguage(language, out var documentLanguage))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["language"] = ["Language must be ar, en, or bi."] });
        }

        var document = BuildAnalyticalDocument(key, data, documentLanguage, currentUser.Role, clock.UtcNow);
        if (document.Metadata.RowCount > reportingOptions.Value.MaxExportRows)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["rows"] = [$"This export contains more than {reportingOptions.Value.MaxExportRows} rows. Narrow the selected filters."]
            });
        }
        var exported = await exportService.ExportAsync(document, ExportFormat.Csv, cancellationToken);
        await LogExportAsync(reportingDbContext, currentUser, clock, $"{key}.csv", $"download://reports/{key}.csv", cancellationToken);
        return Results.File(exported.Content, exported.ContentType, exported.FileName);
    }

    private static string FormatValue(ExportFormat format) => format switch
    {
        ExportFormat.Excel => "xlsx",
        ExportFormat.Csv => "csv",
        _ => "pdf"
    };

    private static string LanguageValue(DocumentLanguage language) => language switch
    {
        DocumentLanguage.Arabic => "ar",
        DocumentLanguage.Bilingual => "bi",
        _ => "en"
    };
}
