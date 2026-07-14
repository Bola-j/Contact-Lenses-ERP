using System;
using System.Collections.Generic;

namespace Lensee.Modules.Payments.Data;

public partial class MainPaymentLog
{
    public Guid Id { get; set; }

    public Guid OperationId { get; set; }

    public Guid? MerchantId { get; set; }

    public decimal TotalAmount { get; set; }

    public decimal AmountPaid { get; set; }

    public string PaymentMethod { get; set; } = null!;

    public string Status { get; set; } = null!;

    public Guid InitializedBy { get; set; }

    public DateTime InitializedAt { get; set; }

    public Guid? AssignedTo { get; set; }

    public DateTime? AssignedAt { get; set; }

    public Guid? LastModifiedBy { get; set; }

    public DateTime LastModifiedAt { get; set; }

    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }

    public virtual ICollection<InstallmentSubLog> InstallmentSubLogs { get; set; } = new List<InstallmentSubLog>();
}
