using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddCanonicalSaleLineTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "line_type",
                schema: "operations",
                table: "operation_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_operation_lines_line_type",
                schema: "operations",
                table: "operation_lines",
                sql: "line_type is null or line_type in ('Paid','Bonus','Offer')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_operation_lines_line_type",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropColumn(
                name: "line_type",
                schema: "operations",
                table: "operation_lines");
        }
    }
}
