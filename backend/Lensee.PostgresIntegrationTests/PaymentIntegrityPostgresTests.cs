using Lensee.Host.Infrastructure;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Payments.Data;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lensee.PostgresIntegrationTests;

public sealed class PaymentIntegrityPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("lensee")
        .WithUsername("lensee_user")
        .WithPassword("SomeStrongPassword123!")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                create extension if not exists "uuid-ossp";
                create schema if not exists shared;
                create schema if not exists catalog;
                create schema if not exists identity;
                create schema if not exists payments;
                create schema if not exists operations;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var shared = CreateSharedContext(connection);
        await using var catalog = CreateCatalogContext(connection);
        await using var identity = CreateIdentityContext(connection);
        await using var payments = CreatePaymentsContext(connection);
        await using var operations = CreateOperationsContext(connection);
        await shared.Database.MigrateAsync();
        await catalog.Database.MigrateAsync();
        await identity.Database.MigrateAsync();
        await payments.Database.MigrateAsync();
        await operations.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [PostgreSqlIntegrationFact]
    public async Task PaymentAggregateTrigger_RollsBackMismatchedDraftTotals()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        var paymentId = Guid.NewGuid();
        await using var createLog = connection.CreateCommand();
        createLog.Transaction = transaction;
        createLog.CommandText = """
            insert into payments.main_payment_logs
                (id, operation_id, merchant_id, total_amount, amount_paid, pending_amount, payment_method, status, initialized_by, initialized_at, last_modified_at, is_deleted)
            values
                (@id, @operation_id, @merchant_id, 100, 0, 0, 'Installment', 'PendingAccountant', @user_id, now(), now(), false);
            """;
        createLog.Parameters.AddWithValue("id", paymentId);
        createLog.Parameters.AddWithValue("operation_id", Guid.NewGuid());
        createLog.Parameters.AddWithValue("merchant_id", Guid.NewGuid());
        createLog.Parameters.AddWithValue("user_id", Guid.NewGuid());
        await createLog.ExecuteNonQueryAsync();

        await using var createDraft = connection.CreateCommand();
        createDraft.Transaction = transaction;
        createDraft.CommandText = """
            insert into payments.installment_sub_logs
                (id, main_log_id, amount, payment_method, date_received, sub_log_status, drafted_by, drafted_at)
            values
                (@id, @main_log_id, 50, 'Installment', current_date, 'Draft', @user_id, now());
            """;
        createDraft.Parameters.AddWithValue("id", Guid.NewGuid());
        createDraft.Parameters.AddWithValue("main_log_id", paymentId);
        createDraft.Parameters.AddWithValue("user_id", Guid.NewGuid());
        await createDraft.ExecuteNonQueryAsync();

        await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task PaymentPendingCheck_BlocksAggregateOverage()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into payments.main_payment_logs
                (id, operation_id, merchant_id, total_amount, amount_paid, pending_amount, payment_method, status, initialized_by, initialized_at, last_modified_at, is_deleted)
            values
                (@id, @operation_id, @merchant_id, 100, 60, 60, 'Installment', 'PendingAccountant', @user_id, now(), now(), false);
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("operation_id", Guid.NewGuid());
        command.Parameters.AddWithValue("merchant_id", Guid.NewGuid());
        command.Parameters.AddWithValue("user_id", Guid.NewGuid());

        await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task OperationCorrectionConstraint_AllowsOnlyOneActiveReversalPerOriginal()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        var originalId = Guid.NewGuid();
        await InsertOperationAsync(connection, originalId, "Standard", null);
        await InsertOperationAsync(connection, Guid.NewGuid(), "Reversal", originalId);

        await Assert.ThrowsAsync<PostgresException>(() => InsertOperationAsync(connection, Guid.NewGuid(), "Reversal", originalId));
    }

    [PostgreSqlIntegrationFact]
    public async Task HardeningMigrations_AreAppliedToAFreshDatabase()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "select \"MigrationId\" from \"__EFMigrationsHistory\" order by \"MigrationId\"";
        await using var reader = await command.ExecuteReaderAsync();
        var migrations = new List<string>();
        while (await reader.ReadAsync()) migrations.Add(reader.GetString(0));

        Assert.Contains("20260820090000_AddOutboxMessages", migrations);
        Assert.Contains("20260825091000_AddOutboxContractMetadata", migrations);
        Assert.Contains("20260825090000_AddOperationCorrections", migrations);
    }

    [PostgreSqlIntegrationFact]
    public async Task CatalogMutationTransaction_RollsBackCatalogAndOutboxTogether()
    {
        var brandId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var catalog = CreateCatalogContext(connection);
            await using var identity = CreateIdentityContext(connection);
            await using var shared = CreateSharedContext(connection);
            var mutation = new CatalogMutationTransaction(identity, shared);

            catalog.Brands.Add(new Brand
            {
                Id = brandId,
                Name = "Rollback proof brand",
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            });

            await Assert.ThrowsAsync<InvalidOperationException>(() => mutation.ExecuteAsync(catalog, async () =>
            {
                await catalog.SaveChangesAsync();
                shared.OutboxMessages.Add(new OutboxMessage
                {
                    Id = Guid.NewGuid(),
                    EventType = "Catalog.BrandCreated",
                    Payload = "{}",
                    Status = "Pending",
                    Attempts = 0,
                    OccurredAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified),
                    NextAttemptAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                });
                await shared.SaveChangesAsync();
                throw new InvalidOperationException("Injected catalog mutation failure.");
            }, CancellationToken.None));
        }

        await using var verificationConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await verificationConnection.OpenAsync();
        await using var verificationCatalog = CreateCatalogContext(verificationConnection);
        await using var verificationShared = CreateSharedContext(verificationConnection);
        Assert.False(await verificationCatalog.Brands.AnyAsync(brand => brand.Id == brandId));
        Assert.DoesNotContain(await verificationShared.OutboxMessages.ToListAsync(), message => message.EventType == "Catalog.BrandCreated");
    }

    private static SharedDbContext CreateSharedContext(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(connection).Options);

    private static CatalogDbContext CreateCatalogContext(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connection).Options);

    private static IdentityDbContext CreateIdentityContext(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connection).Options);

    private static PaymentsDbContext CreatePaymentsContext(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<PaymentsDbContext>().UseNpgsql(connection).Options);

    private static OperationsDbContext CreateOperationsContext(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(connection).Options);

    private static async Task InsertOperationAsync(NpgsqlConnection connection, Guid id, string recordKind, Guid? reversesOperationId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into operations.operation_logs
                (id, operation_number, operation_type, status, created_by, created_at, record_kind, reverses_operation_id, is_deleted)
            values
                (@id, @number, 'WholesaleSale', 'Completed', @user_id, now(), @record_kind, @reverses_operation_id, false);
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("number", $"PG-{id:N}");
        command.Parameters.AddWithValue("user_id", Guid.NewGuid());
        command.Parameters.AddWithValue("record_kind", recordKind);
        command.Parameters.AddWithValue("reverses_operation_id", (object?)reversesOperationId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class PostgreSqlIntegrationFactAttribute : FactAttribute
{
    public PostgreSqlIntegrationFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("LENSEE_RUN_POSTGRES_TESTS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set LENSEE_RUN_POSTGRES_TESTS=true on a machine with Docker to run PostgreSQL/Testcontainers consistency tests.";
        }
    }
}
