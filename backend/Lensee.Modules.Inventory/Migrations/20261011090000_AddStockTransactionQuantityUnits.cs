using Lensee.Modules.Inventory.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Inventory.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("20261011090000_AddStockTransactionQuantityUnits")]
public partial class AddStockTransactionQuantityUnits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "chk_txn_type", schema: "inventory", table: "stock_transactions");

        migrationBuilder.AddColumn<string>(
            name: "quantity_unit",
            schema: "inventory",
            table: "stock_transactions",
            type: "character varying(10)",
            maxLength: 10,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "related_transaction_id",
            schema: "inventory",
            table: "stock_transactions",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "conversion_pieces_per_pack",
            schema: "inventory",
            table: "stock_transactions",
            type: "integer",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "chk_txn_type",
            schema: "inventory",
            table: "stock_transactions",
            sql: "transaction_type in ('Receipt','Sale','ReserveInWarehouse','ReserveWithRep','ReserveReleaseInWarehouse','ReserveReleaseWithRep','WriteOff','SupplyOut','SupplyIn','StocktakeAdjustment','ChangeOut','ChangeIn','ReturnIn','ReturnRestore','PackOpen','QuarantineIn','QuarantineRelease','QuarantineWriteOff')");
        migrationBuilder.AddCheckConstraint(
            name: "chk_txn_quantity_unit",
            schema: "inventory",
            table: "stock_transactions",
            sql: "quantity_unit is null or (quantity_unit in ('Pack','Piece') and quantity_change <> 0)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "chk_txn_quantity_unit", schema: "inventory", table: "stock_transactions");
        migrationBuilder.DropCheckConstraint(name: "chk_txn_type", schema: "inventory", table: "stock_transactions");
        migrationBuilder.DropColumn(name: "quantity_unit", schema: "inventory", table: "stock_transactions");
        migrationBuilder.DropColumn(name: "related_transaction_id", schema: "inventory", table: "stock_transactions");
        migrationBuilder.DropColumn(name: "conversion_pieces_per_pack", schema: "inventory", table: "stock_transactions");
        migrationBuilder.AddCheckConstraint(
            name: "chk_txn_type",
            schema: "inventory",
            table: "stock_transactions",
            sql: "transaction_type in ('Receipt','Sale','ReserveInWarehouse','ReserveWithRep','ReserveReleaseInWarehouse','ReserveReleaseWithRep','WriteOff','SupplyOut','SupplyIn','StocktakeAdjustment','ChangeOut','ChangeIn','ReturnIn','QuarantineIn','QuarantineRelease','QuarantineWriteOff')");
    }
}
