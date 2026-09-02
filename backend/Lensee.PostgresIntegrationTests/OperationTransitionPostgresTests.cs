using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Data;
using Lensee.SharedKernel.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lensee.PostgresIntegrationTests;

/// <summary>
/// These tests make competing HTTP transition requests queue on the exact
/// operation row.  Releasing the external lock is the barrier: PostgreSQL then
/// serializes the endpoints, and the losing request must observe the new state.
/// </summary>
public sealed class OperationTransitionPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("lensee")
        .WithUsername("lensee_user")
        .WithPassword("SomeStrongPassword123!")
        .Build();

    private PostgresApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _factory = new PostgresApplicationFactory(_postgres.GetConnectionString());
        await _factory.MigrateAllAsync();
    }

    public async Task DisposeAsync()
    {
        _factory.Dispose();
        await _postgres.DisposeAsync();
    }

    [PostgreSqlIntegrationFact]
    public async Task ConcurrentConfirms_CommitOneTerminalTransition_AndLeaveOneInventoryEffect()
    {
        var seed = await _factory.SeedOperationReferencesAsync();
        var userId = Guid.NewGuid();
        await _factory.SeedUserAsync(userId);
        var operationId = await CreateInventoryReceiptAsync(seed, userId);

        await using var gate = await LockOperationAsync(operationId);
        using var first = CreateOperationsClient(userId);
        using var second = CreateOperationsClient(userId);
        var firstConfirm = first.PostAsync($"/api/v1/operations/{operationId}/confirm", null);
        await WaitForDatabaseLockWaitersAsync(1);
        var secondConfirm = second.PostAsync($"/api/v1/operations/{operationId}/confirm", null);
        await WaitForDatabaseLockWaitersAsync(2);
        await gate.Transaction.CommitAsync();

        var responses = await Task.WhenAll(firstConfirm, secondConfirm);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
        var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Contains("transition-conflict", await conflict.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using var scope = _factory.Services.CreateAsyncScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var operation = await operations.OperationLogs.SingleAsync(value => value.Id == operationId);
        Assert.Equal("Received", operation.Status);
        Assert.Equal(2, await operations.OperationVersions.CountAsync(value => value.OperationId == operationId));
        Assert.Single(await inventory.StockTransactions.Where(value => value.ReferenceOperationId == operationId).ToListAsync());
        Assert.Single(await inventory.StockBalances.Where(value => value.LocationId == seed.MainLocationId && value.SkuId == seed.SkuId).ToListAsync());
        Assert.Equal(1, await identity.AuditLogs.CountAsync(value => value.EntityType == "Operation" && value.EntityId == operationId && value.Action == "Confirm"));
    }

    [PostgreSqlIntegrationFact]
    public async Task QueuedEditThenConfirm_UsesTheCommittedEdit_AndFinishesOnce()
    {
        var seed = await _factory.SeedOperationReferencesAsync();
        var userId = Guid.NewGuid();
        await _factory.SeedUserAsync(userId);
        var created = await CreateInventoryReceiptDetailAsync(seed, userId);
        var operationId = created.Id;
        var expectedVersion = created.ConcurrencyVersion;
        await using var gate = await LockOperationAsync(operationId);
        using var editor = CreateOperationsClient(userId);
        using var confirmer = CreateOperationsClient(userId);
        var edit = editor.PutAsJsonAsync($"/api/v1/operations/{operationId}", ReceiptRequest(seed, 2, expectedVersion));
        await WaitForDatabaseLockWaitersAsync(1);
        var confirm = confirmer.PostAsync($"/api/v1/operations/{operationId}/confirm", null);
        await WaitForDatabaseLockWaitersAsync(2);
        await gate.Transaction.CommitAsync();

        var responses = await Task.WhenAll(edit, confirm);
        Assert.Equal(HttpStatusCode.NoContent, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, responses[1].StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var operation = await operations.OperationLogs.Include(value => value.OperationLines).SingleAsync(value => value.Id == operationId);
        Assert.Equal("Received", operation.Status);
        Assert.Equal(2, Assert.Single(operation.OperationLines).Quantity);
        Assert.Equal(2, await inventory.StockBalances.Where(value => value.LocationId == seed.MainLocationId && value.SkuId == seed.SkuId).Select(value => value.AvailableQty).SingleAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task ConcurrentCorrectionCreation_UsesSourceLock_AndReturnsOneConflict()
    {
        var seed = await _factory.SeedOperationReferencesAsync();
        var operationId = await _factory.SeedFinalizedCorrectionOperationAsync(seed);
        var firstUser = Guid.NewGuid();
        var secondUser = Guid.NewGuid();
        await _factory.SeedUserAsync(firstUser);
        await _factory.SeedUserAsync(secondUser);
        await using var gate = await LockOperationAsync(operationId);
        using var first = CreateCorrectionClient(firstUser);
        using var second = CreateCorrectionClient(secondUser);
        var firstCreate = first.PostAsJsonAsync($"/api/v1/operations/{operationId}/corrections", new { reason = "Duplicate request one" });
        try
        {
            await WaitForDatabaseLockWaitersAsync(1);
        }
        catch (TimeoutException) when (firstCreate.IsCompleted)
        {
            var body = await firstCreate.Result.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException($"Correction create completed before acquiring the source-operation lock: {(int)firstCreate.Result.StatusCode} {body}");
        }
        var secondCreate = second.PostAsJsonAsync($"/api/v1/operations/{operationId}/corrections", new { reason = "Duplicate request two" });
        await WaitForDatabaseLockWaitersAsync(2);
        await gate.Transaction.CommitAsync();

        var responses = await Task.WhenAll(firstCreate, secondCreate);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        await using var scope = _factory.Services.CreateAsyncScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        Assert.Single(await operations.OperationCorrectionProposals.Where(value => value.OperationId == operationId && value.Status == "PendingApproval").ToListAsync());
    }

    [PostgreSqlIntegrationFact]
    public async Task CorrectionSettlement_PersistsTheLockedProposalValues()
    {
        var seed = await _factory.SeedOperationReferencesAsync();
        var operationId = await _factory.SeedFinalizedCorrectionOperationAsync(seed);
        var requester = Guid.NewGuid();
        await _factory.SeedUserAsync(requester);
        using var client = CreateCorrectionClient(requester);
        var proposalId = await CreateCorrectionAsync(client, operationId, "Settlement persistence");

        var settlement = await client.PostAsJsonAsync($"/api/v1/operations/corrections/{proposalId}/settlement", new
        {
            settlementMethod = "MerchantCredit",
            settlementAmount = 25m
        });
        Assert.Equal(HttpStatusCode.OK, settlement.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var proposal = await operations.OperationCorrectionProposals.SingleAsync(value => value.Id == proposalId);
        Assert.Equal("MerchantCredit", proposal.SettlementMethod);
        Assert.Equal(25m, proposal.SettlementAmount);
    }

    [PostgreSqlIntegrationFact]
    public async Task ReversedOperation_RejectsNewCorrectionProposal()
    {
        var seed = await _factory.SeedOperationReferencesAsync();
        var operationId = await _factory.SeedFinalizedCorrectionOperationAsync(seed);
        var requester = Guid.NewGuid();
        var reviewer = Guid.NewGuid();
        await _factory.SeedUserAsync(requester);
        await _factory.SeedUserAsync(reviewer);
        using var requesterClient = CreateCorrectionClient(requester);
        var proposalId = await CreateCorrectionAsync(requesterClient, operationId, "First approved correction");
        using var reviewerClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        reviewerClient.AuthorizeAs(LenseeRoles.Admin, reviewer, LenseePermissions.OperationsRead, LenseePermissions.OperationsCorrectionsApprove);
        var approved = await reviewerClient.PostAsync($"/api/v1/operations/corrections/{proposalId}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        var second = await requesterClient.PostAsJsonAsync($"/api/v1/operations/{operationId}/corrections", new { reason = "Unapprovable duplicate" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("transition-conflict", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var scope = _factory.Services.CreateAsyncScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        Assert.Empty(await operations.OperationCorrectionProposals.Where(value => value.OperationId == operationId && value.Status == "PendingApproval").ToListAsync());
    }

    private HttpClient CreateOperationsClient(Guid userId)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.AuthorizeAs(LenseeRoles.Admin, userId, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);
        return client;
    }

    private HttpClient CreateCorrectionClient(Guid userId)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.AuthorizeAs(LenseeRoles.Admin, userId, LenseePermissions.OperationsRead, LenseePermissions.OperationsCorrectionsRequest);
        return client;
    }

    private async Task<Guid> CreateInventoryReceiptAsync(PostgresOperationSeed seed, Guid userId) => (await CreateInventoryReceiptDetailAsync(seed, userId)).Id;

    private async Task<OperationResponse> CreateInventoryReceiptDetailAsync(PostgresOperationSeed seed, Guid userId)
    {
        using var client = CreateOperationsClient(userId);
        var response = await client.PostAsJsonAsync("/api/v1/operations", ReceiptRequest(seed, 1, null));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        return JsonSerializer.Deserialize<OperationResponse>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static async Task<Guid> CreateCorrectionAsync(HttpClient client, Guid operationId, string reason)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/operations/{operationId}/corrections", new { reason });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private static object ReceiptRequest(PostgresOperationSeed seed, int quantity, uint? expectedVersion) => new
    {
        operationType = "InventoryReceipt",
        destinationLocationId = seed.MainLocationId,
        receipt = new { supplierName = "Concurrency supplier", invoiceNumber = "PG-CONCURRENCY" },
        lines = new[] { new { skuId = seed.SkuId, packQuantity = quantity, lotNumber = "PG-CONCURRENCY", expiryDate = "2028-06-01" } },
        expectedVersion
    };

    private async Task<LockedOperation> LockOperationAsync(Guid operationId)
    {
        var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "select id from operations.operation_logs where id = @id for update;";
        command.Parameters.AddWithValue("id", operationId);
        await command.ExecuteNonQueryAsync();
        return new LockedOperation(connection, transaction);
    }

    private async Task WaitForDatabaseLockWaitersAsync(int expectedCount)
    {
        await using var inspection = new NpgsqlConnection(_postgres.GetConnectionString());
        await inspection.OpenAsync();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var command = inspection.CreateCommand();
            command.CommandText = "select count(*) from pg_locks where not granted;";
            if (Convert.ToInt32(await command.ExecuteScalarAsync()) >= expectedCount) return;
            await Task.Delay(20);
        }
        throw new TimeoutException($"Expected {expectedCount} PostgreSQL lock waiter(s).");
    }

    private sealed class LockedOperation : IAsyncDisposable
    {
        public LockedOperation(NpgsqlConnection connection, NpgsqlTransaction transaction)
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

    private sealed record OperationResponse(Guid Id, uint ConcurrencyVersion);
}
