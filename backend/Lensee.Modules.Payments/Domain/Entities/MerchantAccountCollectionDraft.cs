namespace Lensee.Modules.Payments.Data;

/// <summary>
/// A proposed collection against the merchant receivable account. It is kept
/// separate from legacy operation payment sub-logs because one collection can
/// cover several operation obligations.
/// </summary>
public sealed class MerchantAccountCollectionDraft
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    /// <summary>Persisted scope for this draft; account drafts are always MerchantAccount.</summary>
    public string Scope { get; set; } = "MerchantAccount";
    /// <summary>Operation that originated this collection work, when it was created from a sale.</summary>
    public Guid? SourceOperationId { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = null!;
    public string? TransactionReference { get; set; }
    public Guid? FinanceAccountId { get; set; }
    public string? AllocationsJson { get; set; }
    public string? Notes { get; set; }
    public string Status { get; set; } = "PendingAdminReview";
    public Guid DraftedBy { get; set; }
    public DateTime DraftedAt { get; set; }
    public Guid? AssignedTo { get; set; }
    public DateTime? AssignedAt { get; set; }
    public Guid? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? RejectionReason { get; set; }
    public MerchantReceivableAccount Account { get; set; } = null!;
}
