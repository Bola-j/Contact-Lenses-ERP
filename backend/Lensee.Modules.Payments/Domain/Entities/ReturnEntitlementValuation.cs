namespace Lensee.Modules.Payments.Data;

/// <summary>
/// Immutable Finance valuation of a merchant return amount that has no
/// original-sale value to inherit (exceptional or unlinked return value).
/// </summary>
public sealed class ReturnEntitlementValuation
{
    public Guid Id { get; set; }
    public Guid ReturnEntitlementId { get; set; }
    public string? Scope { get; set; }
    public decimal Amount { get; set; }
    public string PrincipalName { get; set; } = null!;
    public string Note { get; set; } = null!;
    public Guid ActorId { get; set; }
    public DateTime ValuedAt { get; set; }
    public string? IdempotencyKey { get; set; }
}
