using System;

namespace Lensee.Modules.Operations.Data;

/// <summary>
/// A maker/checker request to correct a finalized operation.  Approval creates
/// new immutable records; it never edits or removes the source operation.
/// </summary>
public partial class OperationCorrectionProposal
{
    public Guid Id { get; set; }

    public Guid OperationId { get; set; }

    public string Status { get; set; } = null!;

    public string Reason { get; set; } = null!;

    public string? SettlementMethod { get; set; }

    public decimal? SettlementAmount { get; set; }

    public bool CreateReplacementDraft { get; set; }

    public Guid RequesterId { get; set; }

    public DateTime RequestedAt { get; set; }

    public Guid? ReviewerId { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string? RejectionReason { get; set; }

    public Guid? ReversalOperationId { get; set; }

    public Guid? ReplacementOperationId { get; set; }

    public virtual OperationLog Operation { get; set; } = null!;
}
