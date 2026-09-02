using System;

namespace Lensee.Modules.Inventory.Data;

public partial class InventoryReceiptCommand
{
    public Guid Id { get; set; }
    public Guid Key { get; set; }
    public string RequestHash { get; set; } = null!;
    public string Status { get; set; } = null!;
    public Guid? BatchId { get; set; }
    public Guid? StockTransactionId { get; set; }
    public int? ResponseBatchQuantity { get; set; }
    public int? ResponseStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}
