using Lensee.Modules.Catalog.Data;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Finance.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.Modules.Reporting.Data;
using Lensee.SharedKernel.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lensee.PostgresIntegrationTests;

/// <summary>Exercises only real EF migration chains against an empty PostgreSQL database.</summary>
public sealed class MigrationIntegrityPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("lensee")
        .WithUsername("lensee_user")
        .WithPassword("SomeStrongPassword123!")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [PostgreSqlIntegrationFact]
    public async Task EmptyDatabase_ReachesLatestForEveryDbContext_UsingMigrationsOnly()
    {
        await using var contexts = await CreateContextsAsync();
        await MigrateAllAsync(contexts);

        foreach (var database in contexts.Databases)
        {
            Assert.Empty(await database.GetPendingMigrationsAsync());
            Assert.NotEmpty(await database.GetAppliedMigrationsAsync());
        }

        var expected = contexts.Databases
            .SelectMany(database => database.GetMigrations())
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected.Length, expected.Distinct(StringComparer.Ordinal).Count());

        await using var command = contexts.Connection.CreateCommand();
        command.CommandText = "select \"MigrationId\" from \"__EFMigrationsHistory\" order by \"MigrationId\";";
        await using var reader = await command.ExecuteReaderAsync();
        var applied = new List<string>();
        while (await reader.ReadAsync()) applied.Add(reader.GetString(0));
        Assert.Equal(expected, applied);
    }

    [PostgreSqlIntegrationFact]
    public async Task InitialHistoricalCheckpoint_UpgradesEveryDbContextToLatest()
    {
        await using var contexts = await CreateContextsAsync();
        foreach (var database in contexts.Databases)
        {
            var initial = database.GetMigrations().First();
            await database.GetService<IMigrator>().MigrateAsync(initial);
        }

        await MigrateAllAsync(contexts);
        foreach (var database in contexts.Databases)
            Assert.Empty(await database.GetPendingMigrationsAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task FinanceLatest_RollsBackOneMigrationAndMigratesForwardAgain()
    {
        await using var contexts = await CreateContextsAsync();
        await MigrateAllAsync(contexts);

        var financeMigrations = contexts.Finance.Database.GetMigrations().ToArray();
        var previous = financeMigrations[^2];
        await contexts.Finance.Database.GetService<IMigrator>().MigrateAsync(previous);
        Assert.Contains(previous, await contexts.Finance.Database.GetAppliedMigrationsAsync());
        Assert.DoesNotContain(financeMigrations[^1], await contexts.Finance.Database.GetAppliedMigrationsAsync());

        await contexts.Finance.Database.MigrateAsync();
        Assert.Empty(await contexts.Finance.Database.GetPendingMigrationsAsync());
    }

    private async Task<MigrationContexts> CreateContextsAsync()
    {
        var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "create extension if not exists \"uuid-ossp\";";
            await command.ExecuteNonQueryAsync();
        }

        TContext Create<TContext>(Func<DbContextOptions<TContext>, TContext> factory) where TContext : DbContext
            => factory(new DbContextOptionsBuilder<TContext>().UseNpgsql(connection).Options);

        return new MigrationContexts(
            connection,
            Create<SharedDbContext>(options => new SharedDbContext(options)),
            Create<IdentityDbContext>(options => new IdentityDbContext(options)),
            Create<CatalogDbContext>(options => new CatalogDbContext(options)),
            Create<InventoryDbContext>(options => new InventoryDbContext(options)),
            Create<CrmDbContext>(options => new CrmDbContext(options)),
            Create<FinanceDbContext>(options => new FinanceDbContext(options)),
            Create<OperationsDbContext>(options => new OperationsDbContext(options)),
            Create<PaymentsDbContext>(options => new PaymentsDbContext(options)),
            Create<NotificationsDbContext>(options => new NotificationsDbContext(options)),
            Create<ReportingDbContext>(options => new ReportingDbContext(options)));
    }

    private static async Task MigrateAllAsync(MigrationContexts contexts)
    {
        // This is the production dependency order; each context owns distinct
        // migration IDs in the shared migration-history table.
        foreach (var database in contexts.Databases)
            await database.MigrateAsync();
    }

    private sealed class MigrationContexts(
        NpgsqlConnection connection,
        SharedDbContext shared,
        IdentityDbContext identity,
        CatalogDbContext catalog,
        InventoryDbContext inventory,
        CrmDbContext crm,
        FinanceDbContext finance,
        OperationsDbContext operations,
        PaymentsDbContext payments,
        NotificationsDbContext notifications,
        ReportingDbContext reporting) : IAsyncDisposable
    {
        public NpgsqlConnection Connection { get; } = connection;
        public FinanceDbContext Finance { get; } = finance;
        public IReadOnlyList<DatabaseFacade> Databases { get; } =
        [
            shared.Database, identity.Database, catalog.Database, inventory.Database,
            crm.Database, finance.Database, operations.Database, payments.Database,
            notifications.Database, reporting.Database
        ];

        public async ValueTask DisposeAsync()
        {
            await shared.DisposeAsync();
            await identity.DisposeAsync();
            await catalog.DisposeAsync();
            await inventory.DisposeAsync();
            await crm.DisposeAsync();
            await Finance.DisposeAsync();
            await operations.DisposeAsync();
            await payments.DisposeAsync();
            await notifications.DisposeAsync();
            await reporting.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
