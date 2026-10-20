using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddShopifyGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "shopify_refund_id",
                schema: "operations",
                table: "shopify_webhook_events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "lease_token",
                schema: "operations",
                table: "shopify_webhook_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "cancelled_at",
                schema: "operations",
                table: "shopify_order_links",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "commercial_snapshot",
                schema: "operations",
                table: "shopify_order_links",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "commercial_snapshot_hash",
                schema: "operations",
                table: "shopify_order_links",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "finance_reconciliation_status",
                schema: "operations",
                table: "shopify_order_links",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "PendingEvidence");

            migrationBuilder.AddColumn<string>(
                name: "financial_status",
                schema: "operations",
                table: "shopify_order_links",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "financial_status_trusted",
                schema: "operations",
                table: "shopify_order_links",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_cancellation_tombstone",
                schema: "operations",
                table: "shopify_order_links",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "presentment_currency",
                schema: "operations",
                table: "shopify_order_links",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reported_currency",
                schema: "operations",
                table: "shopify_order_links",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "reported_discounts",
                schema: "operations",
                table: "shopify_order_links",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "reported_shipping",
                schema: "operations",
                table: "shopify_order_links",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "reported_subtotal",
                schema: "operations",
                table: "shopify_order_links",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "reported_tax",
                schema: "operations",
                table: "shopify_order_links",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "reported_total",
                schema: "operations",
                table: "shopify_order_links",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_review",
                schema: "operations",
                table: "shopify_order_links",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "shop_domain",
                schema: "operations",
                table: "shopify_order_links",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "source_updated_at",
                schema: "operations",
                table: "shopify_order_links",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shopify_commercial_snapshot",
                schema: "operations",
                table: "operation_lines",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "shopify_order_cancellation_tombstones",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    shop_domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    shopify_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    cancelled_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("shopify_order_cancellation_tombstones_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shopify_reconciliation_checkpoints",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    shop_domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    activated_at_utc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_through_utc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    window_started_utc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    window_ended_utc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cursor = table.Column<string>(type: "text", nullable: true),
                    next_run_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    requested_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    lease_until = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    last_success_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("shopify_reconciliation_checkpoints_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shopify_refund_reviews",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    shop_domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    shopify_refund_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    shopify_order_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("shopify_refund_reviews_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shopify_variant_links",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    shop_domain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    shopify_variant_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    shopify_sku = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Pieces"),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: "Unresolved"),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("shopify_variant_links_pkey", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "uq_shopify_order_cancellation_tombstones_order",
                schema: "operations",
                table: "shopify_order_cancellation_tombstones",
                columns: new[] { "shop_domain", "shopify_order_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_shopify_reconciliation_checkpoints_shop",
                schema: "operations",
                table: "shopify_reconciliation_checkpoints",
                column: "shop_domain",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_shopify_refund_reviews_identity",
                schema: "operations",
                table: "shopify_refund_reviews",
                columns: new[] { "shop_domain", "shopify_refund_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_shopify_variant_links_sku",
                schema: "operations",
                table: "shopify_variant_links",
                column: "sku_id");

            migrationBuilder.CreateIndex(
                name: "uq_shopify_variant_links_store_variant",
                schema: "operations",
                table: "shopify_variant_links",
                columns: new[] { "shop_domain", "shopify_variant_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shopify_order_cancellation_tombstones",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "shopify_reconciliation_checkpoints",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "shopify_refund_reviews",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "shopify_variant_links",
                schema: "operations");

            migrationBuilder.DropColumn(
                name: "shopify_refund_id",
                schema: "operations",
                table: "shopify_webhook_events");

            migrationBuilder.DropColumn(
                name: "lease_token",
                schema: "operations",
                table: "shopify_webhook_events");

            migrationBuilder.DropColumn(
                name: "cancelled_at",
                schema: "operations",
                table: "shopify_order_links");

            migrationBuilder.DropColumn(
                name: "commercial_snapshot",
                schema: "operations",
                table: "shopify_order_links");

            migrationBuilder.DropColumn(
                name: "commercial_snapshot_hash",
                schema: "operations",
                table: "shopify_order_links");

            migrationBuilder.DropColumn(
                name: "finance_reconciliation_status",
                schema: "operations",
                table: "shopify_order_links");

            migrationBuilder.DropColumn(
                name: "financial_status",
                schema: "operations",
                table: "shopify_order_links");

            migrationBuilder.DropColumn(
                name: "financial_status_trusted",
                schema: "operations",
                table: "shopify_order_links");

            migrationBuilder.DropColumn(
                name: "is_cancellation_tombstone",
                schema: "operations",
                table: "shopify_order_links");

            migrationBuilder.DropColumn(
                name: "presentment_currency",
                schema: "operations",
                table: "shopify_order_links");

            migrationBuilder.DropColumn(
                name: "reported_currency",
                schema: "operations",
                table: "shopify_order_links");

            migrationBuilder.DropColumn(
                name: "reported_discounts",
                schema: "operations",
                table: "shopify_order_links");

            migrationBuilder.DropColumn(
                name: "reported_shipping",
                schema: "operations",
                table: "shopify_order_links");

            migrationBuilder.DropColumn(
                name: "reported_subtotal",
                schema: "operations",
                table: "shopify_order_links");

            migrationBuilder.DropColumn(
                name: "reported_tax",
                schema: "operations",
                table: "shopify_order_links");

            migrationBuilder.DropColumn(
                name: "reported_total",
                schema: "operations",
                table: "shopify_order_links");

            migrationBuilder.DropColumn(
                name: "requires_review",
                schema: "operations",
                table: "shopify_order_links");

            migrationBuilder.DropColumn(
                name: "shop_domain",
                schema: "operations",
                table: "shopify_order_links");

            migrationBuilder.DropColumn(
                name: "source_updated_at",
                schema: "operations",
                table: "shopify_order_links");

            migrationBuilder.DropColumn(
                name: "shopify_commercial_snapshot",
                schema: "operations",
                table: "operation_lines");
        }
    }
}
