using System.Security.Claims;

namespace Lensee.SharedKernel.Abstractions;

public interface ICurrentUser
{
    Guid? UserId { get; }

    string? Role { get; }

    Guid? LocationId { get; }

    bool IsAuthenticated { get; }

    ClaimsPrincipal Principal { get; }
}
