using Lensee.Modules.Finance.Data;
using Lensee.SharedKernel.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

/// <summary>
/// Financial side of a supply shipment.  This service deliberately never records
/// received quantities or stock: those remain Operations and Inventory concerns.
/// </summary>
public sealed class SupplyFinanceLogService
{
    private readonly FinanceDbContext _finance;
    private readonly IClock _clock;

    public SupplyFinanceLogService(FinanceDbContext finance, IClock clock)
        => (_finance, _clock) = (finance, clock);

    public async Task<SupplyFinanceLog> EnsureAsync(Guid shipmentId, string shipmentNumber, string supplierName, string? notes, Guid actorId, CancellationToken ct)
    {
        if (_finance.Database.IsNpgsql())
        {
            var key = $"supply-finance-shipment:{shipmentId}";
            await _finance.Database.ExecuteSqlInterpolatedAsync($"select pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
        }
        var log = await _finance.SupplyFinanceLogs.SingleOrDefaultAsync(x => x.SupplyShipmentId == shipmentId, ct);
        if (log is not null)
        {
            // The Finance log mirrors the physical document identity but never its quantities.
            log.ShipmentNumber = shipmentNumber;
            log.SupplierName = supplierName;
            log.Notes = notes;
            log.UpdatedByUserId = actorId;
            log.UpdatedAt = _clock.EgyptNow;
            await _finance.SaveChangesAsync(ct);
            return log;
        }
        log = new SupplyFinanceLog
        {
            Id = Guid.NewGuid(), SupplyShipmentId = shipmentId, ShipmentNumber = shipmentNumber,
            SupplierName = supplierName, Notes = notes, Status = "Open", CreatedByUserId = actorId, CreatedAt = _clock.EgyptNow
        };
        _finance.SupplyFinanceLogs.Add(log);
        await _finance.SaveChangesAsync(ct);
        return log;
    }

    public async Task<SupplyFinanceLog> LoadAsync(Guid id, CancellationToken ct)
        => await _finance.SupplyFinanceLogs.Include(x => x.CostEntries).Include(x => x.Installments)
            .SingleAsync(x => x.Id == id, ct);

    public async Task<SupplyFinanceCostEntry> AddCostAsync(Guid logId, string category, decimal amount, DateOnly date, string? notes, Guid actorId, CancellationToken ct)
    {
        if (amount <= 0) throw new InvalidOperationException("Supply cost amount must be positive.");
        var cost = new SupplyFinanceCostEntry { Id = Guid.NewGuid(), SupplyFinanceLogId = logId, Category = category, Origin = "Finance", Amount = amount, BusinessDate = date, Notes = notes, Status = "Active", CreatedByUserId = actorId, CreatedAt = _clock.EgyptNow };
        _finance.SupplyFinanceCostEntries.Add(cost);
        await _finance.SaveChangesAsync(ct);
        return cost;
    }

    public async Task<SupplySupplierInstallment> CreateInstallmentAsync(SupplySupplierInstallment value, Guid actorId, CancellationToken ct)
    {
        if (value.Amount <= 0) throw new InvalidOperationException("Supplier installment amount must be positive.");
        value.Id = Guid.NewGuid(); value.Status = "Draft"; value.CreatedByUserId = actorId; value.CreatedAt = _clock.EgyptNow;
        value.ExternalReference = FinanceLedgerService.NormalizeExternalReference(value.ExternalReference);
        _finance.SupplySupplierInstallments.Add(value);
        await _finance.SaveChangesAsync(ct);
        return value;
    }

}
