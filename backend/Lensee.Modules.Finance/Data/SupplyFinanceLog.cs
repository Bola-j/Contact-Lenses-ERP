namespace Lensee.Modules.Finance.Data;

/// <summary>
/// Financial identity for one supply shipment.  It intentionally references the
/// Operations shipment by ID instead of owning inventory details.
/// </summary>
public sealed class SupplyFinanceLog
{
    public Guid Id { get; set; }
    public Guid SupplyShipmentId { get; set; }
    public string ShipmentNumber { get; set; } = null!;
    public string SupplierName { get; set; } = null!;
    public string Status { get; set; } = "Open";
    public string? Notes { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public ICollection<SupplyFinanceCostEntry> CostEntries { get; set; } = new List<SupplyFinanceCostEntry>();
    public ICollection<SupplySupplierInstallment> Installments { get; set; } = new List<SupplySupplierInstallment>();
}
