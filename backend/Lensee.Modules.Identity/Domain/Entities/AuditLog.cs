using System;
using System.Collections.Generic;

namespace Lensee.Modules.Identity.Data;

public partial class AuditLog
{
    public Guid Id { get; set; }

    public string EntityType { get; set; } = null!;

    public Guid EntityId { get; set; }

    public string Action { get; set; } = null!;

    public string? ChangedFields { get; set; }

    public int? StockDeltaApplied { get; set; }

    public Guid UserId { get; set; }

    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
