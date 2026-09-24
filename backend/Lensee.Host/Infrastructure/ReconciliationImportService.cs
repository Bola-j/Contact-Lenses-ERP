using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Finance.Data;
using Lensee.Modules.Payments.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lensee.Host.Infrastructure;

public sealed class ReconciliationImportService(
    FinanceDbContext finance,
    PaymentsDbContext payments,
    CrmDbContext crm,
    IClock clock)
{
    private const string SchemaVersion = "phase5.v1";
    private static readonly HashSet<string> RequiredColumns = new(StringComparer.Ordinal)
    {
        "row_type", "source_reference", "merchant_id", "finance_account_id", "amount", "as_of_date", "description",
        "legacy_payment_method", "legacy_collection_scope", "client_id", "c_level_user_id"
    };

    public async Task<ReconciliationImportPackage> RegisterAsync(RegisterReconciliationPackageRequest request, Guid actorId, CancellationToken cancellationToken)
    {
        ValidateManifest(request.Manifest, request.Csv);
        var payloadHash = Sha256(request.Csv);
        var existing = await finance.ReconciliationImportPackages.SingleOrDefaultAsync(value => value.PayloadSha256 == payloadHash, cancellationToken);
        if (existing is not null) return existing;

        var rows = ParseCsv(request.Csv);
        var sourceReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!sourceReferences.Add(row.SourceReference))
                throw new InvalidOperationException($"Duplicate source_reference '{row.SourceReference}' in the package.");
        }

        var package = new ReconciliationImportPackage
        {
            Id = Guid.NewGuid(),
            SchemaVersion = request.Manifest.SchemaVersion.Trim(),
            Signer = request.Manifest.Signer.Trim(),
            Signature = request.Manifest.Signature.Trim(),
            PayloadSha256 = payloadHash,
            SourcePeriodStart = request.Manifest.SourcePeriodStart,
            SourcePeriodEnd = request.Manifest.SourcePeriodEnd,
            ExportedAtUtc = request.Manifest.ExportedAtUtc.UtcDateTime,
            RegisteredByUserId = actorId,
            RegisteredAt = clock.EgyptNow
        };
        foreach (var row in rows)
        {
            var decision = await DecideAsync(row, request.Manifest, cancellationToken);
            package.Rows.Add(new ReconciliationImportRow
            {
                Id = Guid.NewGuid(),
                RowNumber = row.RowNumber,
                RowType = row.RowType,
                SourceReference = row.SourceReference,
                OriginalPayload = JsonSerializer.Serialize(row.Values),
                Decision = decision.Decision,
                CanonicalTrack = decision.CanonicalTrack,
                DecisionReason = decision.Reason
            });
        }
        finance.ReconciliationImportPackages.Add(package);
        await finance.SaveChangesAsync(cancellationToken);
        return package;
    }

    public async Task<ReconciliationImportPackage> ResolveAsync(Guid packageId, Guid rowId, ResolveReconciliationRowRequest request, Guid actorId, CancellationToken cancellationToken)
    {
        var package = await finance.ReconciliationImportPackages.Include(value => value.Rows).SingleOrDefaultAsync(value => value.Id == packageId, cancellationToken)
            ?? throw new KeyNotFoundException("The reconciliation package was not found.");
        if (package.Status == "Applied") throw new InvalidOperationException("An applied reconciliation package is immutable.");
        var row = package.Rows.SingleOrDefault(value => value.Id == rowId) ?? throw new KeyNotFoundException("The reconciliation row was not found.");
        if (request.Decision is not ("Approved" or "Rejected") || string.IsNullOrWhiteSpace(request.Note))
            throw new InvalidOperationException("Resolution must be Approved or Rejected and include an evidence note.");
        if (request.Decision == "Approved" && row.RowType is not ("MerchantOpeningBalance" or "TreasuryOpeningBalance"))
            throw new InvalidOperationException("Historical payment tracks and C-Level beneficiaries require a dedicated reconciliation decision and cannot be automatically applied.");
        row.Decision = request.Decision;
        row.ResolutionNote = request.Note.Trim();
        row.ResolvedByUserId = actorId;
        row.ResolvedAt = clock.EgyptNow;
        await finance.SaveChangesAsync(cancellationToken);
        return package;
    }

    public async Task<ReconciliationImportPackage> ApplyAsync(Guid packageId, string idempotencyKey, Guid actorId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new InvalidOperationException("An idempotency key is required.");
        if (!finance.Database.IsRelational() || !payments.Database.IsRelational())
            throw new InvalidOperationException("Historical reconciliation apply requires PostgreSQL transactional storage.");
        var package = await finance.ReconciliationImportPackages.Include(value => value.Rows).SingleOrDefaultAsync(value => value.Id == packageId, cancellationToken)
            ?? throw new KeyNotFoundException("The reconciliation package was not found.");
        if (package.Status == "Applied")
        {
            if (package.ApplyIdempotencyKey == idempotencyKey) return package;
            throw new InvalidOperationException("This reconciliation package has already been applied.");
        }
        if (package.Rows.Any(value => value.Decision == "PendingReview"))
            throw new InvalidOperationException("Every reconciliation row must be resolved before apply.");

        await using var transaction = await finance.Database.BeginTransactionAsync(cancellationToken);
        await payments.Database.UseTransactionAsync(transaction.GetDbTransaction(), cancellationToken);
        foreach (var row in package.Rows.Where(value => value.Decision == "Approved"))
        {
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(row.OriginalPayload) ?? throw new InvalidOperationException("Stored row payload is invalid.");
            if (row.RowType == "MerchantOpeningBalance")
                payments.MerchantOpeningBalanceCharges.Add(new MerchantOpeningBalanceCharge
                {
                    Id = Guid.NewGuid(),
                    MerchantId = RequiredGuid(values, "merchant_id"),
                    Amount = RequiredAmount(values),
                    AsOfDate = RequiredDate(values),
                    Description = RequiredText(values, "description"),
                    Status = "PendingReview",
                    CreatedBy = actorId,
                    CreatedAt = clock.EgyptNow,
                    CorrelationId = $"reconciliation:{package.Id:N}:{row.Id:N}"
                });
            else if (row.RowType == "TreasuryOpeningBalance")
                finance.FinanceOpeningBalances.Add(new FinanceOpeningBalance
                {
                    Id = Guid.NewGuid(),
                    FinanceAccountId = RequiredGuid(values, "finance_account_id"),
                    Amount = RequiredAmount(values),
                    Direction = FinanceLedgerService.Credit,
                    AsOfDate = RequiredDate(values),
                    Description = RequiredText(values, "description"),
                    Status = "PendingReview",
                    CreatedBy = actorId,
                    CreatedAt = clock.EgyptNow,
                    CorrelationId = $"reconciliation:{package.Id:N}:{row.Id:N}"
                });
            row.Decision = "Applied";
        }
        package.Status = "Applied";
        package.ApplyIdempotencyKey = idempotencyKey.Trim();
        package.AppliedByUserId = actorId;
        package.AppliedAt = clock.EgyptNow;
        await payments.SaveChangesAsync(cancellationToken);
        await finance.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return package;
    }

    private async Task<RowDecision> DecideAsync(CsvRow row, ReconciliationManifest manifest, CancellationToken cancellationToken)
    {
        if (row.RowType is not ("MerchantOpeningBalance" or "TreasuryOpeningBalance" or "LegacyPaymentTrack" or "CLevelWithdrawal"))
            return new("PendingReview", null, "Unsupported row_type; manual reconciliation is required.");
        if (row.RowType == "MerchantOpeningBalance")
        {
            if (!TryGuid(row.Values, "merchant_id", out var merchantId) || !TryAmount(row.Values, out _) || !TryDate(row.Values, out var date) || date < manifest.SourcePeriodStart || date > manifest.SourcePeriodEnd || string.IsNullOrWhiteSpace(Get(row.Values, "description")))
                return new("PendingReview", null, "Merchant opening-balance evidence is incomplete or outside the declared source period.");
            var merchant = await crm.Merchants.AsNoTracking().SingleOrDefaultAsync(value => value.Id == merchantId && !value.IsDeleted && value.Status == "Active", cancellationToken);
            return merchant is null ? new("PendingReview", null, "The signed source references no active CRM merchant.") : new("PendingReview", "MerchantAccount", "Awaiting reviewer approval before a pending opening balance is created.");
        }
        if (row.RowType == "TreasuryOpeningBalance")
        {
            if (!TryGuid(row.Values, "finance_account_id", out var accountId) || !TryAmount(row.Values, out _) || !TryDate(row.Values, out var date) || date < manifest.SourcePeriodStart || date > manifest.SourcePeriodEnd || string.IsNullOrWhiteSpace(Get(row.Values, "description")))
                return new("PendingReview", null, "Treasury opening-balance evidence is incomplete or outside the declared source period.");
            var account = await finance.FinanceAccounts.AsNoTracking().SingleOrDefaultAsync(value => value.Id == accountId && value.IsActive, cancellationToken);
            return account is null ? new("PendingReview", null, "The signed source references no active Finance account.") : new("PendingReview", "Finance", "Awaiting reviewer approval before a pending treasury opening balance is created.");
        }
        return new("PendingReview", null, "Legacy payment-track and C-Level beneficiary rows are never inferred automatically.");
    }

    private static void ValidateManifest(ReconciliationManifest manifest, string csv)
    {
        if (manifest.SchemaVersion != SchemaVersion || string.IsNullOrWhiteSpace(manifest.Signer) || string.IsNullOrWhiteSpace(manifest.Signature))
            throw new InvalidOperationException("A signed phase5.v1 manifest is required.");
        if (manifest.SourcePeriodStart > manifest.SourcePeriodEnd || manifest.ExportedAtUtc == default || string.IsNullOrWhiteSpace(csv))
            throw new InvalidOperationException("Manifest period, export timestamp, and CSV payload are required.");
        if (!string.Equals(manifest.PayloadSha256, Sha256(csv), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The CSV SHA-256 does not match the manifest.");
    }

    private static List<CsvRow> ParseCsv(string csv)
    {
        var lines = csv.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) throw new InvalidOperationException("CSV must include a header and at least one row.");
        var header = SplitCsv(lines[0]);
        if (header.Count != RequiredColumns.Count || !header.All(RequiredColumns.Contains)) throw new InvalidOperationException("CSV columns do not match the phase5.v1 schema.");
        var rows = new List<CsvRow>();
        for (var index = 1; index < lines.Length; index++)
        {
            var values = SplitCsv(lines[index]);
            if (values.Count != header.Count) throw new InvalidOperationException($"CSV row {index + 1} has an invalid column count.");
            var map = header.Select((name, valueIndex) => new KeyValuePair<string, string>(name, values[valueIndex])).ToDictionary();
            var rowType = RequiredText(map, "row_type");
            var sourceReference = RequiredText(map, "source_reference");
            rows.Add(new(index + 1, rowType, sourceReference, map));
        }
        return rows;
    }

    private static List<string> SplitCsv(string line)
    {
        var values = new List<string>(); var buffer = new StringBuilder(); var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"' && (i + 1 >= line.Length || line[i + 1] != '"')) { quoted = !quoted; continue; }
            if (line[i] == '"' && quoted && i + 1 < line.Length && line[i + 1] == '"') { buffer.Append('"'); i++; continue; }
            if (line[i] == ',' && !quoted) { values.Add(buffer.ToString().Trim()); buffer.Clear(); continue; }
            buffer.Append(line[i]);
        }
        if (quoted) throw new InvalidOperationException("CSV contains an unterminated quoted value.");
        values.Add(buffer.ToString().Trim()); return values;
    }

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Get(IReadOnlyDictionary<string, string> values, string key) => values.TryGetValue(key, out var value) ? value.Trim() : string.Empty;
    private static string RequiredText(IReadOnlyDictionary<string, string> values, string key) => !string.IsNullOrWhiteSpace(Get(values, key)) ? Get(values, key) : throw new InvalidOperationException($"CSV '{key}' is required.");
    private static bool TryGuid(IReadOnlyDictionary<string, string> values, string key, out Guid result) => Guid.TryParse(Get(values, key), out result) && result != Guid.Empty;
    private static Guid RequiredGuid(IReadOnlyDictionary<string, string> values, string key) => TryGuid(values, key, out var value) ? value : throw new InvalidOperationException($"CSV '{key}' must be a non-empty GUID.");
    private static bool TryAmount(IReadOnlyDictionary<string, string> values, out decimal result) => decimal.TryParse(Get(values, "amount"), NumberStyles.Number, CultureInfo.InvariantCulture, out result) && result > 0;
    private static decimal RequiredAmount(IReadOnlyDictionary<string, string> values) => TryAmount(values, out var value) ? value : throw new InvalidOperationException("CSV amount must be positive.");
    private static bool TryDate(IReadOnlyDictionary<string, string> values, out DateOnly result) => DateOnly.TryParseExact(Get(values, "as_of_date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
    private static DateOnly RequiredDate(IReadOnlyDictionary<string, string> values) => TryDate(values, out var value) ? value : throw new InvalidOperationException("CSV as_of_date must use yyyy-MM-dd.");

    private sealed record CsvRow(int RowNumber, string RowType, string SourceReference, Dictionary<string, string> Values);
    private sealed record RowDecision(string Decision, string? CanonicalTrack, string Reason);
}

public sealed record ReconciliationManifest(string SchemaVersion, string Signer, string Signature, string PayloadSha256, DateOnly SourcePeriodStart, DateOnly SourcePeriodEnd, DateTimeOffset ExportedAtUtc);
public sealed record RegisterReconciliationPackageRequest(ReconciliationManifest Manifest, string Csv);
public sealed record ResolveReconciliationRowRequest(string Decision, string Note);
