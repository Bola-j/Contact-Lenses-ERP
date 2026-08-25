using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionConstraintsOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("alter table if exists operations.stocktake_sessions drop constraint if exists chk_stocktake_status;");
            migrationBuilder.Sql("alter table if exists operations.operation_logs drop constraint if exists chk_op_payment_method;");
            migrationBuilder.Sql("alter table if exists operations.operation_logs drop constraint if exists chk_op_status;");
            migrationBuilder.Sql("alter table if exists operations.operation_logs drop constraint if exists chk_op_type;");
            migrationBuilder.Sql("alter table if exists operations.operation_lines drop constraint if exists chk_operation_lines_bonus_quantity;");
            migrationBuilder.Sql("alter table if exists operations.operation_lines drop constraint if exists chk_operation_lines_entry_mode;");
            migrationBuilder.Sql("alter table if exists operations.operation_lines drop constraint if exists chk_operation_lines_line_total;");
            migrationBuilder.Sql("alter table if exists operations.operation_lines drop constraint if exists chk_operation_lines_quantity;");
            migrationBuilder.Sql("alter table if exists operations.operation_lines drop constraint if exists chk_operation_lines_section;");
            migrationBuilder.Sql("alter table if exists operations.operation_lines drop constraint if exists chk_operation_lines_unit_cost;");
            migrationBuilder.Sql("alter table if exists operations.operation_lines drop constraint if exists chk_operation_lines_unit_price;");

            migrationBuilder.AddCheckConstraint(
                name: "chk_stocktake_status",
                schema: "operations",
                table: "stocktake_sessions",
                sql: "status in ('Draft','Confirmed')");

            migrationBuilder.CreateIndex(
                name: "uq_stocktake_line_batch",
                schema: "operations",
                table: "stocktake_adjustment_lines",
                columns: new[] { "session_id", "sku_id", "lot_number", "expiry_date" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_op_payment_method",
                schema: "operations",
                table: "operation_logs",
                sql: "payment_method is null or payment_method in ('CashHandToHand','CashTransaction','Installment')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_op_status",
                schema: "operations",
                table: "operation_logs",
                sql: "status in ('Draft','Confirmed','Completed','Reserved','Shipped','Received','Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_op_type",
                schema: "operations",
                table: "operation_logs",
                sql: "operation_type in ('InventoryReceipt','WarehouseTransfer','WholesaleSale','RetailSale','Reserve','WriteOff','StocktakeAdjustment','Change','Return')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_operation_lines_bonus_quantity",
                schema: "operations",
                table: "operation_lines",
                sql: "bonus_quantity >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_operation_lines_entry_mode",
                schema: "operations",
                table: "operation_lines",
                sql: "entry_mode in ('Packs','Pieces')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_operation_lines_line_total",
                schema: "operations",
                table: "operation_lines",
                sql: "line_total >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_operation_lines_quantity",
                schema: "operations",
                table: "operation_lines",
                sql: "quantity >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_operation_lines_section",
                schema: "operations",
                table: "operation_lines",
                sql: "section in ('Standard','ChangeOut','ChangeIn')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_operation_lines_unit_cost",
                schema: "operations",
                table: "operation_lines",
                sql: "unit_cost is null or unit_cost >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_operation_lines_unit_price",
                schema: "operations",
                table: "operation_lines",
                sql: "unit_price >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_stocktake_status",
                schema: "operations",
                table: "stocktake_sessions");

            migrationBuilder.DropIndex(
                name: "uq_stocktake_line_batch",
                schema: "operations",
                table: "stocktake_adjustment_lines");

            migrationBuilder.DropCheckConstraint(
                name: "chk_op_payment_method",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropCheckConstraint(
                name: "chk_op_status",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropCheckConstraint(
                name: "chk_op_type",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropCheckConstraint(
                name: "chk_operation_lines_bonus_quantity",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropCheckConstraint(
                name: "chk_operation_lines_entry_mode",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropCheckConstraint(
                name: "chk_operation_lines_line_total",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropCheckConstraint(
                name: "chk_operation_lines_quantity",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropCheckConstraint(
                name: "chk_operation_lines_section",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropCheckConstraint(
                name: "chk_operation_lines_unit_cost",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropCheckConstraint(
                name: "chk_operation_lines_unit_price",
                schema: "operations",
                table: "operation_lines");
        }
    }
}
