using System;
using System.Collections.Generic;

namespace Lensee.Modules.Operations.Data;

public partial class StocktakeSession
{
    public Guid Id { get; set; }

    public Guid LocationId { get; set; }

    public DateTime SessionDate { get; set; }

    public Guid PerformedBy { get; set; }

    public Guid? ConfirmedBy { get; set; }

    public int? ProductsCounted { get; set; }

    public int? TotalDiscrepancyUnits { get; set; }

    public string? Notes { get; set; }

    public string Status { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    public virtual ICollection<StocktakeAdjustmentLine> StocktakeAdjustmentLines { get; set; } = new List<StocktakeAdjustmentLine>();
}
