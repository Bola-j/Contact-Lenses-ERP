using Lensee.Host.Infrastructure;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static class AuthEndpoints
{
    private const string RefreshCookieName = "lensee.refresh";

    public static RouteGroupBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .WithName("Login");

        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .WithName("RefreshToken");

        group.MapPost("/logout", LogoutAsync)
            .AllowAnonymous()
            .WithName("Logout");

        group.MapGet("/me", Me)
            .RequireAuthorization()
            .WithName("CurrentSession");

        return group;
    }

    private static async Task<Results<Ok<AuthResponse>, ValidationProblem, UnauthorizedHttpResult>> LoginAsync(
        LoginRequest request,
        IdentityDbContext dbContext,
        InventoryDbContext inventoryDbContext,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        RefreshTokenSessionService refreshTokenSessionService,
        IHttpContextAccessor httpContextAccessor,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var errors = ValidateLogin(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var username = InputText.NormalizeUsername(request.Username);
        var user = await dbContext.Users.SingleOrDefaultAsync(u => u.Username.ToUpper() == username.ToUpper(), cancellationToken);

        if (user is null || !user.IsActive || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return TypedResults.Unauthorized();
        }

        var issuedToken = await refreshTokenSessionService.IssueAsync(
            user.Id,
            user.Username,
            httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        SetRefreshCookie(httpContextAccessor.HttpContext!, issuedToken.RawToken, issuedToken.ExpiresAt, environment);

        return TypedResults.Ok(await CreateAuthResponseAsync(user, tokenService.CreateAccessToken(user), inventoryDbContext, cancellationToken));
    }

    private static async Task<Results<Ok<AuthResponse>, NoContent, UnauthorizedHttpResult>> RefreshAsync(
        RefreshRequest request,
        InventoryDbContext inventoryDbContext,
        ITokenService tokenService,
        RefreshTokenSessionService refreshTokenSessionService,
        IHttpContextAccessor httpContextAccessor,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var incomingRefreshToken = ReadRefreshToken(request.RefreshToken, httpContextAccessor.HttpContext);
        if (string.IsNullOrWhiteSpace(incomingRefreshToken))
        {
            // A page load without a refresh cookie is the normal anonymous state.
            // Returning 204 keeps browser session restoration quiet while an
            // invalid supplied token still receives 401 below.
            return TypedResults.NoContent();
        }

        var rotation = await refreshTokenSessionService.RotateAsync(
            incomingRefreshToken,
            httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);
        if (rotation.Status != RefreshRotationStatus.Success ||
            rotation.User is null ||
            rotation.RawToken is null ||
            !rotation.ExpiresAt.HasValue)
        {
            ClearRefreshCookie(httpContextAccessor.HttpContext!, environment);
            return TypedResults.Unauthorized();
        }

        SetRefreshCookie(httpContextAccessor.HttpContext!, rotation.RawToken, rotation.ExpiresAt.Value, environment);
        return TypedResults.Ok(await CreateAuthResponseAsync(
            rotation.User,
            tokenService.CreateAccessToken(rotation.User),
            inventoryDbContext,
            cancellationToken));
    }

    private static async Task<NoContent> LogoutAsync(
        LogoutRequest request,
        RefreshTokenSessionService refreshTokenSessionService,
        ICurrentUser currentUser,
        IHttpContextAccessor httpContextAccessor,
        IWebHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        var incomingRefreshToken = ReadRefreshToken(request.RefreshToken, httpContextAccessor.HttpContext);
        if (string.IsNullOrWhiteSpace(incomingRefreshToken))
        {
            if (currentUser.UserId is { } userId)
            {
                await refreshTokenSessionService.RevokeAllAsync(
                    userId,
                    httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
                    true,
                    cancellationToken);
            }
        }
        else
        {
            await refreshTokenSessionService.RevokeOneAsync(
                incomingRefreshToken,
                httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
                true,
                cancellationToken);
        }

        ClearRefreshCookie(httpContextAccessor.HttpContext!, environment);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<SessionResponse>, UnauthorizedHttpResult>> Me(ICurrentUser currentUser, InventoryDbContext inventoryDbContext, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || currentUser.Role is null)
        {
            return TypedResults.Unauthorized();
        }

        var locationType = currentUser.LocationId is { } locationId
            ? await inventoryDbContext.Locations.Where(location => location.Id == locationId && location.IsActive).Select(location => location.LocationType).SingleOrDefaultAsync(cancellationToken)
            : null;
        return TypedResults.Ok(new SessionResponse(userId, currentUser.Role, currentUser.LocationId, locationType));
    }

    private static Dictionary<string, string[]> ValidateLogin(LoginRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        var username = InputText.NormalizeUsername(request.Username);
        if (username.Length == 0)
        {
            errors[nameof(request.Username)] = ["Username is required."];
        }
        else if (InputText.HasWhitespace(username))
        {
            errors[nameof(request.Username)] = ["Username cannot contain spaces."];
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            errors[nameof(request.Password)] = ["Password is required."];
        }

        return errors;
    }

    private static string? ReadRefreshToken(string? bodyToken, HttpContext? httpContext)
    {
        if (!string.IsNullOrWhiteSpace(bodyToken))
        {
            return bodyToken;
        }

        return httpContext?.Request.Cookies.TryGetValue(RefreshCookieName, out var cookieToken) == true
            ? cookieToken
            : null;
    }

    private static void SetRefreshCookie(HttpContext httpContext, string refreshToken, DateTime expiresAt, IWebHostEnvironment environment)
    {
        httpContext.Response.Cookies.Append(RefreshCookieName, refreshToken, CreateRefreshCookieOptions(expiresAt, environment));
    }

    private static void ClearRefreshCookie(HttpContext httpContext, IWebHostEnvironment environment)
    {
        httpContext.Response.Cookies.Delete(RefreshCookieName, CreateRefreshCookieOptions(DateTime.UnixEpoch, environment));
    }

    private static CookieOptions CreateRefreshCookieOptions(DateTime expiresAt, IWebHostEnvironment environment) =>
        new()
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment() && !environment.IsEnvironment("Testing"),
            SameSite = SameSiteMode.Lax,
            Path = "/api/v1/auth",
            Expires = new DateTimeOffset(expiresAt)
        };

    private static async Task<AuthResponse> CreateAuthResponseAsync(User user, string accessToken, InventoryDbContext inventoryDbContext, CancellationToken cancellationToken)
    {
        var locationType = user.LocationId is { } locationId
            ? await inventoryDbContext.Locations.Where(location => location.Id == locationId && location.IsActive).Select(location => location.LocationType).SingleOrDefaultAsync(cancellationToken)
            : null;
        return new AuthResponse(accessToken, new SessionResponse(user.Id, user.Role, user.LocationId, locationType));
    }
}

public sealed record LoginRequest(string Username, string Password);

public sealed record RefreshRequest(string? RefreshToken);

public sealed record LogoutRequest(string? RefreshToken);

public sealed record AuthResponse(string AccessToken, SessionResponse User);

public sealed record SessionResponse(Guid UserId, string Role, Guid? LocationId, string? LocationType = null);
