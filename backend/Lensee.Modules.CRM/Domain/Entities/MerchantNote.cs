using System;
using System.Collections.Generic;

namespace Lensee.Modules.CRM.Data;

public partial class MerchantNote
{
    public Guid Id { get; set; }

    public Guid MerchantId { get; set; }

    public string Note { get; set; } = null!;

    public Guid AddedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Merchant Merchant { get; set; } = null!;
}
