using Lensee.Modules.Inventory.Data;
using Lensee.SharedKernel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

public sealed class OnlineIntakeRequirement : IAuthorizationRequirement
{
    public OnlineIntakeRequirement(bool manage) => Manage = manage;
    public bool Manage { get; }
}

public sealed class OnlineIntakeAuthorizationHandler : AuthorizationHandler<OnlineIntakeRequirement>
{
    private readonly InventoryDbContext _inventory;

    public OnlineIntakeAuthorizationHandler(InventoryDbContext inventory) => _inventory = inventory;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, OnlineIntakeRequirement requirement)
    {
        var role = context.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
            ?? context.User.FindFirst("role")?.Value;
        role = LenseeRoles.Normalize(role);
        if (role is LenseeRoles.Admin or LenseeRoles.ERPAdmin || (!requirement.Manage && role == LenseeRoles.CLevel))
        {
            context.Succeed(requirement);
            return;
        }

        if (role != LenseeRoles.WarehouseClerk)
        {
            return;
        }

        var locationValue = context.User.FindFirst("locationId")?.Value;
        if (!Guid.TryParse(locationValue, out var locationId)) return;
        var isOnline = await _inventory.Locations.AnyAsync(location =>
            location.Id == locationId && location.IsActive && location.LocationType == "Online");
        if (isOnline && (!requirement.Manage || role == LenseeRoles.WarehouseClerk)) context.Succeed(requirement);
    }
}
