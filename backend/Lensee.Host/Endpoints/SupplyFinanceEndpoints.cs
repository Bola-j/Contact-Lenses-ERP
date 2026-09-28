using Lensee.Host.Infrastructure;
using Lensee.Modules.Finance.Data;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static class SupplyFinanceEndpoints
{
    private static readonly string[] Categories = ["ProductCost", "CustomsCost", "ShipmentCost", "Handling", "SupplierPurchase", "Freight", "Customs", "Duties", "Clearance", "Insurance", "Transport", "Storage", "Miscellaneous"];
    private static readonly string[] Methods = ["CashHandToHand", "CashTransaction", "BankTransfer", "Wallet"];

    public static RouteGroupBuilder MapSupplyFinanceEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/finance/supply-logs").WithTags("Finance Supply").RequireAuthorization();
        group.MapGet("/", ListAsync).RequireAuthorization("supply.costs.read");
        group.MapGet("/by-shipment/{shipmentId:guid}", GetByShipmentAsync).RequireAuthorization("supply.costs.read");
        group.MapGet("/{id:guid}", GetAsync).RequireAuthorization("supply.costs.read");
        group.MapPost("/{id:guid}/costs", AddCostAsync).RequireAuthorization("finance.expense.create");
        group.MapPost("/{id:guid}/costs/{costId:guid}/correct", CorrectCostAsync).RequireAuthorization("finance.expense.create");
        group.MapPost("/{id:guid}/installments", AddInstallmentAsync).RequireAuthorization("finance.expense.create");
        group.MapPut("/{id:guid}/installments/{installmentId:guid}", UpdateInstallmentAsync).RequireAuthorization("finance.expense.create");
        group.MapPost("/{id:guid}/installments/{installmentId:guid}/post", PostInstallmentAsync).RequireAuthorization("finance.expense.create");
        group.MapPost("/{id:guid}/installments/{installmentId:guid}/correct", CorrectInstallmentAsync).RequireAuthorization("finance.expense.create");
        return group;
    }
    private static async Task<IResult> ListAsync(string? search, int? page, int? pageSize, FinanceDbContext finance, OperationsDbContext operations, CancellationToken ct)
    {
        var request = new PageRequest(page ?? 1, pageSize ?? 50);
        var query = finance.SupplyFinanceLogs.AsNoTracking().Include(x => x.CostEntries).Include(x => x.Installments).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(x => x.ShipmentNumber.ToLower().Contains(term) || x.SupplierName.ToLower().Contains(term));
        }
        var total = await query.CountAsync(ct);
        var values = await query.OrderByDescending(x => x.CreatedAt).Skip(request.Skip).Take(request.PageSize).ToListAsync(ct);
        var rows = new List<object>();
        foreach (var value in values) rows.Add(await ToResponseAsync(value, operations, ct));
        return Results.Ok(new PagedResult<object>(rows, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> GetByShipmentAsync(Guid shipmentId, FinanceDbContext finance, OperationsDbContext operations, CancellationToken ct)
    {
        var value = await finance.SupplyFinanceLogs.AsNoTracking().Include(x => x.CostEntries).Include(x => x.Installments).SingleOrDefaultAsync(x => x.SupplyShipmentId == shipmentId, ct);
        return value is null ? Results.NotFound() : Results.Ok(await ToResponseAsync(value, operations, ct));
    }
    private static async Task<IResult> GetAsync(Guid id, FinanceDbContext finance, OperationsDbContext operations, CancellationToken ct)
    {
        var value = await finance.SupplyFinanceLogs.AsNoTracking().Include(x => x.CostEntries).Include(x => x.Installments).SingleOrDefaultAsync(x => x.Id == id, ct);
        return value is null ? Results.NotFound() : Results.Ok(await ToResponseAsync(value, operations, ct));
    }
    private static async Task<IResult> AddCostAsync(Guid id, SupplyFinanceCostRequest request, SupplyFinanceLogService service, FinanceDbContext finance, ICurrentUser user, IClock clock, CancellationToken ct)
    {
        if (!Categories.Contains(request.Category, StringComparer.Ordinal) || request.Amount <= 0) return Results.ValidationProblem(new Dictionary<string,string[]> { [nameof(request.Category)] = ["Supply cost category and a positive amount are required."] });
        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        await LockSupplyFinanceLogAsync(finance, id, ct);
        if (!await finance.SupplyFinanceLogs.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
        if (string.Equals(request.Category, "SupplierPurchase", StringComparison.Ordinal) && await finance.SupplyFinanceCostEntries.AnyAsync(x => x.SupplyFinanceLogId == id && x.Status == "Active" && x.Category == "SupplierPurchase", ct))
            return Results.Conflict(new { code = "supply-finance-purchase-total-exists" });
        SupplyFinanceCostEntry cost;
        try
        {
            cost = await service.AddCostAsync(id, request.Category, request.Amount, request.BusinessDate ?? DateOnly.FromDateTime(clock.EgyptNow), Clean(request.Notes), user.UserId ?? Guid.Empty, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { code = "supply-cost-save-concurrency-conflict", detail = "The supply finance log changed during the cost entry. Refresh and try again." });
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { code = "supply-cost-save-conflict", detail = "The supply cost could not be saved." });
        }
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Results.Created($"/api/v1/finance/supply-logs/{id}", ToCostResponse(cost));
    }
    private static async Task<IResult> CorrectCostAsync(Guid id, Guid costId, SupplyFinanceCostCorrectionRequest request, FinanceDbContext finance, ICurrentUser user, IClock clock, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000 || !Categories.Contains(request.Category, StringComparer.Ordinal) || request.Amount <= 0)
            return Results.ValidationProblem(new Dictionary<string,string[]> { [nameof(request.Reason)] = ["A correction reason, supported category, and positive replacement amount are required."] });
        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        await LockSupplyFinanceLogAsync(finance, id, ct);
        var cost = await finance.SupplyFinanceCostEntries.SingleOrDefaultAsync(x => x.Id == costId && x.SupplyFinanceLogId == id, ct);
        if (cost is null) return Results.NotFound();
        if (cost.Status != "Active" || cost.ReplacedByCostEntryId is not null) return Results.Conflict(new { code = "supply-cost-not-correctable" });
        var log = await finance.SupplyFinanceLogs
            .Include(x => x.CostEntries)
            .Include(x => x.Installments)
            .SingleAsync(x => x.Id == id, ct);
        var postedPayments = log.Installments.Where(x => x.Status == "Posted").Sum(x => x.Amount);
        var correctedTotal = log.CostEntries.Where(x => x.Status == "Active" && x.Id != cost.Id).Sum(x => x.Amount) + request.Amount;
        if (postedPayments > correctedTotal)
            return Results.Conflict(new { code = "supply-cost-below-posted-payments", detail = "The corrected landed cost cannot be lower than supplier payments already posted." });
        var replacement = new SupplyFinanceCostEntry { Id = Guid.NewGuid(), SupplyFinanceLogId = id, Category = request.Category, Origin = "Finance", Amount = request.Amount, BusinessDate = request.BusinessDate ?? DateOnly.FromDateTime(clock.EgyptNow), Notes = Clean(request.Notes), Status = "Active", CreatedByUserId = user.UserId ?? Guid.Empty, CreatedAt = clock.EgyptNow, ReversesCostEntryId = cost.Id, CorrectionNote = request.Reason.Trim() };
        cost.Status = "Corrected"; cost.UpdatedByUserId = user.UserId ?? Guid.Empty; cost.UpdatedAt = clock.EgyptNow; cost.ReplacedByCostEntryId = replacement.Id;
        finance.SupplyFinanceCostEntries.Add(replacement);
        try { await PersistenceBoundary.CommitAsync(finance, ct); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { code = "supply-cost-correction-transition-conflict", detail = "The supply cost was changed by another request. Refresh and try again." }); }
        catch (DbUpdateException) { return Results.Conflict(new { code = "supply-cost-correction-save-conflict", detail = "The supply cost correction could not be saved." }); }
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Results.Created($"/api/v1/finance/supply-logs/{id}", ToCostResponse(replacement));
    }
    private static async Task<IResult> AddInstallmentAsync(Guid id, SupplyInstallmentRequest request, SupplyFinanceLogService service, FinanceDbContext finance, ICurrentUser user, IClock clock, CancellationToken ct)
    {
        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        await LockSupplyFinanceLogAsync(finance, id, ct);
        if (!await finance.SupplyFinanceLogs.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
        if (request.FinanceAccountId == Guid.Empty || request.Amount <= 0 || !Methods.Contains(request.MovementMethod, StringComparer.Ordinal)) return Results.ValidationProblem(new Dictionary<string,string[]> { [nameof(request.Amount)] = ["An account, a positive amount, and a valid payment method are required."] });
        var account = await finance.FinanceAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.FinanceAccountId && x.IsActive, ct);
        if (account is null) return Results.ValidationProblem(new Dictionary<string,string[]> { [nameof(request.FinanceAccountId)] = ["The selected Finance account is inactive or does not exist."] });
        if (!FinanceLedgerService.MovementMatchesAccount(request.MovementMethod, account.Type, "SupplierPurchase"))
            return Results.ValidationProblem(new Dictionary<string,string[]> { [nameof(request.MovementMethod)] = ["The payment method does not match the selected Finance account."] });
        SupplySupplierInstallment installment;
        try
        {
            installment = await service.CreateInstallmentAsync(new SupplySupplierInstallment { SupplyFinanceLogId = id, FinanceAccountId = request.FinanceAccountId, Amount = request.Amount, MovementMethod = request.MovementMethod, BusinessDate = request.BusinessDate ?? DateOnly.FromDateTime(clock.EgyptNow), ExternalReference = Clean(request.ExternalReference), Notes = Clean(request.Notes), CorrelationId = Clean(request.CorrelationId) }, user.UserId ?? Guid.Empty, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Conflict(new { code = "supply-installment-create-concurrency-conflict", detail = "The supply finance log changed while creating the installment. Refresh and try again." });
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { code = "supply-installment-create-save-conflict", detail = "The supplier installment could not be saved." });
        }
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Results.Created($"/api/v1/finance/supply-logs/{id}", ToInstallmentResponse(installment));
    }
    private static async Task<IResult> UpdateInstallmentAsync(Guid id, Guid installmentId, SupplyInstallmentRequest request, FinanceDbContext finance, ICurrentUser user, IClock clock, CancellationToken ct)
    {
        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        await LockSupplyFinanceLogAsync(finance, id, ct);
        var value = await finance.SupplySupplierInstallments.SingleOrDefaultAsync(x => x.Id == installmentId && x.SupplyFinanceLogId == id, ct);
        if (value is null) return Results.NotFound();
        if (value.Status != "Draft") return Results.Conflict(new { code = "supply-installment-not-draft" });
        if (request.FinanceAccountId == Guid.Empty || request.Amount <= 0 || !Methods.Contains(request.MovementMethod, StringComparer.Ordinal)) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Amount)] = ["An account, positive amount, and valid payment method are required."] });
        var account = await finance.FinanceAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.FinanceAccountId && x.IsActive, ct);
        if (account is null) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.FinanceAccountId)] = ["The selected Finance account is inactive or does not exist."] });
        if (!FinanceLedgerService.MovementMatchesAccount(request.MovementMethod, account.Type, "SupplierPurchase"))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.MovementMethod)] = ["The payment method does not match the selected Finance account."] });
        value.FinanceAccountId = request.FinanceAccountId; value.Amount = request.Amount; value.MovementMethod = request.MovementMethod; value.BusinessDate = request.BusinessDate ?? DateOnly.FromDateTime(clock.EgyptNow); value.ExternalReference = Clean(request.ExternalReference); value.Notes = Clean(request.Notes); value.CorrelationId = Clean(request.CorrelationId);
        try { await finance.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { code = "supply-installment-update-transition-conflict", detail = "The installment was changed by another request. Refresh and try again." }); }
        catch (DbUpdateException) { return Results.Conflict(new { code = "supply-installment-update-save-conflict", detail = "The installment could not be updated." }); }
        if (transaction is not null) await transaction.CommitAsync(ct); return Results.Ok(ToInstallmentResponse(value));
    }
    private static async Task<IResult> PostInstallmentAsync(Guid id, Guid installmentId, FinanceDbContext finance, FinanceLedgerService ledger, ICurrentUser user, IClock clock, CancellationToken ct)
    {
        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        await LockSupplyFinanceLogAsync(finance, id, ct);
        var value = await finance.SupplySupplierInstallments.SingleOrDefaultAsync(x => x.Id == installmentId && x.SupplyFinanceLogId == id, ct);
        if (value is null) return Results.NotFound();
        if (value.Status == "Posted") return Results.Ok(value);
        if (value.Status != "Draft") return Results.Conflict(new { code = "supply-installment-not-postable" });
        var log = await finance.SupplyFinanceLogs.Include(x => x.CostEntries).Include(x => x.Installments).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (log is null) return Results.NotFound();
        var account = await finance.FinanceAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == value.FinanceAccountId && x.IsActive, ct);
        if (account is null) return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(value.FinanceAccountId)] = ["The selected Finance account is inactive or does not exist."] });
        if (!FinanceLedgerService.MovementMatchesAccount(value.MovementMethod, account.Type, "SupplierPurchase"))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(value.MovementMethod)] = ["The payment method does not match the selected Finance account."] });
        var totalCost = log.CostEntries.Where(x => x.Status == "Active").Sum(x => x.Amount);
        var alreadyPaid = log.Installments.Where(x => x.Status == "Posted" && x.Id != value.Id).Sum(x => x.Amount);
        if (alreadyPaid + value.Amount > totalCost)
            return Results.Conflict(new { code = "supply-installment-exceeds-outstanding", detail = "The payment exceeds the supply log outstanding balance." });
        try
        {
            var entry = await ledger.PostMovementAsync("SupplySupplierInstallment", value.Id, "Finance", value.MovementMethod, value.Amount, value.FinanceAccountId, value.ExternalReference, "SupplierPurchase", FinanceLedgerService.Debit, user.UserId ?? Guid.Empty, value.BusinessDate, value.CorrelationId, ct);
            value.Status = "Posted"; value.PaidByUserId = user.UserId ?? Guid.Empty; value.PaidAt = clock.EgyptNow; value.PostedFinanceLedgerEntryId = entry.Id;
            await PersistenceBoundary.CommitAsync(finance, ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return Results.Ok(ToInstallmentResponse(value));
        }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { code = "supply-installment-posting-transition-conflict", detail = "The installment was changed by another request. Refresh and try again." }); }
        catch (DbUpdateException) { return Results.Conflict(new { code = "supply-installment-posting-save-conflict", detail = "The installment posting could not be saved." }); }
        catch (InvalidOperationException ex) { return Results.Conflict(new { code = "supply-installment-posting-rejected", detail = ex.Message }); }
    }

    private static async Task<IResult> CorrectInstallmentAsync(Guid id, Guid installmentId, SupplyInstallmentCorrectionRequest request, FinanceDbContext finance, FinanceLedgerService ledger, ICurrentUser user, IClock clock, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 1000)
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Reason)] = ["A correction reason is required."] });
        if (request.Amount <= 0 || request.FinanceAccountId == Guid.Empty || !Methods.Contains(request.MovementMethod, StringComparer.Ordinal))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Amount)] = ["A positive amount, account, and valid payment method are required."] });

        await using var transaction = await PersistenceBoundary.OpenTransactionAsync(finance, ct);
        await LockSupplyFinanceLogAsync(finance, id, ct);
        var original = await finance.SupplySupplierInstallments.SingleOrDefaultAsync(x => x.Id == installmentId && x.SupplyFinanceLogId == id, ct);
        if (original is null) return Results.NotFound();
        if (original.Status != "Posted" || original.ReplacedByInstallmentId is not null || original.PostedFinanceLedgerEntryId is not Guid originalEntryId)
            return Results.Conflict(new { code = "supply-installment-not-correctable" });
        var account = await finance.FinanceAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.FinanceAccountId && x.IsActive, ct);
        if (account is null || !FinanceLedgerService.MovementMatchesAccount(request.MovementMethod, account.Type, "SupplierPurchase"))
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.FinanceAccountId)] = ["The replacement Finance account or movement method is invalid."] });
        var log = await finance.SupplyFinanceLogs.Include(x => x.CostEntries).Include(x => x.Installments).SingleAsync(x => x.Id == id, ct);
        var postedAfterReversal = log.Installments.Where(x => x.Status == "Posted" && x.Id != original.Id).Sum(x => x.Amount);
        if (postedAfterReversal + request.Amount > log.CostEntries.Where(x => x.Status == "Active").Sum(x => x.Amount))
            return Results.Conflict(new { code = "supply-installment-exceeds-outstanding" });
        var originalEntry = await finance.FinanceLedgerEntries.SingleAsync(x => x.Id == originalEntryId, ct);
        var replacement = new SupplySupplierInstallment
        {
            Id = Guid.NewGuid(), SupplyFinanceLogId = id, FinanceAccountId = request.FinanceAccountId,
            Amount = request.Amount, MovementMethod = request.MovementMethod,
            BusinessDate = request.BusinessDate ?? original.BusinessDate, ExternalReference = Clean(request.ExternalReference),
            Notes = Clean(request.Notes), Status = "Draft", CreatedByUserId = user.UserId ?? Guid.Empty,
            CreatedAt = clock.EgyptNow, ReversesInstallmentId = original.Id, CorrectionNote = request.Reason.Trim(),
            CorrelationId = Clean(request.CorrelationId)
        };
        await ledger.ReverseMovementAsync(originalEntry, "SupplySupplierInstallmentReversal", replacement.Id, user.UserId ?? Guid.Empty, replacement.CorrelationId, ct, "Supply");
        original.Status = "Corrected";
        original.ReplacedByInstallmentId = replacement.Id;
        finance.SupplySupplierInstallments.Add(replacement);
        try { await PersistenceBoundary.CommitAsync(finance, ct); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(new { code = "supply-installment-correction-transition-conflict", detail = "The installment was changed by another request. Refresh and try again." }); }
        catch (DbUpdateException) { return Results.Conflict(new { code = "supply-installment-correction-save-conflict", detail = "The installment correction could not be saved." }); }
        if (transaction is not null) await transaction.CommitAsync(ct);
        return Results.Created($"/api/v1/finance/supply-logs/{id}", ToInstallmentResponse(replacement));
    }
    private static object ToCostResponse(SupplyFinanceCostEntry value) => new { value.Id, value.SupplyFinanceLogId, value.Category, value.Origin, value.Amount, value.BusinessDate, value.Notes, value.Status, value.CreatedByUserId, value.CreatedAt, value.UpdatedByUserId, value.UpdatedAt, value.ReversesCostEntryId, value.ReplacedByCostEntryId, value.CorrectionNote };

    private static async Task LockSupplyFinanceLogAsync(FinanceDbContext finance, Guid id, CancellationToken ct)
    {
        if (!finance.Database.IsNpgsql()) return;
        var key = $"supply-finance-log:{id}";
        await finance.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
    }

    private static object ToInstallmentResponse(SupplySupplierInstallment value) => new { value.Id, value.SupplyFinanceLogId, value.FinanceAccountId, value.Amount, value.MovementMethod, value.BusinessDate, value.ExternalReference, value.Notes, value.Status, value.CreatedByUserId, value.CreatedAt, value.PaidByUserId, value.PaidAt, value.PostedFinanceLedgerEntryId, value.ReversesInstallmentId, value.ReplacedByInstallmentId, value.CorrectionNote, value.CorrelationId };

    private static async Task<object> ToResponseAsync(SupplyFinanceLog log, OperationsDbContext operations, CancellationToken ct)
    {
        var paidAmount = log.Installments.Where(installment => installment.Status == "Posted").Sum(installment => installment.Amount);
        var landedCost = log.CostEntries.Where(cost => cost.Status == "Active").Sum(cost => cost.Amount);
        var scheduledAmount = log.Installments.Where(installment => installment.Status != "Cancelled").Sum(installment => installment.Amount);
        var categoryTotals = log.CostEntries.Where(cost => cost.Status == "Active")
            .GroupBy(cost => cost.Category)
            .ToDictionary(group => group.Key, group => group.Sum(cost => cost.Amount));
        return new { log.Id, log.SupplyShipmentId, log.ShipmentNumber, log.SupplierName, log.Status, log.Notes, totalLandedCost = landedCost, scheduledAmount, amountPaid = paidAmount, outstandingBalance = landedCost - paidAmount, categoryTotals, costs = log.CostEntries.OrderBy(cost => cost.BusinessDate).Select(ToCostResponse), installments = log.Installments.OrderBy(installment => installment.BusinessDate).Select(ToInstallmentResponse) };
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record SupplyFinanceCostRequest(string Category, decimal Amount, DateOnly? BusinessDate, string? Notes);
public sealed record SupplyFinanceCostCorrectionRequest(string Category, decimal Amount, DateOnly? BusinessDate, string? Notes, string? Reason);
public sealed record SupplyInstallmentRequest(Guid FinanceAccountId, decimal Amount, string MovementMethod, DateOnly? BusinessDate, string? ExternalReference, string? Notes, string? CorrelationId);
public sealed record SupplyInstallmentCorrectionRequest(Guid FinanceAccountId, decimal Amount, string MovementMethod, DateOnly? BusinessDate, string? ExternalReference, string? Notes, string? Reason, string? CorrelationId);
