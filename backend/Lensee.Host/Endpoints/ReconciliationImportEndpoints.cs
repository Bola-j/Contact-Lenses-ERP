using Lensee.Host.Infrastructure;
using Lensee.Modules.Finance.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static class ReconciliationImportEndpoints
{
    public static IEndpointRouteBuilder MapReconciliationImportEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/finance/reconciliation-packages").WithTags("Finance reconciliation").RequireAuthorization("finance.reconcile");
        group.MapPost("/", RegisterAsync);
        group.MapGet("/", ListAsync);
        group.MapGet("/{packageId:guid}", GetAsync);
        group.MapPost("/{packageId:guid}/rows/{rowId:guid}/resolve", ResolveAsync);
        group.MapPost("/{packageId:guid}/apply", ApplyAsync);
        return routes;
    }

    private static async Task<IResult> RegisterAsync(RegisterReconciliationPackageRequest request, ReconciliationImportService service, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        try
        {
            var package = await service.RegisterAsync(request, currentUser.UserId ?? Guid.Empty, cancellationToken);
            return Results.Created($"/api/v1/finance/reconciliation-packages/{package.Id}", ToResponse(package, includeRows: true));
        }
        catch (InvalidOperationException)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["package"] = ["The reconciliation package is not valid."] });
        }
    }

    private static async Task<IResult> ListAsync(int? page, int? pageSize, FinanceDbContext finance, CancellationToken cancellationToken)
    {
        var currentPage = Math.Max(1, page ?? 1);
        var size = Math.Clamp(pageSize ?? 25, 1, 100);
        var query = finance.ReconciliationImportPackages.AsNoTracking().OrderByDescending(value => value.RegisteredAt);
        var totalCount = await query.CountAsync(cancellationToken);
        var packages = await query.Skip((currentPage - 1) * size).Take(size).ToListAsync(cancellationToken);
        return Results.Ok(new { page = currentPage, pageSize = size, totalCount, totalPages = (int)Math.Ceiling(totalCount / (double)size), items = packages.Select(value => ToResponse(value, false)) });
    }

    private static async Task<IResult> GetAsync(Guid packageId, FinanceDbContext finance, CancellationToken cancellationToken)
    {
        var package = await finance.ReconciliationImportPackages.AsNoTracking().Include(value => value.Rows).SingleOrDefaultAsync(value => value.Id == packageId, cancellationToken);
        return package is null ? Results.NotFound() : Results.Ok(ToResponse(package, true));
    }

    private static async Task<IResult> ResolveAsync(Guid packageId, Guid rowId, ResolveReconciliationRowRequest request, ReconciliationImportService service, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        try
        {
            var package = await service.ResolveAsync(packageId, rowId, request, currentUser.UserId ?? Guid.Empty, cancellationToken);
            return Results.Ok(ToResponse(package, true));
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (InvalidOperationException) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["resolution"] = ["The reconciliation resolution is not valid."] }); }
    }

    private static async Task<IResult> ApplyAsync(Guid packageId, ApplyReconciliationPackageRequest request, ReconciliationImportService service, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        try
        {
            var package = await service.ApplyAsync(packageId, request.IdempotencyKey, currentUser.UserId ?? Guid.Empty, cancellationToken);
            return Results.Ok(ToResponse(package, true));
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (InvalidOperationException) { return Results.Conflict(new ProblemDetails { Title = "The reconciliation package cannot be applied in its current state." }); }
    }

    private static object ToResponse(ReconciliationImportPackage package, bool includeRows) => new
    {
        package.Id,
        package.SchemaVersion,
        package.Signer,
        package.PayloadSha256,
        package.SourcePeriodStart,
        package.SourcePeriodEnd,
        package.ExportedAtUtc,
        package.Status,
        package.RegisteredByUserId,
        package.RegisteredAt,
        package.AppliedByUserId,
        package.AppliedAt,
        package.ApplyIdempotencyKey,
        rows = includeRows ? package.Rows.OrderBy(value => value.RowNumber).Select(value => new
        {
            value.Id,
            value.RowNumber,
            value.RowType,
            value.SourceReference,
            value.Decision,
            value.CanonicalTrack,
            value.DecisionReason,
            value.ResolvedByUserId,
            value.ResolvedAt,
            value.ResolutionNote
        }) : null
    };
}

public sealed record ApplyReconciliationPackageRequest(string IdempotencyKey);
