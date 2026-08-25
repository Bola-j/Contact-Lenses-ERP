using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.CRM.Migrations
{
    /// <inheritdoc />
    public partial class AddShopifyExternalCustomerKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_customer_id",
                schema: "crm",
                table: "merchants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_provider",
                schema: "crm",
                table: "merchants",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_merchants_external_customer",
                schema: "crm",
                table: "merchants",
                columns: new[] { "external_provider", "external_customer_id" },
                unique: true,
                filter: "(external_provider IS NOT NULL AND external_customer_id IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_merchants_external_customer",
                schema: "crm",
                table: "merchants");

            migrationBuilder.DropColumn(
                name: "external_customer_id",
                schema: "crm",
                table: "merchants");

            migrationBuilder.DropColumn(
                name: "external_provider",
                schema: "crm",
                table: "merchants");
        }
    }
}
