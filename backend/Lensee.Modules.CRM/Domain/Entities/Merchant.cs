using System;
using System.Collections.Generic;

namespace Lensee.Modules.CRM.Data;

public partial class Merchant
{
    public Guid Id { get; set; }

    public string BusinessName { get; set; } = null!;

    public string ContactPersonName { get; set; } = null!;

    public List<string> PhoneNumbers { get; set; } = null!;

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string BusinessType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string? Notes { get; set; }  

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<MerchantNote> MerchantNotes { get; set; } = new List<MerchantNote>();
}
