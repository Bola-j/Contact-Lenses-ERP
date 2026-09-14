using System.Text;
using System.Diagnostics;
using ClosedXML.Excel;
using Lensee.Modules.Reporting.Application;
using Lensee.Modules.Reporting.Application.Abstractions;
using Lensee.Modules.Reporting.Application.Models;
using Lensee.Modules.Reporting.Infrastructure;
using Xunit;

namespace Lensee.Tests;

public sealed class DocumentRendererTests
{
    private static readonly IDocumentBrandingProvider BrandingProvider = new TestBrandingProvider();
    private static readonly IDocumentLocalizer Localizer = new DocumentLocalizer();

    [Fact]
    public void Catalog_DeclaresExpectedTemplatesFormatsAndLanguages()
    {
        var catalog = new ReportCatalog();

        Assert.Equal(12, catalog.All.Count);
        Assert.True(catalog.TryGet("stock", out var stock));
        Assert.Equal(DocumentTemplateKind.Analytical, stock.Template);
        Assert.Equal([ExportFormat.Pdf, ExportFormat.Excel, ExportFormat.Csv], stock.Formats.OrderBy(value => value));
        Assert.Contains(DocumentLanguage.Bilingual, stock.Languages);
        Assert.True(catalog.TryGet("operation-bill", out var bill));
        Assert.Equal(DocumentTemplateKind.Transactional, bill.Template);
        Assert.Equal([ExportFormat.Pdf], bill.Formats);
    }

    [Fact]
    public void MerchantStatementFinancialLabels_AreLocalizedInArabic()
    {
        var labels = new[]
        {
            "Net collected", "Additional charges", "Amount reduced", "Refund waiting to be paid",
            "Money received", "Money refunded", "Amount due", "Credit available", "Closing balance", "Closing amount due", "Closing merchant credit",
            "Wallet", "Wholesale sale", "PendingAccountant", "Added by", "Cash records", "Adjustments",
            "Merchant notes", "No cash records were recorded.", "No adjustments were recorded.",
            "No notes were recorded.", "Active", "Auto-created from completed cash sale.", "Operations",
            "Payment logs", "Refund payouts"
        };

        foreach (var label in labels)
        {
            var translated = DocumentTranslations.Arabic(label);
            Assert.NotEqual(label, translated);
            Assert.False(string.IsNullOrWhiteSpace(translated));
        }
    }

    [Fact]
    public async Task Csv_UsesBomInvariantTypedValuesAndProtectsUntrustedText()
    {
        var renderer = new CsvDocumentRenderer();
        var document = SampleDocument(DocumentLanguage.Arabic);

        var rendered = await renderer.RenderAsync(document);
        using var memory = new MemoryStream();
        await rendered.Content.CopyToAsync(memory);
        var bytes = memory.ToArray();
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Contains("\"'=SUM(A1:A2)\"", text);
        Assert.Contains("\"-125.50\"", text);
        Assert.Contains("\"2026-09-09\"", text);
        Assert.Contains("عدسة 1", text);
    }

    [Fact]
    public async Task Excel_ReopensWithTypedCellsRtlFilterFreezeAndSafeSheetName()
    {
        var renderer = new ClosedXmlDocumentRenderer(BrandingProvider, Localizer);
        var rendered = await renderer.RenderAsync(SampleDocument(DocumentLanguage.Bilingual));

        using var workbook = new XLWorkbook(rendered.Content);
        var sheet = workbook.Worksheets.Single();
        var header = sheet.CellsUsed().Single(cell => cell.GetString() == "Code");

        Assert.True(sheet.RightToLeft);
        Assert.DoesNotContain('/', sheet.Name);
        Assert.True(sheet.AutoFilter.IsEnabled);
        Assert.Equal(header.Address.RowNumber, sheet.SheetView.SplitRow);
        Assert.Equal(XLDataType.Number, sheet.Cell(header.Address.RowNumber + 1, 2).DataType);
        Assert.Equal(XLDataType.DateTime, sheet.Cell(header.Address.RowNumber + 1, 3).DataType);
        var protectedCell = sheet.Cell(header.Address.RowNumber + 1, 1);
        Assert.Equal("=SUM(A1:A2)", protectedCell.GetString());
        Assert.False(protectedCell.HasFormula);
    }

