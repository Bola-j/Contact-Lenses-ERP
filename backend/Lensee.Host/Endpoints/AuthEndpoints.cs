using Lensee.Host.Infrastructure;
using Lensee.Modules.Identity.Data;
using Lensee.SharedKernel.Abstractions;
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
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IClock clock,
        IHttpContextAccessor httpContextAccessor,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var errors = ValidateLogin(request);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var username = request.Username.Trim();
        var user = await dbContext.Users.SingleOrDefaultAsync(u => u.Username == username, cancellationToken);

        if (user is null || !user.IsActive || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return TypedResults.Unauthorized();
        }

        var refreshToken = tokenService.CreateRefreshToken();
        var refreshTokenDays = configuration.GetValue("Jwt:RefreshTokenDays", 30);
        var refreshTokenExpiresAt = clock.EgyptNow.AddDays(refreshTokenDays);
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenService.HashRefreshToken(refreshToken),
            CreatedAt = clock.EgyptNow,
            ExpiresAt = refreshTokenExpiresAt,
            CreatedByIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        SetRefreshCookie(httpContextAccessor.HttpContext!, refreshToken, refreshTokenExpiresAt, environment);

        return TypedResults.Ok(CreateAuthResponse(user, tokenService.CreateAccessToken(user)));
    }

    private static async Task<Results<Ok<AuthResponse>, ValidationProblem, UnauthorizedHttpResult>> RefreshAsync(
        RefreshRequest request,
        IdentityDbContext dbContext,
        ITokenService tokenService,
        IClock clock,
        IHttpContextAccessor httpContextAccessor,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var incomingRefreshToken = ReadRefreshToken(request.RefreshToken, httpContextAccessor.HttpContext);
        if (string.IsNullOrWhiteSpace(incomingRefreshToken))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.RefreshToken)] = ["Refresh token is required."]
            });
        }

        var tokenHash = tokenService.HashRefreshToken(incomingRefreshToken);
        var existingToken = await dbContext.RefreshTokens
            .Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (existingToken is null)
        {
            return TypedResults.Unauthorized();
        }

        if (existingToken.RevokedAt.HasValue)
        {
            await RevokeAllRefreshTokensAsync(dbContext, existingToken.UserId, clock.EgyptNow, httpContextAccessor, cancellationToken);
            ClearRefreshCookie(httpContextAccessor.HttpContext!, environment);
            return TypedResults.Unauthorized();
        }

        if (existingToken.ExpiresAt <= clock.EgyptNow || !existingToken.User.IsActive)
        {
            ClearRefreshCookie(httpContextAccessor.HttpContext!, environment);
            return TypedResults.Unauthorized();
        }

        var refreshToken = tokenService.CreateRefreshToken();
        var refreshTokenDays = configuration.GetValue("Jwt:RefreshTokenDays", 30);
        var refreshTokenExpiresAt = clock.EgyptNow.AddDays(refreshTokenDays);
        var replacement = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = existingToken.UserId,
            TokenHash = tokenService.HashRefreshToken(refreshToken),
            CreatedAt = clock.EgyptNow,
            ExpiresAt = refreshTokenExpiresAt,
            CreatedByIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
        };

        existingToken.RevokedAt = clock.EgyptNow;
        existingToken.RevokedByIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        existingToken.ReplacedBy = replacement.Id;
        dbContext.RefreshTokens.Add(replacement);

        await dbContext.SaveChangesAsync(cancellationToken);

        SetRefreshCookie(httpContextAccessor.HttpContext!, refreshToken, refreshTokenExpiresAt, environment);

        return TypedResults.Ok(CreateAuthResponse(existingToken.User, tokenService.CreateAccessToken(existingToken.User)));
    }

    private static async Task<NoContent> LogoutAsync(
        LogoutRequest request,
        IdentityDbContext dbContext,
        ITokenService tokenService,
        ICurrentUser currentUser,
        IClock clock,
        IHttpContextAccessor httpContextAccessor,
        IWebHostEnvironment environment,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var incomingRefreshToken = ReadRefreshToken(request.RefreshToken, httpContextAccessor.HttpContext);
        if (string.IsNullOrWhiteSpace(incomingRefreshToken))
        {
            if (currentUser.UserId is { } userId)
            {
                await RevokeAllRefreshTokensAsync(dbContext, userId, clock.EgyptNow, httpContextAccessor, cancellationToken);
                await auditLogWriter.WriteAsync("User", userId, "Logout", cancellationToken: cancellationToken);
            }
        }
        else
        {
            var tokenHash = tokenService.HashRefreshToken(incomingRefreshToken);
            var token = await dbContext.RefreshTokens
                .SingleOrDefaultAsync(value => value.TokenHash == tokenHash, cancellationToken);

            if (token is not null && token.RevokedAt is null)
            {
                token.RevokedAt = clock.EgyptNow;
                token.RevokedByIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
                await dbContext.SaveChangesAsync(cancellationToken);
                await auditLogWriter.WriteAsync("User", token.UserId, "Logout", cancellationToken: cancellationToken);
            }
        }

        ClearRefreshCookie(httpContextAccessor.HttpContext!, environment);

        return TypedResults.NoContent();
    }

    private static Results<Ok<SessionResponse>, UnauthorizedHttpResult> Me(ICurrentUser currentUser)
    {
        if (currentUser.UserId is not { } userId || currentUser.Role is null)
        {
            return TypedResults.Unauthorized();
        }

        return TypedResults.Ok(new SessionResponse(userId, currentUser.Role, currentUser.LocationId));
    }

    private static async Task RevokeAllRefreshTokensAsync(
        IdentityDbContext dbContext,
        Guid userId,
        DateTime revokedAt,
        IHttpContextAccessor httpContextAccessor,
        CancellationToken cancellationToken)
    {
        var tokens = await dbContext.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAt = revokedAt;
            token.RevokedByIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static Dictionary<string, string[]> ValidateLogin(LoginRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            errors[nameof(request.Username)] = ["Username is required."];
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

    private static AuthResponse CreateAuthResponse(User user, string accessToken) =>
        new(
            accessToken,
            new SessionResponse(user.Id, user.Role, user.LocationId));
}

public sealed record LoginRequest(string Username, string Password);

public sealed record RefreshRequest(string? RefreshToken);

public sealed record LogoutRequest(string? RefreshToken);

public sealed record AuthResponse(string AccessToken, SessionResponse User);

public sealed record SessionResponse(Guid UserId, string Role, Guid? LocationId);
