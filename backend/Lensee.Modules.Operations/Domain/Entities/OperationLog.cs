using System;
using System.Collections.Generic;

namespace Lensee.Modules.Operations.Data;

public partial class OperationLog
{
    public Guid Id { get; set; }

    public uint ConcurrencyVersion { get; private set; }

    public string OperationNumber { get; set; } = null!;

    // Optional business/customer/import reference. The generated operation
    // number remains the immutable system identifier; this value is searchable
    // and may be edited only while the operation is a draft.
    public string? ExternalReference { get; set; }

    public string OperationType { get; set; } = null!;

    public string Status { get; set; } = null!;
    public string FinancialClosureStatus { get; set; } = "Open";
    public Guid? FinancialClosureProposalId { get; set; }
    public Guid? FinanciallyClosedBy { get; set; }
    public DateTime? FinanciallyClosedAt { get; set; }

    public Guid? SourceLocationId { get; set; }

    public Guid? DestinationLocationId { get; set; }

    public Guid? ClientId { get; set; }

    public string? ClientName { get; set; }

    public Guid? RepresentativeId { get; set; }

    public Guid? MerchantExpiryRecallId { get; set; }

    public string? AutomationType { get; set; }

    public string? PaymentMethod { get; set; }

    // The actual company-funds destination for an immediately settled sale. The
    // merchant-account track is derived from ClientId and is never stored here.
    public Guid? FinanceAccountId { get; set; }

    public string SalesChannel { get; set; } = "Manual";

    public string? BuyerPhone { get; set; }

    public string? BuyerEmail { get; set; }

    public string? ShippingAddress { get; set; }

    // Online checkout facts are snapshotted on the operation; they are never
    // inferred from a later payment or courier event.
    public decimal DeliveryFee { get; set; }
    public decimal CodCharge { get; set; }
    public string? CodChargeOverrideReason { get; set; }
    public bool BuyerContactVerified { get; set; }
    public string? ExternalPaymentProvider { get; set; }
    public string? ExternalPaymentReference { get; set; }
    public bool ExternalPaymentVerified { get; set; }
    public string? PaymentStatus { get; set; }
    public string? ClientRequestId { get; set; }

    // Return exceptions are one operator interaction, retained immutably on
    // the confirmed return while normal/exception values remain on each line.
    public string? ReturnExceptionReason { get; set; }
    public string? ReturnVerbalAuthorizer { get; set; }
    public decimal ReturnNormalValue { get; set; }
    public decimal ReturnExceptionValue { get; set; }

    // A return may reference a completed sale for customer/payment context, but
    // it never creates per-line source allocations or quantity caps.  The
    // physical lines and entered value remain the operator's own record.
    public Guid? ReturnReferenceOperationId { get; set; }

    // Return workflow records each human decision independently.  The legacy
    // ReturnApprovalReason remains populated as a compatibility summary for
    // existing readers, while these facts retain the actual workflow audit.
    public string? ReturnSubmissionReason { get; set; }
    public Guid? ReturnSubmittedBy { get; set; }
    public DateTime? ReturnSubmittedAt { get; set; }
    public string? ReturnApprovalReason { get; set; }
    public string? ReturnRejectionReason { get; set; }
    public Guid? ReturnApprovedBy { get; set; }
    public DateTime? ReturnApprovedAt { get; set; }
    public Guid? ReturnRejectedBy { get; set; }
    public DateTime? ReturnRejectedAt { get; set; }

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
    public virtual ICollection<ReturnApprovalLine> ReturnApprovalLines { get; set; } = new List<ReturnApprovalLine>();

    public virtual ICollection<OperationVersion> OperationVersions { get; set; } = new List<OperationVersion>();

    public virtual ShopifyOrderLink? ShopifyOrderLink { get; set; }

    public virtual MerchantExpiryRecall? MerchantExpiryRecall { get; set; }
}
