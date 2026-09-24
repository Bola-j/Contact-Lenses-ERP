using Lensee.Host.Infrastructure;
using Lensee.Modules.Finance.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Payments.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static partial class FinanceEndpoints
{
    public static RouteGroupBuilder MapFinanceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/finance").WithTags("Finance").RequireAuthorization();
        group.MapGet("/overview", GetOverviewAsync).RequireAuthorization("finance.read");
        group.MapGet("/accounts", ListAccountsAsync).RequireAuthorization("finance.read");
        group.MapGet("/accounts/{id:guid}", GetAccountAsync).RequireAuthorization("finance.read");
        group.MapPost("/accounts", CreateAccountAsync).RequireAuthorization("finance.accounts.manage");
        group.MapGet("/balances", GetBalancesAsync).RequireAuthorization("finance.read");
        group.MapGet("/ledger", ListLedgerAsync).RequireAuthorization("finance.read");
        group.MapGet("/reconciliation", GetReconciliationAsync).RequireAuthorization("finance.reconcile");
        group.MapGet("/opening-balances", ListOpeningBalancesAsync).RequireAuthorization("finance.read");
        group.MapPost("/opening-balances", CreateOpeningBalanceAsync).RequireAuthorization("finance.opening.create");
        group.MapPost("/opening-balances/{id:guid}/submit", SubmitOpeningBalanceAsync).RequireAuthorization("finance.opening.correct");
        group.MapPost("/opening-balances/{id:guid}/approve", ApproveOpeningBalanceAsync).RequireAuthorization("finance.expense.approve");
        group.MapPost("/opening-balances/{id:guid}/reject", RejectOpeningBalanceAsync).RequireAuthorization("finance.expense.approve");
        group.MapPost("/opening-balances/{id:guid}/correct", CorrectOpeningBalanceAsync).RequireAuthorization("finance.opening.correct");
        group.MapGet("/expenses", ListExpensesAsync).RequireAuthorization("finance.read");
        group.MapPost("/expenses", CreateExpenseAsync).RequireAuthorization("finance.expense.create");
        group.MapPut("/expenses/{id:guid}", UpdatePendingExpenseAsync).RequireAuthorization("finance.expense.create");
        group.MapPost("/expenses/{id:guid}/submit", SubmitExpenseAsync).RequireAuthorization("finance.expense.create");
        group.MapPost("/expenses/{id:guid}/confirm", ApproveExpenseAsync).RequireAuthorization("finance.expense.create");
        group.MapPost("/expenses/{id:guid}/approve", ApproveExpenseAsync).RequireAuthorization("finance.expense.approve");
        group.MapPost("/expenses/{id:guid}/reject", RejectExpenseAsync).RequireAuthorization("finance.expense.approve");
        group.MapPost("/expenses/{id:guid}/correct", CorrectExpenseAsync).RequireAuthorization("finance.expense.create");
        group.MapGet("/c-level-users", ListCLevelUsersAsync).RequireAuthorization("finance.withdrawal.assign");
        group.MapGet("/withdrawals", ListWithdrawalsAsync).RequireAuthorization("finance.read");
        group.MapPost("/withdrawals", CreateWithdrawalAsync).RequireAuthorization("finance.withdrawal.create");
        group.MapPost("/withdrawals/{id:guid}/submit", SubmitWithdrawalAsync).RequireAuthorization("finance.withdrawal.create");
        group.MapPost("/withdrawals/{id:guid}/approve", ApproveWithdrawalAsync).RequireAuthorization("finance.withdrawal.approve");
        group.MapPost("/withdrawals/{id:guid}/reject", RejectWithdrawalAsync).RequireAuthorization("finance.withdrawal.approve");
        group.MapPost("/withdrawals/{id:guid}/correct", CorrectWithdrawalAsync).RequireAuthorization("finance.withdrawal.create");
        group.MapGet("/categories", ListCategoriesAsync).RequireAuthorization("finance.read");
        group.MapPost("/categories", CreateCategoryAsync).RequireAuthorization("finance.accounts.manage");
        group.MapPut("/categories/{id:guid}", UpdateCategoryAsync).RequireAuthorization("finance.accounts.manage");
        group.MapGet("/transfers", ListTransfersAsync).RequireAuthorization("finance.read");
        group.MapPost("/transfers", PostTransferAsync).RequireAuthorization("finance.accounts.manage");
        group.MapGet("/withdrawals/{id:guid}/repayments", ListWithdrawalRepaymentsAsync).RequireAuthorization("finance.read");
        group.MapPost("/withdrawals/{id:guid}/repayments", PostWithdrawalRepaymentAsync).RequireAuthorization("finance.withdrawal.create");
        group.MapPost("/withdrawals/{id:guid}/repayments/{repaymentId:guid}/correct", CorrectWithdrawalRepaymentAsync).RequireAuthorization("finance.withdrawal.create");
        return group;
    }

    private static async Task<IResult> ListAccountsAsync(FinanceLedgerService service, CancellationToken ct)
        => Results.Ok(await service.ListAccountsAsync(ct));

    private static async Task<IResult> GetAccountAsync(Guid id, FinanceDbContext finance, CancellationToken ct)
    {
        var account = await finance.FinanceAccounts.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, ct);
        if (account is null) return Results.NotFound();
        var entries = await finance.FinanceLedgerEntries.AsNoTracking()
            .Where(value => value.FinanceAccountId == id && value.Status == "Posted")
            .OrderByDescending(value => value.BusinessDate).ThenByDescending(value => value.CreatedAt)
            .Take(200)
            .ToListAsync(ct);
        var balance = await finance.FinanceLedgerEntries.AsNoTracking()
            .Where(value => value.FinanceAccountId == id && value.Status == "Posted")
            .SumAsync(value => (decimal?)(value.Direction == FinanceLedgerService.Credit ? value.Amount : -value.Amount), ct) ?? 0m;
        return Results.Ok(new { account, expectedBalance = balance, entries });
    }

    private static async Task<IResult> CreateAccountAsync(
        FinanceAccountRequest request,
        FinanceDbContext finance,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Type is not (FinanceLedgerService.CashOnHand or FinanceLedgerService.BankAccount or FinanceLedgerService.Wallet))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Name)] = ["Name and a valid account type are required."] });

        var account = new FinanceAccount
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Type = request.Type,
            IsActive = true,
            Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim(),
            Details = request.Details,
            CreatedBy = currentUser.UserId ?? Guid.Empty,
            CreatedAt = clock.EgyptNow
        };
        finance.FinanceAccounts.Add(account);
        await PersistenceBoundary.CommitAsync(finance, ct);
        return Results.Created($"/api/v1/finance/accounts/{account.Id}", ToAccountResponse(account));
    }

    private static async Task<IResult> GetBalancesAsync(FinanceLedgerService service, CancellationToken ct)
    {
        var balances = await service.GetExpectedBalancesAsync(ct);
        return Results.Ok(new
        {
            accounts = balances,
            cash = balances.Where(value => value.Type == FinanceLedgerService.CashOnHand).Sum(value => value.ExpectedBalance),
            bank = balances.Where(value => value.Type == FinanceLedgerService.BankAccount).Sum(value => value.ExpectedBalance),
            wallet = balances.Where(value => value.Type == FinanceLedgerService.Wallet).Sum(value => value.ExpectedBalance),
            totalExpectedLiquidFunds = balances.Sum(value => value.ExpectedBalance)
        });
    }

    private static async Task<IResult> ListOpeningBalancesAsync(
        FinanceDbContext finance,
        Guid? accountId = null,
        string? status = null,
        int page = 1,
        int pageSize = 25,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = finance.FinanceOpeningBalances.AsNoTracking().AsQueryable();
        if (accountId.HasValue) query = query.Where(value => value.FinanceAccountId == accountId.Value);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(value => value.Status == status.Trim());
        query = query.OrderByDescending(value => value.AsOfDate).ThenByDescending(value => value.CreatedAt);
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Results.Ok(new
        {
            page,
            pageSize,
            totalCount = total,
            totalPages = (int)Math.Ceiling(total / (double)pageSize),
            items = rows.Select(ToOpeningBalanceResponse)
        });
    }

    private static async Task<IResult> ListLedgerAsync(FinanceDbContext finance, Guid? accountId = null, DateOnly? from = null, DateOnly? to = null, string? status = null, string? sourceType = null, string? category = null, string? reference = null, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        if (from.HasValue && to.HasValue && from > to)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = ["From must be earlier than or equal to To."] });
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = finance.FinanceLedgerEntries.AsNoTracking().AsQueryable();
        if (accountId.HasValue) query = query.Where(value => value.FinanceAccountId == accountId.Value);
        if (from.HasValue) query = query.Where(value => value.BusinessDate >= from.Value);
        if (to.HasValue) query = query.Where(value => value.BusinessDate <= to.Value);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(value => value.Status == status.Trim());
        if (!string.IsNullOrWhiteSpace(sourceType)) query = query.Where(value => value.SourceType == sourceType.Trim());
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(value => value.Category == category.Trim());
        if (!string.IsNullOrWhiteSpace(reference)) query = query.Where(value => value.ExternalReference == reference.Trim());
        query = query.OrderByDescending(value => value.BusinessDate).ThenByDescending(value => value.CreatedAt);
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Results.Ok(new { page, pageSize, totalCount = total, totalPages = (int)Math.Ceiling(total / (double)pageSize), rows });
    }

    private static async Task<IResult> GetReconciliationAsync(
        FinanceDbContext finance,
        Guid? accountId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        if (from.HasValue && to.HasValue && from > to)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = ["From must be earlier than or equal to To."] });
        }

        if (accountId.HasValue && !await finance.FinanceAccounts.AsNoTracking().AnyAsync(value => value.Id == accountId, ct))
        {
            return Results.NotFound();
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var accountsQuery = finance.FinanceAccounts.AsNoTracking().AsQueryable();
        if (accountId.HasValue) accountsQuery = accountsQuery.Where(value => value.Id == accountId.Value);
        var accounts = await accountsQuery.OrderBy(value => value.Type).ThenBy(value => value.Name).ToListAsync(ct);
        var accountIds = accounts.Select(value => value.Id).ToArray();

        var allPosted = await finance.FinanceLedgerEntries.AsNoTracking()
            .Where(value => value.Status == "Posted" && accountIds.Contains(value.FinanceAccountId))
            .ToListAsync(ct);
        var filtered = allPosted.Where(value =>
            (!from.HasValue || value.BusinessDate >= from.Value) &&
            (!to.HasValue || value.BusinessDate <= to.Value));
        var filteredByAccount = filtered.GroupBy(value => value.FinanceAccountId).ToDictionary(group => group.Key, group => group.ToList());
        var allByAccount = allPosted.GroupBy(value => value.FinanceAccountId).ToDictionary(group => group.Key, group => group.ToList());

        var rows = accounts.Select(account =>
        {
            var all = allByAccount.GetValueOrDefault(account.Id, []);
            var scoped = filteredByAccount.GetValueOrDefault(account.Id, []);
            var inflow = scoped.Where(value => value.Direction == FinanceLedgerService.Credit).Sum(value => value.Amount);
            var outflow = scoped.Where(value => value.Direction == FinanceLedgerService.Debit).Sum(value => value.Amount);
            var expectedBalance = all.Where(value => value.Direction == FinanceLedgerService.Credit).Sum(value => value.Amount)
                - all.Where(value => value.Direction == FinanceLedgerService.Debit).Sum(value => value.Amount);
            var running = 0m;
            DateTime? firstNegativeAt = null;
            var minimumPostedBalance = 0m;
            foreach (var entry in all.OrderBy(value => value.CreatedAt)
                         .ThenBy(value => value.Direction == FinanceLedgerService.Credit ? 0 : 1).ThenBy(value => value.Id))
            {
                running += entry.Direction == FinanceLedgerService.Credit ? entry.Amount : -entry.Amount;
                if (running < minimumPostedBalance) minimumPostedBalance = running;
                if (running < 0 && firstNegativeAt is null) firstNegativeAt = entry.CreatedAt;
            }
            return new
            {
                accountId = account.Id,
                accountName = account.Name,
                accountType = account.Type,
                isActive = account.IsActive,
                postedInflow = inflow,
                postedOutflow = outflow,
                netMovement = inflow - outflow,
                expectedBalance,
                historicalNegativeBalance = firstNegativeAt is not null,
                firstNegativeAt,
                minimumPostedBalance,
                postedEntryCount = scoped.Count,
                lastBusinessDate = scoped.Count == 0 ? (DateOnly?)null : scoped.Max(value => value.BusinessDate)
            };
        }).ToList();

        var total = rows.Count;
        return Results.Ok(new
        {
            page,
            pageSize,
            totalCount = total,
            totalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize),
            filters = new { accountId, from, to },
            items = rows.Skip((page - 1) * pageSize).Take(pageSize).ToArray()
        });
    }

    private static async Task<IResult> GetOverviewAsync(FinanceDbContext finance, PaymentsDbContext payments, FinanceLedgerService service, CancellationToken ct)
    {
        var balances = await service.GetExpectedBalancesAsync(ct);
        var merchantOutstanding = await payments.MerchantOperationObligations.AsNoTracking()
            .Where(value => value.Status != "Settled" && value.Status != "Reversed")
            .Select(value => value.OriginalAmount - (payments.EffectiveAllocations()
                .Where(allocation => allocation.ObligationId == value.Id)
                .Sum(allocation => (decimal?)allocation.Amount) ?? 0m))
            .SumAsync(ct);
        var entries = finance.FinanceLedgerEntries.AsNoTracking().Where(value => value.Status == "Posted");
        var rows = await entries.GroupBy(value => value.Category).Select(group => new { category = group.Key, inflow = group.Where(value => value.Direction == FinanceLedgerService.Credit).Sum(value => (decimal?)value.Amount) ?? 0m, outflow = group.Where(value => value.Direction == FinanceLedgerService.Debit).Sum(value => (decimal?)value.Amount) ?? 0m }).ToListAsync(ct);
        return Results.Ok(new { accounts = balances, expectedCash = balances.Where(value => value.Type == FinanceLedgerService.CashOnHand).Sum(value => value.ExpectedBalance), expectedBank = balances.Where(value => value.Type == FinanceLedgerService.BankAccount).Sum(value => value.ExpectedBalance), expectedWallet = balances.Where(value => value.Type == FinanceLedgerService.Wallet).Sum(value => value.ExpectedBalance), totalLiquidFunds = balances.Sum(value => value.ExpectedBalance), outstandingMerchantReceivable = merchantOutstanding, categories = rows });
    }

    private static async Task<IResult> ListExpensesAsync(FinanceDbContext finance, int page = 1, int pageSize = 50, string? status = null, string? category = null, Guid? accountId = null, DateOnly? from = null, DateOnly? to = null, string? reference = null, CancellationToken ct = default)
    {
        if (from.HasValue && to.HasValue && from > to)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = ["From must be earlier than or equal to To."] });
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        var query = finance.FinanceExpenses.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(value => value.Status == status.Trim());
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(value => value.Category == category.Trim());
        if (accountId.HasValue) query = query.Where(value => value.FinanceAccountId == accountId.Value);
        if (from.HasValue) query = query.Where(value => value.BusinessDate >= from.Value);
        if (to.HasValue) query = query.Where(value => value.BusinessDate <= to.Value);
        if (!string.IsNullOrWhiteSpace(reference)) query = query.Where(value => value.ExternalReference == reference.Trim());
        query = query.OrderByDescending(value => value.BusinessDate).ThenByDescending(value => value.CreatedAt);
        var total = await query.CountAsync(ct);
        return Results.Ok(new { page, pageSize, totalCount = total, totalPages = (int)Math.Ceiling(total / (double)pageSize), items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct) });
    }

    private static async Task<IResult> CreateExpenseAsync(FinanceExpenseRequest request, FinanceDbContext finance, ICurrentUser currentUser, IClock clock, CancellationToken ct)
    {
        var errors = ValidateExpense(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var account = await GetActiveAccountAsync(finance, request.FinanceAccountId, ct);
        if (account is null) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.FinanceAccountId)] = ["The selected FinanceAccount is not active."] });
        if (!MovementMatchesAccount(request.MovementMethod, account.Type)) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.MovementMethod)] = ["The movement method does not match the selected FinanceAccount type."] });
        var actor = currentUser.UserId ?? Guid.Empty; if (actor == Guid.Empty) return Results.Unauthorized();
        var category = await finance.FinanceCategories.SingleOrDefaultAsync(value => value.Kind == "Expense" && value.Code == request.Category && value.IsActive, ct);
        if (category is null && request.Category is not ("Salary" or "Rent" or "SocialMedia" or "SoftwareAndTechnologySubscriptions" or "Other"))
            return Results.BadRequest(new { code = "inactive-or-unknown-expense-category" });
        var entity = new FinanceExpense { Id = Guid.NewGuid(), FinanceAccountId = request.FinanceAccountId, Amount = request.Amount, Category = request.Category, CategoryId = category?.Id, MovementMethod = request.MovementMethod, BusinessDate = request.BusinessDate, Description = Clean(request.Description), Status = "Pending", CreatedByUserId = actor, CreatedAt = clock.EgyptNow, ExternalReference = Clean(request.ExternalReference), CorrelationId = Clean(request.CorrelationId) };
        finance.FinanceExpenses.Add(entity); await PersistenceBoundary.CommitAsync(finance, ct);
        return Results.Created($"/api/v1/finance/expenses/{entity.Id}", ToExpenseResponse(entity));
    }

    private static async Task<IResult> UpdatePendingExpenseAsync(Guid id, FinanceExpenseRequest request,
        FinanceDbContext finance, CancellationToken ct)
    {
        var errors = ValidateExpense(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        var expense = await LoadExpenseForUpdateAsync(id, finance, ct);
        if (expense is null) return Results.NotFound();
        if (expense.Status != "Pending" || expense.PostedFinanceLedgerEntryId is not null)
            return Results.Conflict(new { code = "paid-expense-requires-correction" });
        var account = await GetActiveAccountAsync(finance, request.FinanceAccountId, ct);
        if (account is null || !MovementMatchesAccount(request.MovementMethod, account.Type))
            return Results.BadRequest(new { code = "invalid-expense-account-or-method" });
        var category = await finance.FinanceCategories.SingleOrDefaultAsync(value => value.Kind == "Expense" && value.Code == request.Category && value.IsActive, ct);
        if (category is null && request.Category is not ("Salary" or "Rent" or "SocialMedia" or "SoftwareAndTechnologySubscriptions" or "Other"))
            return Results.BadRequest(new { code = "inactive-or-unknown-expense-category" });
        expense.FinanceAccountId = request.FinanceAccountId;
        expense.Amount = request.Amount;
        expense.Category = request.Category;
        expense.CategoryId = category?.Id;
        expense.MovementMethod = request.MovementMethod;
        expense.BusinessDate = request.BusinessDate;
        expense.Description = Clean(request.Description);
        expense.ExternalReference = Clean(request.ExternalReference);
        await PersistenceBoundary.CommitAsync(finance, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Results.Ok(ToExpenseResponse(expense));
    }

    private static async Task<IResult> ApproveExpenseAsync(Guid id, FinanceDbContext finance, FinanceLedgerService ledger, ICurrentUser user, IClock clock, CancellationToken ct)
    {
        var actor = user.UserId ?? Guid.Empty;
        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        var entity = await LoadExpenseForUpdateAsync(id, finance, ct); if (entity is null) return Results.NotFound();
        if (entity.Status is "Paid" or "Posted") return Results.Ok(ToExpenseResponse(entity));
        if (entity.Status is not ("Pending" or "PendingReview" or "Draft")) return Results.Conflict(new { code = "expense-not-payable" });
        if (actor == Guid.Empty) return Results.Unauthorized();
        if (entity.ReversesExpenseId is Guid originalId) { var original = await finance.FinanceExpenses.SingleAsync(value => value.Id == originalId, ct); if (original.PostedFinanceLedgerEntryId is not Guid originalEntry) return Results.Conflict(new { code = "expense-reversal-source-missing" }); var entry = await finance.FinanceLedgerEntries.SingleAsync(value => value.Id == originalEntry, ct); await ledger.ReverseMovementAsync(entry, "FinanceExpenseReversal", entity.Id, actor, entity.CorrelationId, ct); }
        var posted = await ledger.PostMovementAsync("FinanceExpense", entity.Id, "Finance", entity.MovementMethod, entity.Amount, entity.FinanceAccountId, entity.ExternalReference, "OperatingExpense", FinanceLedgerService.Debit, actor, entity.BusinessDate, entity.CorrelationId, ct);
        entity.Status = "Paid"; entity.ApprovedByUserId = actor; entity.ApprovedAt = clock.EgyptNow; entity.PostedFinanceLedgerEntryId = posted.Id; await PersistenceBoundary.CommitAsync(finance, ct); if (transaction is not null) await transaction.CommitAsync(ct); return Results.Ok(ToExpenseResponse(entity));
    }

    private static async Task<IResult> CorrectExpenseAsync(Guid id, FinanceExpenseCorrectionRequest request, FinanceDbContext finance, FinanceLedgerService ledger, ICurrentUser user, IClock clock, CancellationToken ct)
    {
        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        var original = await LoadExpenseForUpdateAsync(id, finance, ct); if (original is null) return Results.NotFound();
        if (original.Status is not ("Posted" or "Paid") || original.ReplacedByExpenseId is not null) return Results.Conflict(new { code = "expense-not-correctable" });
        if (string.IsNullOrWhiteSpace(request.Description)) return Results.BadRequest(new { code = "correction-note-required" });
        var replacementAccountId = request.FinanceAccountId == Guid.Empty ? original.FinanceAccountId : request.FinanceAccountId;
        var replacementMethod = request.MovementMethod ?? original.MovementMethod;
        var account = await GetActiveAccountAsync(finance, replacementAccountId, ct);
        if (account is null || !MovementMatchesAccount(replacementMethod, account.Type)) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.FinanceAccountId)] = ["The replacement FinanceAccount or movement method is invalid."] });
        var replacementCategoryCode = request.Category ?? original.Category;
        var replacementCategory = await finance.FinanceCategories.SingleOrDefaultAsync(value => value.Kind == "Expense" && value.Code == replacementCategoryCode && value.IsActive, ct);
        if (replacementCategory is null && replacementCategoryCode != original.Category)
            return Results.BadRequest(new { code = "inactive-or-unknown-expense-category" });
        var replacement = new FinanceExpense { Id = Guid.NewGuid(), FinanceAccountId = replacementAccountId, Amount = request.Amount, Category = replacementCategoryCode, CategoryId = replacementCategory?.Id ?? original.CategoryId, MovementMethod = replacementMethod, BusinessDate = request.BusinessDate ?? original.BusinessDate, Description = original.Description, CorrectionNote = request.Description.Trim(), Status = "Pending", CreatedByUserId = user.UserId ?? Guid.Empty, CreatedAt = clock.EgyptNow, ReversesExpenseId = original.Id, ExternalReference = Clean(request.ExternalReference), CorrelationId = Clean(request.CorrelationId) };
        var errors = ValidateExpense(new FinanceExpenseRequest(replacement.FinanceAccountId, replacement.Amount, replacement.Category, replacement.MovementMethod, replacement.BusinessDate, replacement.Description, replacement.ExternalReference, replacement.CorrelationId)); if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (original.PostedFinanceLedgerEntryId is not Guid originalEntryId) return Results.Conflict(new { code = "expense-ledger-source-missing" });
        var originalEntry = await finance.FinanceLedgerEntries.SingleAsync(value => value.Id == originalEntryId, ct);
        await ledger.ReverseMovementAsync(originalEntry, "FinanceExpenseReversal", replacement.Id, user.UserId ?? Guid.Empty, replacement.CorrelationId, ct);
        var posted = await ledger.PostMovementAsync("FinanceExpense", replacement.Id, "Finance", replacement.MovementMethod, replacement.Amount,
            replacement.FinanceAccountId, replacement.ExternalReference, "OperatingExpense", FinanceLedgerService.Debit,
            user.UserId ?? Guid.Empty, replacement.BusinessDate, replacement.CorrelationId, ct);
        replacement.Status = "Paid"; replacement.ApprovedByUserId = user.UserId; replacement.ApprovedAt = clock.EgyptNow;
        replacement.PostedFinanceLedgerEntryId = posted.Id;
        original.ReplacedByExpenseId = replacement.Id; original.Status = "Corrected"; finance.FinanceExpenses.Add(replacement);
        await PersistenceBoundary.CommitAsync(finance, ct); if (transaction is not null) await transaction.CommitAsync(ct);
        return Results.Created($"/api/v1/finance/expenses/{replacement.Id}", ToExpenseResponse(replacement));
    }

    private static async Task<IResult> ListCLevelUsersAsync(IdentityDbContext identity, ICurrentUser currentUser, CancellationToken ct)
    {
        if (!await IsPrimaryAdminAsync(identity, currentUser.UserId, ct)) return Results.Forbid();
        return Results.Ok(await identity.Users.AsNoTracking().Where(value => value.IsActive && value.Role == LenseeRoles.CLevel).OrderBy(value => value.FullName).Select(value => new { value.Id, value.FullName, value.Username, value.Role }).ToListAsync(ct));
    }

    private static async Task<IResult> ListWithdrawalsAsync(FinanceDbContext finance, IdentityDbContext identity, int page = 1, int pageSize = 50, string? status = null, Guid? accountId = null, DateOnly? from = null, DateOnly? to = null, string? reference = null, CancellationToken ct = default)
    {
        if (from.HasValue && to.HasValue && from > to)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["dateRange"] = ["From must be earlier than or equal to To."] });
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        var query = finance.CLevelWithdrawals.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(value => value.Status == status.Trim());
        if (accountId.HasValue) query = query.Where(value => value.FinanceAccountId == accountId.Value);
        if (from.HasValue) query = query.Where(value => value.BusinessDate >= from.Value);
        if (to.HasValue) query = query.Where(value => value.BusinessDate <= to.Value);
        if (!string.IsNullOrWhiteSpace(reference)) query = query.Where(value => value.ExternalReference == reference.Trim());
        query = query.OrderByDescending(value => value.BusinessDate).ThenByDescending(value => value.CreatedAt);
        var total = await query.CountAsync(ct); var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var users = await identity.Users.AsNoTracking().Where(value => items.Select(item => item.AssignedToCLevelUserId).Contains(value.Id)).ToDictionaryAsync(value => value.Id, value => value.FullName, ct);
        return Results.Ok(new { page, pageSize, totalCount = total, totalPages = (int)Math.Ceiling(total / (double)pageSize), items = items.Select(value => new { withdrawal = value, assignedToName = users.GetValueOrDefault(value.AssignedToCLevelUserId) }) });
    }

    private static async Task<IResult> CreateWithdrawalAsync(CLevelWithdrawalRequest request, FinanceDbContext finance, IdentityDbContext identity, FinanceLedgerService ledger, ICurrentUser currentUser, IClock clock, CancellationToken ct)
    {
        var actor = currentUser.UserId ?? Guid.Empty;
        if (!await IsPrimaryAdminAsync(identity, actor, ct)) return Results.Forbid();
        var errors = ValidateWithdrawal(request); if (errors.Count > 0) return Results.ValidationProblem(errors);
        var account = await GetActiveAccountAsync(finance, request.FinanceAccountId, ct);
        if (account is null) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.FinanceAccountId)] = ["The selected FinanceAccount is not active."] });
        if (!MovementMatchesAccount(request.MovementMethod, account.Type)) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.MovementMethod)] = ["The movement method does not match the selected FinanceAccount type."] });
        if (!await IsActiveCLevelAsync(identity, request.AssignedToCLevelUserId, ct)) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.AssignedToCLevelUserId)] = ["The beneficiary must be an active C-Level user."] });
        FinanceCategory? category = null;
        if (request.CategoryId is Guid categoryId)
        {
            category = await finance.FinanceCategories.SingleOrDefaultAsync(value => value.Id == categoryId && value.Kind == "Withdrawal" && value.IsActive, ct);
            if (category is null) return Results.BadRequest(new { code = "invalid-withdrawal-category" });
        }
        else category = await finance.FinanceCategories.SingleOrDefaultAsync(value => value.Kind == "Withdrawal" && value.Code == "CLevelWithdrawal" && value.IsActive, ct);
        var entity = new CLevelWithdrawal { Id = Guid.NewGuid(), AssignedToCLevelUserId = request.AssignedToCLevelUserId, AssignedByUserId = actor, FinanceAccountId = request.FinanceAccountId, Amount = request.Amount, MovementMethod = request.MovementMethod, BusinessDate = request.BusinessDate, Reason = request.Reason.Trim(), CategoryId = category?.Id, Status = "Posted", CreatedByUserId = actor, CreatedAt = clock.EgyptNow, ApprovedByUserId = actor, ApprovedAt = clock.EgyptNow, ExternalReference = Clean(request.ExternalReference), CorrelationId = Clean(request.CorrelationId) };
        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        var posted = await ledger.PostMovementAsync("CLevelWithdrawal", entity.Id, "Finance", entity.MovementMethod, entity.Amount,
            entity.FinanceAccountId, entity.ExternalReference, "CLevelWithdrawal", FinanceLedgerService.Debit,
            actor, entity.BusinessDate, entity.CorrelationId, ct);
        entity.PostedFinanceLedgerEntryId = posted.Id;
        finance.CLevelWithdrawals.Add(entity);
        await PersistenceBoundary.CommitAsync(finance, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Results.Created($"/api/v1/finance/withdrawals/{entity.Id}", ToWithdrawalResponse(entity));
    }

    private static async Task<IResult> SubmitWithdrawalAsync(Guid id, FinanceDbContext finance, ICurrentUser currentUser, CancellationToken ct)
    {
        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        var entity = await LoadWithdrawalForUpdateAsync(id, finance, ct);
        if (entity is null) return Results.NotFound();
        if (entity.Status == "PendingReview") return Results.Ok(ToWithdrawalResponse(entity));
        if (entity.Status != "Draft") return Results.Conflict(new { code = "withdrawal-not-submittable" });
        if (currentUser.UserId is not Guid actor || actor != entity.CreatedByUserId) return Results.Forbid();
        entity.Status = "PendingReview";
        await PersistenceBoundary.CommitAsync(finance, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Results.Ok(ToWithdrawalResponse(entity));
    }

    private static async Task<IResult> ApproveWithdrawalAsync(Guid id, FinanceDbContext finance, FinanceLedgerService ledger, ICurrentUser currentUser, IClock clock, CancellationToken ct)
    {
        var actor = currentUser.UserId ?? Guid.Empty;
        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        var entity = await LoadWithdrawalForUpdateAsync(id, finance, ct);
        if (entity is null) return Results.NotFound();
        if (entity.Status == "Posted") return Results.Ok(ToWithdrawalResponse(entity));
        if (entity.Status != "PendingReview") return Results.Conflict(new { code = "withdrawal-not-reviewable" });
        if (actor == Guid.Empty || actor == entity.AssignedToCLevelUserId)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["approval"] = ["The beneficiary cannot post this withdrawal."] });

        var posted = await ledger.PostMovementAsync("CLevelWithdrawal", entity.Id, "Finance", entity.MovementMethod, entity.Amount, entity.FinanceAccountId, entity.ExternalReference, "CLevelWithdrawal", FinanceLedgerService.Debit, actor, entity.BusinessDate, entity.CorrelationId, ct);
        entity.Status = "Posted";
        entity.ApprovedByUserId = actor;
        entity.ApprovedAt = clock.EgyptNow;
        entity.PostedFinanceLedgerEntryId = posted.Id;
        await PersistenceBoundary.CommitAsync(finance, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Results.Ok(ToWithdrawalResponse(entity));
    }

    private static async Task<IResult> RejectWithdrawalAsync(Guid id, FinanceDbContext finance, ICurrentUser currentUser, CancellationToken ct)
    {
        var actor = currentUser.UserId ?? Guid.Empty;
        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        var entity = await LoadWithdrawalForUpdateAsync(id, finance, ct);
        if (entity is null) return Results.NotFound();
        if (entity.Status == "Rejected") return Results.Ok(ToWithdrawalResponse(entity));
        if (entity.Status != "Draft" && entity.Status != "PendingReview") return Results.Conflict(new { code = "withdrawal-not-rejectable" });
        if (actor == Guid.Empty || actor == entity.CreatedByUserId || actor == entity.AssignedToCLevelUserId)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["approval"] = ["The creator and beneficiary cannot reject this protected withdrawal."] });
        entity.Status = "Rejected";
        await PersistenceBoundary.CommitAsync(finance, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Results.Ok(ToWithdrawalResponse(entity));
    }

    private static async Task<IResult> CorrectWithdrawalAsync(Guid id, CLevelWithdrawalCorrectionRequest request, FinanceDbContext finance, IdentityDbContext identity, FinanceLedgerService ledger, ICurrentUser user, IClock clock, CancellationToken ct)
    {
        var actor = user.UserId ?? Guid.Empty; if (!await IsPrimaryAdminAsync(identity, actor, ct)) return Results.Forbid();
        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        var original = await LoadWithdrawalForUpdateAsync(id, finance, ct); if (original is null) return Results.NotFound(); if (original.Status != "Posted" || original.ReplacedByWithdrawalId is not null) return Results.Conflict(new { code = "withdrawal-not-correctable" });
        if (string.IsNullOrWhiteSpace(request.Reason)) return Results.BadRequest(new { code = "correction-note-required" });
        var lineageIds = await WithdrawalLineageIdsAsync(finance, original, ct);
        var repaid = await finance.CLevelWithdrawalRepayments.Where(value => lineageIds.Contains(value.WithdrawalId) && value.ReplacedByRepaymentId == null)
            .SumAsync(value => (decimal?)value.Amount, ct) ?? 0m;
        if (request.Amount < repaid) return Results.Conflict(new { code = "corrected-withdrawal-below-repaid-amount", repaid });
        var assignee = request.AssignedToCLevelUserId == Guid.Empty ? original.AssignedToCLevelUserId : request.AssignedToCLevelUserId; if (!await IsActiveCLevelAsync(identity, assignee, ct)) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.AssignedToCLevelUserId)] = ["The beneficiary must be an active C-Level user."] });
        var replacementAccountId = request.FinanceAccountId == Guid.Empty ? original.FinanceAccountId : request.FinanceAccountId;
        var replacementMethod = request.MovementMethod ?? original.MovementMethod;
        var account = await GetActiveAccountAsync(finance, replacementAccountId, ct);
        if (account is null || !MovementMatchesAccount(replacementMethod, account.Type)) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.FinanceAccountId)] = ["The replacement FinanceAccount or movement method is invalid."] });
        var replacement = new CLevelWithdrawal { Id = Guid.NewGuid(), AssignedToCLevelUserId = assignee, AssignedByUserId = actor, FinanceAccountId = replacementAccountId, Amount = request.Amount, MovementMethod = replacementMethod, BusinessDate = request.BusinessDate ?? original.BusinessDate, Reason = original.Reason, CategoryId = original.CategoryId, CorrectionNote = request.Reason.Trim(), CreatedByUserId = actor, CreatedAt = clock.EgyptNow, ReversesWithdrawalId = original.Id, ExternalReference = Clean(request.ExternalReference), CorrelationId = Clean(request.CorrelationId) };
        var errors = ValidateWithdrawal(new CLevelWithdrawalRequest(replacement.AssignedToCLevelUserId, replacement.FinanceAccountId, replacement.Amount, replacement.MovementMethod, replacement.BusinessDate, replacement.Reason, replacement.ExternalReference, replacement.CorrelationId)); if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (original.PostedFinanceLedgerEntryId is not Guid originalEntryId) return Results.Conflict(new { code = "withdrawal-ledger-source-missing" });
        var originalEntry = await finance.FinanceLedgerEntries.SingleAsync(value => value.Id == originalEntryId, ct);
        await ledger.ReverseMovementAsync(originalEntry, "CLevelWithdrawalReversal", replacement.Id, actor, replacement.CorrelationId, ct);
        var posted = await ledger.PostMovementAsync("CLevelWithdrawal", replacement.Id, "Finance", replacement.MovementMethod,
            replacement.Amount, replacement.FinanceAccountId, replacement.ExternalReference, "CLevelWithdrawal",
            FinanceLedgerService.Debit, actor, replacement.BusinessDate, replacement.CorrelationId, ct);
        replacement.Status = "Posted"; replacement.PostedFinanceLedgerEntryId = posted.Id;
        replacement.ApprovedByUserId = actor; replacement.ApprovedAt = clock.EgyptNow;
        original.ReplacedByWithdrawalId = replacement.Id; original.Status = "Corrected"; finance.CLevelWithdrawals.Add(replacement);
        await PersistenceBoundary.CommitAsync(finance, ct); if (transaction is not null) await transaction.CommitAsync(ct);
        return Results.Created($"/api/v1/finance/withdrawals/{replacement.Id}", ToWithdrawalResponse(replacement));
    }

    private static async Task<IResult> SubmitExpenseAsync(Guid id, FinanceDbContext finance, CancellationToken ct)
    {
        var entity = await finance.FinanceExpenses.SingleOrDefaultAsync(value => value.Id == id, ct); if (entity is null) return Results.NotFound(); if (entity.Status == "PendingReview") return Results.Ok(ToExpenseResponse(entity)); if (entity.Status != "Draft") return Results.Conflict(new { code = "expense-not-submittable" }); entity.Status = "PendingReview"; await PersistenceBoundary.CommitAsync(finance, ct); return Results.Ok(ToExpenseResponse(entity));
    }
    private static async Task<IResult> RejectExpenseAsync(Guid id, FinanceDbContext finance, CancellationToken ct)
    {
        var entity = await finance.FinanceExpenses.SingleOrDefaultAsync(value => value.Id == id, ct); if (entity is null) return Results.NotFound(); if (entity.Status != "PendingReview") return Results.Conflict(new { code = "expense-not-reviewable" }); entity.Status = "Rejected"; await PersistenceBoundary.CommitAsync(finance, ct); return Results.Ok(ToExpenseResponse(entity));
    }
    private static async Task<FinanceAccount?> GetActiveAccountAsync(FinanceDbContext finance, Guid id, CancellationToken ct) => id != Guid.Empty ? await finance.FinanceAccounts.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id && value.IsActive, ct) : null;
    private static async Task<FinanceExpense?> LoadExpenseForUpdateAsync(Guid id, FinanceDbContext finance, CancellationToken ct)
    {
        if (finance.Database.IsRelational())
            await finance.Database.ExecuteSqlInterpolatedAsync($"select 1 from finance.finance_expenses where id = {id} for update", ct);
        return await finance.FinanceExpenses.SingleOrDefaultAsync(value => value.Id == id, ct);
    }
    private static async Task<CLevelWithdrawal?> LoadWithdrawalForUpdateAsync(Guid id, FinanceDbContext finance, CancellationToken ct)
    {
        if (finance.Database.IsRelational())
            await finance.Database.ExecuteSqlInterpolatedAsync($"select 1 from finance.c_level_withdrawals where id = {id} for update", ct);
        return await finance.CLevelWithdrawals.SingleOrDefaultAsync(value => value.Id == id, ct);
    }
    private static async Task<FinanceOpeningBalance?> LoadOpeningBalanceForUpdateAsync(Guid id, FinanceDbContext finance, CancellationToken ct)
    {
        if (finance.Database.IsRelational())
            await finance.Database.ExecuteSqlInterpolatedAsync($"select 1 from finance.finance_opening_balances where id = {id} for update", ct);
        return await finance.FinanceOpeningBalances.SingleOrDefaultAsync(value => value.Id == id, ct);
    }
    private static async Task<bool> IsActiveCLevelAsync(IdentityDbContext identity, Guid id, CancellationToken ct) => id != Guid.Empty && await identity.Users.AsNoTracking().AnyAsync(value => value.Id == id && value.IsActive && value.Role == LenseeRoles.CLevel, ct);
    private static async Task<bool> IsPrimaryAdminAsync(IdentityDbContext identity, Guid? id, CancellationToken ct) => id is Guid actor && await identity.Users.AsNoTracking().AnyAsync(value => value.Id == actor && value.IsActive && value.Role == LenseeRoles.Admin, ct);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static Dictionary<string, string[]> ValidateExpense(FinanceExpenseRequest request) { var errors = new Dictionary<string, string[]>(); if (request.Amount <= 0) errors[nameof(request.Amount)] = ["Amount must be positive."]; if (string.IsNullOrWhiteSpace(request.Category)) errors[nameof(request.Category)] = ["Expense category is required."]; if (request.Category == "Other" && string.IsNullOrWhiteSpace(request.Description)) errors[nameof(request.Description)] = ["Description is required for Other."]; if (!MovementMethods.Contains(request.MovementMethod)) errors[nameof(request.MovementMethod)] = ["Movement method is invalid."]; return errors; }
    private static Dictionary<string, string[]> ValidateWithdrawal(CLevelWithdrawalRequest request) { var errors = new Dictionary<string, string[]>(); if (request.Amount <= 0) errors[nameof(request.Amount)] = ["Amount must be positive."]; if (string.IsNullOrWhiteSpace(request.Reason)) errors[nameof(request.Reason)] = ["Reason is required."]; if (!MovementMethods.Contains(request.MovementMethod)) errors[nameof(request.MovementMethod)] = ["Movement method is invalid."]; return errors; }
    private static readonly HashSet<string> MovementMethods = ["CashHandToHand", "CashTransaction", "BankTransfer", "Wallet"];
    private static bool MovementMatchesAccount(string method, string accountType)
        => FinanceLedgerService.MovementMatchesAccount(method, accountType);

    private static async Task<IResult> CreateOpeningBalanceAsync(
        FinanceOpeningBalanceRequest request,
        FinanceDbContext finance,
        FinanceLedgerService ledger,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken ct)
    {
        if (request.Amount <= 0 || request.FinanceAccountId == Guid.Empty || request.Direction != FinanceLedgerService.Credit || string.IsNullOrWhiteSpace(request.Description))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Amount)] = ["A positive amount, account, direction, and description are required."] });
        if (!await finance.FinanceAccounts.AnyAsync(value => value.Id == request.FinanceAccountId && value.IsActive, ct))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.FinanceAccountId)] = ["The selected FinanceAccount is not active."] });

        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        await LockFinanceAccountAsync(finance, request.FinanceAccountId, ct);
        if (await finance.FinanceOpeningBalances.AnyAsync(value => value.FinanceAccountId == request.FinanceAccountId && value.ReversesOpeningBalanceId == null && value.Status != "Rejected", ct))
            return Results.Conflict(new { code = "account-opening-already-exists" });
        var balance = new FinanceOpeningBalance
        {
            Id = Guid.NewGuid(),
            FinanceAccountId = request.FinanceAccountId,
            Amount = request.Amount,
            Direction = request.Direction,
            AsOfDate = request.AsOfDate,
            Description = request.Description.Trim(),
            Status = "Posted",
            CreatedBy = currentUser.UserId ?? Guid.Empty,
            CreatedAt = clock.EgyptNow,
            ReviewedBy = currentUser.UserId,
            ReviewedAt = clock.EgyptNow,
            CorrelationId = request.CorrelationId
        };
        var entry = await ledger.PostMovementAsync("FinanceOpeningBalance", balance.Id, "Finance", "FinanceOpeningBalance", balance.Amount,
            balance.FinanceAccountId, null, "TreasuryOpeningBalance", balance.Direction, balance.CreatedBy, balance.AsOfDate, balance.CorrelationId, ct);
        balance.PostedEntryId = entry.Id;
        finance.FinanceOpeningBalances.Add(balance);
        await PersistenceBoundary.CommitAsync(finance, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Results.Created($"/api/v1/finance/opening-balances/{balance.Id}", ToOpeningBalanceResponse(balance));
    }

    private static async Task<IResult> SubmitOpeningBalanceAsync(Guid id, FinanceDbContext finance, CancellationToken ct)
    {
        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        var balance = await LoadOpeningBalanceForUpdateAsync(id, finance, ct);
        if (balance is null) return Results.NotFound();
        if (balance.Status == "PendingReview") return Results.Ok(ToOpeningBalanceResponse(balance));
        if (balance.Status != "Draft") return Results.Conflict(new { code = "opening-balance-not-submittable" });
        balance.Status = "PendingReview";
        balance.ReviewedAt = null;
        await PersistenceBoundary.CommitAsync(finance, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Results.Ok(ToOpeningBalanceResponse(balance));
    }

    private static async Task<IResult> ApproveOpeningBalanceAsync(
        Guid id,
        FinanceDbContext finance,
        FinanceLedgerService ledgerService,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken ct)
    {
        await using var transaction = finance.Database.IsRelational()
            ? await PersistenceBoundary.OpenTransactionAsync(finance, ct)
            : null;
        var balance = await LoadOpeningBalanceForUpdateAsync(id, finance, ct);
        if (balance is null) return Results.NotFound();
        if (balance.Status == "Posted") return Results.Ok(ToOpeningBalanceResponse(balance));
        if (balance.Status != "PendingReview") return Results.Conflict(new { code = "opening-balance-not-reviewable" });
        var actorId = currentUser.UserId ?? Guid.Empty;
        if (balance.CreatedBy == actorId && currentUser.Role != LenseeRoles.Admin)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["approval"] = ["An Accountant correction requires Admin approval."] });

        balance.Status = "Posted";
        balance.ReviewedBy = actorId;
        balance.ReviewedAt = clock.EgyptNow;
        await PersistenceBoundary.CommitAsync(finance, ct);
        if (balance.ReversesOpeningBalanceId is Guid originalId)
        {
            var original = await finance.FinanceOpeningBalances.SingleAsync(value => value.Id == originalId, ct);
            if (original.PostedEntryId is null)
                return Results.Conflict(new { code = "opening-balance-reversal-source-missing" });
            // Post the credit leg before the debit leg. A lower replacement may
            // still be valid after money from the original opening was spent.
            if (balance.Direction == FinanceLedgerService.Credit)
            {
                var replacementEntry = await ledgerService.PostMovementAsync("FinanceOpeningBalance", balance.Id, "Finance", "FinanceOpeningBalance",
                    balance.Amount, balance.FinanceAccountId, null, "TreasuryOpeningBalance", balance.Direction, actorId, balance.AsOfDate, balance.CorrelationId, ct);
                balance.PostedEntryId = replacementEntry.Id;
            }
            await ledgerService.PostMovementAsync("FinanceOpeningBalanceReversal", balance.Id, "Finance", "FinanceOpeningBalance",
                original.Amount, original.FinanceAccountId, null, "TreasuryOpeningBalanceReversal",
                original.Direction == FinanceLedgerService.Credit ? FinanceLedgerService.Debit : FinanceLedgerService.Credit,
                actorId, original.AsOfDate, balance.CorrelationId, ct);
            original.Status = "Corrected";
        }
        if (balance.PostedEntryId is null)
        {
            var entry = await ledgerService.PostMovementAsync("FinanceOpeningBalance", balance.Id, "Finance", "FinanceOpeningBalance", balance.Amount, balance.FinanceAccountId, null, "TreasuryOpeningBalance", balance.Direction, actorId, balance.AsOfDate, balance.CorrelationId, ct);
            balance.PostedEntryId = entry.Id;
        }
        await PersistenceBoundary.CommitAsync(finance, ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return Results.Ok(ToOpeningBalanceResponse(balance));
    }

    private static async Task<IResult> RejectOpeningBalanceAsync(
        Guid id, FinanceReviewRequest request, FinanceDbContext finance,
        ICurrentUser currentUser, IClock clock, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Reason)] = ["A rejection note is required."] });
        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        var balance = await LoadOpeningBalanceForUpdateAsync(id, finance, ct);
        if (balance is null) return Results.NotFound();
        if (balance.Status != "PendingReview" || balance.ReversesOpeningBalanceId is not Guid originalId)
            return Results.Conflict(new { code = "opening-balance-not-reviewable" });
        var original = await LoadOpeningBalanceForUpdateAsync(originalId, finance, ct);
        if (original is null || original.ReplacedByOpeningBalanceId != balance.Id)
            return Results.Conflict(new { code = "opening-balance-lineage-conflict" });
        balance.Status = "Rejected";
        balance.ReviewedBy = currentUser.UserId;
        balance.ReviewedAt = clock.EgyptNow;
        balance.Description = $"{balance.Description}\nRejected: {request.Reason.Trim()}";
        original.ReplacedByOpeningBalanceId = null;
        await PersistenceBoundary.CommitAsync(finance, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Results.Ok(ToOpeningBalanceResponse(balance));
    }

    private static async Task<IResult> CorrectOpeningBalanceAsync(
        Guid id,
        FinanceOpeningBalanceCorrectionRequest request,
        FinanceDbContext finance,
        FinanceLedgerService ledger,
        ICurrentUser currentUser,
        IClock clock,
        CancellationToken ct)
    {
        if (request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Description) || request.Direction is not (null or FinanceLedgerService.Credit))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Amount)] = ["A positive replacement amount and description are required."] });

        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        var original = await LoadOpeningBalanceForUpdateAsync(id, finance, ct);
        if (original is null) return Results.NotFound();
        if (original.Status != "Posted" || original.PostedEntryId is null)
            return Results.Conflict(new { code = "opening-balance-not-correctable" });
        if (original.Direction != FinanceLedgerService.Credit)
            return Results.Conflict(new { code = "legacy-debit-opening-needs-review" });
        if (original.ReplacedByOpeningBalanceId is not null)
            return Results.Conflict(new { code = "opening-balance-already-corrected", replacementId = original.ReplacedByOpeningBalanceId });

        var actorId = currentUser.UserId ?? Guid.Empty;
        if (actorId == Guid.Empty) return Results.Unauthorized();
        var replacement = new FinanceOpeningBalance
        {
            Id = Guid.NewGuid(),
            FinanceAccountId = original.FinanceAccountId,
            Amount = request.Amount,
            Direction = request.Direction is FinanceLedgerService.Credit or FinanceLedgerService.Debit ? request.Direction : original.Direction,
            AsOfDate = request.AsOfDate ?? original.AsOfDate,
            Description = request.Description.Trim(),
            Status = "Draft",
            CreatedBy = actorId,
            CreatedAt = clock.EgyptNow,
            ReversesOpeningBalanceId = original.Id,
            CorrelationId = request.CorrelationId
        };
        if (currentUser.Role == LenseeRoles.Admin)
        {
            // Replacement credit is posted first so the final corrected balance,
            // rather than a temporary reversal, determines sufficiency.
            var entry = await ledger.PostMovementAsync("FinanceOpeningBalance", replacement.Id, "Finance", "FinanceOpeningBalance", replacement.Amount,
                replacement.FinanceAccountId, null, "TreasuryOpeningBalance", replacement.Direction, actorId, replacement.AsOfDate, replacement.CorrelationId, ct);
            await ledger.PostMovementAsync("FinanceOpeningBalanceReversal", replacement.Id, "Finance", "FinanceOpeningBalance", original.Amount,
                original.FinanceAccountId, null, "TreasuryOpeningBalanceReversal", FinanceLedgerService.Debit, actorId, original.AsOfDate, replacement.CorrelationId, ct);
            replacement.Status = "Posted"; replacement.PostedEntryId = entry.Id; replacement.ReviewedBy = actorId; replacement.ReviewedAt = clock.EgyptNow;
        }
        else replacement.Status = "PendingReview";
        original.ReplacedByOpeningBalanceId = replacement.Id;
        if (currentUser.Role == LenseeRoles.Admin) original.Status = "Corrected";
        finance.FinanceOpeningBalances.Add(replacement);
        await PersistenceBoundary.CommitAsync(finance, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Results.Created($"/api/v1/finance/opening-balances/{replacement.Id}", ToOpeningBalanceResponse(replacement));
    }

    private static object ToOpeningBalanceResponse(FinanceOpeningBalance balance) => new
    {
        balance.Id,
        balance.FinanceAccountId,
        balance.Amount,
        balance.Direction,
        balance.AsOfDate,
        balance.Description,
        balance.Status,
        balance.CreatedBy,
        balance.CreatedAt,
        balance.ReviewedBy,
        balance.ReviewedAt,
        balance.PostedEntryId,
        balance.ReversesOpeningBalanceId,
        balance.ReplacedByOpeningBalanceId,
        balance.CorrelationId
    };

    private static object ToAccountResponse(FinanceAccount account) => new
    {
        account.Id,
        account.Name,
        account.Type,
        account.IsActive,
        account.Reference,
        account.Details,
        account.CreatedBy,
        account.CreatedAt,
        account.UpdatedAt
    };

    private static object ToExpenseResponse(FinanceExpense expense) => new
    {
        expense.Id,
        expense.FinanceAccountId,
        expense.Amount,
        expense.Category,
        expense.CategoryId,
        expense.CorrectionNote,
        expense.TransferId,
        expense.MovementMethod,
        expense.BusinessDate,
        expense.Description,
        expense.Status,
        expense.CreatedByUserId,
        expense.CreatedAt,
        expense.ApprovedByUserId,
        expense.ApprovedAt,
        expense.PostedFinanceLedgerEntryId,
        expense.ReversesExpenseId,
        expense.ReplacedByExpenseId,
        expense.ExternalReference,
        expense.CorrelationId
    };

    private static object ToWithdrawalResponse(CLevelWithdrawal withdrawal) => new
    {
        withdrawal.Id,
        withdrawal.AssignedToCLevelUserId,
        withdrawal.AssignedByUserId,
        withdrawal.FinanceAccountId,
        withdrawal.Amount,
        withdrawal.MovementMethod,
        withdrawal.BusinessDate,
        withdrawal.Reason,
        withdrawal.CategoryId,
        withdrawal.CorrectionNote,
        withdrawal.Status,
        withdrawal.CreatedByUserId,
        withdrawal.CreatedAt,
        withdrawal.ApprovedByUserId,
        withdrawal.ApprovedAt,
        withdrawal.PostedFinanceLedgerEntryId,
        withdrawal.ReversesWithdrawalId,
        withdrawal.ReplacedByWithdrawalId,
        withdrawal.ExternalReference,
        withdrawal.CorrelationId
    };
}

