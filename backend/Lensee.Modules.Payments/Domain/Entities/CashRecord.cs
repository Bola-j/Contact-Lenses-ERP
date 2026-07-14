using System;
using System.Collections.Generic;

namespace Lensee.Modules.Payments.Data;

public partial class CashRecord
{
    public Guid Id { get; set; }

    public Guid OperationId { get; set; }

    public string PaymentType { get; set; } = null!;

    public string? SubType { get; set; }

    public decimal Amount { get; set; }

    public string Status { get; set; } = null!;

    public DateTime PaymentDate { get; set; }

    public Guid CreatedBy { get; set; }

    public string? Notes { get; set; }
}
