namespace Lensee.Host.Infrastructure;

public static class ProductionConfigurationValidator
{
    public static void ValidateCorsAllowedOrigins(IEnumerable<string?> configuredOrigins)
    {
        var allowedOrigins = configuredOrigins
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Select(origin => origin!.Trim())
            .ToArray();

        if (allowedOrigins.Length == 0)
        {
            throw new InvalidOperationException("Production Cors:AllowedOrigins must contain one or more exact HTTPS origins without paths.");
        }

        var invalidOrigins = allowedOrigins
            .Where(origin => !IsExactHttpsOrigin(origin))
            .ToArray();

        if (invalidOrigins.Length > 0)
        {
            throw new InvalidOperationException(
                "Production Cors:AllowedOrigins must contain one or more exact HTTPS origins without paths. Invalid origins: " +
                string.Join(", ", invalidOrigins));
        }
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
