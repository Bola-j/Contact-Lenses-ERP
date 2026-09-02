using Lensee.Modules.Identity.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

public sealed class RefreshTokenSessionService
{
    private readonly IdentityDbContext _dbContext;
    private readonly ITokenService _tokenService;
    private readonly IClock _clock;
    private readonly IConfiguration _configuration;
    private readonly IAuditLogWriter _auditLogWriter;

    public RefreshTokenSessionService(
        IdentityDbContext dbContext,
        ITokenService tokenService,
        IClock clock,
        IConfiguration configuration,
        IAuditLogWriter auditLogWriter)
    {
        _dbContext = dbContext;
        _tokenService = tokenService;
        _clock = clock;
        _configuration = configuration;
        _auditLogWriter = auditLogWriter;
    }

    public async Task<IssuedRefreshToken> IssueAsync(
        Guid userId,
        string username,
        string? remoteIp,
        CancellationToken cancellationToken)
    {
        var rawToken = _tokenService.CreateRefreshToken();
        var now = _clock.EgyptNow;
        var expiresAt = now.AddDays(_configuration.GetValue("Jwt:RefreshTokenDays", 30));

        if (_dbContext.Database.IsRelational())
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            await LockUserAsync(userId, cancellationToken);
            AddToken(userId, rawToken, now, expiresAt, remoteIp);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditLogWriter.WriteForUserAsync(
                userId,
                string.Empty,
                "User",
                userId,
                "Login",
                new { username },
                cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            AddToken(userId, rawToken, now, expiresAt, remoteIp);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _auditLogWriter.WriteForUserAsync(
                userId,
                string.Empty,
                "User",
                userId,
                "Login",
                new { username },
                cancellationToken: cancellationToken);
        }

        return new IssuedRefreshToken(rawToken, expiresAt);
    }

