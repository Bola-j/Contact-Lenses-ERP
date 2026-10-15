using System;

namespace Lensee.Modules.Operations.Data;

public class ReturnApprovalLine
{
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public Guid OperationLineId { get; set; }
    public Guid SkuId { get; set; }
    public int RequestedQuantity { get; set; }
    public int? EligibleQuantity { get; set; }
    public string? BatchName { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public string MismatchType { get; set; } = null!;
    public string Detail { get; set; } = null!;
    public DateTime CreatedAt { get; set; }

    public virtual OperationLog Operation { get; set; } = null!;
    public virtual OperationLine OperationLine { get; set; } = null!;
}
