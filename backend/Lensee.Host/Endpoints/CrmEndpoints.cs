using System.Text.Json;
using Lensee.Host.Infrastructure;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Primitives;
using Lensee.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static class CrmEndpoints
{
    private const string WholesaleSale = "WholesaleSale";
    private const string RetailSale = "RetailSale";
    private const string Return = "Return";
    private const string Change = "Change";
    private const string ChangeOut = "ChangeOut";
    private const string Completed = "Completed";
    private const string Confirmed = "Confirmed";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HashSet<string> MerchantBusinessTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Merchant",
        "Pharmacy",
        "Oculist",
        "BeautyCenter",
        "Other"
    };

    public static RouteGroupBuilder MapCrmEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/crm").WithTags("CRM");

        group.MapGet("/merchants", ListMerchantsAsync).RequireAuthorization("operations.read");
        group.MapGet("/merchants/{id:guid}", GetMerchantAsync).RequireAuthorization("operations.read");
        group.MapPost("/merchants", CreateMerchantAsync).RequireAuthorization("operations.write");
        group.MapPut("/merchants/{id:guid}", UpdateMerchantAsync).RequireAuthorization("operations.write");
        group.MapPatch("/merchants/{id:guid}/deactivate", SetMerchantInactiveAsync).RequireAuthorization("operations.write");
        group.MapPatch("/merchants/{id:guid}/reactivate", SetMerchantActiveAsync).RequireAuthorization("operations.write");
        group.MapPost("/merchants/{id:guid}/notes", AddMerchantNoteAsync).RequireAuthorization("operations.write");
        group.MapGet("/merchants/{id:guid}/eligibility", GetMerchantEligibilityAsync).RequireAuthorization("operations.read");
        group.MapGet("/representatives", ListRepresentativesAsync).RequireAuthorization("operations.read");
        group.MapPost("/representatives", CreateRepresentativeAsync).RequireAuthorization("operations.write");
        group.MapPut("/representatives/{id:guid}", UpdateRepresentativeAsync).RequireAuthorization("operations.write");
        group.MapPatch("/representatives/{id:guid}/deactivate", SetRepresentativeInactiveAsync).RequireAuthorization("operations.write");
        group.MapPatch("/representatives/{id:guid}/reactivate", SetRepresentativeActiveAsync).RequireAuthorization("operations.write");

        return group;
    }

    private static async Task<IResult> ListMerchantsAsync(bool? includeInactive, int? page, int? pageSize, string? search, CrmDbContext dbContext, CancellationToken cancellationToken)
    {
        var request = new PageRequest(page ?? 1, pageSize ?? 25);
        var query = dbContext.Merchants.Where(merchant => !merchant.IsDeleted);
        if (includeInactive != true)
        {
            query = query.Where(merchant => merchant.Status == "Active");
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLower();
            query = query.Where(merchant => merchant.BusinessName.ToLower().Contains(value) || merchant.ContactPersonName.ToLower().Contains(value));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(merchant => merchant.BusinessName)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(merchant => ToResponse(merchant))
            .ToListAsync(cancellationToken);

        return Results.Ok(new PagedResult<MerchantResponse>(rows, request.Page, request.PageSize, total));
    }

    private static async Task<IResult> GetMerchantAsync(
        Guid id,
        CrmDbContext crmDbContext,
        OperationsDbContext operationsDbContext,
        MerchantBalanceService merchantBalanceService,
        CancellationToken cancellationToken)
    {
        var merchant = await crmDbContext.Merchants
            .Include(value => value.MerchantNotes.OrderByDescending(note => note.CreatedAt).Take(10))
            .FirstOrDefaultAsync(value => value.Id == id && !value.IsDeleted, cancellationToken);
        if (merchant is null)
        {
            return Results.NotFound();
        }

        var operationCount = await operationsDbContext.OperationLogs.CountAsync(operation => operation.ClientId == id && !operation.IsDeleted, cancellationToken);
        var recentOperations = await operationsDbContext.OperationLogs
            .Where(operation => operation.ClientId == id && !operation.IsDeleted)
            .OrderByDescending(operation => operation.CreatedAt)
            .Take(10)
            .Select(operation => new MerchantOperationResponse(
                operation.Id,
                operation.OperationNumber,
                operation.OperationType,
                operation.Status,
                operation.PaymentMethod,
                operation.CreatedAt,
                operation.ConfirmedAt,
                operation.OperationLines.Sum(line => line.Quantity),
                operation.OperationLines.Sum(line => line.BonusQuantity),
                operation.OperationLines.Sum(line => line.LineTotal)))
            .ToListAsync(cancellationToken);
        var sold = await operationsDbContext.OperationLogs
            .Include(operation => operation.OperationLines)
            .Where(operation => operation.ClientId == id && operation.Status == Completed && (operation.OperationType == WholesaleSale || operation.OperationType == RetailSale))
            .SelectMany(operation => operation.OperationLines)
            .ToListAsync(cancellationToken);
        var balance = await merchantBalanceService.CalculateAsync(id, cancellationToken);

        return Results.Ok(new MerchantDetailResponse(
            ToResponse(merchant),
            new MerchantSummaryResponse(operationCount, sold.Where(line => line.EntryMode == "Packs").Sum(line => line.Quantity), sold.Where(line => line.EntryMode == "Pieces").Sum(line => line.Quantity), balance.Balance, 0),
            recentOperations,
            merchant.MerchantNotes.OrderByDescending(note => note.CreatedAt).Select(note => new MerchantNoteResponse(note.Id, note.Note, note.AddedBy, note.CreatedAt)).ToList()));
    }

    private static async Task<IResult> CreateMerchantAsync(MerchantRequest request, CrmDbContext dbContext, IClock clock, CancellationToken cancellationToken)
    {
        var errors = ValidateMerchant(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var now = clock.EgyptNow;
        var merchant = new Merchant
        {
            Id = Guid.NewGuid(),
            BusinessName = request.BusinessName.Trim(),
            ContactPersonName = request.ContactPersonName.Trim(),
            PhoneNumbers = NormalizePhones(request.PhoneNumbers),
            Email = TrimToNull(request.Email),
            Address = TrimToNull(request.Address),
            BusinessType = NormalizeBusinessType(request.BusinessType) ?? "Merchant",
            Status = "Active",
            Notes = TrimToNull(request.Notes),
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.Merchants.Add(merchant);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/crm/merchants/{merchant.Id}", ToResponse(merchant));
    }

    private static async Task<IResult> UpdateMerchantAsync(Guid id, MerchantRequest request, CrmDbContext dbContext, IClock clock, CancellationToken cancellationToken)
    {
        var errors = ValidateMerchant(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var merchant = await dbContext.Merchants.FirstOrDefaultAsync(value => value.Id == id && !value.IsDeleted, cancellationToken);
        if (merchant is null)
        {
            return Results.NotFound();
        }

        merchant.BusinessName = request.BusinessName.Trim();
        merchant.ContactPersonName = request.ContactPersonName.Trim();
        merchant.PhoneNumbers = NormalizePhones(request.PhoneNumbers);
        merchant.Email = TrimToNull(request.Email);
        merchant.Address = TrimToNull(request.Address);
        merchant.BusinessType = NormalizeBusinessType(request.BusinessType) ?? merchant.BusinessType;
        merchant.Notes = TrimToNull(request.Notes);
        merchant.UpdatedAt = clock.EgyptNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(merchant));
    }

    private static Task<IResult> SetMerchantInactiveAsync(Guid id, CrmDbContext dbContext, IClock clock, CancellationToken cancellationToken) =>
        SetMerchantStatusAsync(id, "Inactive", dbContext, clock, cancellationToken);

    private static Task<IResult> SetMerchantActiveAsync(Guid id, CrmDbContext dbContext, IClock clock, CancellationToken cancellationToken) =>
        SetMerchantStatusAsync(id, "Active", dbContext, clock, cancellationToken);

    private static async Task<IResult> SetMerchantStatusAsync(Guid id, string status, CrmDbContext dbContext, IClock clock, CancellationToken cancellationToken)
    {
        var merchant = await dbContext.Merchants.FirstOrDefaultAsync(value => value.Id == id && !value.IsDeleted, cancellationToken);
        if (merchant is null)
        {
            return Results.NotFound();
        }

        merchant.Status = status;
        merchant.UpdatedAt = clock.EgyptNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> AddMerchantNoteAsync(Guid id, NoteRequest request, CrmDbContext dbContext, ICurrentUser currentUser, IClock clock, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Note))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.Note)] = ["Note is required."] });
        }
        if (!await dbContext.Merchants.AnyAsync(value => value.Id == id && !value.IsDeleted, cancellationToken))
        {
            return Results.NotFound();
        }

        var note = new MerchantNote
        {
            Id = Guid.NewGuid(),
            MerchantId = id,
            Note = request.Note.Trim(),
            AddedBy = currentUser.UserId ?? Guid.Empty,
            CreatedAt = clock.EgyptNow
        };
        dbContext.MerchantNotes.Add(note);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/crm/merchants/{id}/notes/{note.Id}", new MerchantNoteResponse(note.Id, note.Note, note.AddedBy, note.CreatedAt));
    }

    private static async Task<IResult> GetMerchantEligibilityAsync(
        Guid id,
        OperationsDbContext operationsDbContext,
        CatalogDbContext catalogDbContext,
        CancellationToken cancellationToken)
    {
        var operations = await operationsDbContext.OperationLogs
            .Include(operation => operation.OperationLines)
            .Include(operation => operation.OperationVersions)
            .Where(operation =>
                operation.ClientId == id &&
                !operation.IsDeleted &&
                (operation.Status == Completed || operation.Status == Confirmed) &&
                (operation.OperationType == WholesaleSale ||
                    operation.OperationType == RetailSale ||
                    operation.OperationType == Return ||
                    operation.OperationType == Change))
            .ToListAsync(cancellationToken);

        var sold = new Dictionary<MerchantEligibilityKey, int>();
        var returned = new Dictionary<MerchantEligibilityKey, int>();
        foreach (var operation in operations)
        {
            if (operation.OperationType is WholesaleSale or RetailSale && operation.Status == Completed)
            {
                var allocations = ReadAllocations(operation);
                foreach (var allocation in allocations)
                {
                    foreach (var batch in allocation.Allocations)
                    {
                        AddQuantity(sold, new MerchantEligibilityKey(allocation.SkuId, NormalizeBlank(batch.LotNumber), batch.ExpiryDate), batch.Quantity);
                    }
                }
                if (allocations.Count == 0)
                {
                    foreach (var line in operation.OperationLines.Where(line => line.EntryMode == "Packs"))
                    {
                        AddQuantity(sold, new MerchantEligibilityKey(line.SkuId, NormalizeBlank(line.LotNumber), line.ExpiryDate), line.Quantity);
                    }
                }
            }
            else if (operation.OperationType == Return && operation.Status == Confirmed)
            {
                foreach (var line in operation.OperationLines)
                {
                    AddQuantity(returned, new MerchantEligibilityKey(line.SkuId, NormalizeBlank(line.LotNumber), line.ExpiryDate), line.Quantity);
                }
            }
            else if (operation.OperationType == Change && operation.Status == Confirmed)
            {
                foreach (var line in operation.OperationLines.Where(line => line.Section == ChangeOut))
                {
                    AddQuantity(returned, new MerchantEligibilityKey(line.SkuId, NormalizeBlank(line.LotNumber), line.ExpiryDate), line.Quantity);
                }
            }
        }

        var keys = sold.Keys.Concat(returned.Keys).Distinct().ToList();
        var skuIds = keys.Select(key => key.SkuId).Distinct().ToArray();
        var skus = await catalogDbContext.Skus
            .Include(sku => sku.Product)
            .Where(sku => skuIds.Contains(sku.Id))
            .ToDictionaryAsync(sku => sku.Id, cancellationToken);

        var rows = keys
            .Select(pair =>
            {
                sold.TryGetValue(pair, out var soldQty);
                returned.TryGetValue(pair, out var returnedQty);
                skus.TryGetValue(pair.SkuId, out var sku);
                return new MerchantEligibilityResponse(
                    pair.SkuId,
                    sku?.SkuCode,
                    sku?.Product.Name,
                    pair.LotNumber,
                    pair.ExpiryDate,
                    soldQty,
                    Math.Max(soldQty - returnedQty, 0),
                    returnedQty,
                    Math.Max(returnedQty - soldQty, 0));
            })
            .OrderBy(row => row.SkuCode ?? row.SkuId.ToString())
            .ThenBy(row => row.ExpiryDate)
            .ThenBy(row => row.LotNumber)
            .ToList();

        return Results.Ok(rows);
    }

    private static IReadOnlyList<TransferAllocationSnapshot> ReadAllocations(OperationLog operation)
    {
        var snapshot = operation.OperationVersions
            .OrderByDescending(version => version.VersionNumber)
            .Select(version =>
            {
                try
                {
                    return JsonSerializer.Deserialize<OperationSnapshot>(version.SnapshotData, JsonOptions);
                }
                catch
                {
                    return null;
                }
            })
            .FirstOrDefault(value => value?.TransferAllocations.Count > 0);

        return snapshot?.TransferAllocations ?? [];
    }

    private static void AddQuantity(Dictionary<MerchantEligibilityKey, int> values, MerchantEligibilityKey key, int quantity)
    {
        values.TryGetValue(key, out var current);
        values[key] = current + quantity;
    }

    private static async Task<IResult> ListRepresentativesAsync(bool? includeInactive, CrmDbContext dbContext, CancellationToken cancellationToken)
    {
        var query = dbContext.Representatives.Where(rep => !rep.IsDeleted);
        if (includeInactive != true)
        {
            query = query.Where(rep => rep.Status == "Active");
        }

        return Results.Ok(await query.OrderBy(rep => rep.Name).Select(rep => ToResponse(rep)).ToListAsync(cancellationToken));
    }

    private static async Task<IResult> CreateRepresentativeAsync(RepresentativeRequest request, CrmDbContext dbContext, CancellationToken cancellationToken)
    {
        var errors = ValidateRepresentative(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var rep = new Representative
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            PhoneNumbers = NormalizePhones(request.PhoneNumbers),
            Email = TrimToNull(request.Email),
            Type = TrimToNull(request.Type) ?? "External",
            AssignedLocationId = request.AssignedLocationId,
            Status = "Active",
            Notes = TrimToNull(request.Notes)
        };
        dbContext.Representatives.Add(rep);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/crm/representatives/{rep.Id}", ToResponse(rep));
    }

    private static async Task<IResult> UpdateRepresentativeAsync(Guid id, RepresentativeRequest request, CrmDbContext dbContext, CancellationToken cancellationToken)
    {
        var errors = ValidateRepresentative(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var rep = await dbContext.Representatives.FirstOrDefaultAsync(value => value.Id == id && !value.IsDeleted, cancellationToken);
        if (rep is null)
        {
            return Results.NotFound();
        }

        rep.Name = request.Name.Trim();
        rep.PhoneNumbers = NormalizePhones(request.PhoneNumbers);
        rep.Email = TrimToNull(request.Email);
        rep.Type = TrimToNull(request.Type) ?? rep.Type;
        rep.AssignedLocationId = request.AssignedLocationId;
        rep.Notes = TrimToNull(request.Notes);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(rep));
    }

    private static Task<IResult> SetRepresentativeInactiveAsync(Guid id, CrmDbContext dbContext, CancellationToken cancellationToken) =>
        SetRepresentativeStatusAsync(id, "Inactive", dbContext, cancellationToken);

    private static Task<IResult> SetRepresentativeActiveAsync(Guid id, CrmDbContext dbContext, CancellationToken cancellationToken) =>
        SetRepresentativeStatusAsync(id, "Active", dbContext, cancellationToken);

    private static async Task<IResult> SetRepresentativeStatusAsync(Guid id, string status, CrmDbContext dbContext, CancellationToken cancellationToken)
    {
        var rep = await dbContext.Representatives.FirstOrDefaultAsync(value => value.Id == id && !value.IsDeleted, cancellationToken);
        if (rep is null)
        {
            return Results.NotFound();
        }

        rep.Status = status;
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static Dictionary<string, string[]> ValidateMerchant(MerchantRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.BusinessName))
        {
            errors[nameof(request.BusinessName)] = ["Business name is required."];
        }
        if (string.IsNullOrWhiteSpace(request.ContactPersonName))
        {
            errors[nameof(request.ContactPersonName)] = ["Contact person is required."];
        }
        if (NormalizeBusinessType(request.BusinessType) is null && !string.IsNullOrWhiteSpace(request.BusinessType))
        {
            errors[nameof(request.BusinessType)] = ["Business type must be Merchant, Pharmacy, Oculist, BeautyCenter, or Other."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateRepresentative(RepresentativeRequest request) =>
        string.IsNullOrWhiteSpace(request.Name)
            ? new Dictionary<string, string[]> { [nameof(request.Name)] = ["Representative name is required."] }
            : [];

    private static List<string> NormalizePhones(IReadOnlyList<string>? phones) =>
        phones?.Where(phone => !string.IsNullOrWhiteSpace(phone)).Select(phone => phone.Trim()).Distinct().ToList() ?? [];

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeBusinessType(string? value)
    {
        var trimmed = TrimToNull(value);
        if (trimmed is null)
        {
            return null;
        }

        return MerchantBusinessTypes.FirstOrDefault(type => string.Equals(type, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static MerchantResponse ToResponse(Merchant merchant) =>
        new(merchant.Id, merchant.BusinessName, merchant.ContactPersonName, merchant.PhoneNumbers, merchant.Email, merchant.Address, merchant.BusinessType, merchant.Status, merchant.Notes, merchant.CreatedAt, merchant.UpdatedAt);

    private static RepresentativeResponse ToResponse(Representative rep) =>
        new(rep.Id, rep.Name, rep.PhoneNumbers, rep.Email, rep.Type, rep.AssignedLocationId, rep.Status, rep.Notes);

    private sealed record MerchantEligibilityKey(Guid SkuId, string? LotNumber, DateOnly? ExpiryDate);

    private sealed record OperationSnapshot(
        string OperationType,
        string Status,
        Guid? SourceLocationId,
        Guid? DestinationLocationId,
        IReadOnlyList<OperationLineSnapshot> Lines,
        IReadOnlyList<TransferAllocationSnapshot> TransferAllocations);

    private sealed record OperationLineSnapshot(Guid SkuId, string SkuCode, string ProductName, string Section, int PackQuantity, string? LotNumber, DateOnly? ExpiryDate);

    private sealed record TransferAllocationSnapshot(Guid SkuId, IReadOnlyList<BatchAllocationSnapshot> Allocations);

    private sealed record BatchAllocationSnapshot(Guid BatchId, int Quantity, string? LotNumber = null, DateOnly? ExpiryDate = null);
}

public sealed record MerchantRequest(string BusinessName, string ContactPersonName, IReadOnlyList<string>? PhoneNumbers, string? Email, string? Address, string? BusinessType, string? Notes);

public sealed record MerchantResponse(Guid Id, string BusinessName, string ContactPersonName, IReadOnlyList<string> PhoneNumbers, string? Email, string? Address, string BusinessType, string Status, string? Notes, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record MerchantDetailResponse(MerchantResponse Merchant, MerchantSummaryResponse Summary, IReadOnlyList<MerchantOperationResponse> RecentOperations, IReadOnlyList<MerchantNoteResponse> Notes);

public sealed record MerchantSummaryResponse(int OperationCount, int SoldPacks, int SoldPieces, decimal Balance, int ReturnEligibilityPlaceholder)
{
    public decimal BalancePlaceholder => Balance;
}

public sealed record MerchantOperationResponse(Guid Id, string OperationNumber, string OperationType, string Status, string? PaymentMethod, DateTime CreatedAt, DateTime? ConfirmedAt, int Quantity, int BonusQuantity, decimal Total);

public sealed record MerchantNoteResponse(Guid Id, string Note, Guid AddedBy, DateTime CreatedAt);

public sealed record MerchantEligibilityResponse(Guid SkuId, string? SkuCode, string? ProductName, string? LotNumber, DateOnly? ExpiryDate, int SoldQty, int ReturnableQty, int ReturnedQty, int OverReturnedQty);

public sealed record NoteRequest(string Note);

public sealed record RepresentativeRequest(string Name, IReadOnlyList<string>? PhoneNumbers, string? Email, string? Type, Guid? AssignedLocationId, string? Notes);

public sealed record RepresentativeResponse(Guid Id, string Name, IReadOnlyList<string> PhoneNumbers, string? Email, string Type, Guid? AssignedLocationId, string Status, string? Notes);
