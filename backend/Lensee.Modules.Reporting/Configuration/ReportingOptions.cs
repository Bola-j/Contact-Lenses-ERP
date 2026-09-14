using System.ComponentModel.DataAnnotations;

namespace Lensee.Modules.Reporting.Configuration;

public sealed class ReportingOptions
{
    public const string SectionName = "Reporting";

    [Range(1, 1_000_000)]
    public int MaxExportRows { get; init; } = 10_000;

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
