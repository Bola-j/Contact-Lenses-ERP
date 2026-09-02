using Lensee.Host.Infrastructure;
using Lensee.Modules.Identity.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lensee.PostgresIntegrationTests;

public sealed class RefreshTokenConcurrencyPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("lensee")
        .WithUsername("lensee_user")
        .WithPassword("SomeStrongPassword123!")
        .Build();

    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _services = BuildServices(_postgres.GetConnectionString());
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [PostgreSqlIntegrationFact]
    public async Task SimultaneousRotation_OneSucceedsAndReplayRevokesReplacement()
    {
        var userId = await SeedUserAsync();
        var original = await IssueInScopeAsync(userId, "initial-issue");

        await using var blockerConnection = await AcquireUserLockAsync(userId);
        await using var blockerTransaction = blockerConnection.Transaction;
        var released = false;
        try
        {
            var firstRotation = RotateInScopeAsync(original.RawToken, "rotation-one");
            await WaitForAdvisoryWaitersAsync(1);
            var secondRotation = RotateInScopeAsync(original.RawToken, "rotation-two");
            await WaitForAdvisoryWaitersAsync(2);

            await blockerTransaction.CommitAsync();
            released = true;
            var results = await Task.WhenAll(firstRotation, secondRotation);

            Assert.Single(results, result => result.Status == RefreshRotationStatus.Success);
            Assert.Single(results, result => result.Status == RefreshRotationStatus.Replay);
        }
        finally
        {
            if (!released)
            {
                await blockerTransaction.RollbackAsync();
            }
        }

        await using var verificationScope = _services.CreateAsyncScope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var sessions = await verification.RefreshTokens.AsNoTracking()
            .Where(token => token.UserId == userId)
            .ToListAsync();

        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, session => Assert.NotNull(session.RevokedAt));
        Assert.Equal(0, sessions.Count(session => session.RevokedAt == null));
        var originalHash = verificationScope.ServiceProvider.GetRequiredService<ITokenService>()
            .HashRefreshToken(original.RawToken);
        var originalSession = Assert.Single(sessions, session => session.TokenHash == originalHash);
        Assert.NotNull(originalSession.ReplacedBy);
        Assert.Contains(sessions, session => session.Id == originalSession.ReplacedBy && session.RevokedAt.HasValue);
    }

    [PostgreSqlIntegrationFact]
    public async Task RevokeAllOverlappingIssue_SerializesAccordingToCommitOrder()
    {
        await AssertRevokeAllAndIssueOrderingAsync(issueFirst: true, expectedActiveSessions: 0);
        await AssertRevokeAllAndIssueOrderingAsync(issueFirst: false, expectedActiveSessions: 1);
    }

    [PostgreSqlIntegrationFact]
    public async Task SessionAuditFailure_RollsBackIssueAndLogoutMutation()
    {
        var userId = await SeedUserAsync();
        await using var failingServices = BuildServices(_postgres.GetConnectionString(), throwAudit: true);

        await using (var issueScope = failingServices.CreateAsyncScope())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                issueScope.ServiceProvider.GetRequiredService<RefreshTokenSessionService>()
                    .IssueAsync(userId, "refresh-concurrency-user", "audit-failure-issue", CancellationToken.None));
        }

        await using (var verificationScope = _services.CreateAsyncScope())
        {
            var verification = verificationScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            Assert.Empty(await verification.RefreshTokens.AsNoTracking().Where(token => token.UserId == userId).ToListAsync());
        }

        var issued = await IssueInScopeAsync(userId, "logout-precondition");
        await using (var logoutScope = failingServices.CreateAsyncScope())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                logoutScope.ServiceProvider.GetRequiredService<RefreshTokenSessionService>()
                    .RevokeOneAsync(issued.RawToken, "audit-failure-logout", true, CancellationToken.None));
        }

        await using (var verificationScope = _services.CreateAsyncScope())
        {
            var verification = verificationScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var active = await verification.RefreshTokens.AsNoTracking()
                .SingleAsync(token => token.UserId == userId);
            Assert.Null(active.RevokedAt);
        }
    }

    private async Task AssertRevokeAllAndIssueOrderingAsync(bool issueFirst, int expectedActiveSessions)
    {
        var userId = await SeedUserAsync();
        var preexisting = await IssueInScopeAsync(userId, "preexisting-issue");

        await using var blockerConnection = await AcquireUserLockAsync(userId);
        await using var blockerTransaction = blockerConnection.Transaction;
        var released = false;
        try
        {
            Task<IssuedRefreshToken> overlappingIssue;
            Task revokeAll;
            if (issueFirst)
            {
                overlappingIssue = IssueInScopeAsync(userId, "overlapping-issue");
                await WaitForAdvisoryWaitersAsync(1);
                revokeAll = RevokeAllInScopeAsync(userId, "revoke-all");
            }
            else
            {
                revokeAll = RevokeAllInScopeAsync(userId, "revoke-all");
                await WaitForAdvisoryWaitersAsync(1);
                overlappingIssue = IssueInScopeAsync(userId, "overlapping-issue");
            }

            await WaitForAdvisoryWaitersAsync(2);
            await blockerTransaction.CommitAsync();
            released = true;
            await Task.WhenAll(overlappingIssue, revokeAll);

            await using var verificationScope = _services.CreateAsyncScope();
            var verification = verificationScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var sessions = await verification.RefreshTokens.AsNoTracking()
                .Where(token => token.UserId == userId)
                .ToListAsync();
            var activeSessions = sessions.Where(session => session.RevokedAt == null).ToList();
            var tokenService = verificationScope.ServiceProvider.GetRequiredService<ITokenService>();

            Assert.Equal(2, sessions.Count);
            Assert.Equal(expectedActiveSessions, activeSessions.Count);
            Assert.NotNull(Assert.Single(sessions, session => session.TokenHash == tokenService.HashRefreshToken(preexisting.RawToken)).RevokedAt);

            var overlappingSession = Assert.Single(
                sessions,
                session => session.TokenHash == tokenService.HashRefreshToken(overlappingIssue.Result.RawToken));
            if (issueFirst)
            {
                Assert.NotNull(overlappingSession.RevokedAt);
                Assert.Equal("revoke-all", overlappingSession.RevokedByIp);
            }
            else
            {
                Assert.Null(overlappingSession.RevokedAt);
                Assert.Equal("overlapping-issue", overlappingSession.CreatedByIp);
            }
        }
        finally
        {
            if (!released)
            {
                await blockerTransaction.RollbackAsync();
            }
        }
    }

    private async Task<Guid> SeedUserAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var userId = Guid.NewGuid();
        identity.Users.Add(new User
        {
            Id = userId,
            Username = $"refresh-{userId:N}",
            PasswordHash = "not-used",
            FullName = "Refresh concurrency user",
            Role = "Admin",
            IsActive = true,
            CreatedAt = TestClock.Now
        });
        await identity.SaveChangesAsync();
        return userId;
    }

    private async Task<IssuedRefreshToken> IssueInScopeAsync(Guid userId, string remoteIp)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RefreshTokenSessionService>()
            .IssueAsync(userId, "refresh-concurrency-user", remoteIp, CancellationToken.None);
    }

    private async Task<RefreshRotationResult> RotateInScopeAsync(string rawToken, string remoteIp)
    {
        await using var scope = _services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RefreshTokenSessionService>()
            .RotateAsync(rawToken, remoteIp, CancellationToken.None);
    }

    private async Task RevokeAllInScopeAsync(Guid userId, string remoteIp)
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RefreshTokenSessionService>()
            .RevokeAllAsync(userId, remoteIp, false, CancellationToken.None);
    }

    private async Task<BlockedAdvisoryLock> AcquireUserLockAsync(Guid userId)
    {
        var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select pg_advisory_xact_lock(hashtextextended(@lock_key, 0));";
        command.Parameters.AddWithValue("lock_key", $"refresh-token-user:{userId:N}");
        await command.ExecuteNonQueryAsync();
        return new BlockedAdvisoryLock(connection, transaction);
    }

    private async Task WaitForAdvisoryWaitersAsync(int expectedCount)
    {
        await using var inspection = new NpgsqlConnection(_postgres.GetConnectionString());
        await inspection.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var command = inspection.CreateCommand();
            command.CommandText = "select count(*) from pg_locks where locktype = 'advisory' and not granted;";
            var waiting = Convert.ToInt32(await command.ExecuteScalarAsync());
            if (waiting >= expectedCount)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Expected {expectedCount} PostgreSQL advisory-lock waiter(s).");
    }

    private static ServiceProvider BuildServices(string connectionString, bool throwAudit = false)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:RefreshTokenDays"] = "30",
                ["Jwt:Secret"] = "refresh-concurrency-test-secret-that-is-long-enough"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IClock, TestClock>();
        services.AddScoped<ITokenService, TokenService>();
        if (throwAudit)
        {
            services.AddScoped<IAuditLogWriter, FailingAuditLogWriter>();
        }
        else
        {
            services.AddScoped<IAuditLogWriter, NoOpAuditLogWriter>();
        }
        services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<RefreshTokenSessionService>();
        return services.BuildServiceProvider();
    }

    private sealed class TestClock : IClock
    {
        public static readonly DateTime Now = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Unspecified);

        public DateTime UtcNow => DateTime.SpecifyKind(Now, DateTimeKind.Utc);

        public DateTime EgyptNow => Now;
    }

    private sealed class NoOpAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(
            string entityType,
            Guid entityId,
            string action,
            object? changedFields = null,
            int? stockDeltaApplied = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task WriteSystemAsync(
            string actorName,
            string entityType,
            Guid entityId,
            string action,
            object? changedFields = null,
            int? stockDeltaApplied = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task WriteForUserAsync(
            Guid actorUserId,
            string actorName,
            string entityType,
            Guid entityId,
            string action,
            object? changedFields = null,
            int? stockDeltaApplied = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FailingAuditLogWriter : IAuditLogWriter
    {
        public Task WriteAsync(
            string entityType,
            Guid entityId,
            string action,
            object? changedFields = null,
            int? stockDeltaApplied = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Injected audit failure.");

        public Task WriteSystemAsync(
            string actorName,
            string entityType,
            Guid entityId,
            string action,
            object? changedFields = null,
            int? stockDeltaApplied = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Injected audit failure.");

        public Task WriteForUserAsync(
            Guid actorUserId,
            string actorName,
            string entityType,
            Guid entityId,
            string action,
            object? changedFields = null,
            int? stockDeltaApplied = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Injected audit failure.");
    }

    private sealed class BlockedAdvisoryLock : IAsyncDisposable
    {
        public BlockedAdvisoryLock(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            Connection = connection;
            Transaction = transaction;
        }

        public NpgsqlConnection Connection { get; }

        public NpgsqlTransaction Transaction { get; }

        public async ValueTask DisposeAsync()
        {
            await Transaction.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
