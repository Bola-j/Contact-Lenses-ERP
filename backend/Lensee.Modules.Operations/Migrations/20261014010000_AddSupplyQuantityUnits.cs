using Lensee.Modules.Operations.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations;

/// <summary>
/// Makes the existing sealed-pack supply contract explicit without changing any quantity.
/// </summary>
[DbContext(typeof(OperationsDbContext))]
[Migration("20261014010000_AddSupplyQuantityUnits")]
public partial class AddSupplyQuantityUnits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "quantity_unit",
            schema: "operations",
            table: "supply_shipment_lines",
            type: "character varying(10)",
            maxLength: 10,
            nullable: false,
            defaultValue: "Pack");
        migrationBuilder.AddColumn<string>(
            name: "quantity_unit",
            schema: "operations",
            table: "supply_receiving_lines",
            type: "character varying(10)",
            maxLength: 10,
            nullable: false,
            defaultValue: "Pack");
        migrationBuilder.AddCheckConstraint(
            name: "chk_supply_shipment_lines_quantity_unit",
            schema: "operations",
            table: "supply_shipment_lines",
            sql: "quantity_unit = 'Pack'");
        migrationBuilder.AddCheckConstraint(
            name: "chk_supply_receiving_lines_quantity_unit",
            schema: "operations",
            table: "supply_receiving_lines",
            sql: "quantity_unit = 'Pack'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("chk_supply_shipment_lines_quantity_unit", "operations", "supply_shipment_lines");
        migrationBuilder.DropCheckConstraint("chk_supply_receiving_lines_quantity_unit", "operations", "supply_receiving_lines");
        migrationBuilder.DropColumn("quantity_unit", "operations", "supply_shipment_lines");
        migrationBuilder.DropColumn("quantity_unit", "operations", "supply_receiving_lines");
    }
}
