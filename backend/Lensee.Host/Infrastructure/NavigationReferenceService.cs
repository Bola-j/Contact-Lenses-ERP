using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Lensee.SharedKernel.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Lensee.Host.Infrastructure;

/// <summary>
/// Issues browser-safe links without exposing internal record identifiers. The token is
/// encrypted, expires quickly, and can only be resolved by the user it was issued to.
/// </summary>
public sealed class NavigationReferenceService
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(30);
    private readonly ITimeLimitedDataProtector _protector;
    private readonly TimeSpan _lifetime;

    public NavigationReferenceService(IDataProtectionProvider dataProtectionProvider, TimeSpan? lifetime = null)
    {
        _protector = dataProtectionProvider
            .CreateProtector("Lensee.NavigationReference.v1")
            .ToTimeLimitedDataProtector();
        _lifetime = lifetime ?? DefaultLifetime;
    }

    public string Issue(Guid userId, NavigationDestination destination, Guid recordId) =>
        _protector.Protect(
            JsonSerializer.Serialize(new NavigationReferencePayload(
                userId,
                destination.Route,
                destination.Focus,
                destination.Permission,
                recordId)),
            _lifetime);

    public bool TryResolve(string? reference, Guid? userId, ClaimsPrincipal principal, out NavigationDestinationResolution resolution)
    {
        resolution = default!;
        if (string.IsNullOrWhiteSpace(reference) || userId is null || !TryUnprotect(reference, out var payload))
        {
            return false;
        }

        if (payload.UserId != userId.Value || !TryGetDestination(payload.Route, payload.Focus, payload.Permission, out var destination))
        {
            return false;
        }

        // Permissions are checked again when resolving, so an access change takes effect
        // even if the original link has not expired yet.
        if (!principal.HasClaim("permission", destination.Permission))
        {
            return false;
        }

        resolution = new NavigationDestinationResolution(destination.Route, destination.Focus, payload.RecordId);
        return true;
    }

    public bool TryGetDestinationForEntity(string? entityType, out NavigationDestination destination) =>
        TryGetDestinationByType(entityType, out destination);

    public static bool TryGetDestinationByType(string? type, out NavigationDestination destination)
    {
        destination = type?.Trim().ToLowerInvariant() switch
        {
            "stockbalance" => new("#/inventory", "stock-balance", LenseePermissions.InventoryRead, "Open inventory balance"),
            "inventorybatch" => new("#/inventory", "inventory-batch", LenseePermissions.InventoryRead, "Open inventory batch"),
            "operation" => new("#/operations", "operation", LenseePermissions.OperationsRead, "Open operation"),
            "paymentlog" or "paymentsublog" or "cashrecord" or "financialadjustment" => new("#/payments", "payment", LenseePermissions.PaymentsRead, "Open payment"),
            "stocktake" => new("#/stocktakes", "stocktake", LenseePermissions.OperationsRead, "Open stocktake"),
            "supplyshipment" => new("#/supply", "supply-shipment", LenseePermissions.SupplyRead, "Open shipment"),
            "merchant" => new("#/crm", "merchant", LenseePermissions.OperationsRead, "Open merchant"),
            "merchantexpiryrecall" => new("#/notifications", "merchant-expiry-recall", LenseePermissions.OperationsRead, "Open merchant recall"),
            "exportlog" or "export" or "reports" => new("#/reports", "export", LenseePermissions.ReportsRead, "Open export"),
            _ => default!
        };
        return destination is not null;
    }

    private bool TryUnprotect(string reference, out NavigationReferencePayload payload)
    {
        payload = default!;
        try
        {
            var json = _protector.Unprotect(reference, out _);
            payload = JsonSerializer.Deserialize<NavigationReferencePayload>(json)!;
            return payload is not null && payload.RecordId != Guid.Empty && payload.UserId != Guid.Empty;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            return false;
        }
    }

    private static bool TryGetDestination(string route, string focus, string permission, out NavigationDestination destination)
    {
        destination = default!;
        if (!TryGetDestinationByType(FocusType(focus), out var known) ||
            !string.Equals(known.Route, route, StringComparison.Ordinal) ||
            !string.Equals(known.Focus, focus, StringComparison.Ordinal) ||
            !string.Equals(known.Permission, permission, StringComparison.Ordinal))
        {
            return false;
        }

        destination = known;
        return true;
    }

    private static string FocusType(string focus) => focus switch
    {
        "stock-balance" => "stockbalance",
        "inventory-batch" => "inventorybatch",
        "operation" => "operation",
        "payment" => "paymentlog",
        "stocktake" => "stocktake",
        "supply-shipment" => "supplyshipment",
        "merchant" => "merchant",
        "merchant-expiry-recall" => "merchantexpiryrecall",
        "export" => "exportlog",
        _ => string.Empty
    };

    private sealed record NavigationReferencePayload(Guid UserId, string Route, string Focus, string Permission, Guid RecordId);
}

public sealed record NavigationDestination(string Route, string Focus, string Permission, string ActionLabel);
public sealed record NavigationDestinationResolution(string Route, string Focus, Guid RecordId);
