namespace Lensee.Modules.Payments.Data;

/// <summary>
/// Append-only payment workflow evidence. The general system audit remains the
/// cross-module record; this table provides payment-specific filtering and
/// financial lineage without parsing a generic JSON payload.
/// </summary>
public sealed class PaymentAuditEvent
{
    public Guid Id { get; set; }
    public string Action { get; set; } = null!;
    public string? PreviousStatus { get; set; }
    public string? NewStatus { get; set; }
    public Guid? MerchantId { get; set; }
    public Guid? OperationId { get; set; }
    public Guid? PaymentLogId { get; set; }
    public Guid? CollectionDraftId { get; set; }
    public Guid ActorId { get; set; }
    public DateTime OccurredAt { get; set; }
    public decimal? Amount { get; set; }
    public string? PaymentMethod { get; set; }
    public string? Reason { get; set; }
    public string? CorrelationId { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? DataJson { get; set; }
}
