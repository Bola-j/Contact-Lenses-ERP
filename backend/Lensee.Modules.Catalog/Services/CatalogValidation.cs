using System.Text.Json;

namespace Lensee.Modules.Catalog.Services;

public static class CatalogValidation
{
    public const string Lens = "Lens";
    public const string Solution = "Solution";

    private static readonly string[] ProductTypes = [Lens, Solution];
    private static readonly string[] SellModes = ["SealedPackOnly", "SinglePiece", "Both"];
    private static readonly string[] ExpiryTypes = ["None", "Batch", "Product"];
    public static Dictionary<string, string[]> ValidateProduct(ProductValidationInput input)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        AddRequired(errors, nameof(input.Name), input.Name, "Product name is required.");

        if (!ProductTypes.Contains(input.ProductType, StringComparer.OrdinalIgnoreCase))
        {
            errors[nameof(input.ProductType)] = ["Product type must be one of: Lens, Solution."];
        }

        if (input.PiecesPerPack is <= 0)
        {
            errors[nameof(input.PiecesPerPack)] = ["Pieces per pack must be greater than zero when provided."];
        }

        if (!string.IsNullOrWhiteSpace(input.OpenedExpiryDuration) && !IsValidDuration(input.OpenedExpiryDuration))
        {
            errors[nameof(input.OpenedExpiryDuration)] = ["Opened expiry duration must look like: 6 months. This legacy value is not used by MVP inventory or operation logic."];
        }

        if (!string.IsNullOrWhiteSpace(input.SellMode) &&
            !SellModes.Contains(input.SellMode, StringComparer.OrdinalIgnoreCase))
        {
            errors[nameof(input.SellMode)] = ["Sell mode must be one of: SealedPackOnly, SinglePiece, Both."];
        }

        if (!string.IsNullOrWhiteSpace(input.ExpiryType) &&
            !ExpiryTypes.Contains(input.ExpiryType, StringComparer.OrdinalIgnoreCase))
        {
            errors[nameof(input.ExpiryType)] = ["Expiry type must be one of: None, Batch, Product."];
        }

        if (RequiresClinicalParams(input.ProductType) && string.IsNullOrWhiteSpace(input.ClinicalParams))
        {
            errors[nameof(input.ClinicalParams)] = ["Clinical parameters are required for lens products."];
        }

        AddJsonError(errors, nameof(input.ClinicalParams), input.ClinicalParams);
        AddJsonError(errors, nameof(input.ExtendedAttributes), input.ExtendedAttributes);

        return errors;
    }

    public static Dictionary<string, string[]> ValidateSku(SkuValidationInput input, string productType)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (RequiresLensSkuAttributes(productType) && string.IsNullOrWhiteSpace(input.ColorName))
        {
            errors[nameof(input.ColorName)] = ["Color name is required for lens SKUs."];
        }

        if (!RequiresLensSkuAttributes(productType) && string.IsNullOrWhiteSpace(input.Size))
        {
            errors[nameof(input.Size)] = ["Size is required for solution SKUs."];
        }

        if (input.PowerSign is not null && input.PowerSign is not "+" and not "-")
        {
            errors[nameof(input.PowerSign)] = ["Power sign must be + or - when provided."];
        }

        if (input.PowerValue is < 0)
        {
            errors[nameof(input.PowerValue)] = ["Power value cannot be negative."];
        }

        return errors;
    }

    public static string NormalizeProductType(string value) =>
        Normalize(value, ProductTypes);

    public static string? NormalizeSellMode(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Normalize(value, SellModes);

    public static string? NormalizeExpiryType(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Normalize(value, ExpiryTypes);

    public static string? NormalizeOpenedDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (int.TryParse(trimmed, out var amount) && amount > 0)
        {
            return $"{amount} {(amount == 1 ? "month" : "months")}";
        }

        return trimmed;
    }

    private static bool RequiresClinicalParams(string productType) =>
        string.Equals(productType, Lens, StringComparison.OrdinalIgnoreCase);

    private static bool RequiresLensSkuAttributes(string productType) =>
        RequiresClinicalParams(productType);

    private static string Normalize(string value, IEnumerable<string> allowed) =>
        allowed.FirstOrDefault(candidate => string.Equals(candidate, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? value.Trim();

    private static void AddRequired(
        Dictionary<string, string[]> errors,
        string key,
        string? value,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[key] = [message];
        }
    }

    private static void AddJsonError(Dictionary<string, string[]> errors, string key, string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            errors[key] = ["Value must be valid JSON."];
        }
    }

    private static bool IsValidDuration(string duration) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            duration.Trim(),
            @"^[1-9][0-9]*\s+(day|days|month|months|year|years)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

}

public sealed record ProductValidationInput(
    string Name,
    string ProductType,
    string? ExpiryType,
    string? SealedExpiryDuration,
    string? SealedExpiryRate,
    string? OpenedExpiryDuration,
    int? PiecesPerPack,
    string? SellMode,
    string? ClinicalParams,
    string? ExtendedAttributes);

public sealed record SkuValidationInput(
    string? PowerSign,
    decimal? PowerValue,
    string? ColorName,
    string? Size);
