using System.Net;
using System.Net.Http.Json;
using Lensee.Host.Infrastructure;
using Lensee.Modules.Identity.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lensee.PostgresIntegrationTests;

/// <summary>
/// Exercises the refresh route through the real HTTP pipeline and PostgreSQL,
/// with a database-held advisory lock guaranteeing that both requests overlap.
/// </summary>
public sealed class AuthEndpointPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("lensee")
        .WithUsername("lensee_user")
        .WithPassword("SomeStrongPassword123!")
        .Build();

    private AuthPostgresFactory _factory = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new AuthPostgresFactory(_postgres.GetConnectionString());
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        _factory.Dispose();
        await _postgres.DisposeAsync();
    }

    [PostgreSqlIntegrationFact]
    public async Task ConcurrentRefreshRequests_OneSucceeds_AndReplayRevokesEverySession()
    {
        await _factory.SeedUserAsync("refresh-http-user", "Password123!");
        using var loginClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await loginClient.PostAsJsonAsync("/api/v1/auth/login", new
        {
            username = "refresh-http-user",
            password = "Password123!"
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie")).Split(';')[0];

        var userId = await _factory.GetUserIdAsync("refresh-http-user");
        await using var lockHandle = await AcquireUserLockAsync(userId);
        var released = false;
        try
        {
            using var firstClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            using var secondClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            var first = RefreshAsync(firstClient, cookie);
            await WaitForAdvisoryWaitersAsync(1);
            var second = RefreshAsync(secondClient, cookie);
            await WaitForAdvisoryWaitersAsync(2);

            await lockHandle.Transaction.CommitAsync();
            released = true;
            var responses = await Task.WhenAll(first, second);

            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
        }
        finally
        {
            if (!released)
            {
                await lockHandle.Transaction.RollbackAsync();
            }
        }

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var identity = verificationScope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var sessions = await identity.RefreshTokens.AsNoTracking()
            .Where(token => token.UserId == userId)
            .ToListAsync();

        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, session => Assert.NotNull(session.RevokedAt));
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string cookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("Cookie", cookie);
        return client.SendAsync(request);
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
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) >= expectedCount)
            {
                return;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException($"Expected {expectedCount} PostgreSQL advisory-lock waiter(s).");
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

internal sealed class AuthPostgresFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public AuthPostgresFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Database:AutoMigrate"] = "false",
                ["Jwt:Secret"] = "AuthEndpointPostgresTestsNeedASecret123!",
                ["Jwt:Issuer"] = "Lensee",
                ["Jwt:Audience"] = "Lensee.App",
                ["Shopify:Enabled"] = "false"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            // Program captures its initial configuration while registering the
            // contexts, but resolves this scoped connection for every request.
            // Replacing it makes every context use the Testcontainers database.
            services.RemoveAll<NpgsqlConnection>();
            services.AddScoped(_ => new NpgsqlConnection(_connectionString));
            services.RemoveAll<IHostedService>();
        });
    }

    public async Task SeedUserAsync(string username, string password)
    {
        await using var scope = Services.CreateAsyncScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        identity.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = username,
            FullName = username,
            PasswordHash = new PasswordHasher().Hash(password),
            Role = "Admin",
            IsActive = true,
            CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
        });
        await identity.SaveChangesAsync();
    }

    public async Task<Guid> GetUserIdAsync(string username)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users
            .Where(user => user.Username == username)
            .Select(user => user.Id)
            .SingleAsync();
    }
}
