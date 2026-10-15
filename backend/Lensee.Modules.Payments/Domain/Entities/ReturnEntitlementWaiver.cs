namespace Lensee.Modules.Payments.Data;

/// <summary>An immutable request and decision to waive a return entitlement amount.</summary>
public sealed class ReturnEntitlementWaiver
{
    public Guid Id { get; set; }
    public Guid ReturnEntitlementId { get; set; }
    public string Scope { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Reason { get; set; } = null!;
    public string Status { get; set; } = null!;
    public Guid RequestedBy { get; set; }
    public DateTime RequestedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string? IdempotencyKey { get; set; }
}
