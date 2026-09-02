using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddConcurrencyAndStocktakeBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_supply_shipments_operation",
                schema: "operations",
                table: "supply_shipments");

            migrationBuilder.AddColumn<int>(
                name: "baseline_stock_row_version",
                schema: "operations",
                table: "stocktake_adjustment_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "uq_supply_shipments_operation",
                schema: "operations",
                table: "supply_shipments",
                column: "inventory_receipt_operation_id",
                unique: true,
                filter: "(inventory_receipt_operation_id IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_supply_shipments_operation",
                schema: "operations",
                table: "supply_shipments");

            migrationBuilder.DropColumn(
                name: "baseline_stock_row_version",
                schema: "operations",
                table: "stocktake_adjustment_lines");

            migrationBuilder.CreateIndex(
                name: "idx_supply_shipments_operation",
                schema: "operations",
                table: "supply_shipments",
                column: "inventory_receipt_operation_id");
        }
    }
}
