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

    // Null on historical lines whose pack size was never captured.
    public int? PiecesPerPackSnapshot { get; set; }

    public int BonusQuantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal LineTotal { get; set; }

    // FullPrice, Promotion50, or PromotionFree for sales. Historical lines
    // remain readable through the default FullPrice/legacy IsBonus mapping.
    public string? PricingMode { get; set; }

    // Canonical sale pricing category. Nullable so historical records remain untouched.
    public string? LineType { get; set; }

    // A return can consume only part of a source sale line. The remainder is
    // an explicit exception, never a fake source allocation.
    public int NormalQuantity { get; set; }
    public int ExceptionQuantity { get; set; }
    public decimal NormalValue { get; set; }
    public decimal ExceptionValue { get; set; }

    public string? WriteOffReason { get; set; }

    public string? WriteOffReasonText { get; set; }

    public string? ReturnDisposition { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public string? LotNumber { get; set; }

    public decimal? UnitCost { get; set; }

    public string? LineNotes { get; set; }

    public string? ShopifyLineItemId { get; set; }

    public string? ShopifyVariantId { get; set; }

    public string? ShopifySkuSnapshot { get; set; }

    public string? ShopifyTitleSnapshot { get; set; }

    public string? ShopifyVariantTitleSnapshot { get; set; }

    public string? ShopifyPropertiesSnapshot { get; set; }

    public virtual OperationLog Operation { get; set; } = null!;

    public virtual ICollection<OperationLineSourceAllocation> SourceAllocations { get; set; } = new List<OperationLineSourceAllocation>();
    public virtual ICollection<OperationLinePieceAllocation> PieceAllocations { get; set; } = new List<OperationLinePieceAllocation>();
}
