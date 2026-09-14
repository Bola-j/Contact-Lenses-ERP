namespace Lensee.Modules.Payments.Data;

public sealed class MerchantRefundReservation
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public string Status { get; set; } = "PendingApproval";
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public Guid? PayoutEntryId { get; set; }
    public string? Notes { get; set; }
}
