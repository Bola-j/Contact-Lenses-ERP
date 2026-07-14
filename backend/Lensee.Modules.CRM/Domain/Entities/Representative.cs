using System;
using System.Collections.Generic;

namespace Lensee.Modules.CRM.Data;

public partial class Representative
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public List<string> PhoneNumbers { get; set; } = null!;

    public string? Email { get; set; }

    public string Type { get; set; } = null!;

    public Guid? LinkedUserId { get; set; }

    public Guid? AssignedLocationId { get; set; }

    public string Status { get; set; } = null!;

    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }
}
