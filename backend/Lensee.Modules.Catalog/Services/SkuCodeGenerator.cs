using System.Globalization;
using System.Text;
using Lensee.Modules.Catalog.Data;

namespace Lensee.Modules.Catalog.Services;

public sealed class SkuCodeGenerator
{
    public string Generate(Product product, SkuCodeInput input)
    {
        var brandCode = ToBrandCode(product.Brand.Name);
        var categoryCode = ToCategoryCode(product.Category.Name);

        if (IsSolution(product.ProductType))
        {
            return JoinParts(brandCode, categoryCode, ToCode(input.Size, 8));
        }

        return JoinParts(
            brandCode,
            categoryCode,
            FormatPower(input.PowerSign, input.PowerValue),
            ToCode(input.ColorName, 12),
            ToOptionalCode(input.Size, 8));
    }

    public string Preview(string brandName, string categoryName, string productType, SkuCodeInput input)
    {
        var brandCode = ToBrandCode(brandName);
        var categoryCode = ToCategoryCode(categoryName);

        if (IsSolution(productType))
        {
            return JoinParts(brandCode, categoryCode, ToCode(input.Size, 8));
        }

        return JoinParts(
            brandCode,
            categoryCode,
            FormatPower(input.PowerSign, input.PowerValue),
            ToCode(input.ColorName, 12),
            ToOptionalCode(input.Size, 8));
    }

    private static bool IsSolution(string productType) =>
        string.Equals(productType, CatalogValidation.Solution, StringComparison.OrdinalIgnoreCase);

    private static string ToCategoryCode(string? value)
    {
        var wordCode = ToWordInitials(value, 3);
        return wordCode.Length >= 2 ? wordCode : ToCode(value, 3);
    }

    private static string ToBrandCode(string? value)
    {
        var wordCode = ToWordInitials(value, 3);
        return wordCode.Length >= 2 ? wordCode : ToCode(value, 3);
    }

    private static string ToWordInitials(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "NA";
        }

        var parts = value
            .Split(new[] { ' ', '/', '-', '_' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => !string.Equals(part, "and", StringComparison.OrdinalIgnoreCase))
            .Where(part => !string.Equals(part, "of", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (parts.Count <= 1)
        {
            return string.Empty;
        }

        return ToCode(string.Concat(parts.Select(part => part[0])), maxLength);
    }

    private static string ToCode(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "NA";
        }

        var builder = new StringBuilder();
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        var code = builder.ToString();
        if (code.Length == 0)
        {
            return "NA";
        }

        return code.Length <= maxLength ? code : code[..maxLength];
    }

    private static string ToOptionalCode(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : ToCode(value, maxLength);

    private static string FormatPower(string? powerSign, decimal? powerValue)
    {
        if (powerValue is null)
        {
            return "P0";
        }

        var normalized = decimal.Round(powerValue.Value, 2).ToString("0.##", CultureInfo.InvariantCulture).Replace(".", string.Empty);
        return $"{(powerSign == "-" ? "M" : "P")}{normalized}";
    }

    private static string JoinParts(params string[] parts) =>
        string.Join("-", parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim('-')));
}

public sealed record SkuCodeInput(
    string? PowerSign,
    decimal? PowerValue,
    string? ColorName,
    string? Size);
