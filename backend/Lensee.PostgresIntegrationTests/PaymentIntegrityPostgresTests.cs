using System.Net.Http.Json;
using System.Text.Json;
using Lensee.Host.Endpoints;
using Lensee.Host.Infrastructure;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Finance.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Payments.Data;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Security;
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
                create schema if not exists inventory;
                create schema if not exists payments;
                create schema if not exists operations;
                create schema if not exists finance;
                """;
            await command.ExecuteNonQueryAsync();
        }

        await using var shared = CreateSharedContext(connection);
        await using var catalog = CreateCatalogContext(connection);
        await using var identity = CreateIdentityContext(connection);
        await using var inventory = CreateInventoryContext(connection);
        await using var crm = CreateCrmContext(connection);
        await using var payments = CreatePaymentsContext(connection);
        await using var finance = CreateFinanceContext(connection);
        await using var operations = CreateOperationsContext(connection);
        await shared.Database.MigrateAsync();
        await catalog.Database.MigrateAsync();
        await identity.Database.MigrateAsync();
        await inventory.Database.MigrateAsync();
        await crm.Database.MigrateAsync();
        // Operations and Payments migrations reference FinanceAccount.  The
        // test database must mirror the production migrator dependency order.
        await finance.Database.MigrateAsync();
        await operations.Database.MigrateAsync();
        await payments.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [PostgreSqlIntegrationFact]
    public async Task ExecutiveSummary_UsesPostedExternalMovementsWithoutInternalTransferInflation()
    {
        var actor = Guid.NewGuid();
        var source = Guid.NewGuid();
        var destination = Guid.NewGuid();
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var day = new DateOnly(2040, 2, 3);
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var finance = CreateFinanceContext(connection);
            finance.FinanceAccounts.AddRange(
                new FinanceAccount { Id = source, Name = "Executive source", Type = FinanceLedgerService.CashOnHand, IsActive = true, CreatedBy = actor, CreatedAt = now },
                new FinanceAccount { Id = destination, Name = "Executive destination", Type = FinanceLedgerService.BankAccount, IsActive = true, CreatedBy = actor, CreatedAt = now });
            await finance.SaveChangesAsync();
            finance.FinanceLedgerEntries.AddRange(
                new FinanceLedgerEntry { Id = Guid.NewGuid(), FinanceAccountId = source, Direction = "Credit", Amount = 100m, Category = "MerchantCollection", MovementMethod = "CashHandToHand", SourceType = "MerchantCollection", SourceId = Guid.NewGuid(), BusinessDate = day, Status = "Posted", CreatedBy = actor, CreatedAt = now },
                new FinanceLedgerEntry { Id = Guid.NewGuid(), FinanceAccountId = source, Direction = "Debit", Amount = 20m, Category = "OperatingExpense", MovementMethod = "CashHandToHand", SourceType = "FinanceExpense", SourceId = Guid.NewGuid(), BusinessDate = day, Status = "Posted", CreatedBy = actor, CreatedAt = now },
                new FinanceLedgerEntry { Id = Guid.NewGuid(), FinanceAccountId = source, Direction = "Debit", Amount = 30m, Category = "InternalTransfer", MovementMethod = "CashHandToHand", SourceType = "FinanceTransferOut", SourceId = Guid.NewGuid(), BusinessDate = day, Status = "Posted", CreatedBy = actor, CreatedAt = now },
                new FinanceLedgerEntry { Id = Guid.NewGuid(), FinanceAccountId = destination, Direction = "Credit", Amount = 30m, Category = "InternalTransfer", MovementMethod = "BankTransfer", SourceType = "FinanceTransferIn", SourceId = Guid.NewGuid(), BusinessDate = day, Status = "Posted", CreatedBy = actor, CreatedAt = now });
            await finance.SaveChangesAsync();
        }

        await using var factory = new PostgresApplicationFactory(_postgres.GetConnectionString());
        using var client = factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, actor, LenseePermissions.ExecutiveSummaryRead);
        using var response = await client.GetAsync("/api/v1/reports/executive-summary?period=daily&date=2040-02-03");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var flows = json.RootElement.GetProperty("flows");
        Assert.Equal(100m, flows.GetProperty("actualCollections").GetDecimal());
        Assert.Equal(20m, flows.GetProperty("paidExpenses").GetDecimal());
        Assert.Equal(80m, flows.GetProperty("netExternalCashMovement").GetDecimal());
        Assert.Equal(0, json.RootElement.GetProperty("units").GetProperty("paidPiecesSold").GetInt32());
    }

    [PostgreSqlIntegrationFact]
    public async Task ExecutiveSummary_MarksHistoricalPackUnitsEstimatedAndSubtractsReturns()
    {
        var actor = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var returnId = Guid.NewGuid();
        var now = new DateTime(2041, 3, 5, 12, 0, 0, DateTimeKind.Unspecified);
        await using var factory = new PostgresApplicationFactory(_postgres.GetConnectionString());
        var seed = await factory.SeedOperationReferencesAsync();
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var operations = CreateOperationsContext(connection);
            await using var payments = CreatePaymentsContext(connection);
            operations.OperationLogs.AddRange(
                new OperationLog
                {
                    Id = saleId,
                    OperationNumber = $"EST-SALE-{saleId:N}",
                    OperationType = "RetailSale",
                    Status = "Completed",
                    RecordKind = "Standard",
                    SourceLocationId = seed.MainLocationId,
                    CreatedBy = actor,
                    CreatedAt = now,
                    ConfirmedAt = now
                },
                new OperationLog
                {
                    Id = returnId,
                    OperationNumber = $"EST-RETURN-{returnId:N}",
                    OperationType = "Return",
                    Status = "Confirmed",
                    RecordKind = "Standard",
                    SourceLocationId = seed.MainLocationId,
                    CreatedBy = actor,
                    CreatedAt = now,
                    ConfirmedAt = now
                });
            operations.OperationLines.AddRange(
                new OperationLine
                {
                    Id = Guid.NewGuid(),
                    OperationId = saleId,
                    SkuId = seed.SkuId,
                    ProductNameSnapshot = "Historical pack",
                    SkuCodeSnapshot = "EST",
                    Section = "Standard",
                    EntryMode = "Packs",
                    Quantity = 3,
                    UnitPrice = 100m,
                    LineTotal = 300m
                },
                new OperationLine
                {
                    Id = Guid.NewGuid(),
                    OperationId = saleId,
                    SkuId = seed.SkuId,
                    ProductNameSnapshot = "Bonus pack",
                    SkuCodeSnapshot = "EST",
                    Section = "Standard",
                    EntryMode = "Packs",
                    Quantity = 1,
                    BonusQuantity = 1,
                    UnitPrice = 0m,
                    LineTotal = 0m
                },
                new OperationLine
                {
                    Id = Guid.NewGuid(),
                    OperationId = returnId,
                    SkuId = seed.SkuId,
                    ProductNameSnapshot = "Returned pack",
                    SkuCodeSnapshot = "EST",
                    Section = "Standard",
                    EntryMode = "Packs",
                    PiecesPerPackSnapshot = 2,
                    Quantity = 1,
                    UnitPrice = 100m,
                    LineTotal = 100m
                });
            await operations.SaveChangesAsync();
            payments.MainPaymentLogs.Add(new MainPaymentLog
            {
                Id = Guid.NewGuid(),
                OperationId = saleId,
                Scope = "OtherPayments",
                TotalAmount = 300m,
                AmountPaid = 300m,
                PendingAmount = 0m,
                PaymentMethod = "CashHandToHand",
                Status = "Completed",
                InitializedBy = actor,
                InitializedAt = now,
                LastModifiedAt = now
            });
            await payments.SaveChangesAsync();
        }
        using var client = factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, actor, LenseePermissions.ExecutiveSummaryRead);
        using var response = await client.GetAsync("/api/v1/reports/executive-summary?period=daily&date=2041-03-05");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(200m, json.RootElement.GetProperty("flows").GetProperty("netSales").GetDecimal());
        var units = json.RootElement.GetProperty("units");
        Assert.Equal(4, units.GetProperty("paidPiecesSold").GetInt32());
        Assert.Equal(2, units.GetProperty("bonusPieces").GetInt32());
        Assert.True(units.GetProperty("estimated").GetBoolean());
        Assert.Equal(0, units.GetProperty("unresolvedPackLines").GetInt32());
        using var monthly = await client.GetAsync("/api/v1/reports/executive-summary?period=monthly&date=2041-03-15");
        Assert.True(monthly.IsSuccessStatusCode, await monthly.Content.ReadAsStringAsync());
        using var monthlyJson = JsonDocument.Parse(await monthly.Content.ReadAsStringAsync());
        Assert.Equal(4, monthlyJson.RootElement.GetProperty("units").GetProperty("paidPiecesSold").GetInt32());
        Assert.Equal("2041-04-01", monthlyJson.RootElement.GetProperty("endExclusive").GetString());
        using var nextDay = await client.GetAsync("/api/v1/reports/executive-summary?period=daily&date=2041-03-06");
        Assert.True(nextDay.IsSuccessStatusCode, await nextDay.Content.ReadAsStringAsync());
        using var nextDayJson = JsonDocument.Parse(await nextDay.Content.ReadAsStringAsync());
        Assert.Equal(0, nextDayJson.RootElement.GetProperty("units").GetProperty("paidPiecesSold").GetInt32());
    }

    [PostgreSqlIntegrationFact]
    public async Task FinanceReconciliation_FlagsLegacyNegativeHistoryWithoutChangingEntries()
    {
        var actor = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var now = new DateTime(2042, 4, 9, 10, 0, 0, DateTimeKind.Unspecified);
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var finance = CreateFinanceContext(connection);
            finance.FinanceAccounts.Add(new FinanceAccount
            {
                Id = accountId,
                Name = "Historical review account",
                Type = FinanceLedgerService.CashOnHand,
                IsActive = true,
                CreatedBy = actor,
                CreatedAt = now
            });
            await finance.SaveChangesAsync();
            finance.FinanceLedgerEntries.AddRange(
                new FinanceLedgerEntry
                {
                    Id = Guid.NewGuid(),
                    FinanceAccountId = accountId,
                    Direction = "Debit",
                    Amount = 10m,
                    Category = "OperatingExpense",
                    MovementMethod = "CashHandToHand",
                    SourceType = "LegacyExpense",
                    SourceId = Guid.NewGuid(),
                    BusinessDate = new DateOnly(2042, 4, 9),
                    Status = "Posted",
                    CreatedBy = actor,
                    CreatedAt = now
                },
                new FinanceLedgerEntry
                {
                    Id = Guid.NewGuid(),
                    FinanceAccountId = accountId,
                    Direction = "Credit",
                    Amount = 20m,
                    Category = "MerchantCollection",
                    MovementMethod = "CashHandToHand",
                    SourceType = "LegacyCollection",
                    SourceId = Guid.NewGuid(),
                    BusinessDate = new DateOnly(2042, 4, 9),
                    Status = "Posted",
                    CreatedBy = actor,
                    CreatedAt = now.AddMinutes(1)
                });
            await finance.SaveChangesAsync();
        }
        await using var factory = new PostgresApplicationFactory(_postgres.GetConnectionString());
        using var client = factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, actor, LenseePermissions.FinanceReconcile);
        using var response = await client.GetAsync($"/api/v1/finance/reconciliation?accountId={accountId}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(10m, row.GetProperty("expectedBalance").GetDecimal());
        Assert.True(row.GetProperty("historicalNegativeBalance").GetBoolean());
        Assert.Equal(-10m, row.GetProperty("minimumPostedBalance").GetDecimal());
        await using var verifyConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await verifyConnection.OpenAsync();
        await using var verify = CreateFinanceContext(verifyConnection);
        Assert.Equal(2, await verify.FinanceLedgerEntries.CountAsync(value => value.FinanceAccountId == accountId));
    }

    [PostgreSqlIntegrationFact]
    public async Task MerchantCollectionReviewLock_UsesMigrationCreatedQuotedIdentifier()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var payments = CreatePaymentsContext(connection);
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var account = new MerchantReceivableAccount
        {
            Id = Guid.NewGuid(),
            MerchantId = Guid.NewGuid(),
            Status = "Open",
            NextSequence = 1,
            OpenedAt = now,
            UpdatedAt = now,
            OpenedBy = Guid.NewGuid()
        };
        var draft = new MerchantAccountCollectionDraft
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Account = account,
            Amount = 125m,
            PaymentMethod = "CashHandToHand",
            Status = "Draft",
            DraftedBy = Guid.NewGuid(),
            DraftedAt = now
        };
        payments.AddRange(account, draft);
        await payments.SaveChangesAsync();

        await using var transaction = await payments.Database.BeginTransactionAsync();
        var affected = await payments.Database.ExecuteSqlInterpolatedAsync(
            $"select 1 from payments.merchant_account_collection_drafts where \"Id\" = {draft.Id} for update");

        Assert.Equal(-1, affected);
        await transaction.RollbackAsync();
    }

    [PostgreSqlIntegrationFact]
    public async Task CollectionScope_IsImmutableAfterInsert()
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var payments = CreatePaymentsContext(connection);
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var account = new MerchantReceivableAccount
        {
            Id = Guid.NewGuid(),
            MerchantId = Guid.NewGuid(),
            Status = "Open",
            NextSequence = 1,
            OpenedAt = now,
            UpdatedAt = now,
            OpenedBy = Guid.NewGuid()
        };
        var draft = new MerchantAccountCollectionDraft
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Account = account,
            Amount = 50m,
            PaymentMethod = "CashHandToHand",
            Status = "Draft",
            DraftedBy = Guid.NewGuid(),
            DraftedAt = now
        };
        payments.AddRange(account, draft);
        await payments.SaveChangesAsync();

        await using var transaction = await payments.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<PostgresException>(() => payments.Database.ExecuteSqlInterpolatedAsync(
            $"update payments.merchant_account_collection_drafts set scope = 'DirectOperation' where \"Id\" = {draft.Id}"));
        await transaction.RollbackAsync();
    }

    [PostgreSqlIntegrationFact]
    public async Task MerchantCollectionApproveAndReject_RunAgainstMigratedPostgreSqlSchema()
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var account = new MerchantReceivableAccount
        {
            Id = Guid.NewGuid(),
            MerchantId = Guid.NewGuid(),
            Status = "Open",
            NextSequence = 1,
            OpenedAt = now,
            UpdatedAt = now,
            OpenedBy = Guid.NewGuid()
        };
        var reviewerId = Guid.NewGuid();
        var approveDraft = NewCollectionDraft(account, now, "BankTransfer", "BANK-APPROVE-001");
        var rejectDraft = NewCollectionDraft(account, now, "CashHandToHand", null);
        var financeAccount = new FinanceAccount
        {
            Id = Guid.NewGuid(),
            Name = $"Test Bank {Guid.NewGuid():N}",
            Type = FinanceLedgerService.BankAccount,
            IsActive = true,
            CreatedBy = reviewerId,
            CreatedAt = now
        };
        approveDraft.FinanceAccountId = financeAccount.Id;
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var identity = CreateIdentityContext(connection);
            await using var payments = CreatePaymentsContext(connection);
            await using var finance = CreateFinanceContext(connection);
            identity.Users.Add(new User
            {
                Id = reviewerId,
                Username = $"reviewer-{reviewerId:N}",
                PasswordHash = "test-only",
                FullName = "Payment Reviewer",
                Role = LenseeRoles.Admin,
                IsActive = true,
                CreatedAt = now
            });
            await identity.SaveChangesAsync();
            payments.AddRange(account, approveDraft, rejectDraft);
            finance.FinanceAccounts.Add(financeAccount);
            await payments.SaveChangesAsync();
            await finance.SaveChangesAsync();
        }

        await using var factory = new PostgresApplicationFactory(_postgres.GetConnectionString());
        using var client = factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, reviewerId, LenseePermissions.PaymentsApprove);
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var approved = await client.PostAsync($"/api/v1/payments/collections/{approveDraft.Id}/approve", null);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var rejected = await client.PostAsJsonAsync(
            $"/api/v1/payments/merchant-account-collections/{rejectDraft.Id}/reject",
            new { reason = "Duplicate bank notice" });

        Console.WriteLine($"APPROVE_RESPONSE {approved.StatusCode}: {await approved.Content.ReadAsStringAsync()}");

        Assert.True(approved.StatusCode == System.Net.HttpStatusCode.OK, $"Approval response {approved.StatusCode}: {await approved.Content.ReadAsStringAsync()}");
        Assert.Equal(System.Net.HttpStatusCode.OK, rejected.StatusCode);
        await using var verificationConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await verificationConnection.OpenAsync();
        await using var verification = CreatePaymentsContext(verificationConnection);
        Assert.Equal("Confirmed", await verification.MerchantAccountCollectionDrafts.Where(value => value.Id == approveDraft.Id).Select(value => value.Status).SingleAsync());
        Assert.Equal("Rejected", await verification.MerchantAccountCollectionDrafts.Where(value => value.Id == rejectDraft.Id).Select(value => value.Status).SingleAsync());
        Assert.Single(await verification.MerchantAccountEntries.Where(value => value.SourceId == approveDraft.Id && value.EntryType == "Collection").ToListAsync());
    }

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
                (@id, @operation_id, @merchant_id, 100, 0, 0, null, 'PendingAccountant', @user_id, now(), now(), false);
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
                (@id, @operation_id, @merchant_id, 100, 60, 60, 'CashHandToHand', 'PendingAccountant', @user_id, now(), now(), false);
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("operation_id", Guid.NewGuid());
        command.Parameters.AddWithValue("merchant_id", Guid.NewGuid());
        command.Parameters.AddWithValue("user_id", Guid.NewGuid());

        await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task FinanceInflow_IsIdempotentForTheSameMerchantCollectionSource()
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var sourceId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var finance = CreateFinanceContext(connection);
        var account = new FinanceAccount { Id = Guid.NewGuid(), Name = "Idempotency cash", Type = FinanceLedgerService.CashOnHand, IsActive = true, CreatedBy = actorId, CreatedAt = now };
        finance.FinanceAccounts.Add(account);
        await finance.SaveChangesAsync();
        var service = new FinanceLedgerService(finance, new IntegrationClock(now));
        await service.PostMovementAsync("MerchantCollection", sourceId, "MerchantAccount", "CashHandToHand", 100m, account.Id, null, "MerchantCollection", FinanceLedgerService.Credit, actorId);
        await service.PostMovementAsync("MerchantCollection", sourceId, "MerchantAccount", "CashHandToHand", 100m, account.Id, null, "MerchantCollection", FinanceLedgerService.Credit, actorId);
        Assert.Single(await finance.FinanceLedgerEntries.Where(value => value.SourceType == "MerchantCollection" && value.SourceId == sourceId).ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task OpeningCorrection_BelowCollectedAmount_PreservesCollectionAndCreatesMerchantCredit()
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var merchantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var payments = CreatePaymentsContext(connection);
        await using var shared = CreateSharedContext(connection);
        await using var operations = CreateOperationsContext(connection);
        await using var finance = CreateFinanceContext(connection);
        var service = new MerchantAccountService(payments, shared, operations);
        var original = new MerchantOpeningBalanceCharge { Id = Guid.NewGuid(), MerchantId = merchantId, Amount = 10000m, AsOfDate = DateOnly.FromDateTime(now), Description = "Historical debt", Status = "PendingReview", CreatedBy = actorId, CreatedAt = now, CorrelationId = Guid.NewGuid().ToString("N") };
        payments.MerchantOpeningBalanceCharges.Add(original);
        await payments.SaveChangesAsync();
        await service.PostOpeningBalanceAsync(original, actorId, now, CancellationToken.None);
        await service.PostCollectionAsync(merchantId, Guid.NewGuid(), 8000m, "CashHandToHand", null, actorId, now, null, "Historical collection", CancellationToken.None);
        var replacement = await service.CorrectOpeningBalanceAsync(original, 6000m, DateOnly.FromDateTime(now), "Corrected historical debt", actorId, now.AddMinutes(1), CancellationToken.None);
        var effective = await payments.EffectiveAllocations().Include(value => value.Entry).Where(value => value.Obligation.SourceId == replacement.Id).ToListAsync();
        var entries = await payments.MerchantAccountEntries.Where(value => value.Account.MerchantId == merchantId).ToListAsync();
        Assert.Equal("Corrected", original.Status);
        Assert.Equal(6000m, replacement.Amount);
        Assert.Single(effective);
        Assert.Equal(6000m, effective.Single().Amount);
        Assert.Equal(-2000m, entries.Sum(value => value.DebitAmount - value.CreditAmount));
        Assert.Empty(await finance.FinanceLedgerEntries.ToListAsync());
        Assert.Single(await payments.MerchantAllocationReconciliations.ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task OpeningCorrection_IncreasedAmount_PreservesPartialSettlement()
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var merchantId = Guid.NewGuid(); var actorId = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString()); await connection.OpenAsync();
        await using var payments = CreatePaymentsContext(connection); await using var shared = CreateSharedContext(connection); await using var operations = CreateOperationsContext(connection);
        var service = new MerchantAccountService(payments, shared, operations);
        var original = new MerchantOpeningBalanceCharge { Id = Guid.NewGuid(), MerchantId = merchantId, Amount = 10000m, AsOfDate = DateOnly.FromDateTime(now), Description = "Historical debt", Status = "PendingReview", CreatedBy = actorId, CreatedAt = now, CorrelationId = Guid.NewGuid().ToString("N") };
        payments.MerchantOpeningBalanceCharges.Add(original); await payments.SaveChangesAsync();
        await service.PostOpeningBalanceAsync(original, actorId, now, CancellationToken.None);
        await service.PostCollectionAsync(merchantId, Guid.NewGuid(), 4000m, "CashHandToHand", null, actorId, now, null, null, CancellationToken.None);
        var replacement = await service.CorrectOpeningBalanceAsync(original, 12000m, DateOnly.FromDateTime(now), "Increased historical debt", actorId, now.AddMinutes(1), CancellationToken.None);
        var replacementObligation = await payments.MerchantOperationObligations.SingleAsync(value => value.SourceId == replacement.Id);
        Assert.Equal(12000m, replacement.Amount);
        Assert.Equal(4000m, await payments.EffectiveAllocations().Where(value => value.ObligationId == replacementObligation.Id).SumAsync(value => value.Amount));
        Assert.Single(await payments.MerchantAllocationReconciliations.ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task ObligationCapacity_ConcurrentCollections_NeverOverAllocate()
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified); var merchantId = Guid.NewGuid(); var actor = Guid.NewGuid();
        Guid obligationId;
        await using (var setup = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await setup.OpenAsync(); await using var payments = CreatePaymentsContext(setup); await using var shared = CreateSharedContext(setup); await using var operations = CreateOperationsContext(setup);
            var service = new MerchantAccountService(payments, shared, operations);
            obligationId = (await service.PostSaleAsync(merchantId, Guid.NewGuid(), 100m, actor, now, null, CancellationToken.None)).Id;
        }
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Attempt(Guid paymentId)
        {
            await gate.Task; await using var connection = new NpgsqlConnection(_postgres.GetConnectionString()); await connection.OpenAsync();
            await using var payments = CreatePaymentsContext(connection); await using var shared = CreateSharedContext(connection); await using var operations = CreateOperationsContext(connection);
            var service = new MerchantAccountService(payments, shared, operations);
            await service.PostCollectionAsync(merchantId, paymentId, 100m, "CashHandToHand", null, actor, now, [new MerchantAllocationInput(obligationId, 100m)], null, CancellationToken.None);
        }
        var first = Attempt(Guid.NewGuid()); var second = Attempt(Guid.NewGuid()); gate.SetResult();
        await Task.WhenAll(first.ContinueWith(_ => { }), second.ContinueWith(_ => { }));
        await using var verifyConnection = new NpgsqlConnection(_postgres.GetConnectionString()); await verifyConnection.OpenAsync(); await using var verify = CreatePaymentsContext(verifyConnection);
        Assert.True((await verify.EffectiveAllocations().Where(value => value.ObligationId == obligationId).SumAsync(value => (decimal?)value.Amount) ?? 0m) <= 100m);
    }

    [PostgreSqlIntegrationFact]
    public async Task OpeningCreate_SameIdempotencyKey_CreatesOneCharge()
    {
        var merchantId = Guid.NewGuid(); var actorId = Guid.NewGuid(); var key = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync(); await using var crm = CreateCrmContext(connection);
            crm.Merchants.Add(new Merchant { Id = merchantId, BusinessName = "Opening race", ContactPersonName = "Opening", PhoneNumbers = [], BusinessType = "Wholesale", Status = "Active", IsDeleted = false, CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified), UpdatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified) });
            await crm.SaveChangesAsync();
        }
        await using var factory = new PostgresApplicationFactory(_postgres.GetConnectionString());
        await factory.SeedUserAsync(actorId);
        HttpClient Client() { var c = factory.CreateClient(); c.AuthorizeAs(LenseeRoles.Admin, actorId, LenseePermissions.PaymentsDraft); c.DefaultRequestHeaders.Add("Idempotency-Key", key.ToString()); return c; }
        var body = new OpeningBalanceRequest(100m, new DateOnly(2026, 1, 1), "Idempotent opening");
        using var firstClient = Client(); using var secondClient = Client();
        await Task.WhenAll(firstClient.PostAsJsonAsync($"/api/v1/payments/merchant-accounts/{merchantId}/opening-balances", body), secondClient.PostAsJsonAsync($"/api/v1/payments/merchant-accounts/{merchantId}/opening-balances", body));
        await using var verifyConnection = new NpgsqlConnection(_postgres.GetConnectionString()); await verifyConnection.OpenAsync(); await using var verify = CreatePaymentsContext(verifyConnection);
        Assert.Single(await verify.MerchantOpeningBalanceCharges.Where(value => value.MerchantId == merchantId).ToListAsync());
        Assert.Single(await verify.PaymentIdempotencyKeys.Where(value => value.Key == key).ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task CollectionCapacity_ConcurrentSameCollection_NeverOverAllocates()
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified); var merchantId = Guid.NewGuid(); var actor = Guid.NewGuid();
        Guid obligationId;
        await using (var setup = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await setup.OpenAsync(); await using var payments = CreatePaymentsContext(setup); await using var shared = CreateSharedContext(setup); await using var operations = CreateOperationsContext(setup);
            obligationId = (await new MerchantAccountService(payments, shared, operations).PostSaleAsync(merchantId, Guid.NewGuid(), 100m, actor, now, null, CancellationToken.None)).Id;
        }
        var paymentId = Guid.NewGuid(); var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Attempt()
        {
            await gate.Task; await using var connection = new NpgsqlConnection(_postgres.GetConnectionString()); await connection.OpenAsync();
            await using var payments = CreatePaymentsContext(connection); await using var shared = CreateSharedContext(connection); await using var operations = CreateOperationsContext(connection);
            await using var transaction = await payments.Database.BeginTransactionAsync();
            await new MerchantAccountService(payments, shared, operations).PostCollectionAsync(merchantId, paymentId, 100m, "CashHandToHand", null, actor, now, [new MerchantAllocationInput(obligationId, 100m)], null, CancellationToken.None);
            await transaction.CommitAsync();
        }
        var first = Attempt(); var second = Attempt(); gate.SetResult();
        var outcomes = await Task.WhenAll(first.ContinueWith(task => task.Exception), second.ContinueWith(task => task.Exception));
        Assert.All(outcomes, error => Assert.Null(error));
        await using var verificationConnection = new NpgsqlConnection(_postgres.GetConnectionString()); await verificationConnection.OpenAsync(); await using var verify = CreatePaymentsContext(verificationConnection);
        var collection = await verify.MerchantAccountEntries.SingleAsync(value => value.SourceId == paymentId && value.EntryType == "Collection");
        var allocated = await verify.EffectiveAllocations().Where(value => value.EntryId == collection.Id).SumAsync(value => (decimal?)value.Amount) ?? 0m;
        Assert.Equal(100m, collection.CreditAmount); Assert.Equal(100m, allocated);
    }

    [PostgreSqlIntegrationFact]
    public async Task OpeningApprove_SameIdempotencyKey_PostsOnce()
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified); var merchantId = Guid.NewGuid(); var actor = Guid.NewGuid(); var key = Guid.NewGuid();
        var charge = new MerchantOpeningBalanceCharge { Id = Guid.NewGuid(), MerchantId = merchantId, Amount = 100m, AsOfDate = DateOnly.FromDateTime(now), Description = "Approve race", Status = "PendingReview", CreatedBy = actor, CreatedAt = now, CorrelationId = Guid.NewGuid().ToString("N") };
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString())) { await connection.OpenAsync(); await using var payments = CreatePaymentsContext(connection); payments.MerchantOpeningBalanceCharges.Add(charge); await payments.SaveChangesAsync(); }
        await using var factory = new PostgresApplicationFactory(_postgres.GetConnectionString()); await factory.SeedUserAsync(actor);
        HttpClient Client() { var c = factory.CreateClient(); c.AuthorizeAs(LenseeRoles.Admin, actor, LenseePermissions.PaymentsApprove); c.DefaultRequestHeaders.Add("Idempotency-Key", key.ToString()); return c; }
        using var a = Client(); using var b = Client(); var results = await Task.WhenAll(a.PostAsync($"/api/v1/payments/opening-balances/{charge.Id}/approve", null), b.PostAsync($"/api/v1/payments/opening-balances/{charge.Id}/approve", null));
        Assert.All(results, result => Assert.True(result.StatusCode is System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.Conflict));
        await using var verifyConnection = new NpgsqlConnection(_postgres.GetConnectionString()); await verifyConnection.OpenAsync(); await using var verify = CreatePaymentsContext(verifyConnection);
        Assert.Equal("Posted", await verify.MerchantOpeningBalanceCharges.Where(value => value.Id == charge.Id).Select(value => value.Status).SingleAsync());
        Assert.Single(await verify.MerchantOperationObligations.Where(value => value.SourceId == charge.Id).ToListAsync());
        Assert.Single(await verify.MerchantAccountEntries.Where(value => value.SourceId == charge.Id && value.EntryType == "OpeningBalanceCharge").ToListAsync());
        Assert.Single(await verify.PaymentIdempotencyKeys.Where(value => value.Key == key).ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task OpeningCorrection_SameIdempotencyKey_CorrectsOnce()
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified); var merchantId = Guid.NewGuid(); var actor = Guid.NewGuid(); var key = Guid.NewGuid();
        var original = await CreatePostedOpeningAsync(merchantId, 100m, actor, now);
        await using (var setup = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await setup.OpenAsync(); await using var payments = CreatePaymentsContext(setup); await using var shared = CreateSharedContext(setup); await using var operations = CreateOperationsContext(setup);
            await new MerchantAccountService(payments, shared, operations).PostCollectionAsync(merchantId, Guid.NewGuid(), 40m, "CashHandToHand", null, actor, now, null, "Historical settlement", CancellationToken.None);
        }
        await using var factory = new PostgresApplicationFactory(_postgres.GetConnectionString()); await factory.SeedUserAsync(actor);
        HttpClient Client() { var c = factory.CreateClient(); c.AuthorizeAs(LenseeRoles.Admin, actor, LenseePermissions.PaymentsAdjustmentsRequest); c.DefaultRequestHeaders.Add("Idempotency-Key", key.ToString()); return c; }
        var body = new OpeningBalanceCorrectionRequest(80m, DateOnly.FromDateTime(now), "Replay correction"); using var a = Client(); using var b = Client();
        var results = await Task.WhenAll(a.PostAsJsonAsync($"/api/v1/payments/opening-balances/{original.Id}/correct", body), b.PostAsJsonAsync($"/api/v1/payments/opening-balances/{original.Id}/correct", body));
        Assert.All(results, result => Assert.True(result.StatusCode is System.Net.HttpStatusCode.OK or System.Net.HttpStatusCode.Conflict));
        await using var verifyConnection = new NpgsqlConnection(_postgres.GetConnectionString()); await verifyConnection.OpenAsync(); await using var verify = CreatePaymentsContext(verifyConnection);
        Assert.Single(await verify.MerchantOpeningBalanceCharges.Where(value => value.ReversesChargeId == original.Id).ToListAsync());
        Assert.Single(await verify.MerchantAccountEntries.Where(value => value.SourceId == original.Id && value.EntryType == "OpeningBalanceReversal").ToListAsync());
        Assert.Single(await verify.MerchantAllocationReconciliations.ToListAsync());
        Assert.Single(await verify.EffectiveAllocations().Where(value => value.Obligation.SourceId != original.Id).ToListAsync());
        Assert.Single(await verify.PaymentIdempotencyKeys.Where(value => value.Key == key).ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    public Task OpeningCorrection_PartialSettlement_PreservesImmutableHistory() =>
        AssertOpeningCorrectionSettlementAsync(40m, 100m, 40m, 60m);

    [PostgreSqlIntegrationFact]
    public Task OpeningCorrection_FullSettlement_PreservesImmutableHistory() =>
        AssertOpeningCorrectionSettlementAsync(100m, 100m, 100m, 0m);

    private async Task AssertOpeningCorrectionSettlementAsync(decimal collected, decimal corrected, decimal effectiveExpected, decimal remainingExpected)
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified); var merchantId = Guid.NewGuid(); var actor = Guid.NewGuid();
        var original = await CreatePostedOpeningAsync(merchantId, 100m, actor, now);
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString()); await connection.OpenAsync(); await using var payments = CreatePaymentsContext(connection); await using var shared = CreateSharedContext(connection); await using var operations = CreateOperationsContext(connection);
        var service = new MerchantAccountService(payments, shared, operations);
        await service.PostCollectionAsync(merchantId, Guid.NewGuid(), collected, "CashHandToHand", null, actor, now, null, null, CancellationToken.None);
        var replacement = await service.CorrectOpeningBalanceAsync(original, corrected, DateOnly.FromDateTime(now), "Corrected", actor, now.AddMinutes(1), CancellationToken.None);
        var obligation = await payments.MerchantOperationObligations.SingleAsync(value => value.SourceId == replacement.Id);
        var effective = await payments.EffectiveAllocations().Where(value => value.ObligationId == obligation.Id).SumAsync(value => (decimal?)value.Amount) ?? 0m;
        Assert.Equal("Corrected", (await payments.MerchantOpeningBalanceCharges.SingleAsync(value => value.Id == original.Id)).Status);
        Assert.Equal(effectiveExpected, effective); Assert.Equal(remainingExpected, corrected - effective); Assert.Equal(remainingExpected == 0m ? "Settled" : "Open", obligation.Status);
        Assert.Single(await payments.MerchantAllocationReconciliations.ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task ReconciledCapacity_UsesOnlyEffectiveAllocations()
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified); var merchantId = Guid.NewGuid(); var actor = Guid.NewGuid();
        var original = await CreatePostedOpeningAsync(merchantId, 100m, actor, now);
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString()); await connection.OpenAsync(); await using var payments = CreatePaymentsContext(connection); await using var shared = CreateSharedContext(connection); await using var operations = CreateOperationsContext(connection);
        var service = new MerchantAccountService(payments, shared, operations); await service.PostCollectionAsync(merchantId, Guid.NewGuid(), 40m, "CashHandToHand", null, actor, now, null, null, CancellationToken.None);
        var replacement = await service.CorrectOpeningBalanceAsync(original, 100m, DateOnly.FromDateTime(now), "Reconcile capacity", actor, now.AddMinutes(1), CancellationToken.None);
        var replacementObligation = await payments.MerchantOperationObligations.SingleAsync(value => value.SourceId == replacement.Id);
        await service.PostCollectionAsync(merchantId, Guid.NewGuid(), 60m, "CashHandToHand", null, actor, now.AddMinutes(2), [new MerchantAllocationInput(replacementObligation.Id, 60m)], null, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.PostCollectionAsync(merchantId, Guid.NewGuid(), 1m, "CashHandToHand", null, actor, now.AddMinutes(3), [new MerchantAllocationInput(replacementObligation.Id, 1m)], null, CancellationToken.None));
        Assert.Equal(100m, await payments.EffectiveAllocations().Where(value => value.ObligationId == replacementObligation.Id).SumAsync(value => value.Amount));
        Assert.Single(await payments.MerchantAllocationReconciliations.ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task OpeningCorrection_ConcurrentSettlement_PreservesCollectionAndReconciles()
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified); var merchantId = Guid.NewGuid(); var actor = Guid.NewGuid();
        var original = await CreatePostedOpeningAsync(merchantId, 100m, actor, now); var collectionSource = Guid.NewGuid();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task Correct()
        {
            await gate.Task; await using var connection = new NpgsqlConnection(_postgres.GetConnectionString()); await connection.OpenAsync(); await using var payments = CreatePaymentsContext(connection); await using var shared = CreateSharedContext(connection); await using var operations = CreateOperationsContext(connection);
            var charge = await payments.MerchantOpeningBalanceCharges.SingleAsync(value => value.Id == original.Id);
            await new MerchantAccountService(payments, shared, operations).CorrectOpeningBalanceAsync(charge, 80m, DateOnly.FromDateTime(now), "Concurrent correction", actor, now.AddMinutes(1), CancellationToken.None);
        }
        async Task Collect()
        {
            await gate.Task; await using var connection = new NpgsqlConnection(_postgres.GetConnectionString()); await connection.OpenAsync(); await using var payments = CreatePaymentsContext(connection); await using var shared = CreateSharedContext(connection); await using var operations = CreateOperationsContext(connection);
            await new MerchantAccountService(payments, shared, operations).PostCollectionAsync(merchantId, collectionSource, 50m, "CashHandToHand", null, actor, now.AddMinutes(1), null, "Concurrent settlement", CancellationToken.None);
        }
        var correction = Correct(); var collection = Collect(); gate.SetResult();
        await Task.WhenAll(correction.ContinueWith(_ => { }), collection.ContinueWith(_ => { }));
        await using var verifyConnection = new NpgsqlConnection(_postgres.GetConnectionString()); await verifyConnection.OpenAsync(); await using var verify = CreatePaymentsContext(verifyConnection);
        if (!await verify.MerchantAccountEntries.AnyAsync(value => value.SourceId == collectionSource && value.EntryType == "Collection"))
        {
            await using var retryShared = CreateSharedContext(verifyConnection); await using var retryOperations = CreateOperationsContext(verifyConnection);
            await new MerchantAccountService(verify, retryShared, retryOperations).PostCollectionAsync(merchantId, collectionSource, 50m, "CashHandToHand", null, actor, now.AddMinutes(2), null, "Collection retry", CancellationToken.None);
        }
        var replacement = await verify.MerchantOpeningBalanceCharges.SingleAsync(value => value.ReversesChargeId == original.Id);
        var replacementObligation = await verify.MerchantOperationObligations.SingleAsync(value => value.SourceId == replacement.Id);
        Assert.Empty(await verify.EffectiveAllocations().Where(value => value.Obligation.SourceId == original.Id).ToListAsync());
        Assert.True((await verify.EffectiveAllocations().Where(value => value.ObligationId == replacementObligation.Id).SumAsync(value => (decimal?)value.Amount) ?? 0m) <= replacementObligation.OriginalAmount);
        Assert.Single(await verify.MerchantAccountEntries.Where(value => value.SourceId == collectionSource && value.EntryType == "Collection").ToListAsync());
        Assert.All(await verify.MerchantAllocationReconciliations.ToListAsync(), value => Assert.NotEqual(value.SourceAllocationId, value.ReplacementAllocationId));
    }

    [PostgreSqlIntegrationFact]
    public async Task ImmediateMerchantSale_RetryProducesOneChargeObligationCollectionAllocationAndFinanceInflow()
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified); var merchantId = Guid.NewGuid(); var actor = Guid.NewGuid(); var operationId = Guid.NewGuid();
        async Task PostSale()
        {
            await using var connection = new NpgsqlConnection(_postgres.GetConnectionString()); await connection.OpenAsync(); await using var payments = CreatePaymentsContext(connection); await using var shared = CreateSharedContext(connection); await using var operations = CreateOperationsContext(connection);
            await new MerchantAccountService(payments, shared, operations).PostSaleAsync(merchantId, operationId, 100m, actor, now, "Immediate merchant sale", CancellationToken.None);
        }
        await PostSale(); await PostSale();
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString()); await connection.OpenAsync(); await using var payments = CreatePaymentsContext(connection); await using var shared = CreateSharedContext(connection); await using var operations = CreateOperationsContext(connection); await using var finance = CreateFinanceContext(connection);
        var obligation = await payments.MerchantOperationObligations.SingleAsync(value => value.OperationId == operationId); var paymentId = Guid.NewGuid();
        var merchant = new MerchantAccountService(payments, shared, operations);
        await merchant.PostCollectionAsync(merchantId, paymentId, 100m, "CashHandToHand", null, actor, now, [new MerchantAllocationInput(obligation.Id, 100m)], "Immediate settlement", CancellationToken.None);
        await merchant.PostCollectionAsync(merchantId, paymentId, 100m, "CashHandToHand", null, actor, now, [new MerchantAllocationInput(obligation.Id, 100m)], "Immediate settlement retry", CancellationToken.None);
        var account = new FinanceAccount { Id = Guid.NewGuid(), Name = "Immediate retry cash", Type = FinanceLedgerService.CashOnHand, IsActive = true, CreatedBy = actor, CreatedAt = now }; finance.FinanceAccounts.Add(account); await finance.SaveChangesAsync();
        var financeService = new FinanceLedgerService(finance, new IntegrationClock(now));
        await financeService.PostMovementAsync("MerchantCollection", paymentId, "MerchantAccount", "CashHandToHand", 100m, account.Id, null, "Immediate merchant settlement", FinanceLedgerService.Credit, actor);
        await financeService.PostMovementAsync("MerchantCollection", paymentId, "MerchantAccount", "CashHandToHand", 100m, account.Id, null, "Immediate merchant settlement", FinanceLedgerService.Credit, actor);
        Assert.Single(await payments.MerchantAccountEntries.Where(value => value.SourceType == "OperationSale" && value.SourceId == operationId && value.EntryType == "SaleCharge").ToListAsync());
        Assert.Single(await payments.MerchantOperationObligations.Where(value => value.OperationId == operationId).ToListAsync());
        Assert.Single(await payments.MerchantAccountEntries.Where(value => value.SourceId == paymentId && value.EntryType == "Collection").ToListAsync());
        Assert.Single(await payments.EffectiveAllocations().Where(value => value.ObligationId == obligation.Id).ToListAsync());
        Assert.Single(await finance.FinanceLedgerEntries.Where(value => value.SourceType == "MerchantCollection" && value.SourceId == paymentId).ToListAsync());
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
        Assert.Contains("20260830173958_AddConcurrencyAndStocktakeBaseline", migrations);
        Assert.Contains("20260830174008_AddFinancialAdjustmentRefundLineage", migrations);
    }

    [PostgreSqlIntegrationFact]
    public async Task CatalogMutationTransaction_RollsBackCatalogAuditAndOutboxTogether()
    {
        var brandId = Guid.NewGuid();
        var auditId = Guid.NewGuid();
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
                identity.AuditLogs.Add(new AuditLog
                {
                    Id = auditId,
                    EntityType = "Brand",
                    EntityId = brandId,
                    Action = "Create",
                    ActorType = "Integration",
                    ActorName = "Rollback proof",
                    CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                });
                await identity.SaveChangesAsync();
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
        await using var verificationIdentity = CreateIdentityContext(verificationConnection);
        await using var verificationShared = CreateSharedContext(verificationConnection);
        Assert.False(await verificationCatalog.Brands.AnyAsync(brand => brand.Id == brandId));
        Assert.False(await verificationIdentity.AuditLogs.AnyAsync(audit => audit.Id == auditId));
        Assert.DoesNotContain(await verificationShared.OutboxMessages.ToListAsync(), message => message.EventType == "Catalog.BrandCreated");
    }

    [PostgreSqlIntegrationFact]
    public async Task OperationMutationAuditTransaction_RollsBackDomainVersionAndAuditOnInjectedFailure()
    {
        var operationId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var auditId = Guid.NewGuid();
        await using (var setupConnection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await setupConnection.OpenAsync();
            await InsertOperationAsync(setupConnection, operationId, "Standard", null, "Draft");
        }

        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var operations = CreateOperationsContext(connection);
            await using var identity = CreateIdentityContext(connection);

            await Assert.ThrowsAsync<InvalidOperationException>(() => SharedDbTransaction.ExecuteAsync(
                operations,
                async () =>
                {
                    var operation = await operations.OperationLogs.SingleAsync(value => value.Id == operationId);
                    operation.Status = "Reserved";
                    operations.OperationVersions.Add(new OperationVersion
                    {
                        Id = versionId,
                        OperationId = operationId,
                        VersionNumber = 1,
                        SnapshotData = "{}",
                        Reason = "Injected failure proof",
                        EditedBy = Guid.NewGuid(),
                        EditedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                    });
                    await operations.SaveChangesAsync();

                    identity.AuditLogs.Add(new AuditLog
                    {
                        Id = auditId,
                        EntityType = "Operation",
                        EntityId = operationId,
                        Action = "Confirm",
                        ActorType = "Integration",
                        ActorName = "Rollback proof",
                        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                    });
                    await identity.SaveChangesAsync();
                    throw new InvalidOperationException("Injected operation audit failure.");
                },
                CancellationToken.None,
                identity));
        }

        await using var verificationConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await verificationConnection.OpenAsync();
        await using var verificationOperations = CreateOperationsContext(verificationConnection);
        await using var verificationIdentity = CreateIdentityContext(verificationConnection);
        Assert.Equal("Draft", await verificationOperations.OperationLogs.Where(value => value.Id == operationId).Select(value => value.Status).SingleAsync());
        Assert.False(await verificationOperations.OperationVersions.AnyAsync(value => value.Id == versionId));
        Assert.False(await verificationIdentity.AuditLogs.AnyAsync(value => value.Id == auditId));
    }

    [PostgreSqlIntegrationFact]
    public async Task OperationMutationAuditTransaction_CommitsExactlyOneAuditWithDomainState()
    {
        var operationId = Guid.NewGuid();
        await using (var setupConnection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await setupConnection.OpenAsync();
            await InsertOperationAsync(setupConnection, operationId, "Standard", null, "Draft");
        }

        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var operations = CreateOperationsContext(connection);
            await using var identity = CreateIdentityContext(connection);

            await SharedDbTransaction.ExecuteAsync(
                operations,
                async () =>
                {
                    var operation = await operations.OperationLogs.SingleAsync(value => value.Id == operationId);
                    operation.Status = "Reserved";
                    await operations.SaveChangesAsync();
                    identity.AuditLogs.Add(new AuditLog
                    {
                        Id = Guid.NewGuid(),
                        EntityType = "Operation",
                        EntityId = operationId,
                        Action = "Confirm",
                        ActorType = "Integration",
                        ActorName = "Commit proof",
                        CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
                    });
                    await identity.SaveChangesAsync();
                },
                CancellationToken.None,
                identity);
        }

        await using var verificationConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await verificationConnection.OpenAsync();
        await using var verificationOperations = CreateOperationsContext(verificationConnection);
        await using var verificationIdentity = CreateIdentityContext(verificationConnection);
        Assert.Equal("Reserved", await verificationOperations.OperationLogs.Where(value => value.Id == operationId).Select(value => value.Status).SingleAsync());
        Assert.Single(await verificationIdentity.AuditLogs.Where(value => value.EntityType == "Operation" && value.EntityId == operationId).ToListAsync());
    }

    private static SharedDbContext CreateSharedContext(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<SharedDbContext>().UseNpgsql(connection).Options);

    private static CatalogDbContext CreateCatalogContext(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<CatalogDbContext>().UseNpgsql(connection).Options);

    private static IdentityDbContext CreateIdentityContext(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connection).Options);

    private static InventoryDbContext CreateInventoryContext(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<InventoryDbContext>().UseNpgsql(connection).Options);

    private static PaymentsDbContext CreatePaymentsContext(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<PaymentsDbContext>().UseNpgsql(connection).Options);

    private static FinanceDbContext CreateFinanceContext(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<FinanceDbContext>().UseNpgsql(connection).Options);

    [PostgreSqlIntegrationFact]
    public async Task FinanceExpense_ConcurrentSameSource_PostsOneOutflowAndExpectedBalanceReconciles()
    {
        var actor = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        await using (var setup = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await setup.OpenAsync(); await using var finance = CreateFinanceContext(setup);
            finance.FinanceAccounts.Add(new FinanceAccount { Id = accountId, Name = $"Expense cash {accountId:N}", Type = FinanceLedgerService.CashOnHand, IsActive = true, CreatedBy = actor, CreatedAt = now });
            await finance.SaveChangesAsync();
            await new FinanceLedgerService(finance, new IntegrationClock(now)).PostMovementAsync("FinanceOpeningBalance", Guid.NewGuid(), "Finance",
                "FinanceOpeningBalance", 100m, accountId, null, "TreasuryOpeningBalance", FinanceLedgerService.Credit, actor, DateOnly.FromDateTime(now));
        }
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task PostAsync()
        {
            await gate.Task;
            await using var connection = new NpgsqlConnection(_postgres.GetConnectionString()); await connection.OpenAsync();
            await using var finance = CreateFinanceContext(connection);
            await new FinanceLedgerService(finance, new IntegrationClock(now)).PostMovementAsync("FinanceExpense", sourceId, "Finance", "CashHandToHand", 25m, accountId, null, "OperatingExpense", FinanceLedgerService.Debit, actor, DateOnly.FromDateTime(now));
        }
        var first = PostAsync(); var second = PostAsync(); gate.SetResult();
        await Task.WhenAll(first, second);
        await using var verifyConnection = new NpgsqlConnection(_postgres.GetConnectionString()); await verifyConnection.OpenAsync(); await using var verify = CreateFinanceContext(verifyConnection);
        Assert.Single(await verify.FinanceLedgerEntries.Where(value => value.SourceType == "FinanceExpense" && value.SourceId == sourceId).ToListAsync());
        var expected = await verify.FinanceLedgerEntries.Where(value => value.FinanceAccountId == accountId && value.Status == "Posted").SumAsync(value => value.Direction == FinanceLedgerService.Credit ? value.Amount : -value.Amount);
        Assert.Equal(75m, expected);
    }

    [PostgreSqlIntegrationFact]
    public async Task TreasuryOpeningBalance_ReplayAndExternalReferenceRace_KeepCanonicalLedgerUnique()
    {
        var actor = Guid.NewGuid(); var accountId = Guid.NewGuid(); var openingId = Guid.NewGuid(); var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        await using (var setup = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await setup.OpenAsync(); await using var finance = CreateFinanceContext(setup);
            finance.FinanceAccounts.Add(new FinanceAccount { Id = accountId, Name = $"Opening cash {accountId:N}", Type = FinanceLedgerService.CashOnHand, IsActive = true, CreatedBy = actor, CreatedAt = now });
            await finance.SaveChangesAsync();
        }
        async Task<FinanceLedgerEntry> OpeningAsync()
        {
            await using var connection = new NpgsqlConnection(_postgres.GetConnectionString()); await connection.OpenAsync(); await using var finance = CreateFinanceContext(connection);
            return await new FinanceLedgerService(finance, new IntegrationClock(now)).PostMovementAsync("FinanceOpeningBalance", openingId, "Finance", "FinanceOpeningBalance", 100m, accountId, null, "TreasuryOpeningBalance", FinanceLedgerService.Credit, actor, DateOnly.FromDateTime(now));
        }
        var first = await OpeningAsync(); var replay = await OpeningAsync();
        Assert.Equal(first.Id, replay.Id);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task PostSameReferenceAsync(Guid sourceId)
        {
            await gate.Task;
            await using var connection = new NpgsqlConnection(_postgres.GetConnectionString()); await connection.OpenAsync(); await using var finance = CreateFinanceContext(connection);
            await new FinanceLedgerService(finance, new IntegrationClock(now)).PostMovementAsync("FinanceReferenceProbe", sourceId, "Finance", "CashHandToHand", 10m, accountId, "TREASURY-REF-001", "MerchantCollection", FinanceLedgerService.Credit, actor, DateOnly.FromDateTime(now));
        }
        var referenceFirst = PostSameReferenceAsync(Guid.NewGuid()); var referenceSecond = PostSameReferenceAsync(Guid.NewGuid()); gate.SetResult();
        var outcomes = await Task.WhenAll(referenceFirst.ContinueWith(task => task.Exception), referenceSecond.ContinueWith(task => task.Exception));
        Assert.Single(outcomes.Where(value => value is null));
        Assert.Single(outcomes.Where(value => value is not null));
        await using var verifyConnection = new NpgsqlConnection(_postgres.GetConnectionString()); await verifyConnection.OpenAsync(); await using var verify = CreateFinanceContext(verifyConnection);
        Assert.Single(await verify.FinanceLedgerEntries.Where(value => value.SourceType == "FinanceOpeningBalance" && value.SourceId == openingId).ToListAsync());
        Assert.Single(await verify.FinanceLedgerEntries.Where(value => value.ExternalReference == "TREASURY-REF-001").ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task CLevelWithdrawal_RepaymentRetry_PostsOneCanonicalInflow()
    {
        var primaryAdminId = Guid.NewGuid();
        var beneficiaryId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        await using (var setup = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await setup.OpenAsync();
            await using var identity = CreateIdentityContext(setup);
            await using var finance = CreateFinanceContext(setup);
            identity.Users.AddRange(
                new User { Id = primaryAdminId, Username = $"primary-{primaryAdminId:N}", FullName = "Primary", PasswordHash = "test", Role = LenseeRoles.Admin, IsPrimaryAdmin = true, IsActive = true, CreatedAt = now },
                new User { Id = beneficiaryId, Username = $"clevel-{beneficiaryId:N}", FullName = "Beneficiary", PasswordHash = "test", Role = LenseeRoles.CLevel, IsActive = true, CreatedAt = now });
            finance.FinanceAccounts.Add(new FinanceAccount { Id = accountId, Name = $"Withdrawal cash {accountId:N}", Type = FinanceLedgerService.CashOnHand, IsActive = true, CreatedBy = primaryAdminId, CreatedAt = now });
            await identity.SaveChangesAsync();
            await finance.SaveChangesAsync();
            await new FinanceLedgerService(finance, new IntegrationClock(now)).PostMovementAsync("FinanceOpeningBalance", Guid.NewGuid(), "Finance",
                "FinanceOpeningBalance", 500m, accountId, null, "TreasuryOpeningBalance", FinanceLedgerService.Credit, primaryAdminId, DateOnly.FromDateTime(now));
        }

        await using var factory = new PostgresApplicationFactory(_postgres.GetConnectionString());
        using var creator = factory.CreateClient();
        creator.AuthorizeAs(LenseeRoles.Admin, primaryAdminId, LenseePermissions.FinanceWithdrawalCreate, LenseePermissions.FinanceWithdrawalAssign, LenseePermissions.FinanceRead);
        var create = await creator.PostAsJsonAsync("/api/v1/finance/withdrawals", new CLevelWithdrawalRequest(beneficiaryId, accountId, 125m, "CashHandToHand", DateOnly.FromDateTime(now), "Concurrent protected withdrawal", null, null));
        Assert.True(create.IsSuccessStatusCode, await create.Content.ReadAsStringAsync());
        var withdrawal = await create.Content.ReadFromJsonAsync<CLevelWithdrawal>();
        Assert.NotNull(withdrawal);
        Assert.Equal("Posted", withdrawal!.Status);
        var requestId = Guid.NewGuid();
        var payload = new WithdrawalRepaymentRequest(requestId, accountId, 50m, "CashHandToHand", DateOnly.FromDateTime(now), "Partial payback", null);
        var results = await Task.WhenAll(
            creator.PostAsJsonAsync($"/api/v1/finance/withdrawals/{withdrawal.Id}/repayments", payload),
            creator.PostAsJsonAsync($"/api/v1/finance/withdrawals/{withdrawal.Id}/repayments", payload));
        Assert.All(results, response => Assert.True(response.IsSuccessStatusCode));

        await using var verifyConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await verifyConnection.OpenAsync();
        await using var verify = CreateFinanceContext(verifyConnection);
        Assert.Equal("Posted", await verify.CLevelWithdrawals.Where(value => value.Id == withdrawal.Id).Select(value => value.Status).SingleAsync());
        Assert.Single(await verify.FinanceLedgerEntries.Where(value => value.SourceType == "CLevelWithdrawal" && value.SourceId == withdrawal.Id).ToListAsync());
        Assert.Single(await verify.PaymentMovementRegistries.Where(value => value.SourceType == "CLevelWithdrawal" && value.SourceId == withdrawal.Id).ToListAsync());
        var repayment = Assert.Single(await verify.CLevelWithdrawalRepayments.Where(value => value.ClientRequestId == requestId).ToListAsync());
        Assert.Single(await verify.FinanceLedgerEntries.Where(value => value.SourceType == "CLevelWithdrawalRepayment" && value.SourceId == repayment.Id).ToListAsync());

        var correctionId = Guid.NewGuid();
        var correction = new WithdrawalRepaymentCorrectionRequest(correctionId, accountId, 40m, "CashHandToHand",
            DateOnly.FromDateTime(now), "Receipt was overstated", "Corrected payback", null);
        var overpayment = await creator.PostAsJsonAsync($"/api/v1/finance/withdrawals/{withdrawal.Id}/repayments/{repayment.Id}/correct",
            correction with { ClientRequestId = Guid.NewGuid(), Amount = 126m });
        Assert.Equal(409, (int)overpayment.StatusCode);
        var missingNote = await creator.PostAsJsonAsync($"/api/v1/finance/withdrawals/{withdrawal.Id}/repayments/{repayment.Id}/correct",
            correction with { ClientRequestId = Guid.NewGuid(), Reason = "" });
        Assert.Equal(400, (int)missingNote.StatusCode);
        var corrected = await creator.PostAsJsonAsync($"/api/v1/finance/withdrawals/{withdrawal.Id}/repayments/{repayment.Id}/correct", correction);
        Assert.True(corrected.IsSuccessStatusCode, await corrected.Content.ReadAsStringAsync());
        var retried = await creator.PostAsJsonAsync($"/api/v1/finance/withdrawals/{withdrawal.Id}/repayments/{repayment.Id}/correct", correction);
        Assert.True(retried.IsSuccessStatusCode, await retried.Content.ReadAsStringAsync());
        verify.ChangeTracker.Clear();
        var correctionRecord = Assert.Single(await verify.CLevelWithdrawalRepayments.Where(value => value.ClientRequestId == correctionId).ToListAsync());
        Assert.Equal(repayment.Id, correctionRecord.ReversesRepaymentId);
        Assert.Equal(correctionRecord.Id, await verify.CLevelWithdrawalRepayments.Where(value => value.Id == repayment.Id).Select(value => value.ReplacedByRepaymentId).SingleAsync());
        Assert.Single(await verify.FinanceLedgerEntries.Where(value => value.SourceType == "CLevelWithdrawalRepaymentReversal" && value.SourceId == correctionRecord.Id).ToListAsync());
        Assert.Single(await verify.FinanceLedgerEntries.Where(value => value.SourceType == "CLevelWithdrawalRepayment" && value.SourceId == correctionRecord.Id).ToListAsync());
        var repaymentSummary = await creator.GetAsync($"/api/v1/finance/withdrawals/{withdrawal.Id}/repayments");
        Assert.True(repaymentSummary.IsSuccessStatusCode);
        var summary = await repaymentSummary.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(40m, summary.GetProperty("repaid").GetDecimal());
        Assert.Equal(85m, summary.GetProperty("remaining").GetDecimal());
    }

    [PostgreSqlIntegrationFact]
    public async Task FinanceTransfer_FeeIsSeparateExpense_RetryIsIdempotent_AndOverdraftIsRejected()
    {
        var actor = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var destinationId = Guid.NewGuid();
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        await using (var setup = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await setup.OpenAsync();
            await using var identity = CreateIdentityContext(setup);
            identity.Users.Add(new User
            {
                Id = actor,
                Username = $"transfer-admin-{actor:N}",
                FullName = "Transfer admin",
                PasswordHash = "test",
                Role = LenseeRoles.Admin,
                IsActive = true,
                CreatedAt = now
            });
            await identity.SaveChangesAsync();
            await using var finance = CreateFinanceContext(setup);
            finance.FinanceAccounts.AddRange(
                new FinanceAccount { Id = sourceId, Name = $"Transfer source {sourceId:N}", Type = FinanceLedgerService.Wallet, IsActive = true, CreatedBy = actor, CreatedAt = now },
                new FinanceAccount { Id = destinationId, Name = $"Transfer destination {destinationId:N}", Type = FinanceLedgerService.BankAccount, IsActive = true, CreatedBy = actor, CreatedAt = now });
            await finance.SaveChangesAsync();
            await new FinanceLedgerService(finance, new IntegrationClock(now)).PostMovementAsync("FinanceOpeningBalance", Guid.NewGuid(), "Finance",
                "FinanceOpeningBalance", 100m, sourceId, null, "TreasuryOpeningBalance", FinanceLedgerService.Credit, actor, DateOnly.FromDateTime(now));
        }
        await using var factory = new PostgresApplicationFactory(_postgres.GetConnectionString());
        using var client = factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, actor, LenseePermissions.FinanceAccountsManage, LenseePermissions.FinanceRead);
        var request = new FinanceTransferRequest(Guid.NewGuid(), sourceId, destinationId, 60m, 5m,
            DateOnly.FromDateTime(now), "Wallet to bank", "Provider charge", null);
        var created = await client.PostAsJsonAsync("/api/v1/finance/transfers", request);
        Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
        var replay = await client.PostAsJsonAsync("/api/v1/finance/transfers", request);
        Assert.Equal(System.Net.HttpStatusCode.OK, replay.StatusCode);
        var overdraft = await client.PostAsJsonAsync("/api/v1/finance/transfers", request with { ClientRequestId = Guid.NewGuid(), Amount = 36m, FeeAmount = 0m });
        Assert.Equal(System.Net.HttpStatusCode.Conflict, overdraft.StatusCode);

        await using var verifyConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await verifyConnection.OpenAsync();
        await using var verify = CreateFinanceContext(verifyConnection);
        var entries = await verify.FinanceLedgerEntries.Where(value => value.FinanceAccountId == sourceId || value.FinanceAccountId == destinationId).ToListAsync();
        Assert.Equal(35m, entries.Where(value => value.FinanceAccountId == sourceId).Sum(value => value.Direction == FinanceLedgerService.Credit ? value.Amount : -value.Amount));
        Assert.Equal(60m, entries.Where(value => value.FinanceAccountId == destinationId).Sum(value => value.Direction == FinanceLedgerService.Credit ? value.Amount : -value.Amount));
        Assert.Single(await verify.FinanceTransfers.Where(value => value.ClientRequestId == request.ClientRequestId).ToListAsync());
        Assert.Single(await verify.FinanceExpenses.Where(value => value.TransferId != null && value.Category == "TransferFee" && value.Status == "Paid").ToListAsync());
    }

    private static CrmDbContext CreateCrmContext(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<CrmDbContext>().UseNpgsql(connection).Options);

    private static OperationsDbContext CreateOperationsContext(NpgsqlConnection connection) =>
        new(new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(connection).Options);

    private async Task<MerchantOpeningBalanceCharge> CreatePostedOpeningAsync(Guid merchantId, decimal amount, Guid actorId, DateTime now)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await using var payments = CreatePaymentsContext(connection);
        await using var shared = CreateSharedContext(connection);
        await using var operations = CreateOperationsContext(connection);
        var charge = new MerchantOpeningBalanceCharge
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            Amount = amount,
            AsOfDate = DateOnly.FromDateTime(now),
            Description = "PostgreSQL opening balance",
            Status = "PendingReview",
            CreatedBy = actorId,
            CreatedAt = now,
            CorrelationId = Guid.NewGuid().ToString("N")
        };
        payments.MerchantOpeningBalanceCharges.Add(charge);
        await payments.SaveChangesAsync();
        await new MerchantAccountService(payments, shared, operations).PostOpeningBalanceAsync(charge, actorId, now, CancellationToken.None);
        return charge;
    }

    private static MerchantAccountCollectionDraft NewCollectionDraft(
        MerchantReceivableAccount account,
        DateTime now,
        string method,
        string? transactionReference) =>
        new()
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            Account = account,
            Amount = 125m,
            PaymentMethod = method,
            TransactionReference = transactionReference,
            Status = "PendingAdminReview",
            DraftedBy = Guid.NewGuid(),
            DraftedAt = now
        };

    private static async Task InsertOperationAsync(
        NpgsqlConnection connection,
        Guid id,
        string recordKind,
        Guid? reversesOperationId,
        string status = "Completed")
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            insert into operations.operation_logs
                (id, operation_number, operation_type, status, created_by, created_at, record_kind, reverses_operation_id, is_deleted)
            values
                (@id, @number, 'WholesaleSale', @status, @user_id, now(), @record_kind, @reverses_operation_id, false);
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("number", $"PG-{id:N}");
        command.Parameters.AddWithValue("user_id", Guid.NewGuid());
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("record_kind", recordKind);
        command.Parameters.AddWithValue("reverses_operation_id", (object?)reversesOperationId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class IntegrationClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
        public DateTime EgyptNow => now;
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
