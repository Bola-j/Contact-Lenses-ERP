using Lensee.Host.Infrastructure;
using Lensee.Modules.Payments.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lensee.Tests;

public sealed class PaymentFinancialCapacityTests
{
    [Fact]
    public void PaymentBalance_SeparatesCollectionRemainingAndRefundDue()
    {
        var paymentLog = PaymentLog();
        paymentLog.TotalAmount = 1000m;
        paymentLog.InstallmentSubLogs =
        [
            new InstallmentSubLog { MainLogId = paymentLog.Id, Amount = 600m, SubLogStatus = "Confirmed" }
        ];
        var reduction = Adjustment(paymentLog, Guid.NewGuid(), "BalanceReduction", "Completed", 500m);

        var beforePayout = PaymentBalanceCalculator.Calculate(paymentLog, [reduction], []);
        Assert.Equal(500m, beforePayout.AdjustedAmount);
        Assert.Equal(0m, beforePayout.RemainingAmount);
        Assert.Equal(100m, beforePayout.RefundDue);

        var afterPayout = PaymentBalanceCalculator.Calculate(
            paymentLog,
            [reduction],
            [CashRecord(paymentLog, "CashRefund", 100m)]);
        Assert.Equal(0m, afterPayout.RemainingAmount);
        Assert.Equal(0m, afterPayout.RefundDue);
    }

    [Fact]
    public void PaymentBalance_AdditionalChargeIncreasesRemainingAmount()
    {
        var paymentLog = PaymentLog();
        paymentLog.TotalAmount = 1000m;
        paymentLog.InstallmentSubLogs =
        [
            new InstallmentSubLog { MainLogId = paymentLog.Id, Amount = 600m, SubLogStatus = "Confirmed" }
        ];

        var balance = PaymentBalanceCalculator.Calculate(
            paymentLog,
            [Adjustment(paymentLog, Guid.NewGuid(), "AdditionalCharge", "Completed", 100m)],
            []);

        Assert.Equal(1100m, balance.AdjustedAmount);
        Assert.Equal(500m, balance.RemainingAmount);
        Assert.Equal(0m, balance.RefundDue);
    }

    [Fact]
    public async Task BalanceReductionCapacity_CompletedLinkedCashRefundReopensReceivable()
    {
        await using var context = CreateContext();
        var paymentLog = PaymentLog();
        var refundAdjustmentId = Guid.NewGuid();

        context.MainPaymentLogs.Add(paymentLog);
        context.CashRecords.AddRange(
            CashRecord(paymentLog, "CashReceived", 100m),
            CashRecord(paymentLog, "CashRefund", 30m, refundAdjustmentId));
        context.FinancialAdjustments.Add(Adjustment(
            paymentLog,
            refundAdjustmentId,
            "CashRefund",
            "Completed",
            30m));
        await context.SaveChangesAsync();

        var capacity = await PaymentFinancialCapacity.BalanceReductionCapacityAsync(
            context,
            paymentLog,
            excludingAdjustmentId: null,
            CancellationToken.None);

        Assert.Equal(30m, capacity);
    }

    [Theory]
    [InlineData("PendingApproval")]
    [InlineData("Completed")]
    public async Task CashRefundCapacity_AdditionalChargeDoesNotConsumeCollectedCash(string creditStatus)
    {
        await using var context = CreateContext();
        var paymentLog = PaymentLog();

        context.MainPaymentLogs.Add(paymentLog);
        context.CashRecords.Add(CashRecord(paymentLog, "CashReceived", 100m));
        context.FinancialAdjustments.Add(Adjustment(
            paymentLog,
            Guid.NewGuid(),
            "AdditionalCharge",
            creditStatus,
            35m));
        await context.SaveChangesAsync();

        var capacity = await PaymentFinancialCapacity.CashRefundCapacityAsync(
            context,
            paymentLog,
            excludingAdjustmentId: null,
            CancellationToken.None);

        Assert.Equal(100m, capacity);
    }

    private static PaymentsDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseInMemoryDatabase($"payment-capacity-{Guid.NewGuid()}")
            .Options);

    private static MainPaymentLog PaymentLog() =>
        new()
        {
            Id = Guid.NewGuid(),
            OperationId = Guid.NewGuid(),
            MerchantId = Guid.NewGuid(),
            TotalAmount = 100m,
            AmountPaid = 100m,
            PendingAmount = 0m,
            PaymentMethod = "CashTransaction",
            Status = "Completed",
            InitializedBy = Guid.NewGuid(),
            InitializedAt = DateTime.UtcNow,
            LastModifiedAt = DateTime.UtcNow
        };

    private static CashRecord CashRecord(
        MainPaymentLog paymentLog,
        string paymentType,
        decimal amount,
        Guid? adjustmentId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            OperationId = paymentLog.OperationId,
            FinancialAdjustmentId = adjustmentId,
            PaymentType = paymentType,
            Amount = amount,
            Status = "Completed",
            PaymentDate = DateTime.UtcNow,
            CreatedBy = Guid.NewGuid()
        };

    private static FinancialAdjustment Adjustment(
        MainPaymentLog paymentLog,
        Guid id,
        string adjustmentType,
        string status,
        decimal amount) =>
        new()
        {
            Id = id,
            MerchantId = paymentLog.MerchantId!.Value,
            OperationId = paymentLog.OperationId,
            PaymentLogId = paymentLog.Id,
            AdjustmentType = adjustmentType,
            Amount = amount,
            Status = status,
            LineageKind = "SourceLinked",
            CreatedBy = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow
        };
}
