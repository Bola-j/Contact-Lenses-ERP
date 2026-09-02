using Lensee.Host.Infrastructure;
using Lensee.Modules.Payments.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Lensee.Tests;

public sealed class PaymentFinancialCapacityTests
{
    [Fact]
    public async Task MerchantCreditCapacity_CompletedLinkedCashRefundConsumesSettlementOnce()
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

        var capacity = await PaymentFinancialCapacity.MerchantCreditCapacityAsync(
            context,
            paymentLog,
            excludingAdjustmentId: null,
            CancellationToken.None);

        Assert.Equal(70m, capacity);
    }

    [Theory]
    [InlineData("PendingApproval")]
    [InlineData("Completed")]
    public async Task CashRefundCapacity_PendingOrCompletedMerchantCreditConsumesSettlement(string creditStatus)
    {
        await using var context = CreateContext();
        var paymentLog = PaymentLog();

        context.MainPaymentLogs.Add(paymentLog);
        context.CashRecords.Add(CashRecord(paymentLog, "CashReceived", 100m));
        context.FinancialAdjustments.Add(Adjustment(
            paymentLog,
            Guid.NewGuid(),
            "MerchantCredit",
            creditStatus,
            35m));
        await context.SaveChangesAsync();

        var capacity = await PaymentFinancialCapacity.CashRefundCapacityAsync(
            context,
            paymentLog,
            excludingAdjustmentId: null,
            CancellationToken.None);

        Assert.Equal(65m, capacity);
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
