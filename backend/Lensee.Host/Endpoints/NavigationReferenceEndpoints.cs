using Lensee.Host.Infrastructure;
using Lensee.SharedKernel.Abstractions;

namespace Lensee.Host.Endpoints;

public static class NavigationReferenceEndpoints
{
    public static RouteGroupBuilder MapNavigationReferenceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/navigation-references")
            .WithTags("Navigation")
            .RequireAuthorization();
        group.MapGet("/{reference}/resolve", ResolveAsync).WithName("ResolveNavigationReference");
        return group;
    }

    private static IResult ResolveAsync(
        string reference,
        NavigationReferenceService navigationReferences,
        ICurrentUser currentUser)
    {
        // Return a generic not-found result for malformed, expired, foreign, and revoked
        // references. This avoids disclosing which part of the reference was invalid.
        return navigationReferences.TryResolve(reference, currentUser.UserId, currentUser.Principal, out var destination)
            ? Results.Ok(destination)
            : Results.NotFound();
    }
}
