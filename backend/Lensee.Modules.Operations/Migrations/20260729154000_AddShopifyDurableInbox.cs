using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddShopifyDurableInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "processed_at",
                schema: "operations",
                table: "shopify_webhook_events",
                type: "timestamp without time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone");

            migrationBuilder.AddColumn<string>(
                name: "api_version",
                schema: "operations",
                table: "shopify_webhook_events",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "attempt_count",
                schema: "operations",
                table: "shopify_webhook_events",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "event_id",
                schema: "operations",
                table: "shopify_webhook_events",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "lease_until",
                schema: "operations",
                table: "shopify_webhook_events",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "next_attempt_at",
                schema: "operations",
                table: "shopify_webhook_events",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "protected_payload",
                schema: "operations",
                table: "shopify_webhook_events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution_note",
                schema: "operations",
                table: "shopify_webhook_events",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "resolved_at",
                schema: "operations",
                table: "shopify_webhook_events",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "resolved_by",
                schema: "operations",
                table: "shopify_webhook_events",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shop_domain",
                schema: "operations",
                table: "shopify_webhook_events",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "triggered_at",
                schema: "operations",
                table: "shopify_webhook_events",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "verified_at",
                schema: "operations",
                table: "shopify_webhook_events",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "edited_actor_name",
                schema: "operations",
                table: "operation_versions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "created_actor_name",
                schema: "operations",
                table: "operation_logs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_shopify_webhook_events_ready",
                schema: "operations",
                table: "shopify_webhook_events",
                columns: new[] { "status", "next_attempt_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_shopify_webhook_events_ready",
                schema: "operations",
                table: "shopify_webhook_events");

            migrationBuilder.DropColumn(
                name: "api_version",
                schema: "operations",
                table: "shopify_webhook_events");

            migrationBuilder.DropColumn(
                name: "attempt_count",
                schema: "operations",
                table: "shopify_webhook_events");

            migrationBuilder.DropColumn(
                name: "event_id",
                schema: "operations",
                table: "shopify_webhook_events");

            migrationBuilder.DropColumn(
                name: "lease_until",
                schema: "operations",
                table: "shopify_webhook_events");

            migrationBuilder.DropColumn(
                name: "next_attempt_at",
                schema: "operations",
                table: "shopify_webhook_events");

            migrationBuilder.DropColumn(
                name: "protected_payload",
                schema: "operations",
                table: "shopify_webhook_events");

            migrationBuilder.DropColumn(
                name: "resolution_note",
                schema: "operations",
                table: "shopify_webhook_events");

            migrationBuilder.DropColumn(
                name: "resolved_at",
                schema: "operations",
                table: "shopify_webhook_events");

            migrationBuilder.DropColumn(
                name: "resolved_by",
                schema: "operations",
                table: "shopify_webhook_events");

            migrationBuilder.DropColumn(
                name: "shop_domain",
                schema: "operations",
                table: "shopify_webhook_events");

            migrationBuilder.DropColumn(
                name: "triggered_at",
                schema: "operations",
                table: "shopify_webhook_events");

            migrationBuilder.DropColumn(
                name: "verified_at",
                schema: "operations",
                table: "shopify_webhook_events");

            migrationBuilder.DropColumn(
                name: "edited_actor_name",
                schema: "operations",
                table: "operation_versions");

            migrationBuilder.DropColumn(
                name: "created_actor_name",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.AlterColumn<DateTime>(
                name: "processed_at",
                schema: "operations",
                table: "shopify_webhook_events",
                type: "timestamp without time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp without time zone",
                oldNullable: true);
        }
    }
}
