using Lensee.Modules.Operations.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations;

/// <summary>
/// Repairs databases whose legacy compatibility initialization recreated the
/// operation-type constraint without the current stocktake type.
/// </summary>
[DbContext(typeof(OperationsDbContext))]
[Migration("20261011094000_RepairOperationTypeConstraintDrift")]
public partial class RepairOperationTypeConstraintDrift : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("alter table if exists operations.operation_logs drop constraint if exists chk_op_type;");
        migrationBuilder.AddCheckConstraint(
            name: "chk_op_type",
            schema: "operations",
            table: "operation_logs",
            sql: "operation_type in ('InventoryReceipt','WarehouseTransfer','WholesaleSale','RetailSale','Reserve','WriteOff','StocktakeAdjustment','Change','Return')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("alter table if exists operations.operation_logs drop constraint if exists chk_op_type;");
        migrationBuilder.AddCheckConstraint(
            name: "chk_op_type",
            schema: "operations",
            table: "operation_logs",
            sql: "operation_type in ('InventoryReceipt','WarehouseTransfer','WholesaleSale','RetailSale','Reserve','WriteOff','StocktakeAdjustment','Change','Return')");
    }
}
