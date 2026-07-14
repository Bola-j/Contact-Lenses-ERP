using System;
using System.Collections.Generic;

namespace Lensee.Modules.Catalog.Data;

public partial class Sku
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public string SkuCode { get; set; } = null!;

    public string? PowerSign { get; set; }

    public decimal? PowerValue { get; set; }

    public string? ColorName { get; set; }

    public string? Size { get; set; }

    public string? Barcode { get; set; }

    public bool IsActive { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual Product Product { get; set; } = null!;
}
