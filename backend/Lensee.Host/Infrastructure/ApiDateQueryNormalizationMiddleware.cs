using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Lensee.Host.Infrastructure;

/// <summary>
/// Lets public query parameters use the same human-readable date formats as
/// JSON while preserving the established Minimal API parameter types.
/// </summary>
public sealed class ApiDateQueryNormalizationMiddleware(RequestDelegate next)
{
    private static readonly string[] DateFormats = ["dd/MM/yyyy", "yyyy-MM-dd"];
    private static readonly string[] DateTimeFormats =
    [
        "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy",
        "O", "o", "s", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", "yyyy-MM-dd'T'HH:mm:ssK",
        "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF", "yyyy-MM-dd'T'HH:mm:ss"
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) || !context.Request.Path.StartsWithSegments("/api/v1"))
        {
            await next(context);
            return;
        }

        var changed = false;
        var normalized = new List<KeyValuePair<string, StringValues>>();
        var timestampRange = UsesTimestampRanges(context.Request.Path);

        foreach (var (key, values) in context.Request.Query)
        {
            if (!IsDateParameter(key))
            {
                normalized.Add(new(key, values));
                continue;
            }

            var replacement = new List<string>(values.Count);
            foreach (var text in values)
            {
                var value = Normalize(text, key, timestampRange, out var wasChanged);
                replacement.Add(value);
                changed |= wasChanged;
            }
            normalized.Add(new(key, new StringValues(replacement.ToArray())));
        }

        if (changed) context.Request.QueryString = QueryString.Create(normalized);
        await next(context);
    }

    private static bool IsDateParameter(string key) =>
        key.Equals("date", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("from", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("to", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("createdFrom", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("createdTo", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("start", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("end", StringComparison.OrdinalIgnoreCase);

    private static bool UsesTimestampRanges(PathString path) =>
        path.StartsWithSegments("/api/v1/audit") ||
        path.StartsWithSegments("/api/v1/operations") ||
        path.StartsWithSegments("/api/v1/payments") ||
        (path.StartsWithSegments("/api/v1/reports") && !path.StartsWithSegments("/api/v1/reports/executive"));

    private static string Normalize(string? text, string key, bool timestampRange, out bool changed)
    {
        changed = false;
        if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;

        if (DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            var result = timestampRange && (key.Equals("to", StringComparison.OrdinalIgnoreCase) || key.Equals("createdTo", StringComparison.OrdinalIgnoreCase))
                ? date.ToDateTime(TimeOnly.MaxValue).ToString("O", CultureInfo.InvariantCulture)
                : date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            changed = !string.Equals(text, result, StringComparison.Ordinal);
            return result;
        }

        if (DateTime.TryParseExact(text, DateTimeFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out var dateTime))
        {
            var result = dateTime.ToString("O", CultureInfo.InvariantCulture);
            changed = !string.Equals(text, result, StringComparison.Ordinal);
            return result;
        }

        return text;
    }
}
