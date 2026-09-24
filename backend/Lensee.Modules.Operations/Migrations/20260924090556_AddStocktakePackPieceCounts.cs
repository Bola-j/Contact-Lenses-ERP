using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddStocktakePackPieceCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "delta_pack_count",
                schema: "operations",
                table: "stocktake_adjustment_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "delta_piece_count",
                schema: "operations",
                table: "stocktake_adjustment_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "physical_pack_count",
                schema: "operations",
                table: "stocktake_adjustment_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "physical_piece_count",
                schema: "operations",
                table: "stocktake_adjustment_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "system_pack_count",
                schema: "operations",
                table: "stocktake_adjustment_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "system_piece_count",
                schema: "operations",
                table: "stocktake_adjustment_lines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE operations.stocktake_adjustment_lines SET physical_pack_count = physical_count, system_pack_count = system_qty_before, delta_pack_count = delta WHERE physical_pack_count = 0 AND physical_piece_count = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "delta_pack_count",
                schema: "operations",
                table: "stocktake_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "delta_piece_count",
                schema: "operations",
                table: "stocktake_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "physical_pack_count",
                schema: "operations",
                table: "stocktake_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "physical_piece_count",
                schema: "operations",
                table: "stocktake_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "system_pack_count",
                schema: "operations",
                table: "stocktake_adjustment_lines");

            migrationBuilder.DropColumn(
                name: "system_piece_count",
                schema: "operations",
                table: "stocktake_adjustment_lines");
        }
    }
}
