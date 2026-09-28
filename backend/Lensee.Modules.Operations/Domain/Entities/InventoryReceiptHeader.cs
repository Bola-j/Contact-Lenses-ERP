using System;
using System.Collections.Generic;

namespace Lensee.Modules.Operations.Data;

public partial class InventoryReceiptHeader
{
    public Guid Id { get; set; }

    public Guid OperationId { get; set; }

    public string SupplierName { get; set; } = null!;

    public string? InvoiceNumber { get; set; }

    public DateTime ReceiptDate { get; set; }

    public Guid? SupplyShipmentId { get; set; }
    public Guid? SupplyFinanceLogId { get; set; }
    public Guid? StocktakeSessionId { get; set; }
    public Guid? SupplyReceivingSessionId { get; set; }

    public virtual OperationLog Operation { get; set; } = null!;
}
