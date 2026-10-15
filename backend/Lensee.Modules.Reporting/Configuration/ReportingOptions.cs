using System.ComponentModel.DataAnnotations;

namespace Lensee.Modules.Reporting.Configuration;

public sealed class ReportingOptions
{
    public const string SectionName = "Reporting";

    [Range(1, 10_000)]
    public int MaxPageSize { get; init; } = 1_000;

    [Range(1, 1_000_000)]
    // Browsing reports is paginated independently from export generation. Keep
    // a generous safety ceiling for accidental/unbounded exports, but never
    // turn an over-limit request into a partial document.
    public int MaxExportRows { get; init; } = 100_000;

    public string QuestPdfLicense { get; init; } = "Community";

    public DocumentBrandingOptions Branding { get; init; } = new();
}

public sealed class DocumentBrandingOptions
{
    public string? LogoBase64 { get; init; }
    public string CompanyNameArabic { get; init; } = "لينسي";
    public string CompanyNameEnglish { get; init; } = "Lensee";
    public string? AddressArabic { get; init; }
    public string? AddressEnglish { get; init; }
    public string? Phone { get; init; }
    public string? TaxNumber { get; init; }
    public string? CommercialRegistration { get; init; }
    public string CurrencyCode { get; init; } = "EGP";
    public string ArabicFontFamily { get; init; } = "Noto Sans Arabic";
    public string LatinFontFamily { get; init; } = "Noto Sans";
}
