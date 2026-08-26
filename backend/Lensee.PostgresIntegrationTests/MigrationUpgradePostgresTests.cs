using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lensee.PostgresIntegrationTests;

public sealed class MigrationUpgradePostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("lensee")
        .WithUsername("lensee_user")
        .WithPassword("SomeStrongPassword123!")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [PostgreSqlIntegrationFact]
    public async Task UpgradeFromPriorReleaseSchema_AppliesOperationsAndOutboxHardeningMigrations()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using (var schemas = connection.CreateCommand())
        {
            schemas.CommandText = """
                create extension if not exists "uuid-ossp";
                create schema if not exists operations;
                create schema if not exists shared;
                """;
            await schemas.ExecuteNonQueryAsync();
        }

        await using var operations = new OperationsDbContext(new DbContextOptionsBuilder<OperationsDbContext>()
            .UseNpgsql(connection)
            .Options);
        await using var shared = new SharedDbContext(new DbContextOptionsBuilder<SharedDbContext>()
            .UseNpgsql(connection)
            .Options);

        await operations.Database.GetService<IMigrator>()
            .MigrateAsync("20260817223500_AddReplenishmentRunsAndAutomation");
        await shared.Database.GetService<IMigrator>()
            .MigrateAsync("20260820090000_AddOutboxMessages");

        await operations.Database.MigrateAsync();
        await shared.Database.MigrateAsync();

        Assert.Empty(await operations.Database.GetPendingMigrationsAsync());
        Assert.Empty(await shared.Database.GetPendingMigrationsAsync());

        await using var verification = connection.CreateCommand();
        verification.CommandText = """
            select
                exists (select 1 from information_schema.tables where table_schema = 'operations' and table_name = 'operation_correction_proposals'),
                exists (select 1 from information_schema.columns where table_schema = 'shared' and table_name = 'outbox_messages' and column_name = 'event_version');
            """;
        await using var reader = await verification.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
    }
}