    public async Task<RefreshRotationResult> RotateAsync(string rawToken, string? remoteIp, CancellationToken cancellationToken)
    {
        var tokenHash = _tokenService.HashRefreshToken(rawToken);
        var userId = await _dbContext.RefreshTokens.AsNoTracking()
            .Where(token => token.TokenHash == tokenHash)
            .Select(token => (Guid?)token.UserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!userId.HasValue)
        {
            return RefreshRotationResult.Missing();
        }

        if (!_dbContext.Database.IsRelational())
        {
            return await RotateNonRelationalAsync(tokenHash, userId.Value, remoteIp, cancellationToken);
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockUserAsync(userId.Value, cancellationToken);
        var existingToken = await _dbContext.RefreshTokens
            .Include(token => token.User)
            .AsNoTracking()
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);
        if (existingToken is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return RefreshRotationResult.Missing();
        }

        var now = _clock.EgyptNow;
        if (existingToken.RevokedAt.HasValue)
        {
            await RevokeAllLockedAsync(userId.Value, now, remoteIp, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RefreshRotationResult.Replay();
        }
        if (existingToken.ExpiresAt <= now || !existingToken.User.IsActive)
        {
            await transaction.CommitAsync(cancellationToken);
            return RefreshRotationResult.Invalid();
        }

        var replacementId = Guid.NewGuid();
        var replacementRawToken = _tokenService.CreateRefreshToken();
        var replacementExpiresAt = now.AddDays(_configuration.GetValue("Jwt:RefreshTokenDays", 30));
        AddToken(userId.Value, replacementRawToken, now, replacementExpiresAt, remoteIp, replacementId);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var claimed = await _dbContext.RefreshTokens
            .Where(token => token.Id == existingToken.Id && token.RevokedAt == null && token.ExpiresAt > now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.RevokedAt, now)
                .SetProperty(token => token.RevokedByIp, remoteIp)
                .SetProperty(token => token.ReplacedBy, replacementId), cancellationToken);
        if (claimed != 1)
        {
            await RevokeAllLockedAsync(userId.Value, now, remoteIp, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return RefreshRotationResult.Replay();
        }

        await transaction.CommitAsync(cancellationToken);
        return RefreshRotationResult.Success(existingToken.User, replacementRawToken, replacementExpiresAt);
    }

    public async Task RevokeAllAsync(
        Guid userId,
        string? remoteIp,
        bool writeLogoutAudit,
        CancellationToken cancellationToken)
    {
        var now = _clock.EgyptNow;
        if (_dbContext.Database.IsRelational())
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            await LockUserAsync(userId, cancellationToken);
            await RevokeAllLockedAsync(userId, now, remoteIp, cancellationToken);
            if (writeLogoutAudit)
            {
                await _auditLogWriter.WriteAsync("User", userId, "Logout", cancellationToken: cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await RevokeAllNonRelationalAsync(userId, now, remoteIp, cancellationToken);
        if (writeLogoutAudit)
        {
            await _auditLogWriter.WriteAsync("User", userId, "Logout", cancellationToken: cancellationToken);
        }
    }

    public async Task<Guid?> RevokeOneAsync(
        string rawToken,
        string? remoteIp,
        bool writeLogoutAudit,
        CancellationToken cancellationToken)
    {
        var tokenHash = _tokenService.HashRefreshToken(rawToken);
        var userId = await _dbContext.RefreshTokens.AsNoTracking()
            .Where(token => token.TokenHash == tokenHash)
            .Select(token => (Guid?)token.UserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!userId.HasValue)
        {
            return null;
        }

        if (_dbContext.Database.IsRelational())
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            await LockUserAsync(userId.Value, cancellationToken);
            var revoked = await _dbContext.RefreshTokens
                .Where(token => token.TokenHash == tokenHash && token.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(token => token.RevokedAt, _clock.EgyptNow)
                    .SetProperty(token => token.RevokedByIp, remoteIp), cancellationToken);
            if (revoked == 1 && writeLogoutAudit)
            {
                await _auditLogWriter.WriteAsync("User", userId.Value, "Logout", cancellationToken: cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return revoked == 1 ? userId : null;
        }

        var token = await _dbContext.RefreshTokens
            .SingleOrDefaultAsync(value => value.TokenHash == tokenHash && value.RevokedAt == null, cancellationToken);
        if (token is null)
        {
            return null;
        }

        token.RevokedAt = _clock.EgyptNow;
        token.RevokedByIp = remoteIp;
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (writeLogoutAudit)
        {
            await _auditLogWriter.WriteAsync("User", userId.Value, "Logout", cancellationToken: cancellationToken);
        }
        return userId;
    }

    private async Task<RefreshRotationResult> RotateNonRelationalAsync(
        string tokenHash,
        Guid userId,
        string? remoteIp,
        CancellationToken cancellationToken)
    {
        var existingToken = await _dbContext.RefreshTokens
            .Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);
        if (existingToken is null)
        {
            return RefreshRotationResult.Missing();
        }

        var now = _clock.EgyptNow;
        if (existingToken.RevokedAt.HasValue)
        {
            await RevokeAllNonRelationalAsync(userId, now, remoteIp, cancellationToken);
            return RefreshRotationResult.Replay();
        }
        if (existingToken.ExpiresAt <= now || !existingToken.User.IsActive)
        {
            return RefreshRotationResult.Invalid();
        }

        var replacementId = Guid.NewGuid();
        var replacementRawToken = _tokenService.CreateRefreshToken();
        var replacementExpiresAt = now.AddDays(_configuration.GetValue("Jwt:RefreshTokenDays", 30));
        existingToken.RevokedAt = now;
        existingToken.RevokedByIp = remoteIp;
        existingToken.ReplacedBy = replacementId;
        AddToken(userId, replacementRawToken, now, replacementExpiresAt, remoteIp, replacementId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return RefreshRotationResult.Success(existingToken.User, replacementRawToken, replacementExpiresAt);
    }

    private void AddToken(
        Guid userId,
        string rawToken,
        DateTime createdAt,
        DateTime expiresAt,
        string? remoteIp,
        Guid? tokenId = null)
    {
        _dbContext.RefreshTokens.Add(new RefreshToken
        {
            Id = tokenId ?? Guid.NewGuid(),
            UserId = userId,
            TokenHash = _tokenService.HashRefreshToken(rawToken),
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            CreatedByIp = remoteIp
        });
    }

    private Task LockUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var lockKey = $"refresh-token-user:{userId:N}";
        return _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"select pg_advisory_xact_lock(hashtextextended({lockKey}, 0))",
            cancellationToken);
    }

    private Task RevokeAllLockedAsync(Guid userId, DateTime revokedAt, string? remoteIp, CancellationToken cancellationToken) =>
        _dbContext.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.RevokedAt, revokedAt)
                .SetProperty(token => token.RevokedByIp, remoteIp), cancellationToken);

    private async Task RevokeAllNonRelationalAsync(Guid userId, DateTime revokedAt, string? remoteIp, CancellationToken cancellationToken)
    {
        var tokens = await _dbContext.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.RevokedAt = revokedAt;
            token.RevokedByIp = remoteIp;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed record IssuedRefreshToken(string RawToken, DateTime ExpiresAt);

public sealed record RefreshRotationResult(
    RefreshRotationStatus Status,
    User? User,
    string? RawToken,
    DateTime? ExpiresAt)
{
    public static RefreshRotationResult Success(User user, string rawToken, DateTime expiresAt) =>
        new(RefreshRotationStatus.Success, user, rawToken, expiresAt);

    public static RefreshRotationResult Missing() => new(RefreshRotationStatus.Missing, null, null, null);

    public static RefreshRotationResult Invalid() => new(RefreshRotationStatus.Invalid, null, null, null);

    public static RefreshRotationResult Replay() => new(RefreshRotationStatus.Replay, null, null, null);
}

public enum RefreshRotationStatus
{
    Success,
    Missing,
    Invalid,
    Replay
}
