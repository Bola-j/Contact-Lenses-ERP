using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lensee.Host.Infrastructure;
using Lensee.Modules.Finance.Data;
using Lensee.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lensee.Tests;

public sealed class FinanceEndpointContractTests : IClassFixture<OperationsEndpointFactory>
{
    private readonly OperationsEndpointFactory _factory;

    public FinanceEndpointContractTests(OperationsEndpointFactory factory) => _factory = factory;

    [Fact]
    public async Task OtherExpenseRequiresDescriptionAndDoesNotCreateLedgerEffect()
    {
        await _factory.SeedAsync();
        var accountId = await _factory.GetFinanceAccountIdAsync();
        using var beforeScope = _factory.Services.CreateScope();
        var beforeFinance = beforeScope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var beforeCount = await beforeFinance.FinanceExpenses.CountAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, Guid.NewGuid(), LenseePermissions.FinanceRead, LenseePermissions.FinanceExpenseCreate);

        var response = await client.PostAsJsonAsync("/api/v1/finance/expenses", new
        {
            financeAccountId = accountId,
            amount = 25m,
            category = "Other",
            movementMethod = "CashHandToHand",
            businessDate = "2026-09-20",
            description = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.Equal(beforeCount, await finance.FinanceExpenses.CountAsync());
    }

    [Fact]
    public async Task PostedExpenseCreatesExactlyOneFinanceDebitAndReplayIsIdempotent()
    {
        await _factory.SeedAsync();
        var creatorId = Guid.NewGuid();
        using var creator = _factory.CreateClient();
        creator.AuthorizeAs(LenseeRoles.Admin, creatorId, LenseePermissions.FinanceRead, LenseePermissions.FinanceExpenseCreate, LenseePermissions.FinanceOpeningCreate);
        var accountId = await CreateFundedAccountAsync(creator, creatorId);

        var created = await creator.PostAsJsonAsync("/api/v1/finance/expenses", new
        {
            financeAccountId = accountId,
            amount = 25m,
            category = "SoftwareAndTechnologySubscriptions",
            movementMethod = "CashHandToHand",
            businessDate = "2026-09-20",
            description = "Monthly service"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var expense = await created.Content.ReadFromJsonAsync<FinanceExpense>();
        Assert.NotNull(expense);
        Assert.Equal("Pending", expense!.Status);
        var approved = await creator.PostAsync($"/api/v1/finance/expenses/{expense.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var replay = await creator.PostAsync($"/api/v1/finance/expenses/{expense.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.Equal(1, await finance.FinanceLedgerEntries.CountAsync(value => value.SourceType == "FinanceExpense" && value.SourceId == expense.Id));
        Assert.Equal(1, await finance.PaymentMovementRegistries.CountAsync(value => value.SourceType == "FinanceExpense" && value.SourceId == expense.Id));
    }

    [Fact]
    public async Task PendingExpenseCanBeEditedBeforePaymentWithoutLedgerEffect()
    {
        await _factory.SeedAsync();
        using var admin = _factory.CreateClient();
        admin.AuthorizeAs(LenseeRoles.Admin, Guid.NewGuid(), LenseePermissions.FinanceRead,
            LenseePermissions.FinanceExpenseCreate, LenseePermissions.FinanceOpeningCreate);
        var accountId = await CreateFundedAccountAsync(admin, Guid.NewGuid());
        var created = await admin.PostAsJsonAsync("/api/v1/finance/expenses", new
        {
            financeAccountId = accountId,
            amount = 15m,
            category = "Other",
            movementMethod = "CashHandToHand",
            businessDate = "2026-09-20",
            description = "Initial note"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var expense = await created.Content.ReadFromJsonAsync<FinanceExpense>();
        Assert.NotNull(expense);
        var edited = await admin.PutAsJsonAsync($"/api/v1/finance/expenses/{expense!.Id}", new
        {
            financeAccountId = accountId,
            amount = 18m,
            category = "Other",
            movementMethod = "CashHandToHand",
            businessDate = "2026-09-21",
            description = "Corrected draft cost"
        });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.Equal(18m, (await finance.FinanceExpenses.SingleAsync(value => value.Id == expense.Id)).Amount);
        Assert.False(await finance.FinanceLedgerEntries.AnyAsync(value => value.SourceId == expense.Id));
    }

    [Fact]
    public async Task AdminWithdrawalPostsOnce_AndErpAdminCannotApproveFinance()
    {
        await _factory.SeedAsync();
        var primaryAdminId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        var beneficiaryId = Guid.NewGuid();
        await SeedFinanceUsersAsync(primaryAdminId, reviewerId, beneficiaryId);

        using var primaryAdmin = _factory.CreateClient();
        primaryAdmin.AuthorizeAs(LenseeRoles.Admin, primaryAdminId, LenseePermissions.FinanceRead, LenseePermissions.FinanceWithdrawalCreate, LenseePermissions.FinanceWithdrawalAssign, LenseePermissions.FinanceOpeningCreate);
        var accountId = await CreateFundedAccountAsync(primaryAdmin, primaryAdminId);
        var draftResponse = await primaryAdmin.PostAsJsonAsync("/api/v1/finance/withdrawals", new
        {
            assignedToCLevelUserId = beneficiaryId,
            financeAccountId = accountId,
            amount = 125m,
            movementMethod = "CashHandToHand",
            businessDate = "2026-09-22",
            reason = "Executive draw"
        });
        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        var draft = await draftResponse.Content.ReadFromJsonAsync<CLevelWithdrawal>();
        Assert.NotNull(draft);
        Assert.Equal("Posted", draft!.Status);
        await AssertLedgerCountAsync(draft.Id, 1);

        using var beneficiary = _factory.CreateClient();
        beneficiary.AuthorizeAs(LenseeRoles.CLevel, beneficiaryId, LenseePermissions.FinanceRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await beneficiary.PostAsync($"/api/v1/finance/withdrawals/{draft.Id}/approve", null)).StatusCode);

        using var reviewer = _factory.CreateClient();
        reviewer.AuthorizeAs(LenseeRoles.ERPAdmin, reviewerId, LenseePermissions.FinanceRead, LenseePermissions.FinanceWithdrawalApprove);
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.PostAsync($"/api/v1/finance/withdrawals/{draft.Id}/approve", null)).StatusCode);
        await AssertLedgerCountAsync(draft.Id, 1);
    }

    [Fact]
    public async Task WithdrawalAssignment_RequiresActiveCLevelBeneficiary()
    {
        await _factory.SeedAsync();
        var accountId = await _factory.GetFinanceAccountIdAsync();
        var secondaryAdminId = Guid.NewGuid();
        var inactiveCLevelId = Guid.NewGuid();
        await SeedFinanceUsersAsync(secondaryAdminId, Guid.NewGuid(), inactiveCLevelId, cLevelActive: false);
        using var secondaryAdmin = _factory.CreateClient();
        secondaryAdmin.AuthorizeAs(LenseeRoles.Admin, secondaryAdminId, LenseePermissions.FinanceWithdrawalCreate, LenseePermissions.FinanceWithdrawalAssign);
        var forbidden = await secondaryAdmin.PostAsJsonAsync("/api/v1/finance/withdrawals", new { assignedToCLevelUserId = inactiveCLevelId, financeAccountId = accountId, amount = 1m, movementMethod = "CashHandToHand", businessDate = "2026-09-22", reason = "Invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, forbidden.StatusCode);
    }

    private async Task SeedFinanceUsersAsync(Guid primaryAdminId, Guid reviewerId, Guid cLevelId, bool cLevelActive = true)
    {
        using var scope = _factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<Lensee.Modules.Identity.Data.IdentityDbContext>();
        identity.Users.AddRange(
            new Lensee.Modules.Identity.Data.User { Id = primaryAdminId, Username = $"primary-{primaryAdminId:N}", FullName = "Primary admin", PasswordHash = "test", Role = LenseeRoles.Admin, IsPrimaryAdmin = true, IsActive = true, CreatedAt = DateTime.UtcNow },
            new Lensee.Modules.Identity.Data.User { Id = reviewerId, Username = $"reviewer-{reviewerId:N}", FullName = "Reviewer", PasswordHash = "test", Role = LenseeRoles.ERPAdmin, IsActive = true, CreatedAt = DateTime.UtcNow },
            new Lensee.Modules.Identity.Data.User { Id = cLevelId, Username = $"clevel-{cLevelId:N}", FullName = "C-Level", PasswordHash = "test", Role = LenseeRoles.CLevel, IsActive = cLevelActive, CreatedAt = DateTime.UtcNow });
        await identity.SaveChangesAsync();
    }

    [Fact]
    public async Task AccountantOpeningCorrectionCanBeRejectedWithNoteAndResubmitted()
    {
        await _factory.SeedAsync();
        using var admin = _factory.CreateClient();
        admin.AuthorizeAs(LenseeRoles.Admin, Guid.NewGuid(), LenseePermissions.FinanceRead,
            LenseePermissions.FinanceOpeningCreate, LenseePermissions.FinanceExpenseApprove);
        var accountId = await CreateFundedAccountAsync(admin, Guid.NewGuid());
        using var scope = _factory.Services.CreateScope();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var original = await finance.FinanceOpeningBalances.SingleAsync(value => value.FinanceAccountId == accountId);

        using var accountant = _factory.CreateClient();
        accountant.AuthorizeAs(LenseeRoles.Accountant, Guid.NewGuid(), LenseePermissions.FinanceOpeningCorrect);
        var proposed = await accountant.PostAsJsonAsync($"/api/v1/finance/opening-balances/{original.Id}/correct", new
        {
            amount = 900m,
            description = "Count correction",
            asOfDate = "2026-09-20"
        });
        Assert.Equal(HttpStatusCode.Created, proposed.StatusCode);
        using var proposedJson = JsonDocument.Parse(await proposed.Content.ReadAsStringAsync());
        var proposalId = proposedJson.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("PendingReview", proposedJson.RootElement.GetProperty("status").GetString());

        var rejected = await admin.PostAsJsonAsync($"/api/v1/finance/opening-balances/{proposalId}/reject", new { reason = "Recheck source count" });
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        finance.ChangeTracker.Clear();
        Assert.Null((await finance.FinanceOpeningBalances.SingleAsync(value => value.Id == original.Id)).ReplacedByOpeningBalanceId);
        Assert.Equal("Rejected", (await finance.FinanceOpeningBalances.SingleAsync(value => value.Id == proposalId)).Status);
        var retry = await accountant.PostAsJsonAsync($"/api/v1/finance/opening-balances/{original.Id}/correct", new
        {
            amount = 950m,
            description = "Recount with evidence",
            asOfDate = "2026-09-20"
        });
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    [Fact]
    public async Task ErpAdminCannotReadFinanceOrExecutiveSummary()
    {
        await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.ERPAdmin, Guid.NewGuid(), LenseePermissions.FinanceRead,
            LenseePermissions.ExecutiveSummaryRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/finance/overview")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/reports/executive-summary?period=daily")).StatusCode);
    }

    private async Task AssertLedgerCountAsync(Guid sourceId, int expected)
    {
        using var scope = _factory.Services.CreateScope();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.Equal(expected, await finance.FinanceLedgerEntries.CountAsync(value => value.SourceType == "CLevelWithdrawal" && value.SourceId == sourceId));
    }

    private async Task<Guid> CreateFundedAccountAsync(HttpClient client, Guid ownerId)
    {
        var accountId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            finance.FinanceAccounts.Add(new FinanceAccount
            {
                Id = accountId,
                Name = $"Test cash {accountId:N}",
                Type = FinanceLedgerService.CashOnHand,
                IsActive = true,
                CreatedBy = ownerId,
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            });
            await finance.SaveChangesAsync();
        }
        var opening = await client.PostAsJsonAsync("/api/v1/finance/opening-balances", new
        {
            financeAccountId = accountId,
            amount = 1000m,
            direction = "Credit",
            asOfDate = "2026-09-20",
            description = "Test starting cash"
        });
        Assert.Equal(HttpStatusCode.Created, opening.StatusCode);
        return accountId;
    }

    [Fact]
    public async Task ReconciliationReturnsCanonicalPostedLedgerTotalsAndHonorsDateFilter()
    {
        await _factory.SeedAsync();
        var accountId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            finance.FinanceAccounts.Add(new FinanceAccount
            {
                Id = accountId,
                Name = $"Opening cash {accountId:N}",
                Type = FinanceLedgerService.CashOnHand,
                IsActive = true,
                CreatedBy = Guid.NewGuid(),
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            });
            await finance.SaveChangesAsync();
        }
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, Guid.NewGuid(), LenseePermissions.FinanceRead, LenseePermissions.FinanceReconcile);

        using var response = await client.GetAsync($"/api/v1/finance/reconciliation?accountId={accountId}&from=2026-09-20&to=2026-09-20&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Equal(1, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, root.GetProperty("items").GetArrayLength());
        var row = root.GetProperty("items")[0];
        Assert.Equal(accountId, row.GetProperty("accountId").GetGuid());
        Assert.True(row.GetProperty("postedEntryCount").GetInt32() >= 0);
        Assert.True(row.TryGetProperty("expectedBalance", out _));
        Assert.True(row.TryGetProperty("netMovement", out _));

        using var invalid = await client.GetAsync("/api/v1/finance/reconciliation?from=2026-09-21&to=2026-09-20");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task OpeningBalancesArePagedAndVisibleWithoutPostingDrafts()
    {
        await _factory.SeedAsync();
        var accountId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            finance.FinanceAccounts.Add(new FinanceAccount
            {
                Id = accountId,
                Name = $"Opening cash {accountId:N}",
                Type = FinanceLedgerService.CashOnHand,
                IsActive = true,
                CreatedBy = Guid.NewGuid(),
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            });
            await finance.SaveChangesAsync();
        }
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Accountant, Guid.NewGuid(), LenseePermissions.FinanceRead, LenseePermissions.FinanceOpeningCreate);

        var created = await client.PostAsJsonAsync("/api/v1/finance/opening-balances", new
        {
            financeAccountId = accountId,
            amount = 125m,
            direction = "Credit",
            asOfDate = "2026-09-20",
            description = "Opening cash position"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var listed = await client.GetAsync("/api/v1/finance/opening-balances?page=1&pageSize=10&status=Posted");
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        using var document = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
        Assert.Equal(1, document.RootElement.GetProperty("page").GetInt32());
        Assert.True(document.RootElement.GetProperty("totalCount").GetInt32() >= 1);
        Assert.Equal("Posted", document.RootElement.GetProperty("items")[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task ReconciliationPackageRejectsHashMismatchWithoutPersistingSourceRows()
    {
        await _factory.SeedAsync();
        using var beforeScope = _factory.Services.CreateScope();
        var beforeFinance = beforeScope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var beforeCount = await beforeFinance.ReconciliationImportPackages.CountAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, Guid.NewGuid(), LenseePermissions.FinanceRead, LenseePermissions.FinanceReconcile);
        const string csv = "row_type,source_reference,merchant_id,finance_account_id,amount,as_of_date,description,legacy_payment_method,legacy_collection_scope,client_id,c_level_user_id\nLegacyPaymentTrack,legacy-1,,,,,,,,,";
        var response = await client.PostAsJsonAsync("/api/v1/finance/reconciliation-packages", new
        {
            manifest = new { schemaVersion = "phase5.v1", signer = "External Ledger", signature = "signed", payloadSha256 = "bad", sourcePeriodStart = "2026-01-01", sourcePeriodEnd = "2026-01-31", exportedAtUtc = "2026-02-01T00:00:00Z" },
            csv
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        Assert.Equal(beforeCount, await finance.ReconciliationImportPackages.CountAsync());
    }

    [Fact]
    public async Task LegacyTrackPackageIsPersistedAsManualReconciliationOnly()
    {
        await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, Guid.NewGuid(), LenseePermissions.FinanceRead, LenseePermissions.FinanceReconcile);
        const string csv = "row_type,source_reference,merchant_id,finance_account_id,amount,as_of_date,description,legacy_payment_method,legacy_collection_scope,client_id,c_level_user_id\nLegacyPaymentTrack,legacy-1,,,,,,Installment,DirectOperation,,";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(csv))).ToLowerInvariant();
        var created = await client.PostAsJsonAsync("/api/v1/finance/reconciliation-packages", new
        {
            manifest = new { schemaVersion = "phase5.v1", signer = "External Ledger", signature = "signed", payloadSha256 = hash, sourcePeriodStart = "2026-01-01", sourcePeriodEnd = "2026-01-31", exportedAtUtc = "2026-02-01T00:00:00Z" },
            csv
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var document = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var row = document.RootElement.GetProperty("rows")[0];
        Assert.Equal("PendingReview", row.GetProperty("decision").GetString());
        Assert.False(row.TryGetProperty("canonicalTrack", out var track) && track.ValueKind == JsonValueKind.String);
    }
}
