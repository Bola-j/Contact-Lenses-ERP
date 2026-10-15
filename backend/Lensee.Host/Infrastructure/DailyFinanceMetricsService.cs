using Lensee.Modules.Finance.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

/// <summary>
/// Canonical daily executive metrics. Sales are attributed to the source sales
/// location; treasury movements are attributed only when their Finance account
/// has explicitly been assigned to that same location. Company-wide accounts
/// are deliberately kept separate rather than guessed into an area.
/// </summary>
public sealed class DailyFinanceMetricsService(
    FinanceDbContext finance,
    OperationsDbContext operations,
    InventoryDbContext inventory,
    IClock clock)
{
    public async Task<DailyFinanceMetricsPage> GetAsync(DateOnly? requestedDate, Guid? locationId, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var date = requestedDate ?? DateOnly.FromDateTime(clock.EgyptNow);

        var allLocations = await inventory.Locations.AsNoTracking()
            .Where(value => value.IsActive || value.Id == locationId)
            .OrderBy(value => value.Name)
            .Select(value => new MetricLocation(value.Id, value.Name, value.LocationType, value.IsActive))
            .ToListAsync(ct);
        if (locationId.HasValue && allLocations.All(value => value.Id != locationId.Value))
            throw new ArgumentException("The selected sales area does not exist.", nameof(locationId));

        var startTime = date.ToDateTime(TimeOnly.MinValue);
        var endTime = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var sales = await operations.OperationLogs.AsNoTracking()
                .Include(value => value.OperationLines)
                .Where(value => !value.IsDeleted && value.ConfirmedAt >= startTime && value.ConfirmedAt < endTime &&
                    ((value.OperationType == "WholesaleSale" && value.Status == "Completed") ||
                     (value.OperationType == "RetailSale" &&
                        (value.Status == "Completed" ||
                         ((value.SalesChannel == "Online" || value.SalesChannel == "Shopify") && value.Status == "Delivered"))) ||
                     (value.OperationType == "Return" && value.Status == "Confirmed")))
                .Select(value => new DailySaleEffect(
                    value.SourceLocationId,
                    value.OperationType,
                    value.RecordKind,
                    (value.OperationLines.Sum(line => (decimal?)line.LineTotal) ?? 0m) +
                    (value.OperationType == "RetailSale" && (value.SalesChannel == "Online" || value.SalesChannel == "Shopify") ? value.CodCharge : 0m)))
                .ToListAsync(ct);

        var movements = await finance.FinanceLedgerEntries.AsNoTracking()
                .Include(value => value.FinanceAccount)
                .Where(value => value.Status == "Posted" && value.BusinessDate == date)
                .Select(value => new DailyFinanceEffect(value.FinanceAccount.LocationId, value.Direction, value.Amount))
                .ToListAsync(ct);

        var candidateLocations = locationId.HasValue
            ? allLocations.Where(value => value.Id == locationId.Value)
            : allLocations;
        var rows = candidateLocations.Select(location =>
        {
            var areaSales = sales.Where(value => value.LocationId == location.Id).ToArray();
            var areaMovements = movements.Where(value => value.LocationId == location.Id).ToArray();
            var salesMath = DailySalesMath.From(areaSales);
            var financeMath = DailyFinanceMath.From(areaMovements);
            return new DailyFinanceAreaMetric(location.Id, location.Name, location.LocationType, salesMath.GrossSales, salesMath.Returns, salesMath.NetSales,
                salesMath.SaleCount, salesMath.ReturnCount, financeMath.Inflow, financeMath.Outflow, financeMath.Net, financeMath.MovementCount);
        }).OrderByDescending(value => value.NetSales).ThenBy(value => value.LocationName, StringComparer.OrdinalIgnoreCase).ToList();

        var scopedSales = locationId.HasValue ? sales.Where(value => value.LocationId == locationId.Value) : sales;
        var scopedMovements = locationId.HasValue ? movements.Where(value => value.LocationId == locationId.Value) : movements;
        var totalsSales = DailySalesMath.From(scopedSales);
        var totalsFinance = DailyFinanceMath.From(scopedMovements);
        var unassignedFinance = DailyFinanceMath.From(movements.Where(value => !value.LocationId.HasValue));
        var unassignedSales = DailySalesMath.From(sales.Where(value => !value.LocationId.HasValue));
        var totalCount = rows.Count;

        return new DailyFinanceMetricsPage(
            date,
            locationId,
            totalsSales,
            totalsFinance,
            unassignedSales,
            unassignedFinance,
            page,
            pageSize,
            totalCount,
            allLocations.Select(value => new DailyFinanceMetricAreaOption(value.Id, value.Name, value.LocationType)).ToArray(),
            rows.Skip((page - 1) * pageSize).Take(pageSize).ToArray());
    }

    private sealed record MetricLocation(Guid Id, string Name, string LocationType, bool IsActive);
}

public sealed record DailySaleEffect(Guid? LocationId, string OperationType, string RecordKind, decimal Amount);
public sealed record DailyFinanceEffect(Guid? LocationId, string Direction, decimal Amount);

public sealed record DailySalesMath(decimal GrossSales, decimal Returns, decimal NetSales, int SaleCount, int ReturnCount)
{
    public static DailySalesMath From(IEnumerable<DailySaleEffect> effects)
    {
        var grossSales = 0m;
        var returns = 0m;
        var saleCount = 0;
        var returnCount = 0;
        foreach (var effect in effects)
        {
            var signedAmount = effect.RecordKind == "Reversal" ? -effect.Amount : effect.Amount;
            if (effect.OperationType == "Return")
            {
                returns += signedAmount;
                returnCount += effect.RecordKind == "Reversal" ? -1 : 1;
            }
            else
            {
                grossSales += signedAmount;
                saleCount += effect.RecordKind == "Reversal" ? -1 : 1;
            }
        }
        return new DailySalesMath(grossSales, returns, grossSales - returns, saleCount, returnCount);
    }
}

public sealed record DailyFinanceMath(decimal Inflow, decimal Outflow, decimal Net, int MovementCount)
{
    public static DailyFinanceMath From(IEnumerable<DailyFinanceEffect> effects)
    {
        var rows = effects.ToArray();
        var inflow = rows.Where(value => value.Direction == FinanceLedgerService.Credit).Sum(value => value.Amount);
        var outflow = rows.Where(value => value.Direction == FinanceLedgerService.Debit).Sum(value => value.Amount);
        return new DailyFinanceMath(inflow, outflow, inflow - outflow, rows.Length);
    }
}

public sealed record DailyFinanceAreaMetric(Guid LocationId, string LocationName, string LocationType, decimal GrossSales, decimal Returns,
    decimal NetSales, int SaleCount, int ReturnCount, decimal FinanceInflow, decimal FinanceOutflow, decimal NetFinance, int FinanceMovementCount);

public sealed record DailyFinanceMetricAreaOption(Guid Id, string Name, string LocationType);

public sealed record DailyFinanceMetricsPage(DateOnly Date,
    Guid? LocationId, DailySalesMath Sales, DailyFinanceMath Finance, DailySalesMath UnassignedSales, DailyFinanceMath UnassignedFinance,
    int Page, int PageSize, int TotalCount, IReadOnlyList<DailyFinanceMetricAreaOption> Areas, IReadOnlyList<DailyFinanceAreaMetric> Items)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
