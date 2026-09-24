namespace Lensee.Modules.Finance.Data;

public sealed class PaymentMovementRegistry
{
    public Guid Id { get; set; }
    public string SourceType { get; set; } = null!;
    public Guid SourceId { get; set; }
    public string Track { get; set; } = null!;
    public string MovementMethod { get; set; } = null!;
    public decimal Amount { get; set; }
    public Guid FinanceAccountId { get; set; }
    public string? NormalizedExternalReference { get; set; }
    public string Status { get; set; } = "Posted";
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CorrelationId { get; set; }
}
