using Lensee.Host.Infrastructure;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Xunit;

namespace Lensee.Tests;

public sealed class FinancialProjectionTests
{
    [Fact]
    public void Calculate_AppliesOperationLineageAndCashRefundSigns()
    {
        var sale = Operation("WholesaleSale", "Completed", 100m);
        var saleReversal = Operation("WholesaleSale", "Completed", 100m, "Reversal");
        var returnOperation = Operation("Return", "Confirmed", 20m);
        var returnReversal = Operation("Return", "Confirmed", 20m, "Reversal");
        var change = Operation("Change", "Confirmed", 0m);
        change.OperationLines = [Line(30m, "ChangeIn"), Line(10m, "ChangeOut")];
        var changeReversal = Operation("Change", "Confirmed", 0m, "Reversal");
        changeReversal.OperationLines = [Line(30m, "ChangeIn"), Line(10m, "ChangeOut")];

        var projection = FinancialProjection.Calculate(
            [sale, saleReversal, returnOperation, returnReversal, change, changeReversal],
            [new InstallmentSubLog { Amount = 30m, SubLogStatus = "Confirmed", MainLog = new MainPaymentLog { OperationId = sale.Id } }],
            [
                new CashRecord { OperationId = sale.Id, Amount = 20m, PaymentType = "CashReceived", Status = "Completed" },
                new CashRecord { OperationId = sale.Id, Amount = 10m, PaymentType = "CashRefund", Status = "Completed" }
            ],
            [
                new FinancialAdjustment { Amount = 5m, AdjustmentType = "MerchantCredit", Status = "Completed" },
                new FinancialAdjustment { Amount = 2m, AdjustmentType = "BalanceReduction", Status = "Completed" },
                new FinancialAdjustment { Amount = 100m, AdjustmentType = "MerchantCredit", Status = "Approved" }
            ]);

        Assert.Equal(0m, projection.SaleTotal);
        Assert.Equal(0m, projection.ReturnTotal);
        Assert.Equal(0m, projection.ChangeNet);
        Assert.Equal(50m, projection.PaymentsReceived);
        Assert.Equal(10m, projection.CashRefunded);
        Assert.Equal(5m, projection.MerchantCredits);
        Assert.Equal(2m, projection.BalanceReductions);
        Assert.Equal(-47m, projection.Balance);
    }

    [Fact]
    public void OperationEffect_IgnoresNonFinalizedOperations()
    {
        var draftSale = Operation("WholesaleSale", "Draft", 100m);
        var deletedSale = Operation("WholesaleSale", "Completed", 100m);
        deletedSale.IsDeleted = true;

        Assert.Equal(0m, FinancialProjection.OperationEffect(draftSale));
        Assert.Equal(0m, FinancialProjection.OperationEffect(deletedSale));
    }

    private static OperationLog Operation(string type, string status, decimal amount, string recordKind = "Standard", string? section = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            OperationType = type,
            Status = status,
            RecordKind = recordKind,
            OperationLines = [Line(amount, section ?? "Standard")]
        };

    private static OperationLine Line(decimal amount, string section) =>
        new() { Id = Guid.NewGuid(), LineTotal = amount, Section = section };
}
