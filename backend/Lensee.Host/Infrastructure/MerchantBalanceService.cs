using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

public sealed class MerchantBalanceService
{
    private const string WholesaleSale = "WholesaleSale";
    private const string RetailSale = "RetailSale";
    private const string Return = "Return";
    private const string Change = "Change";
    private const string ChangeOut = "ChangeOut";
    private const string ChangeIn = "ChangeIn";
    private const string Completed = "Completed";
    private const string Confirmed = "Confirmed";
    private const string CashReceived = "CashReceived";
    private const string CashRefund = "CashRefund";
    private const string MerchantCredit = "MerchantCredit";
    private const string BalanceReduction = "BalanceReduction";

    private readonly OperationsDbContext _operationsDbContext;
    private readonly PaymentsDbContext _paymentsDbContext;

    public MerchantBalanceService(
        OperationsDbContext operationsDbContext,
        PaymentsDbContext paymentsDbContext)
    {
        _operationsDbContext = operationsDbContext;
        _paymentsDbContext = paymentsDbContext;
    }

    public async Task<MerchantBalanceSnapshot> CalculateAsync(Guid merchantId, CancellationToken cancellationToken)
    {
        var operations = await _operationsDbContext.OperationLogs
            .Include(operation => operation.OperationLines)
            .Where(operation => operation.ClientId == merchantId && !operation.IsDeleted)
            .ToListAsync(cancellationToken);

        var operationIds = operations.Select(operation => operation.Id).ToArray();
        var saleTotal = operations
            .Where(operation => operation.Status == Completed && operation.OperationType is WholesaleSale or RetailSale)
            .Sum(operation => operation.OperationLines.Sum(line => line.LineTotal));
        var returnTotal = operations
            .Where(operation => operation.Status == Confirmed && operation.OperationType == Return)
            .Sum(operation => operation.OperationLines.Sum(line => line.LineTotal));
        var changeNet = operations
            .Where(operation => operation.Status == Confirmed && operation.OperationType == Change)
            .Sum(operation =>
                operation.OperationLines.Where(line => line.Section == ChangeIn).Sum(line => line.LineTotal) -
                operation.OperationLines.Where(line => line.Section == ChangeOut).Sum(line => line.LineTotal));

        var confirmedSubLogTotal = await _paymentsDbContext.InstallmentSubLogs
            .Where(sub => sub.SubLogStatus == Confirmed && sub.MainLog.MerchantId == merchantId && !sub.MainLog.IsDeleted)
            .SumAsync(sub => sub.Amount, cancellationToken);
        var adjustments = await _paymentsDbContext.FinancialAdjustments
            .Where(adjustment => adjustment.MerchantId == merchantId && adjustment.Status == Completed)
            .ToListAsync(cancellationToken);
        var merchantCredits = adjustments
            .Where(adjustment => adjustment.AdjustmentType == MerchantCredit)
            .Sum(adjustment => adjustment.Amount);
        var balanceReductions = adjustments
            .Where(adjustment => adjustment.AdjustmentType == BalanceReduction)
            .Sum(adjustment => adjustment.Amount);
        var cashRecords = operationIds.Length == 0
            ? []
            : await _paymentsDbContext.CashRecords
                .Where(record => operationIds.Contains(record.OperationId) && record.Status == Completed)
                .ToListAsync(cancellationToken);
        var cashReceived = cashRecords
            .Where(record => record.PaymentType == CashReceived)
            .Sum(record => record.Amount);
        var cashRefunded = cashRecords
            .Where(record => record.PaymentType == CashRefund)
            .Sum(record => record.Amount);
        var paymentsReceived = confirmedSubLogTotal + cashReceived;
        var balance = saleTotal + changeNet - returnTotal - confirmedSubLogTotal - cashReceived - merchantCredits - balanceReductions;

        return new MerchantBalanceSnapshot(
            merchantId,
            saleTotal,
            returnTotal,
            changeNet,
            paymentsReceived,
            cashRefunded,
            merchantCredits,
            balanceReductions,
            balance);
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
