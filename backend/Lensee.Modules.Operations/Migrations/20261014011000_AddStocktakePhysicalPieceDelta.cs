using Lensee.Modules.Operations.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations;

[DbContext(typeof(OperationsDbContext))]
[Migration("20261014011000_AddStocktakePhysicalPieceDelta")]
public partial class AddStocktakePhysicalPieceDelta : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "pieces_per_pack_snapshot",
            table: "stocktake_adjustment_lines",
            schema: "operations",
            nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "delta_physical_pieces",
            table: "stocktake_adjustment_lines",
            schema: "operations",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("pieces_per_pack_snapshot", "stocktake_adjustment_lines", "operations");
        migrationBuilder.DropColumn("delta_physical_pieces", "stocktake_adjustment_lines", "operations");
    }
}
