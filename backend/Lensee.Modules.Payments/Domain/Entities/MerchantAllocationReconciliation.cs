namespace Lensee.Modules.Payments.Data;

/// <summary>Immutable audit link that moves an effective collection allocation during an opening-balance correction.</summary>
public sealed class MerchantAllocationReconciliation
{
    public Guid Id { get; set; }
    public Guid SourceAllocationId { get; set; }
    public Guid ReplacementAllocationId { get; set; }
    public Guid CorrectionChargeId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CorrelationId { get; set; } = null!;
    public MerchantEntryAllocation SourceAllocation { get; set; } = null!;
    public MerchantEntryAllocation ReplacementAllocation { get; set; } = null!;
    public MerchantOpeningBalanceCharge CorrectionCharge { get; set; } = null!;
}
