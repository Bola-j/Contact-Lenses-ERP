using System.Text.Json;
using Lensee.Modules.Reporting.Application.Abstractions;
using Lensee.Modules.Reporting.Application.Models;
using Lensee.Modules.Reporting.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

public sealed record DocumentExportJobResponse(
    Guid Id,
    string Status,
    int RowCount,
    DateTime GeneratedAt,
    DateTime ExpiresAt,
    string? FileName = null,
    string? Error = null);

/// <summary>
/// Persists a fully materialized document before rendering it.  A queued job is
/// therefore an as-of snapshot, never a later re-query of mutable business data.
/// </summary>
public sealed class DocumentExportJobService(
    ReportingDbContext reporting,
    IDocumentExportService exportService,
    IClock clock,
    ILogger<DocumentExportJobService> logger)
{
    public const int ImmediateRowLimit = 1_000;
    private static readonly JsonSerializerOptions SnapshotJson = new(JsonSerializerDefaults.Web);

    public async Task<DocumentExportJobResponse> CreateAsync(
        DocumentModel document,
        ExportFormat format,
        ICurrentUser currentUser,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (key is not null)
        {
            var existing = await reporting.DocumentExportJobs.AsNoTracking()
                .Where(job => job.RequestedBy == currentUser.UserId && job.IdempotencyKey == key && job.ExpiresAt > clock.EgyptNow)
                .OrderByDescending(job => job.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null) return ToResponse(existing);
        }

        var now = clock.EgyptNow;
        var job = new DocumentExportJob
        {
            Id = Guid.NewGuid(),
            DocumentKey = document.Metadata.DocumentKey,
            Format = format.ToString(),
            Language = document.Metadata.Language.ToString(),
            Status = document.Metadata.RowCount <= ImmediateRowLimit ? "Rendering" : "Queued",
            RequestedBy = currentUser.UserId,
            RequestedByRole = currentUser.Role,
            IdempotencyKey = key,
            RowCount = document.Metadata.RowCount,
            SnapshotJson = JsonSerializer.Serialize(document, SnapshotJson),
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = now.AddHours(24)
        };

        reporting.DocumentExportJobs.Add(job);
        try
        {
            await reporting.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (key is not null)
        {
            var existing = await reporting.DocumentExportJobs.AsNoTracking()
                .Where(item => item.RequestedBy == currentUser.UserId && item.IdempotencyKey == key)
                .OrderByDescending(item => item.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null) return ToResponse(existing);
            throw;
        }

        if (job.Status == "Queued") return ToResponse(job);
        await RenderAsync(job, document, format, cancellationToken);
        return ToResponse(job);
    }

    public async Task<DocumentExportJob?> GetForRequesterAsync(Guid id, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        await reporting.DocumentExportJobs.AsNoTracking()
            .SingleOrDefaultAsync(job => job.Id == id && job.RequestedBy == currentUser.UserId && job.ExpiresAt > clock.EgyptNow, cancellationToken);

    public async Task<DocumentExportJob?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        var now = clock.EgyptNow;
        if (!reporting.Database.IsRelational())
        {
            var memoryJob = await reporting.DocumentExportJobs.OrderBy(job => job.CreatedAt).FirstOrDefaultAsync(job => job.Status == "Queued", cancellationToken);
            if (memoryJob is null) return null;
            memoryJob.Status = "Rendering";
            memoryJob.UpdatedAt = now;
            await reporting.SaveChangesAsync(cancellationToken);
            return memoryJob;
        }

        await using var transaction = await reporting.Database.BeginTransactionAsync(cancellationToken);
        var job = await reporting.DocumentExportJobs
            .FromSqlRaw("select * from reporting.document_export_jobs where status = 'Queued' and expires_at > CURRENT_TIMESTAMP order by created_at for update skip locked limit 1")
            .SingleOrDefaultAsync(cancellationToken);
        if (job is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }
        job.Status = "Rendering";
        job.UpdatedAt = now;
        await reporting.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    public async Task RenderQueuedAsync(DocumentExportJob job, CancellationToken cancellationToken)
    {
        try
        {
            var document = JsonSerializer.Deserialize<DocumentModel>(job.SnapshotJson, SnapshotJson)
                ?? throw new InvalidOperationException("Document snapshot could not be read.");
            if (!Enum.TryParse<ExportFormat>(job.Format, true, out var format))
                throw new InvalidOperationException("Document format is not supported.");
            await RenderAsync(job, document, format, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Document export job {JobId} failed.", job.Id);
            job.Status = "Failed";
            job.Error = "The report could not be generated.";
            job.UpdatedAt = clock.EgyptNow;
            await reporting.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task RenderAsync(DocumentExportJob job, DocumentModel document, ExportFormat format, CancellationToken cancellationToken)
    {
        try
        {
            var exported = await exportService.ExportAsync(document, format, cancellationToken);
            await using var content = exported.Content;
            await using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            job.Content = buffer.ToArray();
            job.ContentType = exported.ContentType;
            job.FileName = exported.FileName;
            job.Status = "Ready";
            job.Error = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Document export job {JobId} failed during rendering.", job.Id);
            job.Status = "Failed";
            job.Error = "The report could not be generated.";
        }
        finally
        {
            job.UpdatedAt = clock.EgyptNow;
            await reporting.SaveChangesAsync(cancellationToken);
        }
    }

    private static DocumentExportJobResponse ToResponse(DocumentExportJob job) =>
        new(job.Id, job.Status, job.RowCount, job.CreatedAt, job.ExpiresAt, job.FileName, job.Error);
}

public sealed class DocumentExportJobWorker(IServiceScopeFactory scopeFactory, ILogger<DocumentExportJobWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<DocumentExportJobService>();
                var job = await service.ClaimNextAsync(stoppingToken);
                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }
                await service.RenderQueuedAsync(job, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Document export worker iteration failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
