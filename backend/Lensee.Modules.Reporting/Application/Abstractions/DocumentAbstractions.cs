using Lensee.Modules.Reporting.Application.Models;

namespace Lensee.Modules.Reporting.Application.Abstractions;

public interface IReportDataQuery<in TFilter, TData>
{
    Task<TData> ExecuteAsync(TFilter filter, CancellationToken cancellationToken = default);
}

public interface IDocumentBuilder<in TData>
{
    DocumentModel Build(TData data, DocumentBuildContext context);
}

public interface IDocumentRenderer
{
    ExportFormat Format { get; }

    Task<RenderedDocument> RenderAsync(DocumentModel document, CancellationToken cancellationToken = default);
}

public interface IDocumentExportService
{
    Task<DocumentExportResult> ExportAsync(
        DocumentModel document,
        ExportFormat format,
        CancellationToken cancellationToken = default);
}

public interface IDocumentBrandingProvider
{
    DocumentBranding Branding { get; }
}

public interface IDocumentLocalizer
{
    string Text(string key, DocumentLanguage language);
}

public interface IFileNameGenerator
{
    string Generate(DocumentMetadata metadata, string extension);
}

public sealed record DocumentBranding(
    string CompanyNameArabic,
    string CompanyNameEnglish,
    string? AddressArabic,
    string? AddressEnglish,
    string? Phone,
    string? TaxNumber,
    string? CommercialRegistration,
    string CurrencyCode,
    string ArabicFontFamily,
    string LatinFontFamily,
    byte[]? Logo);
