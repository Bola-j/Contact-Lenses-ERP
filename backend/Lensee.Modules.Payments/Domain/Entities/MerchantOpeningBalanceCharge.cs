namespace Lensee.Modules.Payments.Data;

public sealed class MerchantOpeningBalanceCharge
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public decimal Amount { get; set; }
    public DateOnly AsOfDate { get; set; }
    public string Description { get; set; } = null!;
    public string Status { get; set; } = "Draft";
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewReason { get; set; }
    public Guid? PostedEntryId { get; set; }
    public Guid? ReversesChargeId { get; set; }
    public Guid? ReplacedByChargeId { get; set; }
    public string? CorrelationId { get; set; }
}
