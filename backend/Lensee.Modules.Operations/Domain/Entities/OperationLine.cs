using System;
using System.Collections.Generic;

namespace Lensee.Modules.Operations.Data;

public partial class OperationLine
{
    public Guid Id { get; set; }

    public Guid OperationId { get; set; }

    public Guid SkuId { get; set; }

    public string ProductNameSnapshot { get; set; } = null!;

    public string SkuCodeSnapshot { get; set; } = null!;

    public string? MerchantNameSnapshot { get; set; }

    public string? RepresentativeNameSnapshot { get; set; }

    public string Section { get; set; } = null!;

    public int Quantity { get; set; }

    public string EntryMode { get; set; } = null!;

    public int BonusQuantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }

    public string? WriteOffReason { get; set; }

    public string? WriteOffReasonText { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public string? LotNumber { get; set; }

    public decimal? UnitCost { get; set; }

    public string? LineNotes { get; set; }

    public virtual OperationLog Operation { get; set; } = null!;
}
