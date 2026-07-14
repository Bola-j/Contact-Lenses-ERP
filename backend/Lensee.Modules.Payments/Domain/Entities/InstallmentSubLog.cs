using System;
using System.Collections.Generic;

namespace Lensee.Modules.Payments.Data;

public partial class InstallmentSubLog
{
    public Guid Id { get; set; }

    public Guid MainLogId { get; set; }

    public decimal Amount { get; set; }

    public string? PaymentMethod { get; set; }

    public DateOnly DateReceived { get; set; }

    public string SubLogStatus { get; set; } = null!;

    public Guid DraftedBy { get; set; }

    public DateTime DraftedAt { get; set; }

    public Guid? ConfirmedBy { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    public string? RejectionReason { get; set; }

    public string? Notes { get; set; }

    public virtual MainPaymentLog MainLog { get; set; } = null!;
}
