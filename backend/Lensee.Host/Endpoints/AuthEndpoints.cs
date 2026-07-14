using Lensee.Host.Infrastructure;
using Lensee.Modules.Identity.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static class AuthEndpoints
{
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
            .RequireAuthorization()
            .WithName("Logout");

        group.MapGet("/me", Me)
            .RequireAuthorization()
            .WithName("CurrentSession");

        return group;
    }

    private static async Task<Results<Ok<AuthResponse>, UnauthorizedHttpResult>> LoginAsync(
        LoginRequest request,
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        IClock clock,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var user = await dbContext.Users.SingleOrDefaultAsync(u => u.Username == username, cancellationToken);

        if (user is null || !user.IsActive || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return TypedResults.Unauthorized();
        }

        var refreshToken = tokenService.CreateRefreshToken();
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = tokenService.HashRefreshToken(refreshToken),
            CreatedAt = clock.EgyptNow,
            ExpiresAt = clock.EgyptNow.AddDays(configuration.GetValue("Jwt:RefreshTokenDays", 30)),
            CreatedByIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(CreateAuthResponse(user, tokenService.CreateAccessToken(user), refreshToken));
    }

    private static async Task<Results<Ok<AuthResponse>, UnauthorizedHttpResult>> RefreshAsync(
        RefreshRequest request,
        IdentityDbContext dbContext,
        ITokenService tokenService,
        IClock clock,
        IHttpContextAccessor httpContextAccessor,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);
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
            return TypedResults.Unauthorized();
        }

        if (existingToken.ExpiresAt <= clock.EgyptNow || !existingToken.User.IsActive)
        {
            return TypedResults.Unauthorized();
        }

        var refreshToken = tokenService.CreateRefreshToken();
        var replacement = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = existingToken.UserId,
            TokenHash = tokenService.HashRefreshToken(refreshToken),
            CreatedAt = clock.EgyptNow,
            ExpiresAt = clock.EgyptNow.AddDays(configuration.GetValue("Jwt:RefreshTokenDays", 30)),
            CreatedByIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
        };

        existingToken.RevokedAt = clock.EgyptNow;
        existingToken.RevokedByIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        existingToken.ReplacedBy = replacement.Id;
        dbContext.RefreshTokens.Add(replacement);

        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(CreateAuthResponse(existingToken.User, tokenService.CreateAccessToken(existingToken.User), refreshToken));
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> LogoutAsync(
        LogoutRequest request,
        IdentityDbContext dbContext,
        ITokenService tokenService,
        ICurrentUser currentUser,
        IClock clock,
        IHttpContextAccessor httpContextAccessor,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await RevokeAllRefreshTokensAsync(dbContext, userId, clock.EgyptNow, httpContextAccessor, cancellationToken);
        }
        else
        {
            var tokenHash = tokenService.HashRefreshToken(request.RefreshToken);
            var token = await dbContext.RefreshTokens
                .SingleOrDefaultAsync(value => value.UserId == userId && value.TokenHash == tokenHash, cancellationToken);

            if (token is not null && token.RevokedAt is null)
            {
                token.RevokedAt = clock.EgyptNow;
                token.RevokedByIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        await auditLogWriter.WriteAsync("User", userId, "Logout", cancellationToken: cancellationToken);

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

    private static AuthResponse CreateAuthResponse(User user, string accessToken, string refreshToken) =>
        new(
            accessToken,
            refreshToken,
            new SessionResponse(user.Id, user.Role, user.LocationId));
}

public sealed record LoginRequest(string Username, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string? RefreshToken);

public sealed record AuthResponse(string AccessToken, string RefreshToken, SessionResponse User);

public sealed record SessionResponse(Guid UserId, string Role, Guid? LocationId);
