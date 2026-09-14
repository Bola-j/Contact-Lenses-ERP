using System.Globalization;
using Lensee.Modules.Reporting.Application.Abstractions;
using Lensee.Modules.Reporting.Application.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lensee.Modules.Reporting.Infrastructure;

public interface IQuestPdfTemplate
{
    DocumentTemplateKind Kind { get; }
    void Compose(PageDescriptor page, DocumentModel document, DocumentRenderContext context);
}

public sealed record DocumentRenderContext(DocumentBranding Branding, IDocumentLocalizer Localizer);

public sealed class QuestPdfDocumentRenderer(
    IDocumentBrandingProvider brandingProvider,
    IDocumentLocalizer localizer) : IDocumentRenderer
{
    static QuestPdfDocumentRenderer()
    {
        ReportingFontRegistry.RegisterBundledFonts();
    }

    private readonly IReadOnlyDictionary<DocumentTemplateKind, IQuestPdfTemplate> _templates =
        new IQuestPdfTemplate[]
        {
            new TransactionalPdfTemplate(),
            new AnalyticalPdfTemplate(),
            new OperationalPdfTemplate()
        }.ToDictionary(template => template.Kind);

    public ExportFormat Format => ExportFormat.Pdf;

    public Task<RenderedDocument> RenderAsync(DocumentModel document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var context = new DocumentRenderContext(brandingProvider.Branding, localizer);
        var stream = new MemoryStream();
        var questDocument = QuestPDF.Fluent.Document.Create(container =>
            container.Page(page => _templates[document.Metadata.Template].Compose(page, document, context)));
        cancellationToken.ThrowIfCancellationRequested();
        questDocument.GeneratePdf(stream);
        stream.Position = 0;
        return Task.FromResult(new RenderedDocument(stream, "application/pdf", "pdf"));
    }
}

public abstract class QuestPdfTemplateBase : IQuestPdfTemplate
{
    public abstract DocumentTemplateKind Kind { get; }

