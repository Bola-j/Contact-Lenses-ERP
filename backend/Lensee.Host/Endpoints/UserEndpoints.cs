using Lensee.Host.Infrastructure;
using Lensee.Modules.Identity.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static class UserEndpoints
{
    public static RouteGroupBuilder MapUserEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/users")
            .WithTags("Users")
            .RequireAuthorization();

        group.MapGet("/", ListUsersAsync)
            .RequireAuthorization("users.read")
            .WithName("ListUsers");

        group.MapPost("/", CreateUserAsync)
            .RequireAuthorization("users.write")
            .WithName("CreateUser");

        group.MapPatch("/{id:guid}/password", ChangePasswordAsync)
            .RequireAuthorization("users.write")
            .WithName("ChangeUserPassword");

        group.MapPatch("/{id:guid}/activate", ActivateUserAsync)
            .RequireAuthorization("users.write")
            .WithName("ActivateUser");

        group.MapPatch("/{id:guid}/deactivate", DeactivateUserAsync)
            .RequireAuthorization("users.write")
            .WithName("DeactivateUser");

        return group;
    }

    private static async Task<Ok<IReadOnlyList<UserResponse>>> ListUsersAsync(
        IdentityDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var users = await dbContext.Users
            .OrderBy(user => user.Username)
            .Select(user => new UserResponse(
                user.Id,
                user.Username,
                user.FullName,
                user.Role,
                user.LocationId,
                user.IsActive,
                user.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<UserResponse>>(users);
    }

    private static async Task<Results<Created<UserResponse>, ValidationProblem, Conflict>> CreateUserAsync(
        CreateUserRequest request,
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateCreateUser(request);
        if (validationErrors.Count > 0)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        var username = request.Username.Trim();
        if (await dbContext.Users.AnyAsync(user => user.Username == username, cancellationToken))
        {
            return TypedResults.Conflict();
        }

        var role = NormalizeRole(request.Role);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = passwordHasher.Hash(request.Password),
            FullName = request.FullName.Trim(),
            Role = role,
            LocationId = request.LocationId,
            IsActive = true,
            CreatedAt = clock.EgyptNow
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditLogWriter.WriteAsync(
            "User",
            user.Id,
            "Create",
            new { user.Username, user.FullName, user.Role, user.LocationId, user.IsActive },
            cancellationToken: cancellationToken);

        var response = ToResponse(user);
        return TypedResults.Created($"/api/v1/users/{user.Id}", response);
    }

    private static async Task<Results<NoContent, ValidationProblem, NotFound>> ChangePasswordAsync(
        Guid id,
        ChangePasswordRequest request,
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        IClock clock,
        IHttpContextAccessor httpContextAccessor,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidatePassword(request.NewPassword);
        if (validationErrors.Count > 0)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        var user = await dbContext.Users.FindAsync([id], cancellationToken);
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);

        var revokedAt = clock.EgyptNow;
        var revokedByIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
        var refreshTokens = await dbContext.RefreshTokens
            .Where(token => token.UserId == user.Id && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var refreshToken in refreshTokens)
        {
            refreshToken.RevokedAt = revokedAt;
            refreshToken.RevokedByIp = revokedByIp;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditLogWriter.WriteAsync(
            "User",
            user.Id,
            "ChangePassword",
            new { user.Username, RevokedRefreshTokens = refreshTokens.Count },
            cancellationToken: cancellationToken);

        return TypedResults.NoContent();
    }

    private static Task<Results<Ok<UserResponse>, NotFound>> ActivateUserAsync(
        Guid id,
        IdentityDbContext dbContext,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken) =>
        SetUserActiveStateAsync(id, true, dbContext, auditLogWriter, cancellationToken);

    private static Task<Results<Ok<UserResponse>, NotFound>> DeactivateUserAsync(
        Guid id,
        IdentityDbContext dbContext,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken) =>
        SetUserActiveStateAsync(id, false, dbContext, auditLogWriter, cancellationToken);

    private static async Task<Results<Ok<UserResponse>, NotFound>> SetUserActiveStateAsync(
        Guid id,
        bool isActive,
        IdentityDbContext dbContext,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FindAsync([id], cancellationToken);
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        if (user.IsActive != isActive)
        {
            user.IsActive = isActive;
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditLogWriter.WriteAsync(
                "User",
                user.Id,
                isActive ? "Activate" : "Deactivate",
                new { user.IsActive },
                cancellationToken: cancellationToken);
        }

        return TypedResults.Ok(ToResponse(user));
    }

    private static Dictionary<string, string[]> ValidateCreateUser(CreateUserRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.Username))
        {
            errors[nameof(request.Username)] = ["Username is required."];
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            errors[nameof(request.Password)] = ["Password must be at least 8 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            errors[nameof(request.FullName)] = ["Full name is required."];
        }

        if (NormalizeRole(request.Role) is not { Length: > 0 })
        {
            errors[nameof(request.Role)] = ["Role must be one of: CLevel, Admin, Accountant, WarehouseClerk."];
        }

        if (NormalizeRole(request.Role) == LenseeRoles.WarehouseClerk && request.LocationId is null)
        {
            errors[nameof(request.LocationId)] = ["WarehouseClerk users must be assigned to a location."];
        }

        if (NormalizeRole(request.Role) != LenseeRoles.WarehouseClerk && request.LocationId is not null)
        {
            errors[nameof(request.LocationId)] = ["Only WarehouseClerk users can be assigned to a location."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidatePassword(string? password)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            errors[nameof(ChangePasswordRequest.NewPassword)] = ["Password must be at least 8 characters."];
        }

        return errors;
    }

    private static string NormalizeRole(string? role) =>
        LenseeRoles.Normalize(role);

    private static UserResponse ToResponse(User user) =>
        new(user.Id, user.Username, user.FullName, user.Role, user.LocationId, user.IsActive, user.CreatedAt);
}

public sealed record CreateUserRequest(
    string Username,
    string Password,
    string FullName,
    string Role,
    Guid? LocationId);

public sealed record ChangePasswordRequest(string NewPassword);

public sealed record UserResponse(
    Guid Id,
    string Username,
    string FullName,
    string Role,
    Guid? LocationId,
    bool IsActive,
    DateTime CreatedAt);
