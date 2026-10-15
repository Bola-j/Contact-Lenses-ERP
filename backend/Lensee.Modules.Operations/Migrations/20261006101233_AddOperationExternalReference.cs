using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationExternalReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_reference",
                schema: "operations",
                table: "operation_logs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_operation_logs_external_reference",
                schema: "operations",
                table: "operation_logs",
                column: "external_reference");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_operation_logs_external_reference",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "external_reference",
                schema: "operations",
                table: "operation_logs");
        }
    }
}
