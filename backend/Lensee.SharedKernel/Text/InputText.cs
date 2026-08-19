using System.Text.RegularExpressions;

namespace Lensee.SharedKernel.Text;

public static partial class InputText
{
    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();

    public static string NormalizeSingleLine(string? value) =>
        Whitespace().Replace(value?.Trim() ?? string.Empty, " ");

    public static string? NormalizeOptionalSingleLine(string? value)
    {
        var normalized = NormalizeSingleLine(value);
        return normalized.Length == 0 ? null : normalized;
    }

    public static string NormalizeMultiline(string? value) =>
        (value ?? string.Empty).Replace("\r\n", "\n").Trim();

    public static bool HasWhitespace(string? value) =>
        !string.IsNullOrEmpty(value) && Whitespace().IsMatch(value);

    public static string NormalizeUsername(string? value) => NormalizeSingleLine(value);
}
