using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddShopifyIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "buyer_email",
                schema: "operations",
                table: "operation_logs",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "buyer_phone",
                schema: "operations",
                table: "operation_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sales_channel",
                schema: "operations",
                table: "operation_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<string>(
                name: "shipping_address",
                schema: "operations",
                table: "operation_logs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "shopify_order_links",
                schema: "operations",
                columns: table => new
                {
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shopify_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    shopify_order_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    payment_reference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("shopify_order_links_pkey", x => x.operation_id);
                    table.ForeignKey(
                        name: "shopify_order_links_operation_id_fkey",
                        column: x => x.operation_id,
                        principalSchema: "operations",
                        principalTable: "operation_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shopify_variant_mappings",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    shopify_variant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Packs"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("shopify_variant_mappings_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shopify_webhook_events",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    webhook_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    topic = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    detail = table.Column<string>(type: "text", nullable: true),
                    shopify_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("shopify_webhook_events_pkey", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_op_logs_sales_channel",
                schema: "operations",
                table: "operation_logs",
                column: "sales_channel");

            migrationBuilder.CreateIndex(
                name: "uq_shopify_order_links_order",
                schema: "operations",
                table: "shopify_order_links",
                column: "shopify_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_shopify_variant_mappings_sku",
                schema: "operations",
                table: "shopify_variant_mappings",
                column: "sku_id");

            migrationBuilder.CreateIndex(
                name: "uq_shopify_variant_mappings_variant",
                schema: "operations",
                table: "shopify_variant_mappings",
                column: "shopify_variant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_shopify_webhook_events_order",
                schema: "operations",
                table: "shopify_webhook_events",
                column: "shopify_order_id");

            migrationBuilder.CreateIndex(
                name: "uq_shopify_webhook_events_webhook",
                schema: "operations",
                table: "shopify_webhook_events",
                column: "webhook_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shopify_order_links",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "shopify_variant_mappings",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "shopify_webhook_events",
                schema: "operations");

            migrationBuilder.DropIndex(
                name: "idx_op_logs_sales_channel",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "buyer_email",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "buyer_phone",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "sales_channel",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "shipping_address",
                schema: "operations",
                table: "operation_logs");
        }
    }
}
