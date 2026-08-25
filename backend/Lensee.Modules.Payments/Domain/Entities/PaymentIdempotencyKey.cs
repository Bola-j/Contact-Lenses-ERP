using System;

namespace Lensee.Modules.Payments.Data;

public partial class PaymentIdempotencyKey
{
    public Guid Id { get; set; }

    public Guid Key { get; set; }

    public string Scope { get; set; } = null!;

    public string RequestHash { get; set; } = null!;

    public string Status { get; set; } = null!;

    public int? ResponseStatusCode { get; set; }

    public string? ResponseBody { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime LastSeenAt { get; set; }

    public DateTime ExpiresAt { get; set; }
}
