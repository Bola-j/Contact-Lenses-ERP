using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnLineDisposition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "return_disposition",
                schema: "operations",
                table: "operation_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_operation_lines_return_disposition",
                schema: "operations",
                table: "operation_lines",
                sql: "return_disposition is null or return_disposition in ('Sellable','Quarantine','WriteOff')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_operation_lines_return_disposition",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropColumn(
                name: "return_disposition",
                schema: "operations",
                table: "operation_lines");
        }
    }
}
