namespace Lensee.Modules.Operations.Data;

public partial class ShopifyWebhookEvent
{
    public Guid Id { get; set; }
    public string WebhookId { get; set; } = null!;
    public string Topic { get; set; } = null!;
    public string ShopDomain { get; set; } = null!;
    public string? EventId { get; set; }
    public string? ApiVersion { get; set; }
    public string PayloadHash { get; set; } = null!;
    public string? ProtectedPayload { get; set; }
    public string Status { get; set; } = null!;
    public string? Detail { get; set; }
    public string? ShopifyOrderId { get; set; }
    public Guid? OperationId { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public DateTime? TriggeredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedBy { get; set; }
    public string? ResolutionNote { get; set; }
}
