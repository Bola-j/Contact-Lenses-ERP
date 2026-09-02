using System.Security.Cryptography;
using System.Text.Json;
using Lensee.Modules.Payments.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Lensee.Host.Infrastructure;

public sealed class PaymentIdempotencyService
{
    private const string Pending = "Pending";
    private const string Completed = "Completed";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly PaymentsDbContext _paymentsDbContext;
    private readonly IClock _clock;

    public PaymentIdempotencyService(PaymentsDbContext paymentsDbContext, IClock clock)
    {
        _paymentsDbContext = paymentsDbContext;
        _clock = clock;
    }

    public async Task<PaymentIdempotencyHandle> StartAsync(
        string? idempotencyKey,
        string scope,
        object request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(idempotencyKey, out var parsedKey) || parsedKey == Guid.Empty)
        {
            return new(null, Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["Idempotency-Key"] = ["A valid UUID Idempotency-Key header is required for payment mutations."]
            }), null);
        }

        // Idempotency timestamps are persisted as UTC-neutral database timestamps.
        // Using EgyptNow (Kind=Unspecified) makes Npgsql infer timestamptz and reject
        // the parameter before the command reaches PostgreSQL.
        var now = _clock.UtcNow;
        var requestHash = ComputeRequestHash(scope, request);
        if (!_paymentsDbContext.Database.IsRelational())
        {
            var existing = await _paymentsDbContext.PaymentIdempotencyKeys
                .FirstOrDefaultAsync(entry => entry.Key == parsedKey && entry.Scope == scope, cancellationToken);
            if (existing is not null)
            {
                return ToExistingResult(existing, requestHash);
            }

            var entry = NewEntry(parsedKey, scope, requestHash, now);
            _paymentsDbContext.PaymentIdempotencyKeys.Add(entry);
            return new(entry, null, null);
        }

        var transaction = await _paymentsDbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var insertedRows = await _paymentsDbContext.Database.ExecuteSqlInterpolatedAsync($"""
                insert into payments.payment_idempotency_keys
                    (id, key, scope, request_hash, status, created_at, last_seen_at, expires_at)
                values ({Guid.NewGuid()}, {parsedKey}, {scope}, {requestHash}, {Pending}, {now}, {now}, {now.AddDays(90)})
                on conflict (key, scope) do nothing
                """, cancellationToken);

            var entry = await _paymentsDbContext.PaymentIdempotencyKeys
                .FromSqlInterpolated($"""
                    select * from payments.payment_idempotency_keys
                    where key = {parsedKey} and scope = {scope}
                    for update
                    """)
                .SingleAsync(cancellationToken);

            if (!string.Equals(entry.RequestHash, requestHash, StringComparison.Ordinal) || entry.Status == Completed)
            {
                var result = ToExistingResult(entry, requestHash);
                await transaction.RollbackAsync(CancellationToken.None);
                await transaction.DisposeAsync();
                return result;
            }

            if (insertedRows == 0)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                await transaction.DisposeAsync();
                return new(null, Results.Conflict(new
                {
                    code = "idempotency-key-pending-reconciliation",
                    detail = "This payment key is pending historical reconciliation and cannot be replayed automatically."
                }), null);
            }

            return new(entry, null, transaction);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            await transaction.DisposeAsync();
            throw;
        }
    }

    public async Task<IResult> CompleteAsync(
        PaymentIdempotencyHandle idempotency,
        object response,
        int statusCode,
        CancellationToken cancellationToken)
    {
        try
        {
            if (idempotency.Entry is not null)
            {
                idempotency.Entry.Status = Completed;
                idempotency.Entry.ResponseStatusCode = statusCode;
                idempotency.Entry.ResponseBody = JsonSerializer.Serialize(response, JsonOptions);
                await _paymentsDbContext.SaveChangesAsync(cancellationToken);
            }
            if (idempotency.Transaction is not null)
            {
                await idempotency.Transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (idempotency.Transaction is not null)
            {
                await idempotency.Transaction.RollbackAsync(CancellationToken.None);
            }
            throw;
        }
        finally
        {
            if (idempotency.Transaction is not null)
            {
                await idempotency.Transaction.DisposeAsync();
            }
            idempotency.MarkFinished();
        }

        return Results.Json(response, statusCode: statusCode, options: JsonOptions);
    }

    public static async Task<IResult> AbortAsync(PaymentIdempotencyHandle idempotency, IResult result)
    {
        if (idempotency.Transaction is null)
        {
            return result;
        }

        try
        {
            await idempotency.Transaction.RollbackAsync(CancellationToken.None);
        }
        finally
        {
            await idempotency.Transaction.DisposeAsync();
            idempotency.MarkFinished();
        }

        return result;
    }

    private static string ComputeRequestHash(string scope, object request)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { scope, request }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static PaymentIdempotencyKey NewEntry(Guid key, string scope, string requestHash, DateTime now) =>
        new()
        {
            Id = Guid.NewGuid(),
            Key = key,
            Scope = scope,
            RequestHash = requestHash,
            Status = Pending,
            CreatedAt = now,
            LastSeenAt = now,
            ExpiresAt = now.AddDays(90)
        };

    private static PaymentIdempotencyHandle ToExistingResult(PaymentIdempotencyKey entry, string requestHash)
    {
        if (!string.Equals(entry.RequestHash, requestHash, StringComparison.Ordinal))
        {
            return new(null, Results.Conflict(new
            {
                code = "idempotency-key-reused",
                detail = "Idempotency-Key was already used with a different request payload."
            }), null);
        }
        if (entry.Status == Completed && entry.ResponseBody is not null && entry.ResponseStatusCode.HasValue)
        {
            using var document = JsonDocument.Parse(entry.ResponseBody);
            return new(null, Results.Json(document.RootElement.Clone(), statusCode: entry.ResponseStatusCode.Value, options: JsonOptions), null);
        }

        return new(null, Results.Conflict(new
        {
            code = "idempotency-key-pending-reconciliation",
            detail = "This payment key is pending historical reconciliation and cannot be replayed automatically."
        }), null);
    }
}

public sealed class PaymentIdempotencyHandle : IAsyncDisposable
{
    private bool _finished;

    public PaymentIdempotencyHandle(
        PaymentIdempotencyKey? entry,
        IResult? result,
        IDbContextTransaction? transaction)
    {
        Entry = entry;
        Result = result;
        Transaction = transaction;
    }

    public PaymentIdempotencyKey? Entry { get; }

    public IResult? Result { get; }

    public IDbContextTransaction? Transaction { get; }

    internal void MarkFinished() => _finished = true;

    public async ValueTask DisposeAsync()
    {
        if (_finished || Transaction is null)
        {
            return;
        }

        try
        {
            await Transaction.RollbackAsync(CancellationToken.None);
        }
        finally
        {
            await Transaction.DisposeAsync();
            _finished = true;
        }
    }
}
