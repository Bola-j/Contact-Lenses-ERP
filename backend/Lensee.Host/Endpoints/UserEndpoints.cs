using Lensee.Host.Infrastructure;
using Lensee.Modules.Identity.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Security;
using Lensee.SharedKernel.Text;
using Microsoft.AspNetCore.Mvc;
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
            .RequireAuthorization("users.create")
            .WithName("CreateUser");

        group.MapPatch("/{id:guid}/password", ChangePasswordAsync)
            .RequireAuthorization("users.password.write")
            .WithName("ChangeUserPassword");

        group.MapPatch("/{id:guid}/activate", ActivateUserAsync)
            .RequireAuthorization("primary-admin")
            .WithName("ActivateUser");

        group.MapPatch("/{id:guid}/deactivate", DeactivateUserAsync)
            .RequireAuthorization("primary-admin")
            .WithName("DeactivateUser");

        group.MapPost("/{id:guid}/transfer-primary", TransferPrimaryAdminAsync)
            .RequireAuthorization("users.delete")
            .WithName("TransferPrimaryAdmin");

        group.MapDelete("/{id:guid}", DeleteUserAsync)
            .RequireAuthorization("users.delete")
            .WithName("DeleteUser");

        return group;
    }

    private static async Task<Ok<IReadOnlyList<UserResponse>>> ListUsersAsync(
        IdentityDbContext dbContext,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var users = await dbContext.Users.AsNoTracking()
            .OrderBy(user => user.Username)
            .ToListAsync(cancellationToken);

        var actor = currentUser.UserId is { } actorId
            ? users.SingleOrDefault(user => user.Id == actorId)
            : null;
        var actorCanDelete = actor is not null
            && actor.IsActive
            && string.Equals(actor.Role, LenseeRoles.Admin, StringComparison.OrdinalIgnoreCase);

        return TypedResults.Ok<IReadOnlyList<UserResponse>>(users
            .Select(user => ToResponse(user, GetDeletionAvailability(user, actor, actorCanDelete)))
            .ToList());
    }

    private static async Task<IResult> CreateUserAsync(
        CreateUserRequest request,
        IdentityDbContext dbContext,
        IPasswordHasher passwordHasher,
        IClock clock,
        ICurrentUser currentUser,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateCreateUser(request);
        if (validationErrors.Count > 0)
        {
            return TypedResults.ValidationProblem(validationErrors);
        }

        var username = InputText.NormalizeUsername(request.Username);
        if (await dbContext.Users.AnyAsync(user => user.Username.ToUpper() == username.ToUpper(), cancellationToken))
        {
            return Results.Conflict(new ProblemDetails
            {
                Title = "Username already in use.",
                Detail = "This username is already in use. Choose a different username.",
                Status = StatusCodes.Status409Conflict
            });
        }

        var role = NormalizeRole(request.Role);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = passwordHasher.Hash(request.Password),
            FullName = InputText.NormalizeSingleLine(request.FullName),
            Role = role,
            LocationId = request.LocationId,
            IsActive = true,
            CreatedAt = clock.EgyptNow,
            CreatedByAdminId = currentUser.UserId
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

    private static Task<IResult> ActivateUserAsync(
        Guid id,
        IdentityDbContext dbContext,
        ICurrentUser currentUser,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken) =>
        SetUserActiveStateAsync(id, true, dbContext, currentUser, auditLogWriter, cancellationToken);

    private static Task<IResult> DeactivateUserAsync(
        Guid id,
        IdentityDbContext dbContext,
        ICurrentUser currentUser,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken) =>
        SetUserActiveStateAsync(id, false, dbContext, currentUser, auditLogWriter, cancellationToken);

    private static async Task<IResult> SetUserActiveStateAsync(
        Guid id,
        bool isActive,
        IdentityDbContext dbContext,
        ICurrentUser currentUser,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var actorIsPrimaryAdmin = currentUser.UserId is { } actorId
            && await dbContext.Users.AsNoTracking().AnyAsync(
                actor => actor.Id == actorId
                    && actor.IsActive
                    && actor.IsPrimaryAdmin
                    && actor.Role == LenseeRoles.Admin,
                cancellationToken);
        if (!actorIsPrimaryAdmin)
        {
            return Results.Problem(
                title: "Primary Administrator authority is required.",
                detail: "Only the active primary Administrator can change account status.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var user = await dbContext.Users.FindAsync([id], cancellationToken);
        if (user is null)
        {
            return TypedResults.NotFound();
        }

        if (!isActive && currentUser.UserId == user.Id)
        {
            return Results.Conflict(new ProblemDetails
            {
                Title = "Current account cannot be deactivated.",
                Detail = "Sign in with a different primary Administrator account to deactivate this account.",
                Status = StatusCodes.Status409Conflict
            });
        }

        if (!isActive && user.IsPrimaryAdmin)
        {
            return Results.Conflict(new ProblemDetails
            {
                Title = "Primary Administrator account cannot be deactivated.",
                Detail = "Transfer primary authority before deactivating this account.",
                Status = StatusCodes.Status409Conflict
            });
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

    private static async Task<IResult> DeleteUserAsync(
        Guid id,
        IdentityDbContext dbContext,
        ICurrentUser currentUser,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.FindAsync([id], cancellationToken);
        if (user is null) return Results.NotFound();

        if (currentUser.UserId == user.Id)
        {
            return Results.Conflict(new ProblemDetails
            {
                Title = "Current account cannot be deleted.",
                Detail = "Sign in with a different Administrator account to delete this account.",
                Status = StatusCodes.Status409Conflict
            });
        }

        var actorIsPrimaryAdmin = currentUser.UserId is { } actorId
            && await dbContext.Users.AsNoTracking().AnyAsync(
                actor => actor.Id == actorId
                    && actor.IsActive
                    && actor.IsPrimaryAdmin
                    && actor.Role == LenseeRoles.Admin,
                cancellationToken);

        if (string.Equals(user.Role, LenseeRoles.Admin, StringComparison.OrdinalIgnoreCase)
            && !actorIsPrimaryAdmin)
        {
            return Results.Problem(
                title: "Administrator deletion is restricted.",
                detail: "Only the active primary Administrator can delete another Administrator account.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var historicalLogs = await dbContext.AuditLogs
            .Where(log => log.UserId == user.Id)
            .ToListAsync(cancellationToken);
        foreach (var historicalLog in historicalLogs)
        {
            historicalLog.UserId = null;
        }

        if (dbContext.Database.IsRelational())
        {
            await dbContext.Users
                .Where(remainingUser => remainingUser.CreatedByAdminId == user.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(remainingUser => remainingUser.CreatedByAdminId, (Guid?)null),
                    cancellationToken);
        }
        else
        {
            var accountsCreatedByUser = await dbContext.Users
                .Where(remainingUser => remainingUser.CreatedByAdminId == user.Id)
                .ToListAsync(cancellationToken);
            foreach (var account in accountsCreatedByUser)
            {
                account.CreatedByAdminId = null;
            }
        }

        var refreshTokens = await dbContext.RefreshTokens
            .Where(token => token.UserId == user.Id)
            .ToListAsync(cancellationToken);
        dbContext.RefreshTokens.RemoveRange(refreshTokens);
        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        await auditLogWriter.WriteAsync(
            "User",
            id,
            "Delete",
            new { user.Username, user.FullName, user.Role },
            cancellationToken: cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> TransferPrimaryAdminAsync(
        Guid id,
        IdentityDbContext dbContext,
        ICurrentUser currentUser,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return Results.Forbid();
        }

        var actor = await dbContext.Users.FindAsync([actorId], cancellationToken);
        if (actor is null
            || !actor.IsActive
            || !actor.IsPrimaryAdmin
            || !string.Equals(actor.Role, LenseeRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Problem(
                title: "Primary Administrator authority is required.",
                detail: "Only the active primary Administrator can transfer primary authority.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        if (id == actor.Id)
        {
            return Results.Conflict(new ProblemDetails
            {
                Title = "Choose a different Administrator account.",
                Detail = "Primary authority is already assigned to the current account.",
                Status = StatusCodes.Status409Conflict
            });
        }

        var target = await dbContext.Users.FindAsync([id], cancellationToken);
        if (target is null)
        {
            return Results.NotFound();
        }

        if (!target.IsActive || !string.Equals(target.Role, LenseeRoles.Admin, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Conflict(new ProblemDetails
            {
                Title = "Primary Administrator target is invalid.",
                Detail = "Primary authority can be transferred only to a different active Administrator account.",
                Status = StatusCodes.Status409Conflict
            });
        }

        if (dbContext.Database.IsRelational())
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            actor.IsPrimaryAdmin = false;
            await dbContext.SaveChangesAsync(cancellationToken);
            target.IsPrimaryAdmin = true;
            await dbContext.SaveChangesAsync(cancellationToken);

            await WritePrimaryTransferAuditAsync(actor, target, auditLogWriter, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            actor.IsPrimaryAdmin = false;
            target.IsPrimaryAdmin = true;
            await dbContext.SaveChangesAsync(cancellationToken);
            await WritePrimaryTransferAuditAsync(actor, target, auditLogWriter, cancellationToken);
        }

        return Results.NoContent();
    }

    private static Task WritePrimaryTransferAuditAsync(
        User actor,
        User target,
        IAuditLogWriter auditLogWriter,
        CancellationToken cancellationToken) =>
        auditLogWriter.WriteAsync(
            "User",
            target.Id,
            "TransferPrimaryAdmin",
            new
            {
                Summary = $"Transferred primary Administrator authority to {target.FullName}.",
                RecordName = target.FullName,
                Changes = new[]
                {
                    new { Field = "Previous primary Administrator", Before = (string?)actor.FullName, After = (string?)null },
                    new { Field = "New primary Administrator", Before = (string?)null, After = (string?)target.FullName }
                }
            },
            cancellationToken: cancellationToken);

    private static Dictionary<string, string[]> ValidateCreateUser(CreateUserRequest request)
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

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 8)
        {
            errors[nameof(request.Password)] = ["Password must be at least 8 characters."];
        }

        if (InputText.NormalizeSingleLine(request.FullName).Length == 0)
        {
            errors[nameof(request.FullName)] = ["Full name is required."];
        }

        if (NormalizeRole(request.Role) is not { Length: > 0 })
        {
            errors[nameof(request.Role)] = ["Role must be one of: CLevel, Admin, ERPAdmin, Accountant, WarehouseClerk."];
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

    private static DeletionAvailability GetDeletionAvailability(User target, User? actor, bool actorCanDelete)
    {
        if (!actorCanDelete)
        {
            return new(false, "Only an active Administrator can delete accounts.");
        }

        if (target.Id == actor!.Id)
        {
            return new(false, "You cannot delete the account currently signed in.");
        }

        if (string.Equals(target.Role, LenseeRoles.Admin, StringComparison.OrdinalIgnoreCase) && !actor!.IsPrimaryAdmin)
        {
            return new(false, "Only the active primary Administrator can delete another Administrator account.");
        }

        return new(true, null);
    }

    private static UserResponse ToResponse(User user, DeletionAvailability? deletionAvailability = null)
    {
        var availability = deletionAvailability ?? new DeletionAvailability(true, null);
        return new(
            user.Id,
            user.Username,
            user.FullName,
            user.Role,
            user.LocationId,
            user.IsActive,
            user.CreatedAt,
            user.IsPrimaryAdmin,
            availability.CanDelete,
            availability.BlockedReason);
    }

    private sealed record DeletionAvailability(bool CanDelete, string? BlockedReason);
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
    DateTime CreatedAt,
    bool IsPrimaryAdmin,
    bool CanDelete,
    string? DeletionBlockedReason);
