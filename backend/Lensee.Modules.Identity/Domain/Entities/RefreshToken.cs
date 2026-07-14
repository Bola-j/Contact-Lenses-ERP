using System;
using System.Collections.Generic;

namespace Lensee.Modules.Identity.Data;

public partial class RefreshToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public Guid? ReplacedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? CreatedByIp { get; set; }

    public string? RevokedByIp { get; set; }

    public virtual ICollection<RefreshToken> InverseReplacedByNavigation { get; set; } = new List<RefreshToken>();

    public virtual RefreshToken? ReplacedByNavigation { get; set; }

    public virtual User User { get; set; } = null!;
}
