using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class BackfillSupplyFinanceLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Finance must be migrated before Operations (the production migrator's
            // dependency order). This copies only descriptive legacy shipment facts;
            // it deliberately creates no Finance ledger entry or inventory movement.
            migrationBuilder.Sql("""
                insert into finance.supply_finance_logs
                    ("Id", "SupplyShipmentId", "ShipmentNumber", "SupplierName", "Status", "Notes", "CreatedByUserId", "CreatedAt")
                select uuid_generate_v4(), shipment.id, shipment.shipment_number, shipment.supplier_name,
                    'Open', shipment.notes, shipment.created_by, shipment.created_at
                from operations.supply_shipments shipment
                where not exists (
                    select 1 from finance.supply_finance_logs log
                    where log."SupplyShipmentId" = shipment.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The compatibility backfill is intentionally forward-only.
        }
    }
}
