using System;
using System.Collections.Generic;

namespace Lensee.Modules.Operations.Data;

public partial class StocktakeAdjustmentLine
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }

    public Guid SkuId { get; set; }

    public string? LotNumber { get; set; }

    public DateOnly? ExpiryDate { get; set; }

    public int SystemQtyBefore { get; set; }

    public int PhysicalCount { get; set; }

    public int Delta { get; set; }

    public string? LineNote { get; set; }

    public virtual StocktakeSession Session { get; set; } = null!;
}
