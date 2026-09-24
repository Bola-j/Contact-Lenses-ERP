using Lensee.Modules.Finance.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lensee.Host.Infrastructure;

public sealed class FinanceLedgerService
{
    public const string CashOnHand = "CashOnHand";
    public const string BankAccount = "BankAccount";
    public const string Wallet = "Wallet";
    public const string Credit = "Credit";
    public const string Debit = "Debit";

    private readonly FinanceDbContext _finance;
    private readonly IClock _clock;

    public FinanceLedgerService(FinanceDbContext finance, IClock clock)
    {
        _finance = finance;
        _clock = clock;
    }

    public static string? NormalizeExternalReference(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return string.Join(' ', value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToUpperInvariant();
    }

    public async Task<FinanceLedgerEntry> PostMovementAsync(
        string sourceType,
        Guid sourceId,
        string track,
        string movementMethod,
        decimal amount,
        Guid? financeAccountId,
        string? externalReference,
        string category,
        string direction,
        Guid actorId,
        DateOnly? businessDate = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        if (amount <= 0)
            throw new InvalidOperationException("Finance movement amount must be positive.");
        if (financeAccountId is null || financeAccountId == Guid.Empty)
            throw new InvalidOperationException("A FinanceAccountId is required for every posted finance movement.");
        if (direction is not Credit and not Debit)
            throw new InvalidOperationException("Finance movement direction is invalid.");

        var normalizedReference = NormalizeExternalReference(externalReference);
        IDbContextTransaction? ownedTransaction = null;
        try
        {
            if (_finance.Database.IsRelational() && _finance.Database.CurrentTransaction is null)
                ownedTransaction = await _finance.Database.BeginTransactionAsync(cancellationToken);

            // These transaction-scoped PostgreSQL locks close the check-then-insert
            // race without weakening the database uniqueness constraints.  The lock
            // is deliberately keyed by canonical source and, when present, the
            // normalized real-world reference.
            if (_finance.Database.IsNpgsql())
            {
                // Balance checks and posting must be serialized for every account,
                // regardless of which business module originated the movement.
                var accountLockKey = $"finance-account:{financeAccountId.Value}";
                await _finance.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({accountLockKey}, 0))", cancellationToken);
                var sourceLockKey = $"finance-source:{sourceType}:{sourceId}";
                await _finance.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({sourceLockKey}, 0))", cancellationToken);
                if (normalizedReference is not null)
                {
                    var referenceLockKey = $"finance-reference:{normalizedReference}";
                    await _finance.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({referenceLockKey}, 0))", cancellationToken);
                }
            }

            var existing = await _finance.FinanceLedgerEntries
                .SingleOrDefaultAsync(value => value.SourceType == sourceType && value.SourceId == sourceId, cancellationToken);
            if (existing is not null)
            {
                if (existing.FinanceAccountId != financeAccountId.Value || existing.Amount != amount ||
                    existing.Direction != direction || existing.Category != category ||
                    existing.MovementMethod != movementMethod || existing.ExternalReference != normalizedReference)
                    throw new InvalidOperationException("The Finance source has already been posted with different movement details.");
                if (ownedTransaction is not null) await ownedTransaction.CommitAsync(cancellationToken);
                return existing;
            }

            var account = await _finance.FinanceAccounts
                .SingleOrDefaultAsync(value => value.Id == financeAccountId.Value && value.IsActive, cancellationToken);
            if (account is null)
                throw new InvalidOperationException("The selected FinanceAccount does not exist or is inactive.");
            if (!MovementMatchesAccount(movementMethod, account.Type, category))
                throw new InvalidOperationException("The movement method does not match the selected FinanceAccount type.");
            if (direction == Debit)
            {
                var available = await _finance.FinanceLedgerEntries
                    .Where(value => value.FinanceAccountId == account.Id && value.Status == "Posted")
                    .SumAsync(value => (decimal?)(value.Direction == Credit ? value.Amount : -value.Amount), cancellationToken) ?? 0m;
                if (available < amount)
                    throw new InvalidOperationException("The selected Finance account has insufficient posted funds.");
            }
            if (normalizedReference is not null && await _finance.PaymentMovementRegistries.AnyAsync(
                    value => value.NormalizedExternalReference == normalizedReference, cancellationToken))
                throw new InvalidOperationException("The external payment reference has already been posted.");

