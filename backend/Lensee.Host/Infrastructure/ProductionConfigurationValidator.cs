namespace Lensee.Host.Infrastructure;

public static class ProductionConfigurationValidator
{
    public static string[] GetValidatedCorsAllowedOrigins(IEnumerable<string?> configuredOrigins)
    {
        var allowedOrigins = configuredOrigins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin!.Trim())
            .ToArray();

        var exactHttpsOrigins = allowedOrigins
            .Where(IsExactHttpsOrigin)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (exactHttpsOrigins.Length == 0)
        {
            var invalidOrigins = allowedOrigins.Length == 0 ? "none" : string.Join(", ", allowedOrigins);
            throw new InvalidOperationException(
                "Production Cors:AllowedOrigins must contain one or more exact HTTPS origins without paths. Invalid origins: " +
                invalidOrigins);
        }

        return exactHttpsOrigins;
    }

    private static bool IsExactHttpsOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        return uri.AbsolutePath is "" or "/";
    }
}
