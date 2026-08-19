using System.Text.Json;
using Lensee.Modules.Identity.Data;

namespace Lensee.Host.Infrastructure;

/// <summary>Converts stored audit payloads into language that is useful to an operator, not HTTP diagnostics.</summary>
public static class AuditEventPresentation
{
    private static readonly HashSet<string> TransportFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "path", "section", "routeValues", "method", "request", "response"
    };

    public static AuditPresentation From(AuditLog audit, string? businessReference = null)
    {
        var payload = ParsePayload(audit.ChangedFields);
        var recordName = GetString(payload, "recordName")
            ?? FindRecordName(payload)
            ?? $"{AuditEventPayload.DisplayEntity(audit.EntityType)} {AuditEventPayload.FriendlyReference(audit.EntityType, audit.EntityId)}";
        recordName = AuditEventPayload.SanitizeReferenceText(recordName, audit.EntityType, audit.EntityId, businessReference)!;
        var changes = GetChanges(payload)
            .Select(change => change with
            {
                Before = AuditEventPayload.SanitizeReferenceText(change.Before, audit.EntityType, audit.EntityId, businessReference),
                After = AuditEventPayload.SanitizeReferenceText(change.After, audit.EntityType, audit.EntityId, businessReference)
            })
            .ToList();
        var summary = GetString(payload, "summary")
            ?? $"{ActionPhrase(audit.Action)} {recordName}.";
        summary = AuditEventPayload.SanitizeReferenceText(summary, audit.EntityType, audit.EntityId, businessReference)!;

        return new AuditPresentation(summary, recordName, changes);
    }

    private static JsonElement? ParsePayload(string? changedFields)
    {
        if (string.IsNullOrWhiteSpace(changedFields)) return null;
        try
        {
            using var document = JsonDocument.Parse(changedFields);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<AuditChange> GetChanges(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } root) return [];
        if (TryGetProperty(root, "changes", out var storedChanges) && storedChanges.ValueKind == JsonValueKind.Array)
        {
            return storedChanges.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.Object)
                .Select(value => new AuditChange(
                    GetString(value, "field") ?? "Updated value",
                    GetString(value, "before"),
                    GetString(value, "after")))
                .Where(change => !string.IsNullOrWhiteSpace(change.After) || !string.IsNullOrWhiteSpace(change.Before))
                .ToList();
        }

        return root.EnumerateObject()
            .Where(property => !TransportFields.Contains(property.Name) && property.Name is not "summary" and not "recordName")
            .Select(property => new AuditChange(AuditEventPayload.SplitWords(property.Name), null, FormatValue(property.Value)))
            .Where(change => !string.IsNullOrWhiteSpace(change.After))
            .ToList();
    }

    private static string? FindRecordName(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } root) return null;
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("username", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("operationNumber", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("invoiceNumber", StringComparison.OrdinalIgnoreCase))
            {
                return FormatValue(property.Value);
            }
        }

        return null;
    }

    private static string ActionPhrase(string action) => action.Trim().ToUpperInvariant() switch
    {
        "POST" or "CREATE" or "CREATED" => "Created",
        "PUT" or "PATCH" or "UPDATE" or "UPDATED" => "Updated",
        "DELETE" or "DELETED" => "Deleted",
        "CONFIRM" or "CONFIRMED" => "Confirmed",
        "CANCEL" or "CANCELLED" => "Cancelled",
        "APPROVE" or "APPROVED" => "Approved",
        "REJECT" or "REJECTED" => "Rejected",
        "DEACTIVATE" or "DEACTIVATED" => "Deactivated",
        "REACTIVATE" or "REACTIVATED" => "Reactivated",
        _ => "Changed"
    };

    private static string? GetString(JsonElement? payload, string name) =>
        payload is { ValueKind: JsonValueKind.Object } root && TryGetProperty(root, name, out var value)
            ? FormatValue(value)
            : null;

    private static bool TryGetProperty(JsonElement value, string name, out JsonElement result)
    {
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                result = property.Value;
                return true;
            }
        }

        result = default;
        return false;
    }

    private static string? FormatValue(JsonElement value) => value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
        ? null
        : AuditEventPayload.FormatValue(value);
}

public sealed record AuditPresentation(string Summary, string RecordName, IReadOnlyList<AuditChange> Changes);
public sealed record AuditChange(string Field, string? Before, string? After);
