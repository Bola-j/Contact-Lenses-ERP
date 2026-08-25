using System;
using System.Collections.Generic;

namespace Lensee.Modules.Operations.Data;

public partial class OperationLog
{
    public Guid Id { get; set; }

    public string OperationNumber { get; set; } = null!;

    public string OperationType { get; set; } = null!;

    public string Status { get; set; } = null!;

    public Guid? SourceLocationId { get; set; }

    public Guid? DestinationLocationId { get; set; }

    public Guid? ClientId { get; set; }

    public string? ClientName { get; set; }

    public Guid? RepresentativeId { get; set; }

    public Guid? MerchantExpiryRecallId { get; set; }

    public string? AutomationType { get; set; }

    public string? PaymentMethod { get; set; }

    public string SalesChannel { get; set; } = "Manual";

    public string? BuyerPhone { get; set; }

    public string? BuyerEmail { get; set; }

    public string? ShippingAddress { get; set; }

    public Guid? CurrentVersionId { get; set; }

    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public Guid CreatedBy { get; set; }

    public string? CreatedActorName { get; set; }

    public Guid? ConfirmedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    // Financially-final operations are never edited in place.  These fields form
    // immutable lineage between the original, its compensating reversal, and an
    // optional replacement draft.
    public string RecordKind { get; set; } = "Standard";

    public Guid? ReversesOperationId { get; set; }

    public Guid? ReplacedOperationId { get; set; }

    public Guid? CorrectionProposalId { get; set; }

    public string? CorrectionReason { get; set; }

    public Guid? CorrectedBy { get; set; }

    public DateTime? CorrectedAt { get; set; }

    public virtual OperationVersion? CurrentVersion { get; set; }

    public virtual InventoryReceiptHeader? InventoryReceiptHeader { get; set; }

    public virtual ICollection<OperationLine> OperationLines { get; set; } = new List<OperationLine>();

    public virtual ICollection<OperationVersion> OperationVersions { get; set; } = new List<OperationVersion>();

    public virtual ShopifyOrderLink? ShopifyOrderLink { get; set; }

    public virtual MerchantExpiryRecall? MerchantExpiryRecall { get; set; }
}
