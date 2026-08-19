using System.Security.Claims;
using Lensee.Host.Infrastructure;
using Lensee.SharedKernel.Security;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Lensee.Tests;

public sealed class NavigationReferenceServiceTests
{
    [Fact]
    public async Task Reference_IsStableForItsLifetimeAndExpiresSafely()
    {
        var provider = new EphemeralDataProtectionProvider();
        var service = new NavigationReferenceService(provider, TimeSpan.FromSeconds(2));
        var userId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var destination = new NavigationDestination("#/payments", "payment", LenseePermissions.PaymentsRead, "Open payment");
        var reference = service.Issue(userId, destination, recordId);
        var principal = PrincipalWith(LenseePermissions.PaymentsRead);

        Assert.True(service.TryResolve(reference, userId, principal, out var resolved));
        Assert.Equal(recordId, resolved.RecordId);

        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.False(service.TryResolve(reference, userId, principal, out _));
    }

    [Fact]
    public void DestinationDefinitions_RejectUnknownRoutesAndKeepInternalIdsOutOfTheTokenContract()
    {
        var provider = new EphemeralDataProtectionProvider();
        var service = new NavigationReferenceService(provider);
        var userId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var reference = service.Issue(userId, new NavigationDestination("#/inventory", "inventory-batch", LenseePermissions.InventoryRead, "Open inventory batch"), recordId);

        Assert.DoesNotContain(recordId.ToString(), reference, StringComparison.OrdinalIgnoreCase);
        Assert.False(service.TryResolve(reference, userId, PrincipalWith(LenseePermissions.OperationsRead), out _));
        Assert.False(NavigationReferenceService.TryGetDestinationByType("unknown", out _));
    }

    private static ClaimsPrincipal PrincipalWith(string permission) =>
        new(new ClaimsIdentity([new Claim("permission", permission)], "test"));
}