    public virtual void Compose(PageDescriptor page, DocumentModel document, DocumentRenderContext context)
    {
        var arabic = document.Metadata.Language is DocumentLanguage.Arabic or DocumentLanguage.Bilingual;
        page.Size(document.Metadata.Landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
        page.Margin(18);
        page.PageColor(Colors.White);
        if (arabic) page.ContentFromRightToLeft();
        else page.ContentFromLeftToRight();

        var fontFamily = arabic ? context.Branding.ArabicFontFamily : context.Branding.LatinFontFamily;
        page.DefaultTextStyle(text => text.FontFamily(fontFamily).FontSize(8.5f).FontColor(Colors.Grey.Darken3));
        page.Header().Element(container => RenderDocumentHeader(container, document, context, arabic));
        page.Content().PaddingTop(8).Column(column => RenderContent(column, document, arabic));
        page.Footer().Element(container => RenderDocumentFooter(container, document, context, arabic));
    }

    private static void RenderContent(ColumnDescriptor column, DocumentModel document, bool arabic)
    {
        var sections = document.InfoSections;
        var language = document.Metadata.Language;
        var overview = sections.Count > 0 && IsOverview(sections[0]) ? sections[0] : null;
        var overviewFacts = overview?.Fields ?? [];
        var remainingSections = overview is null ? sections : sections.Skip(1).ToArray();

        if (document.Metadata.DocumentKey.Equals("cash-receipt", StringComparison.OrdinalIgnoreCase))
        {
            var amount = overviewFacts.FirstOrDefault(field =>
                field.Label.Contains(arabic ? "المدفوع" : "Paid", StringComparison.OrdinalIgnoreCase) ||
                field.Label.Contains(arabic ? "الإجمالي" : "Total", StringComparison.OrdinalIgnoreCase))?.Value;
            if (!string.IsNullOrWhiteSpace(amount))
            {
                column.Item().Element(container => RenderCashAmount(container, amount, language));
            }
        }

        if (overviewFacts.Count > 0)
        {
            column.Item().PaddingTop(4).Element(container => RenderSectionHeading(container, TemplateText("overview", language), arabic));
            column.Item().PaddingTop(5).Element(container => RenderOverviewPanel(container, overviewFacts, arabic));
        }

        var metrics = document.SummaryMetrics.Concat(document.Totals?.Values ?? []).ToArray();
        if (metrics.Length > 0)
        {
            column.Item().PaddingTop(8).Element(container => RenderFactGrid(container, metrics, arabic, Math.Min(4, metrics.Length), false));
        }

        // Merchant statements are dossiers rather than single-page receipts. Put
        // the compact sign-off fields on page one with the account summary so a
        // long Arabic appendix cannot strand them on a nearly blank final page.
        var signaturesOnFirstPage = document.Metadata.DocumentKey.Equals("merchant-statement", StringComparison.OrdinalIgnoreCase);
        if (signaturesOnFirstPage && document.Signatures is { Labels.Count: > 0 })
        {
            column.Item().PaddingTop(8).Element(container => RenderSignatureBlocks(container, document.Signatures, language));
        }

        if (document.Metadata.DocumentKey.Equals("supply-landed-cost", StringComparison.OrdinalIgnoreCase))
        {
            RenderSupplySections(column, remainingSections, document.Tables, arabic);
        }
        else if (document.Metadata.DocumentKey.Equals("cash-receipt", StringComparison.OrdinalIgnoreCase))
        {
            RenderCashSections(column, remainingSections, document.Tables, language);
        }
        else
        {
            foreach (var section in remainingSections)
            {
                RenderInfoSection(column, section, arabic);
            }

            foreach (var table in document.Tables)
            {
                RenderTableWithTitle(column, table, arabic);
            }
        }

        foreach (var note in document.Notes)
        {
            column.Item().PaddingTop(8).Element(container => RenderNote(container, note, language));
        }

        if (!signaturesOnFirstPage && document.Signatures is { Labels.Count: > 0 })
        {
            column.Item().PaddingTop(12).Element(container => RenderSignatureBlocks(container, document.Signatures, language));
        }
    }

    private static bool IsOverview(InfoSection section) =>
        string.Equals(section.Title, "Overview", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(section.Title, "بيانات المستند", StringComparison.OrdinalIgnoreCase);

    private static void RenderInfoSection(ColumnDescriptor column, InfoSection section, bool arabic)
    {
        if (section.Fields.Count == 0) return;
        column.Item().PaddingTop(10).Element(container => RenderSectionHeading(container, section.Title ?? string.Empty, arabic));
        var compact = IsMetricSection(section.Title ?? string.Empty, arabic);
        column.Item().PaddingTop(5).Element(container =>
        {
            if (compact) RenderFactGrid(container, section.Fields, arabic, Math.Min(4, section.Fields.Count), false);
            else RenderOverviewPanel(container, section.Fields, arabic);
        });
    }

    private static void RenderSupplySections(
        ColumnDescriptor column,
        IReadOnlyList<InfoSection> sections,
        IReadOnlyList<TableSection> tables,
        bool arabic)
    {
        foreach (var section in sections)
        {
            RenderInfoSection(column, section, arabic);
        }

        if (tables.Count > 0)
        {
            RenderTableWithTitle(column, tables[0], arabic);
        }

        if (tables.Count > 1)
        {
            column.Item().PaddingTop(10).Row(row =>
            {
                if (arabic)
                {
                    if (tables.Count > 2) row.RelativeItem().Column(inner => RenderTableWithTitle(inner, tables[2], arabic));
                    row.ConstantItem(12);
                    row.RelativeItem().Column(inner => RenderTableWithTitle(inner, tables[1], arabic));
                }
                else
                {
                    row.RelativeItem().Column(inner => RenderTableWithTitle(inner, tables[1], arabic));
                    if (tables.Count > 2)
                    {
                        row.ConstantItem(12);
                        row.RelativeItem().Column(inner => RenderTableWithTitle(inner, tables[2], arabic));
                    }
                }
            });
        }

        for (var index = 3; index < tables.Count; index++)
        {
            RenderTableWithTitle(column, tables[index], arabic);
        }
    }

    private static void RenderCashSections(
        ColumnDescriptor column,
        IReadOnlyList<InfoSection> sections,
        IReadOnlyList<TableSection> tables,
        DocumentLanguage language)
    {
        var arabic = language is DocumentLanguage.Arabic or DocumentLanguage.Bilingual;
        var merchant = sections.FirstOrDefault(section => ContainsTitle(section.Title, "merchant", "التاجر"));
        var operation = sections.FirstOrDefault(section => ContainsTitle(section.Title, "operation", "العملية"));
        var custodyFacts = (merchant?.Fields ?? []).Concat(operation?.Fields ?? []).ToArray();
        if (custodyFacts.Length > 0)
        {
            RenderInfoSection(column, new InfoSection(TemplateText("cash custody details", language), custodyFacts), arabic);
        }

        foreach (var section in sections)
        {
            if (ReferenceEquals(section, merchant) || ReferenceEquals(section, operation)) continue;
            RenderInfoSection(column, section, arabic);
        }

        foreach (var table in tables)
        {
            RenderTableWithTitle(column, table, arabic);
        }
    }

    private static bool ContainsTitle(string? title, string english, string arabic) =>
        !string.IsNullOrWhiteSpace(title) &&
        (title.Contains(english, StringComparison.OrdinalIgnoreCase) || title.Contains(arabic, StringComparison.OrdinalIgnoreCase));

    private static void RenderTableWithTitle(ColumnDescriptor column, TableSection table, bool arabic)
    {
        column.Item().PaddingTop(10).Element(container =>
            AlignByLanguage(container, arabic).Text(table.Title ?? string.Empty).SemiBold().FontSize(8.5f).FontColor(Colors.Grey.Darken4));
        column.Item().PaddingTop(3).Element(container => RenderTable(container, table, arabic));
    }

    private static void RenderDocumentHeader(IContainer container, DocumentModel document, DocumentRenderContext context, bool arabic)
    {
        var header = container.BorderTop(4).BorderColor(Colors.Grey.Darken4).PaddingTop(14).ContentFromLeftToRight();
        header.Column(column =>
        {
            column.Item().Row(row =>
            {
                if (arabic)
                {
                    row.ConstantItem(270).Element(item => RenderDocumentIdentity(item, document, false, arabic));
                    row.RelativeItem().AlignRight().Element(item => RenderBrand(item, context.Branding, true));
                }
                else
                {
                    row.RelativeItem().Element(item => RenderBrand(item, context.Branding, false));
                    row.ConstantItem(270).Element(item => RenderDocumentIdentity(item, document, true, arabic));
                }
            });
            AlignByLanguage(column.Item().PaddingTop(9), arabic)
                .Text(context.Localizer.Text("InternalDocument", document.Metadata.Language).ToUpperInvariant())
                .SemiBold().FontSize(6.5f).FontColor(Colors.Grey.Darken1);
            column.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
        });
    }

    private static void RenderBrand(IContainer container, DocumentBranding branding, bool arabic)
    {
        container.Column(column =>
        {
            AlignByLanguage(column.Item(), arabic).Text(branding.CompanyNameEnglish.ToUpperInvariant()).SemiBold().FontSize(18).FontColor(Colors.Grey.Darken4);
            AlignByLanguage(column.Item(), arabic).Text(arabic ? "نظام إدارة العمليات" : "OPTICAL OPERATIONS").SemiBold().FontSize(6).FontColor(Colors.Grey.Darken1);
        });
    }

    private static void RenderDocumentIdentity(IContainer container, DocumentModel document, bool alignRight, bool arabic)
    {
        container.Column(column =>
        {
            AlignByLanguage(column.Item(), alignRight).ScaleToFit().Text(document.Header.Title).SemiBold().FontSize(16).FontColor(Colors.Grey.Darken4);
            if (!string.IsNullOrWhiteSpace(document.Header.Subtitle))
            {
                AlignByLanguage(column.Item(), alignRight).ScaleToFit().Text(document.Header.Subtitle).FontSize(8).FontColor(Colors.Grey.Darken1);
            }
            if (!string.IsNullOrWhiteSpace(document.Metadata.ReferenceNumber) || !string.IsNullOrWhiteSpace(document.Header.Status))
            {
                // Keep the long business reference in its own flow row. Sharing a
                // row with the status badge allowed long merchant references to
                // run underneath the badge in landscape statements.
                column.Item().PaddingTop(8).Column(metadata =>
                {
                    if (!string.IsNullOrWhiteSpace(document.Metadata.ReferenceNumber))
                    {
                        AlignByLanguage(metadata.Item().AlignRight().ScaleToFit(), alignRight)
                            .ContentFromLeftToRight().Text(ToPdfText(document.Metadata.ReferenceNumber, DocumentTextDirection.LeftToRight, arabic))
                            .SemiBold().FontSize(document.Metadata.ReferenceNumber.Length > 25 ? 7 : 9).FontColor(Colors.Grey.Darken4);
                    }
                    if (!string.IsNullOrWhiteSpace(document.Header.Status))
                    {
                        AlignByLanguage(metadata.Item().PaddingTop(3).AlignRight().Width(76).Border(1).BorderColor(Colors.Grey.Lighten1).PaddingVertical(5).AlignCenter(), alignRight)
                            .Text(document.Header.Status).SemiBold().FontSize(6.5f).FontColor(Colors.Grey.Darken4);
                    }
                });
            }
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

    private static void RenderFactGrid(IContainer container, IReadOnlyList<InfoField> facts, bool arabic, int columnsCount, bool accent)
    {
        var orderedFacts = arabic ? facts.Reverse().ToArray() : facts.ToArray();
        columnsCount = Math.Max(1, columnsCount);
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                for (var index = 0; index < columnsCount; index++) columns.RelativeColumn();
            });

            foreach (var fact in orderedFacts)
            {
                var cell = table.Cell().Padding(2).Border(1).BorderColor(Colors.Grey.Lighten2).Padding(7);
                if (accent) cell = arabic ? cell.BorderRight(4).BorderColor(Colors.Grey.Darken3) : cell.BorderLeft(4).BorderColor(Colors.Grey.Darken3);
                cell.Column(column =>
                {
                    AlignByLanguage(column.Item(), arabic).Text(fact.Label).SemiBold().FontSize(6.5f).FontColor(Colors.Grey.Darken1);
                    ApplyFieldFlow(column.Item().PaddingTop(3), fact, arabic)
                        .Text(ToPdfText(fact.Value, ResolveFieldDirection(fact), arabic)).SemiBold().FontSize(9.5f).FontColor(Colors.Grey.Darken4);
                });
            }

            for (var index = orderedFacts.Length; index % columnsCount != 0; index++) table.Cell().Padding(2);
        });
    }

    private static void RenderFactGrid(IContainer container, IReadOnlyList<SummaryMetric> metrics, bool arabic, int columnsCount, bool accent)
    {
        RenderFactGrid(container, metrics.Select(metric => new InfoField(metric.Label, metric.Value)).ToArray(), arabic, columnsCount, accent);
    }

    private static void RenderOverviewPanel(IContainer container, IReadOnlyList<InfoField> facts, bool arabic)
    {
        var orderedFacts = arabic ? facts.Reverse().ToArray() : facts.ToArray();
        var panel = container.Border(1).BorderColor(Colors.Grey.Lighten2).Background(Colors.Grey.Lighten5).ContentFromLeftToRight();
        panel.Row(row =>
        {
            row.ConstantItem(4).Background(Colors.Grey.Darken3);
            row.RelativeItem().Padding(8).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.RelativeColumn();
            });
            foreach (var fact in orderedFacts)
            {
                table.Cell().PaddingVertical(3).Column(column =>
                {
                    AlignByLanguage(column.Item(), arabic).Text(fact.Label).SemiBold().FontSize(6.5f).FontColor(Colors.Grey.Darken1);
                    var value = ApplyFieldFlow(column.Item().PaddingTop(2), fact, arabic);
                    value.Text(ToPdfText(fact.Value, ResolveFieldDirection(fact), arabic)).SemiBold().FontSize(9).FontColor(Colors.Grey.Darken4);
                });
            }
            if (orderedFacts.Length % 2 != 0) table.Cell();
        });
        });
    }

    private static void RenderCashAmount(IContainer container, string amount, DocumentLanguage language)
    {
        var arabic = language is DocumentLanguage.Arabic or DocumentLanguage.Bilingual;
        container.Background(Colors.Grey.Lighten4).Padding(12).Row(row =>
        {
            if (arabic)
            {
                row.RelativeItem().ContentFromLeftToRight().Text(ToPdfText(amount, DocumentTextDirection.LeftToRight, arabic)).SemiBold().FontSize(21).FontColor(Colors.Grey.Darken4);
                row.RelativeItem().AlignRight().Column(column =>
                {
                    column.Item().Text(TemplateText("payment type", language)).SemiBold().FontSize(7).FontColor(Colors.Grey.Darken1);
                    column.Item().Text(TemplateText("cash hand to hand", language)).SemiBold().FontSize(10).FontColor(Colors.Grey.Darken4);
                });
            }
            else
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Text(TemplateText("payment type", language)).SemiBold().FontSize(7).FontColor(Colors.Grey.Darken1);
                    column.Item().Text(TemplateText("cash hand to hand", language)).SemiBold().FontSize(10).FontColor(Colors.Grey.Darken4);
                });
                row.RelativeItem().AlignRight().ContentFromLeftToRight().Text(ToPdfText(amount, DocumentTextDirection.LeftToRight, arabic)).SemiBold().FontSize(21).FontColor(Colors.Grey.Darken4);
            }
        });
    }

    private static void RenderNote(IContainer container, string note, DocumentLanguage language)
    {
        var arabic = language is DocumentLanguage.Arabic or DocumentLanguage.Bilingual;
        container.Border(1).BorderColor(Colors.Grey.Lighten2).Background(Colors.Grey.Lighten5).Padding(8).Column(column =>
        {
            AlignByLanguage(column.Item(), arabic).Text(TemplateText("note", language)).SemiBold().FontSize(6.5f).FontColor(Colors.Grey.Darken1);
            AlignByLanguage(column.Item().PaddingTop(4), arabic).Text(note).FontSize(8).FontColor(Colors.Grey.Darken4);
        });
    }

    private static void RenderTable(IContainer container, TableSection section, bool arabic)
    {
        var columns = arabic ? section.Columns.Reverse().ToArray() : section.Columns.ToArray();
        var rows = arabic
            ? section.Rows.Select(row => (IReadOnlyList<DocumentCell>)row.Cells.Reverse().ToArray()).ToArray()
            : section.Rows.Select(row => row.Cells).ToArray();

        container.Table(table =>
        {
            table.ColumnsDefinition(definition =>
            {
                foreach (var column in columns) definition.RelativeColumn((float)(column.RelativeWidth ?? 1m));
            });
            if (section.RepeatHeader)
            {
                table.Header(header =>
                {
                    foreach (var column in columns)
                    {
                        var headerCell = header.Cell().Background(Colors.Grey.Lighten3).Padding(5);
                        ApplyColumnFlow(headerCell, column, arabic)
                            .Text(column.Header).SemiBold().FontSize(6.5f).FontColor(Colors.Grey.Darken4);
                    }
                });
            }

            if (rows.Length == 0)
            {
                AlignByLanguage(table.Cell().ColumnSpan((uint)Math.Max(1, columns.Length)).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(8), arabic)
                    .Text(section.EmptyMessage).FontColor(Colors.Grey.Darken1);
            }

            for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                for (var columnIndex = 0; columnIndex < columns.Length; columnIndex++)
                {
                    var cellValue = columnIndex < rows[rowIndex].Count ? rows[rowIndex][columnIndex] : DocumentCell.FromText(string.Empty);
                    var cell = table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2);
                    if (rowIndex % 2 == 1) cell = cell.Background(Colors.Grey.Lighten5);
                    var direction = cellValue switch
                    {
                        DocumentCell.Text text when text.Direction != DocumentTextDirection.Auto => text.Direction,
                        DocumentCell.Code code => code.Direction,
                        DocumentCell.Integer or DocumentCell.Number or DocumentCell.Decimal or DocumentCell.Money or
                            DocumentCell.Percentage or DocumentCell.Date or DocumentCell.DateTimeValue or DocumentCell.Boolean => DocumentTextDirection.LeftToRight,
                        _ => columns[columnIndex].Direction
                    };
                    var columnDefinition = columns[columnIndex];
                    ApplyColumnFlow(cell.Padding(5), columnDefinition, arabic, direction)
                        .Text(ToPdfText(
                            ReportingServiceCollectionExtensions.CellText(cellValue, columnDefinition.NumberFormat),
                            direction,
                            arabic))
                        .FontSize(7.2f);
                }
            }
        });
    }

    private static void RenderSignatureBlocks(IContainer container, SignatureSection signatures, DocumentLanguage language)
    {
        var arabic = language is DocumentLanguage.Arabic or DocumentLanguage.Bilingual;
        container.ShowEntire().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                foreach (var _ in signatures.Labels) columns.RelativeColumn();
            });
            foreach (var label in arabic ? signatures.Labels.Reverse() : signatures.Labels)
            {
                table.Cell().Padding(3).Border(1).BorderColor(Colors.Grey.Lighten1).Padding(9).Column(column =>
                {
                    AlignByLanguage(column.Item(), arabic).Text(label).SemiBold().FontSize(6.5f).FontColor(Colors.Grey.Darken1);
                    column.Item().PaddingTop(34).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                    AlignByLanguage(column.Item().PaddingTop(3), arabic).Text(TemplateText("name signature date", language)).FontSize(6).FontColor(Colors.Grey.Darken1);
                });
            }
        });
    }

    private static void RenderDocumentFooter(IContainer container, DocumentModel document, DocumentRenderContext context, bool arabic)
    {
        var footer = container.BorderTop(1).BorderColor(Colors.Grey.Lighten2).PaddingTop(7).ContentFromLeftToRight();
        footer.Row(row =>
        {
            if (arabic)
            {
                row.RelativeItem().Text(context.Localizer.Text("InternalDocument", document.Metadata.Language)).FontSize(6.5f).FontColor(Colors.Grey.Darken1).AlignLeft();
                row.RelativeItem().AlignCenter().ContentFromLeftToRight().Text(ToPdfText(document.Metadata.ReferenceNumber ?? string.Empty, DocumentTextDirection.LeftToRight, arabic)).FontSize(6.5f).FontColor(Colors.Grey.Darken1);
            }
            else
            {
                row.RelativeItem().Text($"{context.Branding.CompanyNameEnglish} ERP | {context.Localizer.Text("InternalDocument", document.Metadata.Language)}").FontSize(6.5f).FontColor(Colors.Grey.Darken1);
                row.RelativeItem().AlignCenter().ContentFromLeftToRight().Text($"Reference: {document.Metadata.ReferenceNumber}").FontSize(6.5f).FontColor(Colors.Grey.Darken1);
            }
            if (document.Footer?.ShowPageNumbers != false)
            {
                row.RelativeItem().AlignRight().ContentFromLeftToRight().Text(text =>
                {
                    text.Span(arabic ? "صفحة " : "Page ").FontSize(6.5f);
                    text.CurrentPageNumber().FontSize(6.5f);
                    text.Span(arabic ? " من " : " of ").FontSize(6.5f);
                    text.TotalPages().FontSize(6.5f);
                });
            }
        });
    }

    private static bool IsMetricSection(string title, bool arabic)
    {
        var normalized = title.ToLowerInvariant();
        return normalized.Contains(arabic ? "ملخص" : "summary") ||
            normalized.Contains(arabic ? "دفع" : "payment") ||
            normalized.Contains(arabic ? "رصيد" : "balance");
    }

    private static string ToPdfText(string value, DocumentTextDirection direction, bool arabic) =>
        arabic && direction == DocumentTextDirection.LeftToRight ? $"\u200E{value}\u200E" : value;

    private static IContainer AlignByLanguage(IContainer container, bool alignRight) => alignRight ? container.AlignRight() : container.AlignLeft();

    private static IContainer ApplyFieldFlow(IContainer container, InfoField field, bool arabic)
    {
        var direction = ResolveFieldDirection(field);
        if (direction == DocumentTextDirection.LeftToRight)
        {
            return container.ContentFromLeftToRight().AlignLeft();
        }

        if (direction == DocumentTextDirection.RightToLeft)
        {
            return container.ContentFromRightToLeft().AlignRight();
        }

        return AlignByLanguage(container, arabic);
    }

    private static IContainer ApplyColumnFlow(
        IContainer container,
        TableColumn column,
        bool arabic,
        DocumentTextDirection? cellDirection = null)
    {
        var direction = cellDirection.GetValueOrDefault() is { } explicitDirection && explicitDirection != DocumentTextDirection.Auto
            ? explicitDirection
            : column.Direction;

        if (direction == DocumentTextDirection.LeftToRight)
        {
            container = container.ContentFromLeftToRight();
        }
        else if (direction == DocumentTextDirection.RightToLeft)
        {
            container = container.ContentFromRightToLeft();
        }

        return column.Alignment switch
        {
            DocumentTextAlignment.Center => container.AlignCenter(),
            DocumentTextAlignment.End => arabic ? container.AlignLeft() : container.AlignRight(),
            _ when direction == DocumentTextDirection.LeftToRight => container.AlignLeft(),
            _ => AlignByLanguage(container, arabic)
        };
    }

    private static DocumentTextDirection ResolveFieldDirection(InfoField field)
    {
        if (field.Direction != DocumentTextDirection.Auto)
        {
            return field.Direction;
        }

        var label = field.Label.ToLowerInvariant();
        return label.Contains("sku", StringComparison.Ordinal) ||
            label.Contains("code", StringComparison.Ordinal) ||
            label.Contains("reference", StringComparison.Ordinal) ||
            label.Contains("invoice", StringComparison.Ordinal) ||
            label.Contains("operation", StringComparison.Ordinal) ||
            label.Contains("email", StringComparison.Ordinal) ||
            label.Contains("url", StringComparison.Ordinal) ||
            label.Contains("id", StringComparison.Ordinal) ||
            label.Contains("الهاتف", StringComparison.Ordinal) ||
            label.Contains("البريد", StringComparison.Ordinal) ||
            label.Contains("التاريخ", StringComparison.Ordinal) ||
            label.Contains("المرجع", StringComparison.Ordinal)
            ? DocumentTextDirection.LeftToRight
            : DocumentTextDirection.Auto;
    }

    private static string TemplateText(string key, DocumentLanguage language) =>
        language == DocumentLanguage.Bilingual
            ? $"{TemplateText(key, true)} / {TemplateText(key, false)}"
            : TemplateText(key, language == DocumentLanguage.Arabic);

    private static string TemplateText(string key, bool arabic) => arabic switch
    {
        true => key switch
        {
            "overview" => "بيانات المستند",
            "internal document" => "نظام لينسي - مستند أعمال داخلي",
            "payment type" => "نوع الدفع",
            "cash hand to hand" => "تسليم نقدي باليد",
            "cash custody details" => "بيانات حيازة النقدية",
            "note" => "ملاحظة",
            "name signature date" => "الاسم / التوقيع / التاريخ",
            _ => key
        },
        _ => key switch
        {
            "overview" => "OVERVIEW",
            "internal document" => "LENSEE ERP - INTERNAL BUSINESS DOCUMENT",
            "payment type" => "PAYMENT TYPE",
            "cash hand to hand" => "CASH HAND-TO-HAND",
            "cash custody details" => "CASH CUSTODY DETAILS",
            "note" => "NOTE",
            "name signature date" => "Name / signature / date",
            _ => key
        }
    };
}

public sealed class TransactionalPdfTemplate : QuestPdfTemplateBase
{
    public override DocumentTemplateKind Kind => DocumentTemplateKind.Transactional;
}

public sealed class AnalyticalPdfTemplate : QuestPdfTemplateBase
{
    public override DocumentTemplateKind Kind => DocumentTemplateKind.Analytical;
}

public sealed class OperationalPdfTemplate : QuestPdfTemplateBase
{
    public override DocumentTemplateKind Kind => DocumentTemplateKind.Operational;
}
