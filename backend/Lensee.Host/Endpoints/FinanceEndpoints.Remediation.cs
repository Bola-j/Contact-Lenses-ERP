using Lensee.Host.Infrastructure;
using Lensee.Modules.Finance.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static partial class FinanceEndpoints
{
    private static async Task<Guid[]> WithdrawalLineageIdsAsync(FinanceDbContext finance, CLevelWithdrawal withdrawal, CancellationToken ct)
    {
        var ids = new List<Guid> { withdrawal.Id };
        var cursor = withdrawal.ReversesWithdrawalId;
        while (cursor is Guid previousId)
        {
            if (ids.Contains(previousId) || ids.Count > 100)
                throw new InvalidOperationException("Withdrawal correction lineage is invalid.");
            ids.Add(previousId);
            cursor = await finance.CLevelWithdrawals.AsNoTracking().Where(value => value.Id == previousId)
                .Select(value => value.ReversesWithdrawalId).SingleOrDefaultAsync(ct);
        }
        return ids.ToArray();
    }

    private static async Task LockFinanceAccountAsync(FinanceDbContext finance, Guid id, CancellationToken ct)
    {
        if (finance.Database.IsNpgsql())
        {
            var key = $"finance-account:{id}";
            await finance.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }
    }

    private static async Task<IResult> ListCategoriesAsync(FinanceDbContext finance, string? kind, CancellationToken ct)
    {
        var query = finance.FinanceCategories.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(kind)) query = query.Where(value => value.Kind == kind);
        return Results.Ok(await query.OrderBy(value => value.Kind).ThenBy(value => value.SortOrder).ThenBy(value => value.EnglishName).ToListAsync(ct));
    }

    private static async Task<IResult> CreateCategoryAsync(FinanceCategoryRequest request, FinanceDbContext finance, ICurrentUser user, IClock clock, CancellationToken ct)
    {
        if (request.Kind is not ("Expense" or "Withdrawal") || string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.EnglishName) || string.IsNullOrWhiteSpace(request.ArabicName) || request.SortOrder < 0)
            return Results.BadRequest(new { code = "invalid-category" });
        var code = request.Code.Trim();
        if (await finance.FinanceCategories.AnyAsync(value => value.Kind == request.Kind && value.Code == code, ct))
            return Results.Conflict(new { code = "category-code-exists" });
        if (request.ParentId is Guid parentId && !await finance.FinanceCategories.AnyAsync(value => value.Id == parentId && value.Kind == request.Kind, ct))
            return Results.BadRequest(new { code = "invalid-parent-category" });
        var category = new FinanceCategory
        {
            Id = Guid.NewGuid(),
            Kind = request.Kind,
            Code = code,
            EnglishName = request.EnglishName.Trim(),
            ArabicName = request.ArabicName.Trim(),
            ParentId = request.ParentId,
            SortOrder = request.SortOrder,
            IsActive = true,
            CreatedBy = user.UserId ?? Guid.Empty,
            CreatedAt = clock.EgyptNow
        };
        finance.FinanceCategories.Add(category);
        await finance.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/finance/categories/{category.Id}", category);
    }

    private static async Task<IResult> UpdateCategoryAsync(Guid id, FinanceCategoryUpdateRequest request, FinanceDbContext finance, IClock clock, CancellationToken ct)
    {
        var category = await finance.FinanceCategories.SingleOrDefaultAsync(value => value.Id == id, ct);
        if (category is null) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(request.EnglishName) || string.IsNullOrWhiteSpace(request.ArabicName) || request.SortOrder < 0)
            return Results.BadRequest(new { code = "invalid-category" });
        category.EnglishName = request.EnglishName.Trim();
        category.ArabicName = request.ArabicName.Trim();
        category.SortOrder = request.SortOrder;
        category.IsActive = request.IsActive;
        category.UpdatedAt = clock.EgyptNow;
        await finance.SaveChangesAsync(ct);
        return Results.Ok(category);
    }

    private static async Task<IResult> ListTransfersAsync(FinanceDbContext finance, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        var query = finance.FinanceTransfers.AsNoTracking().OrderByDescending(value => value.BusinessDate).ThenByDescending(value => value.CreatedAt);
        return Results.Ok(new { page, pageSize, totalCount = await query.CountAsync(ct), items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct) });
    }

    private static string AccountMethod(FinanceAccount account) => account.Type switch
    {
        FinanceLedgerService.CashOnHand => "CashHandToHand",
        FinanceLedgerService.BankAccount => "BankTransfer",
        FinanceLedgerService.Wallet => "Wallet",
        _ => throw new InvalidOperationException("Invalid Finance account type.")
    };

    private static async Task<IResult> PostTransferAsync(FinanceTransferRequest request, FinanceDbContext finance, FinanceLedgerService ledger, ICurrentUser user, IClock clock, CancellationToken ct)
    {
        if (request.ClientRequestId == Guid.Empty || request.SourceAccountId == Guid.Empty || request.DestinationAccountId == Guid.Empty ||
            request.SourceAccountId == request.DestinationAccountId || request.Amount <= 0 || request.FeeAmount < 0)
            return Results.BadRequest(new { code = "invalid-transfer" });
        var reference = FinanceLedgerService.NormalizeExternalReference(request.ExternalReference);
        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        if (finance.Database.IsNpgsql())
        {
            var key = $"finance-transfer-request:{request.ClientRequestId}";
            await finance.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }
        var prior = await finance.FinanceTransfers.AsNoTracking().SingleOrDefaultAsync(value => value.ClientRequestId == request.ClientRequestId, ct);
        if (prior is not null)
            return prior.SourceAccountId == request.SourceAccountId && prior.DestinationAccountId == request.DestinationAccountId &&
                prior.Amount == request.Amount && prior.FeeAmount == request.FeeAmount && prior.BusinessDate == request.BusinessDate &&
                prior.ExternalReference == reference ? Results.Ok(prior) : Results.Conflict(new { code = "transfer-request-conflict" });
        if (finance.Database.IsNpgsql())
            foreach (var id in new[] { request.SourceAccountId, request.DestinationAccountId }.Order())
            {
                var key = $"finance-account:{id}";
                await finance.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
            }
        if (reference is not null)
        {
            if (finance.Database.IsNpgsql())
            {
                var key = $"finance-reference:{reference}";
                await finance.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
            }
            var existing = await finance.FinanceTransfers.AsNoTracking().SingleOrDefaultAsync(value => value.ExternalReference == reference, ct);
            if (existing is not null)
                return existing.SourceAccountId == request.SourceAccountId && existing.DestinationAccountId == request.DestinationAccountId &&
                    existing.Amount == request.Amount && existing.FeeAmount == request.FeeAmount
                    ? Results.Ok(existing) : Results.Conflict(new { code = "transfer-reference-conflict" });
            if (await finance.PaymentMovementRegistries.AnyAsync(value => value.NormalizedExternalReference == reference, ct))
                return Results.Conflict(new { code = "external-reference-already-posted" });
        }
        var accounts = await finance.FinanceAccounts.Where(value => (value.Id == request.SourceAccountId || value.Id == request.DestinationAccountId) && value.IsActive).ToListAsync(ct);
        if (accounts.Count != 2) return Results.BadRequest(new { code = "inactive-transfer-account" });
        var available = await finance.FinanceLedgerEntries.Where(value => value.FinanceAccountId == request.SourceAccountId && value.Status == "Posted")
            .SumAsync(value => (decimal?)(value.Direction == FinanceLedgerService.Credit ? value.Amount : -value.Amount), ct) ?? 0m;
        if (available < request.Amount + request.FeeAmount) return Results.Conflict(new { code = "insufficient-funds" });
        var source = accounts.Single(value => value.Id == request.SourceAccountId);
        var destination = accounts.Single(value => value.Id == request.DestinationAccountId);
        var actor = user.UserId ?? Guid.Empty;
        var transfer = new FinanceTransfer
        {
            Id = Guid.NewGuid(),
            ClientRequestId = request.ClientRequestId,
            SourceAccountId = source.Id,
            DestinationAccountId = destination.Id,
            Amount = request.Amount,
            FeeAmount = request.FeeAmount,
            BusinessDate = request.BusinessDate,
            Notes = Clean(request.Notes),
            ExternalReference = reference,
            CreatedBy = actor,
            CreatedAt = clock.EgyptNow
        };
        // The external reference belongs to the transfer envelope. Ledger legs have independent source identities.
        var debit = await ledger.PostMovementAsync("FinanceTransferOut", transfer.Id, "Finance", AccountMethod(source), request.Amount, source.Id,
            null, "InternalTransfer", FinanceLedgerService.Debit, actor, request.BusinessDate, null, ct);
        var credit = await ledger.PostMovementAsync("FinanceTransferIn", transfer.Id, "Finance", AccountMethod(destination), request.Amount, destination.Id,
            null, "InternalTransfer", FinanceLedgerService.Credit, actor, request.BusinessDate, null, ct);
        transfer.SourceEntryId = debit.Id; transfer.DestinationEntryId = credit.Id;
        if (request.FeeAmount > 0)
        {
            var feeCategoryId = await finance.FinanceCategories.AsNoTracking()
                .Where(value => value.Kind == "Expense" && value.Code == "TransferFee")
                .Select(value => (Guid?)value.Id).SingleOrDefaultAsync(ct);
            var fee = new FinanceExpense
            {
                Id = Guid.NewGuid(),
                FinanceAccountId = source.Id,
                Amount = request.FeeAmount,
                Category = "TransferFee",
                CategoryId = feeCategoryId,
                MovementMethod = AccountMethod(source),
                BusinessDate = request.BusinessDate,
                Description = Clean(request.FeeNotes) ?? "Transfer fee",
                Status = "Paid",
                TransferId = transfer.Id,
                CreatedByUserId = actor,
                CreatedAt = clock.EgyptNow,
                ApprovedByUserId = actor,
                ApprovedAt = clock.EgyptNow
            };
            finance.FinanceExpenses.Add(fee);
            var feeEntry = await ledger.PostMovementAsync("FinanceExpense", fee.Id, "Finance", fee.MovementMethod, fee.Amount, fee.FinanceAccountId,
                null, "OperatingExpense", FinanceLedgerService.Debit, actor, request.BusinessDate, null, ct);
            fee.PostedFinanceLedgerEntryId = feeEntry.Id;
            transfer.FeeExpenseId = fee.Id;
        }
        finance.FinanceTransfers.Add(transfer);
        await finance.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Results.Created($"/api/v1/finance/transfers/{transfer.Id}", transfer);
    }

    private static async Task<IResult> ListWithdrawalRepaymentsAsync(Guid id, FinanceDbContext finance, CancellationToken ct)
    {
        if (!await finance.CLevelWithdrawals.AnyAsync(value => value.Id == id, ct)) return Results.NotFound();
        var withdrawal = await finance.CLevelWithdrawals.AsNoTracking().SingleAsync(value => value.Id == id, ct);
        var lineageIds = await WithdrawalLineageIdsAsync(finance, withdrawal, ct);
        var items = await finance.CLevelWithdrawalRepayments.AsNoTracking().Where(value => lineageIds.Contains(value.WithdrawalId))
            .OrderBy(value => value.BusinessDate).ThenBy(value => value.CreatedAt).ToListAsync(ct);
        var repaid = items.Where(value => value.ReplacedByRepaymentId == null).Sum(value => value.Amount);
        return Results.Ok(new
        {
            withdrawalId = id,
            amount = withdrawal.Amount,
            repaid,
            remaining = withdrawal.Amount - repaid,
            items
        });
    }

    private static async Task<IResult> PostWithdrawalRepaymentAsync(Guid id, WithdrawalRepaymentRequest request, FinanceDbContext finance, FinanceLedgerService ledger, ICurrentUser user, IClock clock, CancellationToken ct)
    {
        if (request.ClientRequestId == Guid.Empty || request.Amount <= 0) return Results.BadRequest(new { code = "invalid-repayment-amount" });
        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        if (finance.Database.IsNpgsql())
        {
            var key = $"finance-repayment-request:{request.ClientRequestId}";
            await finance.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }
        var prior = await finance.CLevelWithdrawalRepayments.AsNoTracking().SingleOrDefaultAsync(value => value.ClientRequestId == request.ClientRequestId, ct);
        if (prior is not null)
            return prior.WithdrawalId == id && prior.FinanceAccountId == request.FinanceAccountId && prior.Amount == request.Amount &&
                prior.MovementMethod == request.MovementMethod && prior.BusinessDate == request.BusinessDate &&
                prior.Notes == Clean(request.Notes) && prior.ExternalReference == FinanceLedgerService.NormalizeExternalReference(request.ExternalReference)
                ? Results.Ok(prior) : Results.Conflict(new { code = "repayment-request-conflict" });
        var withdrawal = await LoadWithdrawalForUpdateAsync(id, finance, ct);
        if (withdrawal is null) return Results.NotFound();
        if (withdrawal.Status != "Posted" || withdrawal.ReplacedByWithdrawalId is not null) return Results.Conflict(new { code = "withdrawal-not-current" });
        var reference = FinanceLedgerService.NormalizeExternalReference(request.ExternalReference);
        if (reference is not null)
        {
            var existing = await finance.CLevelWithdrawalRepayments.AsNoTracking().SingleOrDefaultAsync(value => value.ExternalReference == reference, ct);
            if (existing is not null) return existing.WithdrawalId == id && existing.Amount == request.Amount && existing.FinanceAccountId == request.FinanceAccountId &&
                existing.MovementMethod == request.MovementMethod && existing.BusinessDate == request.BusinessDate && existing.Notes == Clean(request.Notes)
                ? Results.Ok(existing) : Results.Conflict(new { code = "repayment-reference-conflict" });
        }
        var lineageIds = await WithdrawalLineageIdsAsync(finance, withdrawal, ct);
        var repaid = await finance.CLevelWithdrawalRepayments.Where(value => lineageIds.Contains(value.WithdrawalId) && value.ReplacedByRepaymentId == null).SumAsync(value => (decimal?)value.Amount, ct) ?? 0m;
        if (request.Amount > withdrawal.Amount - repaid) return Results.Conflict(new { code = "repayment-exceeds-outstanding", remaining = withdrawal.Amount - repaid });
        var account = await GetActiveAccountAsync(finance, request.FinanceAccountId, ct);
        if (account is null || !MovementMatchesAccount(request.MovementMethod, account.Type)) return Results.BadRequest(new { code = "invalid-repayment-account" });
        var actor = user.UserId ?? Guid.Empty;
        var repayment = new CLevelWithdrawalRepayment
        {
            Id = Guid.NewGuid(),
            ClientRequestId = request.ClientRequestId,
            WithdrawalId = id,
            FinanceAccountId = account.Id,
            Amount = request.Amount,
            MovementMethod = request.MovementMethod,
            BusinessDate = request.BusinessDate,
            Notes = Clean(request.Notes),
            ExternalReference = reference,
            CreatedBy = actor,
            CreatedAt = clock.EgyptNow
        };
        var entry = await ledger.PostMovementAsync("CLevelWithdrawalRepayment", repayment.Id, "Finance", repayment.MovementMethod,
            repayment.Amount, repayment.FinanceAccountId, reference, "CLevelWithdrawalRepayment", FinanceLedgerService.Credit,
            actor, request.BusinessDate, null, ct);
        repayment.PostedEntryId = entry.Id;
        finance.CLevelWithdrawalRepayments.Add(repayment);
        await finance.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Results.Created($"/api/v1/finance/withdrawals/{id}/repayments/{repayment.Id}", repayment);
    }

    private static async Task<IResult> CorrectWithdrawalRepaymentAsync(Guid id, Guid repaymentId, WithdrawalRepaymentCorrectionRequest request,
        FinanceDbContext finance, FinanceLedgerService ledger, ICurrentUser user, IClock clock, CancellationToken ct)
    {
        if (request.ClientRequestId == Guid.Empty || request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Reason))
            return Results.BadRequest(new { code = "repayment-correction-note-and-positive-amount-required" });
        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        if (finance.Database.IsNpgsql())
        {
            var key = $"finance-repayment-request:{request.ClientRequestId}";
            await finance.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }
        var prior = await finance.CLevelWithdrawalRepayments.AsNoTracking().SingleOrDefaultAsync(value => value.ClientRequestId == request.ClientRequestId, ct);
        if (prior is not null)
            return prior.ReversesRepaymentId == repaymentId && prior.WithdrawalId == id && prior.Amount == request.Amount &&
                prior.FinanceAccountId == request.FinanceAccountId && prior.MovementMethod == request.MovementMethod &&
                prior.BusinessDate == request.BusinessDate && prior.CorrectionNote == request.Reason.Trim() &&
                prior.Notes == Clean(request.Notes) && prior.ExternalReference == FinanceLedgerService.NormalizeExternalReference(request.ExternalReference)
                ? Results.Ok(prior) : Results.Conflict(new { code = "repayment-correction-request-conflict" });
        var withdrawal = await LoadWithdrawalForUpdateAsync(id, finance, ct);
        if (withdrawal is null) return Results.NotFound();
        if (withdrawal.Status != "Posted" || withdrawal.ReplacedByWithdrawalId is not null)
            return Results.Conflict(new { code = "withdrawal-not-current" });
        var lineageIds = await WithdrawalLineageIdsAsync(finance, withdrawal, ct);
        var original = await finance.CLevelWithdrawalRepayments.SingleOrDefaultAsync(value => value.Id == repaymentId && lineageIds.Contains(value.WithdrawalId), ct);
        if (original is null) return Results.NotFound();
        if (original.ReplacedByRepaymentId is not null) return Results.Conflict(new { code = "repayment-already-corrected" });
        var account = await GetActiveAccountAsync(finance, request.FinanceAccountId, ct);
        if (account is null || !MovementMatchesAccount(request.MovementMethod, account.Type))
            return Results.BadRequest(new { code = "invalid-repayment-account" });
        var reference = FinanceLedgerService.NormalizeExternalReference(request.ExternalReference);
        if (reference is not null && await finance.CLevelWithdrawalRepayments.AnyAsync(value => value.ExternalReference == reference, ct))
            return Results.Conflict(new { code = "repayment-reference-conflict" });
        var otherRepaid = await finance.CLevelWithdrawalRepayments
            .Where(value => lineageIds.Contains(value.WithdrawalId) && value.ReplacedByRepaymentId == null && value.Id != original.Id)
            .SumAsync(value => (decimal?)value.Amount, ct) ?? 0m;
        if (otherRepaid + request.Amount > withdrawal.Amount)
            return Results.Conflict(new { code = "repayment-exceeds-outstanding", remaining = withdrawal.Amount - otherRepaid });
        foreach (var accountId in new[] { original.FinanceAccountId, request.FinanceAccountId }.Distinct().Order())
            await LockFinanceAccountAsync(finance, accountId, ct);
        var originalEntry = await finance.FinanceLedgerEntries.SingleOrDefaultAsync(value => value.Id == original.PostedEntryId, ct);
        if (originalEntry is null) return Results.Conflict(new { code = "repayment-ledger-source-missing" });
        var actor = user.UserId ?? Guid.Empty;
        var replacement = new CLevelWithdrawalRepayment
        {
            Id = Guid.NewGuid(),
            ClientRequestId = request.ClientRequestId,
            WithdrawalId = id,
            FinanceAccountId = request.FinanceAccountId,
            Amount = request.Amount,
            MovementMethod = request.MovementMethod,
            BusinessDate = request.BusinessDate,
            Notes = Clean(request.Notes),
            CorrectionNote = request.Reason.Trim(),
            ExternalReference = reference,
            CreatedBy = actor,
            CreatedAt = clock.EgyptNow,
            ReversesRepaymentId = original.Id
        };
        await ledger.ReverseMovementAsync(originalEntry, "CLevelWithdrawalRepaymentReversal", replacement.Id, actor, null, ct);
        var posted = await ledger.PostMovementAsync("CLevelWithdrawalRepayment", replacement.Id, "Finance", replacement.MovementMethod,
            replacement.Amount, replacement.FinanceAccountId, reference, "CLevelWithdrawalRepayment", FinanceLedgerService.Credit,
            actor, replacement.BusinessDate, null, ct);
        replacement.PostedEntryId = posted.Id;
        original.ReplacedByRepaymentId = replacement.Id;
        finance.CLevelWithdrawalRepayments.Add(replacement);
        await finance.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Results.Created($"/api/v1/finance/withdrawals/{id}/repayments/{replacement.Id}", replacement);
    }
}

public sealed record FinanceCategoryRequest(string Kind, string Code, string EnglishName, string ArabicName, Guid? ParentId, int SortOrder);
public sealed record FinanceCategoryUpdateRequest(string EnglishName, string ArabicName, int SortOrder, bool IsActive);
public sealed record FinanceTransferRequest(Guid ClientRequestId, Guid SourceAccountId, Guid DestinationAccountId, decimal Amount, decimal FeeAmount, DateOnly BusinessDate, string? Notes, string? FeeNotes, string? ExternalReference);
public sealed record WithdrawalRepaymentRequest(Guid ClientRequestId, Guid FinanceAccountId, decimal Amount, string MovementMethod, DateOnly BusinessDate, string? Notes, string? ExternalReference);
public sealed record WithdrawalRepaymentCorrectionRequest(Guid ClientRequestId, Guid FinanceAccountId, decimal Amount, string MovementMethod, DateOnly BusinessDate, string Reason, string? Notes, string? ExternalReference);
