using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotOperationPackSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "pieces_per_pack_snapshot",
                schema: "operations",
                table: "operation_lines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_operation_lines_pieces_per_pack_snapshot",
                schema: "operations",
                table: "operation_lines",
                sql: "pieces_per_pack_snapshot is null or pieces_per_pack_snapshot > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_operation_lines_pieces_per_pack_snapshot",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropColumn(
                name: "pieces_per_pack_snapshot",
                schema: "operations",
                table: "operation_lines");
        }
    }
}