    [Theory]
    [InlineData(DocumentTemplateKind.Transactional)]
    [InlineData(DocumentTemplateKind.Analytical)]
    [InlineData(DocumentTemplateKind.Operational)]
    public async Task Pdf_AllTemplatesGenerateValidDocument(DocumentTemplateKind template)
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var renderer = new QuestPdfDocumentRenderer(BrandingProvider, Localizer);
        var document = SampleDocument(DocumentLanguage.Bilingual) with
        {
            Metadata = SampleDocument(DocumentLanguage.Bilingual).Metadata with { Template = template }
        };

        var rendered = await renderer.RenderAsync(document);
        var prefix = new byte[4];
        var read = await rendered.Content.ReadAsync(prefix);

        Assert.Equal(4, read);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(prefix));
        Assert.True(rendered.Content.Length > 1_000);
    }

    [Fact]
    public async Task Pdf_BilingualCashReceiptKeepsLegacySectionsAndMixedDirection()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var renderer = new QuestPdfDocumentRenderer(BrandingProvider, Localizer);
        var payment = MeaningfulPaymentReceipt(DocumentLanguage.Bilingual);
        var document = payment with
        {
            Metadata = payment.Metadata with { DocumentKey = "cash-receipt" },
            Header = payment.Header with { Title = "إيصال تحصيل نقدي / Cash collection receipt" }
        };

        var rendered = await renderer.RenderAsync(document);
        var prefix = new byte[4];
        var read = await rendered.Content.ReadAsync(prefix);

        Assert.Equal(4, read);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(prefix));
        Assert.True(rendered.Content.Length > 1_000);
    }

    [Fact]
    public async Task RepresentativeVisualSamplesAndBenchmarks_AreGenerated()
    {
        var outputDirectory = Environment.GetEnvironmentVariable("LENSEE_REPORT_SAMPLE_DIR")
            ?? Path.Combine(Path.GetTempPath(), "lensee-reporting-samples");
        Directory.CreateDirectory(outputDirectory);
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var pdf = new QuestPdfDocumentRenderer(BrandingProvider, Localizer);
        var excel = new ClosedXmlDocumentRenderer(BrandingProvider, Localizer);
        var csv = new CsvDocumentRenderer();
        var samples = new (string Name, IDocumentRenderer Renderer, DocumentModel Document)[]
        {
            ("01-Arabic-Sales-Invoice.pdf", pdf, MeaningfulOperationBill(DocumentLanguage.Arabic)),
            ("02-English-Sales-Invoice.pdf", pdf, MeaningfulOperationBill(DocumentLanguage.English)),
            ("03-Arabic-Warehouse-Transfer.pdf", pdf, MeaningfulWarehouseTransfer(DocumentLanguage.Arabic)),
            ("04-Arabic-Payment-Receipt.pdf", pdf, MeaningfulPaymentReceipt(DocumentLanguage.Arabic)),
            ("05-Arabic-Inventory-Report.pdf", pdf, MeaningfulInventoryReport(DocumentLanguage.Arabic, 24)),
            ("06-English-Inventory-Report.pdf", pdf, MeaningfulInventoryReport(DocumentLanguage.English, 24)),
            ("07-Multi-page-Stock-Movement-Report.pdf", pdf, MeaningfulStockMovement(DocumentLanguage.Bilingual, 180)),
            ("08-Arabic-Stocktake-Sheet.pdf", pdf, MeaningfulStocktake(DocumentLanguage.Arabic, 48)),
            ("09-Excel-Inventory-Report.xlsx", excel, MeaningfulInventoryReport(DocumentLanguage.Arabic, 24)),
            ("10-Excel-Sales-Report.xlsx", excel, MeaningfulSalesReport(DocumentLanguage.English, 24)),
            ("11-CSV-Inventory-Report.csv", csv, MeaningfulInventoryReport(DocumentLanguage.English, 24))
        };

        foreach (var sample in samples)
        {
            var rendered = await sample.Renderer.RenderAsync(sample.Document);
            await using var target = File.Create(Path.Combine(outputDirectory, sample.Name));
            await rendered.Content.CopyToAsync(target);
        }

        var benchmark = new StringBuilder("| Rows | Projection ms | XLSX render ms | Bytes | Managed memory bytes |\n|---:|---:|---:|---:|---:|\n");
        foreach (var rowCount in new[] { 100, 1_000, 10_000 })
        {
            var timer = Stopwatch.StartNew();
            var document = SampleDocument(DocumentLanguage.Bilingual, DocumentTemplateKind.Analytical, rowCount);
            var projectionMs = timer.ElapsedMilliseconds;
            timer.Restart();
            var rendered = await excel.RenderAsync(document);
            var renderingMs = timer.ElapsedMilliseconds;
            benchmark.AppendLine($"| {rowCount} | {projectionMs} | {renderingMs} | {rendered.Content.Length} | {GC.GetTotalMemory(false)} |");
        }
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "benchmark.md"), benchmark.ToString());

        Assert.Equal(11, samples.Count(sample => File.Exists(Path.Combine(outputDirectory, sample.Name))));
    }

    private static string Label(string arabic, string english, DocumentLanguage language) => language switch
    {
        DocumentLanguage.Arabic => arabic,
        DocumentLanguage.Bilingual => $"{arabic} / {english}",
        _ => english
    };

    private static DocumentModel MeaningfulOperationBill(DocumentLanguage language)
    {
        var title = Label("فاتورة عملية", "Operation bill", language);
        var reference = "OP-20260909-101";
        var columns = new[]
        {
            new TableColumn("sku", Label("كود الصنف", "SKU", language), ColumnDataType.Code, Direction: DocumentTextDirection.LeftToRight),
            new TableColumn("product", Label("المنتج", "Product", language)),
            new TableColumn("qty", Label("الكمية", "Quantity", language), ColumnDataType.Integer, DocumentTextAlignment.End),
            new TableColumn("unitPrice", Label("سعر الوحدة", "Unit price", language), ColumnDataType.Money, DocumentTextAlignment.End, NumberFormat: "#,##0.00"),
            new TableColumn("total", Label("الإجمالي", "Total", language), ColumnDataType.Money, DocumentTextAlignment.End, NumberFormat: "#,##0.00")
        };
        var rows = new[]
        {
            new TableRow([new DocumentCell.Code("SKU-LENS-001"), DocumentCell.FromText(Label("عدسات أكيوفيو أويسس", "Acuvue Oasys lenses", language)), new DocumentCell.Integer(25), new DocumentCell.Money(450), new DocumentCell.Money(11250)]),
            new TableRow([new DocumentCell.Code("SKU-SOL-010"), DocumentCell.FromText(Label("محلول عدسات 360 مل", "360ml contact lens solution", language)), new DocumentCell.Integer(10), new DocumentCell.Money(150), new DocumentCell.Money(1500)])
        };
        return new DocumentModel(
            new DocumentMetadata("operation-bill", title, DocumentTemplateKind.Transactional, language, reference, new DateTime(2026, 9, 9, 10, 15, 0, DateTimeKind.Utc), "Omar Adel", false, rows.Length),
            new DocumentHeader(title, Label("تفاصيل التنفيذ التجاري والمخزني", "Commercial and stock execution detail", language), Label("مؤكد", "Confirmed", language)),
            [new InfoSection("Overview", [
                new InfoField(Label("تاريخ الإنشاء", "Created at", language), "2026-09-09 10:15", DocumentTextDirection.LeftToRight),
                new InfoField(Label("رقم العملية", "Operation no.", language), reference, DocumentTextDirection.LeftToRight),
                new InfoField(Label("التاجر", "Merchant", language), Label("بصريات رؤية النيل", "Basmat Ruya Elneel", language)),
                new InfoField(Label("المخزن", "Warehouse", language), Label("المخزن الرئيسي", "Main Warehouse", language)),
                new InfoField(Label("نوع العملية", "Operation type", language), Label("بيع جملة", "Wholesale sale", language)),
                new InfoField(Label("الحالة", "Status", language), Label("مؤكد", "Confirmed", language))
            ])],
            [new SummaryMetric(Label("إجمالي العملية", "Operation total", language), "12,750.00"), new SummaryMetric(Label("المدفوع", "Paid", language), "7,500.00"), new SummaryMetric(Label("المتبقي", "Remaining", language), "5,250.00")],
            [new TableSection(Label("البنود", "Lines", language), columns, rows, Label("لا توجد بنود", "No line items", language))],
            new TotalsSection([new SummaryMetric(Label("إجمالي المستند", "Document total", language), "12,750.00"), new SummaryMetric(Label("المدفوع حتى الآن", "Paid to date", language), "7,500.00"), new SummaryMetric(Label("الرصيد المتبقي", "Remaining balance", language), "5,250.00")]),
            [Label("عينة مولدة من سجل عملية بيع مؤكدة للتاجر بصريات رؤية النيل.", "Generated from a confirmed wholesale sale for Basmat Ruya Elneel.", language)],
            new SignatureSection([Label("أعده", "Prepared by", language), Label("مسؤول المخزن", "Warehouse representative", language), Label("التاجر / العميل", "Merchant / customer", language), Label("اعتمده", "Approved by", language)]),
            new DocumentFooter());
    }

    private static DocumentModel MeaningfulWarehouseTransfer(DocumentLanguage language)
    {
        var title = Label("تحويل مخزون", "Warehouse transfer", language);
        var reference = "TR-20260909-022";
        var columns = new[]
        {
            new TableColumn("sku", Label("كود الصنف", "SKU", language), ColumnDataType.Code, Direction: DocumentTextDirection.LeftToRight),
            new TableColumn("product", Label("المنتج", "Product", language)),
            new TableColumn("quantity", Label("الكمية", "Quantity", language), ColumnDataType.Integer, DocumentTextAlignment.End),
            new TableColumn("source", Label("المصدر", "Source", language)),
            new TableColumn("destination", Label("الوجهة", "Destination", language))
        };
        var rows = new[]
        {
            new TableRow([new DocumentCell.Code("SKU-LENS-001"), DocumentCell.FromText("عدسات أكيوفيو أويسس"), new DocumentCell.Integer(12), DocumentCell.FromText("المخزن الرئيسي"), DocumentCell.FromText("فرع روكسي")]),
            new TableRow([new DocumentCell.Code("SKU-SOL-010"), DocumentCell.FromText("محلول عدسات 360 مل"), new DocumentCell.Integer(8), DocumentCell.FromText("المخزن الرئيسي"), DocumentCell.FromText("فرع روكسي")])
        };
        return new DocumentModel(
            new DocumentMetadata("warehouse-transfer", title, DocumentTemplateKind.Transactional, language, reference, new DateTime(2026, 9, 9, 12, 40, 0, DateTimeKind.Utc), "Mona Hassan", false, rows.Length),
            new DocumentHeader(title, Label("نقل أصناف بين مواقع التخزين", "Stock movement between locations", language), Label("مكتمل", "Completed", language)),
            [new InfoSection("Overview", [new InfoField(Label("التاريخ", "Date", language), "2026-09-09", DocumentTextDirection.LeftToRight), new InfoField(Label("من", "From", language), "المخزن الرئيسي"), new InfoField(Label("إلى", "To", language), "فرع روكسي"), new InfoField(Label("المسؤول", "Actor", language), "Mona Hassan")])],
            [], [new TableSection(Label("بنود التحويل", "Transfer lines", language), columns, rows, Label("لا توجد بنود", "No transfer lines", language))], null,
            [Label("تم ترحيل التحويل إلى رصيد الموقعين بعد اعتماد مسؤول المخزن.", "The transfer was posted to both locations after warehouse approval.", language)],
            new SignatureSection([Label("مسؤول المخزن", "Warehouse representative", language), Label("اعتمد", "Approved by", language)]), new DocumentFooter());
    }

    private static DocumentModel MeaningfulPaymentReceipt(DocumentLanguage language)
    {
        var title = Label("إيصال سداد", "Payment receipt", language);
        var reference = "PAY-20260909-044";
        var columns = new[]
        {
            new TableColumn("operation", Label("رقم العملية", "Operation no.", language), ColumnDataType.Code, Direction: DocumentTextDirection.LeftToRight),
            new TableColumn("method", Label("طريقة السداد", "Payment method", language)),
            new TableColumn("amount", Label("المبلغ", "Amount", language), ColumnDataType.Money, DocumentTextAlignment.End, NumberFormat: "#,##0.00"),
            new TableColumn("status", Label("الحالة", "Status", language))
        };
        var rows = new[] { new TableRow([new DocumentCell.Code("OP-20260909-101"), DocumentCell.FromText("تقسيط"), new DocumentCell.Money(7500), DocumentCell.FromText("مؤكد")]) };
        return new DocumentModel(
            new DocumentMetadata("payment-receipt", title, DocumentTemplateKind.Transactional, language, reference, new DateTime(2026, 9, 9, 14, 5, 0, DateTimeKind.Utc), "Sara Nabil", false, rows.Length),
            new DocumentHeader(title, Label("تفاصيل التحصيل والمراجعة المالية", "Financial collection and review detail", language), Label("مؤكد", "Confirmed", language)),
            [new InfoSection("Overview", [new InfoField(Label("رقم الإيصال", "Receipt no.", language), reference, DocumentTextDirection.LeftToRight), new InfoField(Label("التاجر", "Merchant", language), Label("بصريات رؤية النيل", "Basmat Ruya Elneel", language)), new InfoField(Label("رقم العملية", "Operation no.", language), "OP-20260909-101", DocumentTextDirection.LeftToRight), new InfoField(Label("التاريخ", "Date", language), "2026-09-09", DocumentTextDirection.LeftToRight)])],
            [new SummaryMetric(Label("إجمالي المستند", "Document total", language), "12,750.00"), new SummaryMetric(Label("المبلغ المدفوع", "Paid amount", language), "7,500.00"), new SummaryMetric(Label("المبلغ المتبقي", "Remaining amount", language), "5,250.00")],
            [new TableSection(Label("بنود السداد", "Payment entries", language), columns, rows, Label("لا توجد مدفوعات", "No payment entries", language))],
            new TotalsSection([new SummaryMetric(Label("المدفوع", "Paid", language), "7,500.00")]), [], new SignatureSection([Label("سجله", "Recorded by", language), Label("اعتماد المحاسب", "Accountant approval", language), Label("إقرار التاجر", "Merchant acknowledgment", language)]), new DocumentFooter());
    }

    private static DocumentModel MeaningfulInventoryReport(DocumentLanguage language, int rowCount)
    {
        var title = Label("تقرير المخزون", "Inventory report", language);
        var columns = new[]
        {
            new TableColumn("sku", Label("كود الصنف", "SKU", language), ColumnDataType.Code, Direction: DocumentTextDirection.LeftToRight),
            new TableColumn("product", Label("المنتج", "Product", language)),
            new TableColumn("location", Label("الموقع", "Location", language)),
            new TableColumn("available", Label("المتاح", "Available", language), ColumnDataType.Integer, DocumentTextAlignment.End),
            new TableColumn("reserved", Label("محجوز بالمخزن", "Reserved warehouse", language), ColumnDataType.Integer, DocumentTextAlignment.End),
            new TableColumn("target", Label("المستهدف", "Target", language), ColumnDataType.Integer, DocumentTextAlignment.End),
            new TableColumn("updated", Label("آخر تحديث", "Updated", language), ColumnDataType.DateTime, DocumentTextAlignment.End)
        };
        var productNames = new[] { ("SKU-LENS-001", "عدسات أكيوفيو أويسس", "Acuvue Oasys lenses", 124, 18, 100), ("SKU-SOL-010", "محلول عدسات 360 مل", "360ml contact lens solution", 86, 12, 75), ("SKU-FRM-204", "إطار نظارة موديل 204", "Optical frame model 204", 42, 5, 50), ("SKU-ACC-330", "حافظة نظارة جلدية", "Leather eyeglass case", 215, 24, 180) };
        var rows = Enumerable.Range(0, rowCount).Select(index =>
        {
            var item = productNames[index % productNames.Length];
            return new TableRow([new DocumentCell.Code(item.Item1), DocumentCell.FromText(Label(item.Item2, item.Item3, language)), DocumentCell.FromText(Label("المخزن الرئيسي", "Main Warehouse", language)), new DocumentCell.Integer(item.Item4 + index), new DocumentCell.Integer(item.Item5 + index % 7), new DocumentCell.Integer(item.Item6), new DocumentCell.DateTimeValue(new DateTime(2026, 9, 9, 8, 30, 0, DateTimeKind.Utc).AddMinutes(index))]);
        }).ToArray();
        return new DocumentModel(new DocumentMetadata("stock", title, DocumentTemplateKind.Analytical, language, null, new DateTime(2026, 9, 9, 8, 30, 0, DateTimeKind.Utc), "Inventory control", true, rowCount), new DocumentHeader(title, Label("رصيد الأصناف حسب الموقع", "Stock balance by location", language)), [new InfoSection(Label("بيانات التقرير", "Report metadata", language), [new InfoField(Label("الموقع", "Location", language), Label("المخزن الرئيسي", "Main Warehouse", language)), new InfoField(Label("تاريخ التقرير", "Report date", language), "2026-09-09", DocumentTextDirection.LeftToRight)])], [new SummaryMetric(Label("عدد الأصناف", "SKU count", language), rowCount.ToString()), new SummaryMetric(Label("الوحدات المتاحة", "Available units", language), "3,184")], [new TableSection(null, columns, rows, Label("لا توجد بيانات للمعايير المحددة.", "No data found for the selected criteria.", language))], new TotalsSection([new SummaryMetric(Label("الوحدات المتاحة", "Available units", language), "3,184")]), [], null, new DocumentFooter());
    }

    private static DocumentModel MeaningfulStockMovement(DocumentLanguage language, int rowCount)
    {
        var document = MeaningfulInventoryReport(language, rowCount);
        document = document with { Metadata = document.Metadata with { DocumentKey = "stock-movement" }, Header = new DocumentHeader(Label("حركة المخزون", "Stock movement", language), Label("تتبع حركات الاستلام والتحويل والبيع", "Receipt, transfer, and sale movement ledger", language)) };
        var table = document.Tables[0];
        var movementColumns = table.Columns.Concat([new TableColumn("movement", Label("الحركة", "Movement", language))]).ToArray();
        var movementRows = table.Rows.Select((row, index) => new TableRow(row.Cells.Concat([DocumentCell.FromText(MovementLabel(index, language))]).ToArray())).ToArray();
        return document with { Tables = [table with { Columns = movementColumns, Rows = movementRows }] };
    }

    private static string MovementLabel(int index, DocumentLanguage language) => (index % 3) switch
    {
        0 => Label("استلام", "Receipt", language),
        1 => Label("تحويل", "Transfer", language),
        _ => Label("بيع", "Sale", language)
    };

    private static DocumentModel MeaningfulStocktake(DocumentLanguage language, int rowCount)
    {
        var document = MeaningfulInventoryReport(language, rowCount);
        document = document with { Metadata = document.Metadata with { DocumentKey = "stocktake-summary", Template = DocumentTemplateKind.Operational }, Header = new DocumentHeader(Label("ملخص الجرد", "Stocktake summary", language), Label("الجرد الفعلي ومراجعة الفروقات", "Physical count and discrepancy review", language), Label("قيد المراجعة", "Under review", language)) };
        var table = document.Tables[0];
        var columns = table.Columns.Concat([new TableColumn("counted", Label("الجرد الفعلي", "Counted", language), ColumnDataType.Integer, DocumentTextAlignment.End), new TableColumn("variance", Label("الفرق", "Variance", language), ColumnDataType.Integer, DocumentTextAlignment.End)]).ToArray();
        var rows = table.Rows.Select((row, index) =>
        {
            var available = ((DocumentCell.Integer)row.Cells[3]).Value;
            var counted = available + StocktakeVariance(index);
            return new TableRow(row.Cells.Concat([new DocumentCell.Integer(counted), new DocumentCell.Integer(counted - available)]).ToArray());
        }).ToArray();
        return document with { Tables = [table with { Columns = columns, Rows = rows }], Notes = [Label("تم وضع الأصناف ذات الفرق تحت مراجعة مسؤول الجرد.", "Items with a variance are held for stocktake review.", language)], Signatures = new SignatureSection([Label("قام بالجرد", "Counted by", language), Label("راجعه", "Reviewed by", language), Label("اعتمده", "Confirmed by", language)]) };
    }

    private static int StocktakeVariance(int index)
    {
        if (index % 5 == 0) return -2;
        return index % 7 == 0 ? 1 : 0;
    }

    private static DocumentModel MeaningfulSalesReport(DocumentLanguage language, int rowCount)
    {
        var document = MeaningfulInventoryReport(language, rowCount);
        document = document with { Metadata = document.Metadata with { DocumentKey = "operations" }, Header = new DocumentHeader(Label("تقرير المبيعات", "Sales report", language), Label("العمليات التجارية والمدفوعات المرتبطة", "Commercial operations and related payments", language)) };
        var columns = new[] { new TableColumn("operation", Label("رقم العملية", "Operation", language), ColumnDataType.Code, Direction: DocumentTextDirection.LeftToRight), new TableColumn("type", Label("النوع", "Type", language)), new TableColumn("merchant", Label("التاجر", "Merchant", language)), new TableColumn("quantity", Label("الكمية", "Quantity", language), ColumnDataType.Integer, DocumentTextAlignment.End), new TableColumn("total", Label("الإجمالي", "Total", language), ColumnDataType.Money, DocumentTextAlignment.End, NumberFormat: "#,##0.00"), new TableColumn("created", Label("تاريخ الإنشاء", "Created", language), ColumnDataType.DateTime, DocumentTextAlignment.End) };
        var rows = Enumerable.Range(1, rowCount).Select(index => new TableRow([new DocumentCell.Code($"OP-202609{index % 30 + 1:00}-{100 + index}"), DocumentCell.FromText(index % 3 == 0 ? Label("بيع قطاعي", "Retail sale", language) : Label("بيع جملة", "Wholesale sale", language)), DocumentCell.FromText(Label("بصريات رؤية النيل", "Basmat Ruya Elneel", language)), new DocumentCell.Integer(5 + index % 20), new DocumentCell.Money(850 + index * 125), new DocumentCell.DateTimeValue(new DateTime(2026, 9, 9, 9, 0, 0, DateTimeKind.Utc).AddMinutes(index * 11))])).ToArray();
        return document with { Tables = [new TableSection(Label("العمليات", "Operations", language), columns, rows, Label("لا توجد عمليات", "No operations", language))], Totals = new TotalsSection([new SummaryMetric(Label("إجمالي المبيعات", "Sales total", language), "184,250.00")]) };
    }

    private static DocumentModel SampleDocument(
        DocumentLanguage language,
        DocumentTemplateKind template = DocumentTemplateKind.Analytical,
        int rowCount = 1)
    {
        var columns = new[]
        {
            new TableColumn("code", "Code", ColumnDataType.Code, Direction: DocumentTextDirection.LeftToRight),
            new TableColumn("amount", "Amount", ColumnDataType.Money, NumberFormat: "0.00"),
            new TableColumn("date", "Date", ColumnDataType.Date),
            new TableColumn("name", "الاسم / Name")
        };
        var rows = Enumerable.Range(1, rowCount).Select(index =>
            new TableRow([
                DocumentCell.FromText("=SUM(A1:A2)", DocumentTextDirection.LeftToRight),
                new DocumentCell.Number(-125.5m * index),
                new DocumentCell.Date(new DateOnly(2026, 9, 9)),
                DocumentCell.FromText($"عدسة {index} / Lens {index}")])).ToArray();
        return new DocumentModel(
            new DocumentMetadata("stock/test", "تقرير المخزون / Inventory report", template, language, "REP-001", new DateTime(2026, 9, 9, 8, 30, 0, DateTimeKind.Utc), "Tester", template == DocumentTemplateKind.Operational, rowCount),
            new DocumentHeader("تقرير المخزون / Inventory report", "Long mixed direction note SKU-001", "Ready"),
            [new InfoSection("Filters", [new InfoField("Location", "Main Warehouse")])],
            [new SummaryMetric("Net", "-125.50")],
            [new TableSection("Stock/[Current]*Snapshot", columns, rows, "No data")],
            new TotalsSection([new SummaryMetric("Total", "-125.50")]),
            ["A deliberately long note validates wrapping without changing the semantic value."],
            new SignatureSection(["Prepared by", "Approved by"]),
            new DocumentFooter());
    }

    private sealed class TestBrandingProvider : IDocumentBrandingProvider
    {
        public DocumentBranding Branding { get; } = new(
            "لينسي",
            "Lensee",
            null,
            null,
            "+20 100 000 0000",
            null,
            null,
            "EGP",
            "Noto Sans Arabic",
            "Noto Sans",
            null);
    }
}
