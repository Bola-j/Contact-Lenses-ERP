namespace Lensee.Modules.Reporting.Application.Models;

public enum DocumentTemplateKind
{
    Transactional,
    Analytical,
    Operational
}

public enum ExportFormat
{
    Pdf,
    Excel,
    Csv
}

public enum DocumentLanguage
{
    Arabic,
    English,
    Bilingual
}

public enum DocumentTextDirection
{
    Auto,
    LeftToRight,
    RightToLeft
}

public enum ColumnDataType
{
    Text,
    Integer,
    Decimal,
    Money,
    Percentage,
    Date,
    DateTime,
    Boolean,
    Code
}

public enum DocumentTextAlignment
{
    Start,
    Center,
    End
}

public sealed record DocumentMetadata(
    string DocumentKey,
    string Title,
    DocumentTemplateKind Template,
    DocumentLanguage Language,
    string? ReferenceNumber,
    DateTime GeneratedAtUtc,
    string? GeneratedBy,
    bool Landscape = false,
    int RowCount = 0);

public sealed record DocumentModel(
    DocumentMetadata Metadata,
    DocumentHeader Header,
    IReadOnlyList<InfoSection> InfoSections,
    IReadOnlyList<SummaryMetric> SummaryMetrics,
    IReadOnlyList<TableSection> Tables,
    TotalsSection? Totals,
    IReadOnlyList<string> Notes,
    SignatureSection? Signatures,
    DocumentFooter? Footer);

public sealed record DocumentHeader(string Title, string? Subtitle = null, string? Status = null);

public sealed record InfoSection(string? Title, IReadOnlyList<InfoField> Fields);

public sealed record InfoField(
    string Label,
    string Value,
    DocumentTextDirection Direction = DocumentTextDirection.Auto);

public sealed record SummaryMetric(string Label, string Value, string? Tone = null);

public sealed record TableSection(
    string? Title,
    IReadOnlyList<TableColumn> Columns,
    IReadOnlyList<TableRow> Rows,
    string EmptyMessage,
    bool RepeatHeader = true);

public sealed record TableColumn(
    string Key,
    string Header,
    ColumnDataType DataType = ColumnDataType.Text,
    DocumentTextAlignment Alignment = DocumentTextAlignment.Start,
    decimal? RelativeWidth = null,
    string? NumberFormat = null,
    DocumentTextDirection Direction = DocumentTextDirection.Auto);

public sealed record TableRow(IReadOnlyList<DocumentCell> Cells);

public abstract record DocumentCell
{
    private DocumentCell() { }

    public sealed record Text(string Value, DocumentTextDirection Direction = DocumentTextDirection.Auto) : DocumentCell;
    public sealed record Code(string Value, DocumentTextDirection Direction = DocumentTextDirection.LeftToRight) : DocumentCell;
    public sealed record Integer(long Value) : DocumentCell;
    public sealed record Number(decimal Value) : DocumentCell;
    public sealed record Decimal(decimal Value) : DocumentCell;
    public sealed record Money(decimal Value, string? CurrencyCode = null) : DocumentCell;
    public sealed record Percentage(decimal Value) : DocumentCell;
    public sealed record Date(DateOnly Value) : DocumentCell;
    public sealed record DateTimeValue(DateTime Value) : DocumentCell;
    public sealed record Boolean(bool Value) : DocumentCell;

    public static DocumentCell FromText(string? value, DocumentTextDirection direction = DocumentTextDirection.Auto) =>
        new Text(value ?? string.Empty, direction);
}

public sealed record TotalsSection(IReadOnlyList<SummaryMetric> Values);

public sealed record SignatureSection(IReadOnlyList<string> Labels);

public sealed record DocumentFooter(string? Text = null, bool ShowPageNumbers = true);

public sealed record RenderedDocument(Stream Content, string ContentType, string FileExtension);

public sealed record DocumentExportResult(Stream Content, string FileName, string ContentType);

public sealed record DocumentBuildContext(
    DocumentLanguage Language,
    DateTime GeneratedAtUtc,
    string? GeneratedBy = null);
