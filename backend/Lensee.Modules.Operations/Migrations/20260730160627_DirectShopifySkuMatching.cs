using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class DirectShopifySkuMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "shopify_variant_mappings",
                schema: "operations",
                newName: "shopify_variant_mappings_legacy",
                newSchema: "operations");

            migrationBuilder.AddColumn<string>(
                name: "shopify_line_item_id",
                schema: "operations",
                table: "operation_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shopify_properties_snapshot",
                schema: "operations",
                table: "operation_lines",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shopify_sku_snapshot",
                schema: "operations",
                table: "operation_lines",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shopify_title_snapshot",
                schema: "operations",
                table: "operation_lines",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shopify_variant_id",
                schema: "operations",
                table: "operation_lines",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "shopify_variant_title_snapshot",
                schema: "operations",
                table: "operation_lines",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "shopify_line_item_id",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropColumn(
                name: "shopify_properties_snapshot",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropColumn(
                name: "shopify_sku_snapshot",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropColumn(
                name: "shopify_title_snapshot",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropColumn(
                name: "shopify_variant_id",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.DropColumn(
                name: "shopify_variant_title_snapshot",
                schema: "operations",
                table: "operation_lines");

            migrationBuilder.RenameTable(
                name: "shopify_variant_mappings_legacy",
                schema: "operations",
                newName: "shopify_variant_mappings",
                newSchema: "operations");
        }
    }
}
