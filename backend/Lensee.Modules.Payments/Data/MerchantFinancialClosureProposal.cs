using System;
using System.Collections.Generic;

namespace Lensee.Modules.Payments.Data;

public sealed class MerchantFinancialClosureProposal
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public string Status { get; set; } = "PendingAdminReview";
    public Guid SubmittedBy { get; set; }
    public DateTime SubmittedAt { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewReason { get; set; }
    public string? Notes { get; set; }
    public string? IdempotencyKey { get; set; }
    public ICollection<MerchantFinancialClosureItem> Items { get; set; } = new List<MerchantFinancialClosureItem>();
}

public sealed class MerchantFinancialClosureItem
{
    public Guid Id { get; set; }
    public Guid ProposalId { get; set; }
    public Guid OperationId { get; set; }
    public string OperationNumber { get; set; } = null!;
    public decimal SettlementAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public string Decision { get; set; } = "Pending";
    public string? RejectionReason { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTime? DecidedAt { get; set; }
    public MerchantFinancialClosureProposal Proposal { get; set; } = null!;
}
