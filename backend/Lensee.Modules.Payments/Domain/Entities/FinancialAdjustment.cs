using System;

namespace Lensee.Modules.Payments.Data;

public partial class FinancialAdjustment
{
    public Guid Id { get; set; }

    public Guid MerchantId { get; set; }

    public Guid? OperationId { get; set; }

    public string AdjustmentType { get; set; } = null!;

    public decimal Amount { get; set; }

    public string Status { get; set; } = null!;

    public string? Notes { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}
