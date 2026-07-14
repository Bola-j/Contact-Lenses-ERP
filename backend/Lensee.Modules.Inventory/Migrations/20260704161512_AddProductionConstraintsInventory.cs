using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Inventory.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionConstraintsInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("alter table if exists inventory.stock_transactions drop constraint if exists chk_txn_type;");
            migrationBuilder.Sql("alter table if exists inventory.stock_balances drop constraint if exists chk_stock_balances_available;");
            migrationBuilder.Sql("alter table if exists inventory.stock_balances drop constraint if exists chk_stock_balances_reserved_rep;");
            migrationBuilder.Sql("alter table if exists inventory.stock_balances drop constraint if exists chk_stock_balances_reserved_warehouse;");
            migrationBuilder.Sql("alter table if exists inventory.stock_balances drop constraint if exists chk_stock_balances_row_version;");
            migrationBuilder.Sql("alter table if exists inventory.stock_balances drop constraint if exists chk_stock_balances_target;");
            migrationBuilder.Sql("alter table if exists inventory.opened_piece_lots drop constraint if exists chk_opened_piece_lots_quantity;");
            migrationBuilder.Sql("alter table if exists inventory.locations drop constraint if exists chk_locations_type;");
            migrationBuilder.Sql("alter table if exists inventory.inventory_batches drop constraint if exists chk_inventory_batches_quantity;");

            migrationBuilder.AddCheckConstraint(
                name: "chk_txn_type",
                schema: "inventory",
                table: "stock_transactions",
                sql: "transaction_type in ('Receipt','Sale','ReserveInWarehouse','ReserveWithRep','ReserveReleaseInWarehouse','ReserveReleaseWithRep','WriteOff','SupplyOut','SupplyIn','StocktakeAdjustment','ChangeOut','ChangeIn','ReturnIn')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_stock_balances_available",
                schema: "inventory",
                table: "stock_balances",
                sql: "available_qty >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_stock_balances_reserved_rep",
                schema: "inventory",
                table: "stock_balances",
                sql: "reserved_with_rep_qty >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_stock_balances_reserved_warehouse",
                schema: "inventory",
                table: "stock_balances",
                sql: "reserved_in_warehouse_qty >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_stock_balances_row_version",
                schema: "inventory",
                table: "stock_balances",
                sql: "row_version >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_stock_balances_target",
                schema: "inventory",
                table: "stock_balances",
                sql: "target_qty is null or target_qty >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_opened_piece_lots_quantity",
                schema: "inventory",
                table: "opened_piece_lots",
                sql: "loose_piece_quantity >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_locations_type",
                schema: "inventory",
                table: "locations",
                sql: "location_type in ('MainWarehouse','SubWarehouse','Online')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_inventory_batches_quantity",
                schema: "inventory",
                table: "inventory_batches",
                sql: "quantity >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_txn_type",
                schema: "inventory",
                table: "stock_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_stock_balances_available",
                schema: "inventory",
                table: "stock_balances");

            migrationBuilder.DropCheckConstraint(
                name: "chk_stock_balances_reserved_rep",
                schema: "inventory",
                table: "stock_balances");

            migrationBuilder.DropCheckConstraint(
                name: "chk_stock_balances_reserved_warehouse",
                schema: "inventory",
                table: "stock_balances");

            migrationBuilder.DropCheckConstraint(
                name: "chk_stock_balances_row_version",
                schema: "inventory",
                table: "stock_balances");

            migrationBuilder.DropCheckConstraint(
                name: "chk_stock_balances_target",
                schema: "inventory",
                table: "stock_balances");

            migrationBuilder.DropCheckConstraint(
                name: "chk_opened_piece_lots_quantity",
                schema: "inventory",
                table: "opened_piece_lots");

            migrationBuilder.DropCheckConstraint(
                name: "chk_locations_type",
                schema: "inventory",
                table: "locations");

            migrationBuilder.DropCheckConstraint(
                name: "chk_inventory_batches_quantity",
                schema: "inventory",
                table: "inventory_batches");
        }
    }
}
