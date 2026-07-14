using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class FixCatalogSellModeConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_products_sell_mode",
                schema: "catalog",
                table: "products");

            migrationBuilder.AddCheckConstraint(
                name: "chk_products_sell_mode",
                schema: "catalog",
                table: "products",
                sql: "sell_mode in ('SealedPackOnly','SinglePiece','Both')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_products_sell_mode",
                schema: "catalog",
                table: "products");

            migrationBuilder.AddCheckConstraint(
                name: "chk_products_sell_mode",
                schema: "catalog",
                table: "products",
                sql: "sell_mode in ('Pieces','Packs','Both')");
        }
    }
}
