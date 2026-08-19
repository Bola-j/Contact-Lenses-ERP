using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lensee.Host.Infrastructure;

public static class AuditEventPayload
{
    private static readonly string[] SensitiveNames = ["password", "token", "secret", "hash", "payload"];

    public static object FromRequest(string action, string entityType, Guid entityId, string? body)
    {
        var values = ParseBusinessValues(body);
        var recordName = FindRecordName(values) ?? $"{DisplayEntity(entityType)} {FriendlyReference(entityType, entityId)}";
        return new
        {
            Summary = $"{action} {recordName}.",
            RecordName = recordName,
            Changes = values.Select(value => new { value.Field, Before = (string?)null, value.After }).ToList()
        };
    }

    public static string DisplayEntity(string value) => value switch
    {
        "User" => "employee account",
        "Merchant" => "merchant",
        "Representative" => "representative",
        "Operation" => "operation",
        "Payment" => "payment",
        "SupplyShipment" => "shipment",
        "Stocktake" => "stocktake",
        "InventoryReceipt" => "inventory receipt",
        "Notification" => "notification",
        "ShopifyWebhookEvent" => "Shopify event",
        _ => SplitWords(value).ToLowerInvariant()
    };

    /// <summary>Creates the stable, staff-facing reference used when an audit event has no business number.</summary>
    public static string FriendlyReference(string entityType, Guid entityId)
    {
        const string alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
        var bytes = Convert.FromHexString(entityId.ToString("N")[12..]);
        var value = new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        var encoded = new char[16];

        for (var index = encoded.Length - 1; index >= 0; index--)
        {
            encoded[index] = alphabet[(int)(value & 31)];
            value >>= 5;
        }

        var compact = new string(encoded);
        return $"{FriendlyPrefix(entityType)}-{compact[..4]}-{compact[4..8]}-{compact[8..12]}-{compact[12..]}";
    }

    /// <summary>Replaces the current audit record's legacy UUID values in persisted user-facing text.</summary>
    public static string? SanitizeReferenceText(string? text, string entityType, Guid entityId, string? preferredReference = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var fallbackReference = FriendlyReference(entityType, entityId);
        var reference = string.IsNullOrWhiteSpace(preferredReference) ? fallbackReference : preferredReference;
        var fullId = entityId.ToString("D");
        var legacyShortId = entityId.ToString("N")[..8];
        var normalized = Regex.Replace(text, Regex.Escape(fullId), reference, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(
            normalized,
            Regex.Escape(fallbackReference),
            reference,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Regex.Replace(
            normalized,
            $@"(?<![0-9A-F]){legacyShortId}(?![0-9A-F])",
            reference,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string FriendlyPrefix(string entityType) => entityType.Trim().ToUpperInvariant() switch
    {
        "OPERATION" or "OPERATIONLINE" => "OP",
        "PAYMENT" or "PAYMENTLOG" or "PAYMENTSUBLOG" or "CASHTRANSACTION" or "FINANCIALADJUSTMENT" => "PAY",
        "SUPPLYSHIPMENT" => "SUP",
        "STOCKTAKE" or "STOCKBALANCE" or "INVENTORYRECEIPT" => "STK",
        "INVENTORYBATCH" or "BATCH" => "BAT",
        "SKU" or "PRODUCTSKU" => "SKU",
        "LOCATION" or "WAREHOUSE" => "LOC",
        "USER" => "USR",
        "MERCHANT" => "MER",
        "REPRESENTATIVE" => "REP",
        "NOTIFICATION" => "NTF",
        "AUDITLOG" or "AUDIT" => "AUD",
        "CATEGORY" => "CAT",
        "PRODUCT" => "PRD",
        "BRAND" => "BRD",
        _ => "REF"
    };

    private static IReadOnlyList<AuditFieldValue> ParseBusinessValues(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return [];
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return [];
            return document.RootElement.EnumerateObject()
                .Where(property => !SensitiveNames.Any(name => property.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
                .Select(property => new AuditFieldValue(SplitWords(property.Name), FormatValue(property.Value)))
                .Where(value => !string.IsNullOrWhiteSpace(value.After))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? FindRecordName(IEnumerable<AuditFieldValue> values) => values
        .FirstOrDefault(value => value.Field is "Name" or "Username" or "Business Name" or "Supplier Name" or "Operation Number")?.After;

    public static string FormatValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
        JsonValueKind.Null => "Cleared",
        JsonValueKind.Array => string.Join("; ", value.EnumerateArray().Select(FormatValue).Where(item => !string.IsNullOrWhiteSpace(item))),
        JsonValueKind.Object => string.Join(", ", value.EnumerateObject()
            .Where(property => !SensitiveNames.Any(name => property.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
            .Select(property => $"{SplitWords(property.Name)}: {FormatValue(property.Value)}")
            .Where(item => !string.IsNullOrWhiteSpace(item))),
        _ => string.Empty
    };

    public static string SplitWords(string value)
    {
        var separated = System.Text.RegularExpressions.Regex.Replace(value, "([a-z])([A-Z])", "$1 $2");
        return string.IsNullOrWhiteSpace(separated)
            ? separated
            : char.ToUpperInvariant(separated[0]) + separated[1..];
    }
}

public sealed record AuditFieldValue(string Field, string After);
