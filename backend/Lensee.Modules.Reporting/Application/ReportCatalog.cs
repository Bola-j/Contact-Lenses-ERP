using Lensee.Modules.Reporting.Application.Models;

namespace Lensee.Modules.Reporting.Application;

public sealed record ReportDescriptor(
    string Key,
    DocumentTemplateKind Template,
    IReadOnlySet<ExportFormat> Formats,
    IReadOnlySet<DocumentLanguage> Languages,
    IReadOnlyList<string> Filters,
    string AuthorizationPolicy,
    bool LocationScoped,
    bool IsDocument = false);

public interface IReportCatalog
{
    IReadOnlyList<ReportDescriptor> All { get; }

    bool TryGet(string key, out ReportDescriptor descriptor);
}

public sealed class ReportCatalog : IReportCatalog
{
    private static readonly IReadOnlySet<DocumentLanguage> AllLanguages =
        new HashSet<DocumentLanguage>(Enum.GetValues<DocumentLanguage>());

    private static readonly ExportFormat[] AnalyticalFormats = [ExportFormat.Pdf, ExportFormat.Excel, ExportFormat.Csv];

    public IReadOnlyList<ReportDescriptor> All { get; } =
    [
        Report("financial-summary", DocumentTemplateKind.Analytical, [ExportFormat.Pdf, ExportFormat.Excel]),
        Report("stock", DocumentTemplateKind.Analytical, AnalyticalFormats, ["locationId"], locationScoped: true),
        Report("operations", DocumentTemplateKind.Analytical, AnalyticalFormats, ["from", "to", "operationType"]),
        Report("payments", DocumentTemplateKind.Analytical, AnalyticalFormats),
        Report("supply", DocumentTemplateKind.Analytical, AnalyticalFormats, ["from", "to", "status"]),
        Report("merchant-balances", DocumentTemplateKind.Analytical, AnalyticalFormats),
        Document("operation-bill", DocumentTemplateKind.Transactional, [ExportFormat.Pdf]),
        Document("payment-receipt", DocumentTemplateKind.Transactional, [ExportFormat.Pdf]),
        Document("cash-receipt", DocumentTemplateKind.Transactional, [ExportFormat.Pdf]),
        Document("supply-landed-cost", DocumentTemplateKind.Analytical, [ExportFormat.Pdf, ExportFormat.Excel]),
        Document("merchant-statement", DocumentTemplateKind.Analytical, [ExportFormat.Pdf, ExportFormat.Excel]),
        Document("stocktake-summary", DocumentTemplateKind.Operational, [ExportFormat.Pdf, ExportFormat.Excel])
    ];

    public bool TryGet(string key, out ReportDescriptor descriptor)
    {
        descriptor = All.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))!;
        return descriptor is not null;
    }

    private static ReportDescriptor Report(
        string key,
        DocumentTemplateKind template,
        IReadOnlyList<ExportFormat> formats,
        IReadOnlyList<string>? filters = null,
        bool locationScoped = false) =>
        new(key, template, new HashSet<ExportFormat>(formats), AllLanguages, filters ?? [], "reports.read", locationScoped, false);

    private static ReportDescriptor Document(
        string key,
        DocumentTemplateKind template,
        IReadOnlyList<ExportFormat> formats) =>
        new(key, template, new HashSet<ExportFormat>(formats), AllLanguages, [], "reports.read", false, true);
}
