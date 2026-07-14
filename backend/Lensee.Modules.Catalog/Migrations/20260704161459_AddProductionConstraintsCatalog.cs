using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionConstraintsCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("alter table if exists catalog.skus drop constraint if exists chk_skus_power_sign;");
            migrationBuilder.Sql("alter table if exists catalog.products drop constraint if exists chk_products_pieces_per_pack;");
            migrationBuilder.Sql("alter table if exists catalog.products drop constraint if exists chk_products_product_type;");
            migrationBuilder.Sql("alter table if exists catalog.products drop constraint if exists chk_products_sealed_expiry_rate;");
            migrationBuilder.Sql("alter table if exists catalog.products drop constraint if exists chk_products_sell_mode;");

            migrationBuilder.AddCheckConstraint(
                name: "chk_skus_power_sign",
                schema: "catalog",
                table: "skus",
                sql: "power_sign is null or power_sign in ('+','-')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_products_pieces_per_pack",
                schema: "catalog",
                table: "products",
                sql: "pieces_per_pack is null or pieces_per_pack > 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_products_product_type",
                schema: "catalog",
                table: "products",
                sql: "product_type in ('Lens','Solution')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_products_sealed_expiry_rate",
                schema: "catalog",
                table: "products",
                sql: "sealed_expiry_rate is null or sealed_expiry_rate in ('Daily','Monthly','Annual')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_products_sell_mode",
                schema: "catalog",
                table: "products",
                sql: "sell_mode in ('Pieces','Packs','Both')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_skus_power_sign",
                schema: "catalog",
                table: "skus");

            migrationBuilder.DropCheckConstraint(
                name: "chk_products_pieces_per_pack",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "chk_products_product_type",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "chk_products_sealed_expiry_rate",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropCheckConstraint(
                name: "chk_products_sell_mode",
                schema: "catalog",
                table: "products");
        }
    }
}
