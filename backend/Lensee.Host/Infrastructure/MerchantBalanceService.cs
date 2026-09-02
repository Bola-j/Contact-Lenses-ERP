using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

public sealed class MerchantBalanceService
{
    private const string Completed = "Completed";

    private readonly OperationsDbContext _operationsDbContext;
    private readonly PaymentsDbContext _paymentsDbContext;

    public MerchantBalanceService(
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext)
    {
        _operationsDbContext = operationsDbContext;
        _paymentsDbContext = paymentsDbContext;
    }

    public async Task<MerchantBalanceSnapshot> CalculateAsync(
        Guid merchantId,
        CancellationToken cancellationToken,
        Guid? locationId = null)
    {
        var operationsQuery = _operationsDbContext.OperationLogs
            .Include(operation => operation.OperationLines)
            .Where(operation => operation.ClientId == merchantId && !operation.IsDeleted);
        if (locationId.HasValue)
        {
            operationsQuery = operationsQuery.Where(operation =>
                operation.SourceLocationId == locationId.Value ||
                operation.DestinationLocationId == locationId.Value);
        }

        var operations = await operationsQuery.ToListAsync(cancellationToken);

        var operationIds = operations.Select(operation => operation.Id).ToArray();
        var installmentQuery = _paymentsDbContext.InstallmentSubLogs
            .Include(sub => sub.MainLog)
            .Where(sub => sub.MainLog.MerchantId == merchantId && !sub.MainLog.IsDeleted);
        var adjustmentQuery = _paymentsDbContext.FinancialAdjustments
            .Where(adjustment => adjustment.MerchantId == merchantId);
        if (locationId.HasValue)
        {
            installmentQuery = installmentQuery.Where(sub => operationIds.Contains(sub.MainLog.OperationId));
            adjustmentQuery = adjustmentQuery.Where(adjustment =>
                adjustment.OperationId.HasValue && operationIds.Contains(adjustment.OperationId.Value));
        }

        var installmentSubLogs = await installmentQuery.ToListAsync(cancellationToken);
        var adjustments = await adjustmentQuery.ToListAsync(cancellationToken);
        var cashRecords = operationIds.Length == 0
            ? []
            : await _paymentsDbContext.CashRecords
                .Where(record => operationIds.Contains(record.OperationId) && record.Status == Completed)
                .ToListAsync(cancellationToken);
        var projection = FinancialProjection.Calculate(operations, installmentSubLogs, cashRecords, adjustments);

        return new MerchantBalanceSnapshot(
            merchantId,
            projection.SaleTotal,
            projection.ReturnTotal,
            projection.ChangeNet,
            projection.PaymentsReceived,
            projection.CashRefunded,
            projection.MerchantCredits,
            projection.BalanceReductions,
            projection.Balance);
    }
}

public sealed record MerchantBalanceSnapshot(
    Guid MerchantId,
    decimal SaleTotal,
    decimal ReturnTotal,
    decimal ChangeNet,
    decimal PaymentsReceived,
    decimal CashRefunded,
    decimal MerchantCredits,
    decimal BalanceReductions,
    decimal Balance);
