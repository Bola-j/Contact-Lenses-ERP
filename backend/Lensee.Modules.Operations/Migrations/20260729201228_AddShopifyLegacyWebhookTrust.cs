using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddShopifyLegacyWebhookTrust : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "verification_mode",
                schema: "operations",
                table: "shopify_webhook_events",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Hmac");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "verification_mode",
                schema: "operations",
                table: "shopify_webhook_events");
        }
    }
}
