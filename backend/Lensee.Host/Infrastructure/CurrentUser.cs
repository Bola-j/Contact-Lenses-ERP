using System.Security.Claims;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Security;

namespace Lensee.Host.Infrastructure;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ClaimsPrincipal Principal => _httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());

    public Guid? UserId => TryGetGuid(LenseeClaims.UserId) ?? TryGetGuid(ClaimTypes.NameIdentifier);

    public string? Role => Principal.FindFirstValue(LenseeClaims.Role) ?? Principal.FindFirstValue(ClaimTypes.Role);

    public Guid? LocationId => TryGetGuid(LenseeClaims.LocationId);

    public bool IsAuthenticated => Principal.Identity?.IsAuthenticated == true;

    private Guid? TryGetGuid(string claimType)
    {
        var value = Principal.FindFirstValue(claimType);
        return Guid.TryParse(value, out var id) ? id : null;
    }
}
