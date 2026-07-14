using System;
using System.Collections.Generic;

namespace Lensee.Modules.Catalog.Data;

public partial class Brand
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Product> Products { get; set; } = new List<Product>();
}