public sealed record FinanceAccountRequest(string Name, string Type, string? Reference, string? Details);
public sealed record FinanceOpeningBalanceRequest(Guid FinanceAccountId, decimal Amount, string Direction, DateOnly AsOfDate, string Description, string? CorrelationId);
public sealed record FinanceOpeningBalanceCorrectionRequest(decimal Amount, string? Direction, DateOnly? AsOfDate, string Description, string? CorrelationId);
public sealed record FinanceExpenseRequest(Guid FinanceAccountId, decimal Amount, string Category, string MovementMethod, DateOnly BusinessDate, string? Description, string? ExternalReference, string? CorrelationId);
public sealed record FinanceExpenseCorrectionRequest(Guid FinanceAccountId, decimal Amount, string? Category, string? MovementMethod, DateOnly? BusinessDate, string? Description, string? ExternalReference, string? CorrelationId);
public sealed record FinanceReviewRequest(string Reason);
public sealed record CLevelWithdrawalRequest(Guid AssignedToCLevelUserId, Guid FinanceAccountId, decimal Amount, string MovementMethod, DateOnly BusinessDate, string Reason, string? ExternalReference, string? CorrelationId, Guid? CategoryId = null);
public sealed record CLevelWithdrawalCorrectionRequest(Guid AssignedToCLevelUserId, Guid FinanceAccountId, decimal Amount, string? MovementMethod, DateOnly? BusinessDate, string? Reason, string? ExternalReference, string? CorrelationId);
