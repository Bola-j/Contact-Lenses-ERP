using System;
using System.Collections.Generic;

namespace Lensee.Modules.Catalog.Data;

public partial class Product
{
    public Guid Id { get; set; }

    public Guid CategoryId { get; set; }

    public Guid BrandId { get; set; }

    public string Name { get; set; } = null!;

    public string ProductType { get; set; } = null!;

    public string? ExpiryType { get; set; }

    public string? SealedExpiryDuration { get; set; }

    public string? SealedExpiryRate { get; set; }

    public string? OpenedExpiryDuration { get; set; }

    public int? PiecesPerPack { get; set; }

    public string? SellMode { get; set; }

    public string? ClinicalParams { get; set; }

    public string? ExtendedAttributes { get; set; }

    public bool IsActive { get; set; }

    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Brand Brand { get; set; } = null!;

    public virtual Category Category { get; set; } = null!;

    public virtual ICollection<Sku> Skus { get; set; } = new List<Sku>();
}