            var now = _clock.EgyptNow;
            var ledger = new FinanceLedgerEntry
            {
                Id = Guid.NewGuid(),
                FinanceAccountId = account.Id,
                Direction = direction,
                Amount = amount,
                Category = category,
                MovementMethod = movementMethod,
                SourceType = sourceType,
                SourceId = sourceId,
                BusinessDate = businessDate ?? DateOnly.FromDateTime(now),
                Status = "Posted",
                CreatedBy = actorId,
                CreatedAt = now,
                CorrelationId = correlationId,
                ExternalReference = normalizedReference
            };
            var registry = new PaymentMovementRegistry
            {
                Id = Guid.NewGuid(),
                SourceType = sourceType,
                SourceId = sourceId,
                Track = track,
                MovementMethod = movementMethod,
                Amount = amount,
                FinanceAccountId = account.Id,
                NormalizedExternalReference = normalizedReference,
                Status = "Posted",
                CreatedBy = actorId,
                CreatedAt = now,
                CorrelationId = correlationId
            };
            _finance.PaymentMovementRegistries.Add(registry);
            _finance.FinanceLedgerEntries.Add(ledger);
            await _finance.SaveChangesAsync(cancellationToken);
            if (ownedTransaction is not null) await ownedTransaction.CommitAsync(cancellationToken);
            return ledger;
        }
        catch
        {
            if (ownedTransaction is not null) await ownedTransaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (ownedTransaction is not null) await ownedTransaction.DisposeAsync();
        }
    }

    public static bool MovementMatchesAccount(string movementMethod, string accountType, string? category = null)
    {
        if (movementMethod == "FinanceOpeningBalance")
            return category is "TreasuryOpeningBalance" or "TreasuryOpeningBalanceReversal";
        return accountType switch
        {
            CashOnHand => movementMethod is "CashHandToHand" or "CashTransaction",
            BankAccount => movementMethod == "BankTransfer",
            Wallet => movementMethod == "Wallet",
            _ => false
        };
    }

    public async Task<IReadOnlyList<FinanceAccount>> ListAccountsAsync(CancellationToken cancellationToken)
        => await _finance.FinanceAccounts.AsNoTracking().OrderBy(value => value.Type).ThenBy(value => value.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FinanceBalance>> GetExpectedBalancesAsync(CancellationToken cancellationToken)
    {
        var rows = await _finance.FinanceAccounts.AsNoTracking()
            .Select(account => new
            {
                account.Id,
                account.Name,
                account.Type,
                Balance = _finance.FinanceLedgerEntries.Where(entry => entry.FinanceAccountId == account.Id && entry.Status == "Posted")
                    .Sum(entry => (decimal?)(entry.Direction == Credit ? entry.Amount : -entry.Amount)) ?? 0m
            })
            .ToListAsync(cancellationToken);
        return rows.Select(value => new FinanceBalance(value.Id, value.Name, value.Type, value.Balance)).ToList();
    }

    public async Task<FinanceLedgerEntry> ReverseMovementAsync(
        FinanceLedgerEntry original,
        string sourceType,
        Guid sourceId,
        Guid actorId,
        string? correlationId,
        CancellationToken cancellationToken,
        string track = "Finance")
    {
        if (string.IsNullOrWhiteSpace(original.MovementMethod))
            throw new InvalidOperationException("A legacy Finance ledger movement without a movement method must be reconciled before it can be reversed.");

        var direction = original.Direction == Credit ? Debit : Credit;
        var reversal = await PostMovementAsync(
            sourceType,
            sourceId,
            track,
            original.MovementMethod,
            original.Amount,
            original.FinanceAccountId,
            null,
            $"{original.Category}Reversal",
            direction,
            actorId,
            original.BusinessDate,
            correlationId,
            cancellationToken);
        reversal.ReversesEntryId = original.Id;
        await _finance.SaveChangesAsync(cancellationToken);
        return reversal;
    }
}

public sealed record FinanceBalance(Guid FinanceAccountId, string Name, string Type, decimal ExpectedBalance);
