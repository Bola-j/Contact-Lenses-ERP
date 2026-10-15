using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Inventory.Migrations
{
    /// <inheritdoc />
    public partial class AddQuarantinedInventoryBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_txn_type",
                schema: "inventory",
                table: "stock_transactions");

            migrationBuilder.AddColumn<bool>(
                name: "is_quarantined",
                schema: "inventory",
                table: "inventory_batches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "chk_txn_type",
                schema: "inventory",
                table: "stock_transactions",
                sql: "transaction_type in ('Receipt','Sale','ReserveInWarehouse','ReserveWithRep','ReserveReleaseInWarehouse','ReserveReleaseWithRep','WriteOff','SupplyOut','SupplyIn','StocktakeAdjustment','ChangeOut','ChangeIn','ReturnIn','QuarantineIn','QuarantineRelease','QuarantineWriteOff')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_txn_type",
                schema: "inventory",
                table: "stock_transactions");

            migrationBuilder.DropColumn(
                name: "is_quarantined",
                schema: "inventory",
                table: "inventory_batches");

            migrationBuilder.AddCheckConstraint(
                name: "chk_txn_type",
                schema: "inventory",
                table: "stock_transactions",
                sql: "transaction_type in ('Receipt','Sale','ReserveInWarehouse','ReserveWithRep','ReserveReleaseInWarehouse','ReserveReleaseWithRep','WriteOff','SupplyOut','SupplyIn','StocktakeAdjustment','ChangeOut','ChangeIn','ReturnIn')");
        }
    }
}
