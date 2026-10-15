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

    public int BaselineStockRowVersion { get; set; }

    public int PhysicalCount { get; set; }

    public int SystemPackCount { get; set; }

    public int SystemPieceCount { get; set; }

    public int PhysicalPackCount { get; set; }

    public int PhysicalPieceCount { get; set; }

    public int DeltaPackCount { get; set; }

    public int DeltaPieceCount { get; set; }

    public int? PiecesPerPackSnapshot { get; set; }

    /// <summary>Canonical discrepancy: sealed-pack delta at the saved ratio plus loose-piece delta.</summary>
    public int? DeltaPhysicalPieces { get; set; }

    public int Delta { get; set; }

    public string? LineNote { get; set; }

    public virtual StocktakeSession Session { get; set; } = null!;
}
