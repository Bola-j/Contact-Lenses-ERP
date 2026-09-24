using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.Modules.Finance.Data;
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
    public async Task UpgradeFromPriorReleaseSchema_AppliesAllModuleHardeningMigrations()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using (var schemas = connection.CreateCommand())
        {
            schemas.CommandText = """
                create extension if not exists "uuid-ossp";
                create schema if not exists inventory;
                create schema if not exists operations;
                create schema if not exists payments;
                create schema if not exists finance;
                create schema if not exists shared;
                """;
            await schemas.ExecuteNonQueryAsync();
        }

        await using var inventory = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connection)
            .Options);
        await using var operations = new OperationsDbContext(new DbContextOptionsBuilder<OperationsDbContext>()
            .UseNpgsql(connection)
            .Options);
        await using var payments = new PaymentsDbContext(new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql(connection)
            .Options);
        await using var finance = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseNpgsql(connection)
            .Options);
        await using var shared = new SharedDbContext(new DbContextOptionsBuilder<SharedDbContext>()
            .UseNpgsql(connection)
            .Options);

        await inventory.Database.GetService<IMigrator>()
            .MigrateAsync("20260704161512_AddProductionConstraintsInventory");
        await operations.Database.GetService<IMigrator>()
            .MigrateAsync("20260817223500_AddReplenishmentRunsAndAutomation");
        await payments.Database.GetService<IMigrator>()
            .MigrateAsync("20260820091000_AddPaymentIntegrityControls");
        await shared.Database.GetService<IMigrator>()
            .MigrateAsync("20260820090000_AddOutboxMessages");

        await inventory.Database.MigrateAsync();
        await finance.Database.MigrateAsync();
        await operations.Database.MigrateAsync();
        await payments.Database.MigrateAsync();
        await shared.Database.MigrateAsync();

        Assert.Empty(await inventory.Database.GetPendingMigrationsAsync());
        Assert.Empty(await finance.Database.GetPendingMigrationsAsync());
        Assert.Empty(await operations.Database.GetPendingMigrationsAsync());
        Assert.Empty(await payments.Database.GetPendingMigrationsAsync());
        Assert.Empty(await shared.Database.GetPendingMigrationsAsync());

        await using var verification = connection.CreateCommand();
        verification.CommandText = """
            select
                exists (select 1 from information_schema.tables where table_schema = 'inventory' and table_name = 'inventory_receipt_commands'),
                exists (select 1 from information_schema.tables where table_schema = 'finance' and table_name = 'finance_accounts'),
                exists (select 1 from information_schema.tables where table_schema = 'operations' and table_name = 'operation_correction_proposals'),
                exists (select 1 from information_schema.columns where table_schema = 'payments' and table_name = 'financial_adjustments' and column_name = 'reverses_adjustment_id'),
                exists (select 1 from information_schema.columns where table_schema = 'shared' and table_name = 'outbox_messages' and column_name = 'event_version');
            """;
        await using var reader = await verification.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
        Assert.True(reader.GetBoolean(3));
        Assert.True(reader.GetBoolean(4));
    }

    [PostgreSqlIntegrationFact]
    public async Task PaymentsUpgrade_WithHistoricalDeferredPaymentLabelsAndAggregateDrift_AllowsLabelCleanup()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using (var schemas = connection.CreateCommand())
        {
            schemas.CommandText = """
                create extension if not exists "uuid-ossp";
                create schema if not exists payments;
                create schema if not exists operations;
                create schema if not exists inventory;
                create schema if not exists finance;
                create schema if not exists shared;
                """;
            await schemas.ExecuteNonQueryAsync();
        }

        await using var inventory = new InventoryDbContext(new DbContextOptionsBuilder<InventoryDbContext>().UseNpgsql(connection).Options);
        await using var finance = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(connection).Options);
        await using var operations = new OperationsDbContext(new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(connection).Options);
        await inventory.Database.MigrateAsync();
        await finance.Database.MigrateAsync();
        await operations.Database.MigrateAsync();

        await using var payments = new PaymentsDbContext(new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseNpgsql(connection)
            .Options);

        await payments.Database.GetService<IMigrator>()
            .MigrateAsync("20260922004441_AddMerchantAllocationReconciliations");

        var paymentLogId = Guid.NewGuid();
        var operationId = Guid.NewGuid();
        var merchantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        await payments.Database.ExecuteSqlInterpolatedAsync($"""
            insert into payments.main_payment_logs (
                id, operation_id, merchant_id, total_amount, amount_paid, pending_amount,
                payment_method, status, initialized_by, initialized_at, last_modified_at, is_deleted, scope)
            values (
                {paymentLogId}, {operationId}, {merchantId}, 10.0000, 10.0000, 0.0000,
                'MerchantAccount', 'Completed', {actorId}, now(), now(), false, 'MerchantAccount');
            """);

        await using (var reproduction = connection.CreateCommand())
        {
            reproduction.CommandText = """
                begin;
                update payments.main_payment_logs
                set payment_method = null
                where id = @paymentLogId;
                set constraints all immediate;
                """;
            reproduction.Parameters.AddWithValue("paymentLogId", paymentLogId);
            await Assert.ThrowsAsync<PostgresException>(() => reproduction.ExecuteNonQueryAsync());
        }
        await using (var rollback = connection.CreateCommand())
        {
            rollback.CommandText = "rollback;";
            await rollback.ExecuteNonQueryAsync();
        }

        await payments.Database.MigrateAsync();

        Assert.Empty(await payments.Database.GetPendingMigrationsAsync());

        await using var verification = connection.CreateCommand();
        verification.CommandText = """
            select
                (select count(*) from "__EFMigrationsHistory" where "MigrationId" = '20260922012642_AllowDeferredMerchantPaymentLogs'),
                (select is_nullable = 'YES' from information_schema.columns where table_schema = 'payments' and table_name = 'main_payment_logs' and column_name = 'payment_method'),
                (select pg_get_constraintdef(oid) from pg_constraint where conrelid = 'payments.main_payment_logs'::regclass and conname = 'chk_main_payment_method'),
                (select count(*) from payments.main_payment_logs where payment_method in ('MerchantAccount','Installment','Installlaugment')),
                (select payment_method is null from payments.main_payment_logs where id = @paymentLogId);
            """;
        verification.Parameters.AddWithValue("paymentLogId", paymentLogId);

        await using var reader = await verification.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1L, reader.GetInt64(0));
        Assert.True(reader.GetBoolean(1));
        var constraint = reader.GetString(2);
        Assert.Contains("payment_method", constraint, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CashHandToHand", constraint, StringComparison.Ordinal);
        Assert.Contains("CashTransaction", constraint, StringComparison.Ordinal);
        Assert.Contains("BankTransfer", constraint, StringComparison.Ordinal);
        Assert.Contains("Wallet", constraint, StringComparison.Ordinal);
        Assert.Equal(0L, reader.GetInt64(3));
        Assert.True(reader.GetBoolean(4));
    }
}
