using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class AddSkuCaseInsensitiveMatchKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                do $$
                begin
                    if exists (
                        select 1
                        from catalog.skus
                        group by upper(btrim(sku_code))
                        having count(*) > 1
                    ) then
                        raise exception 'Cannot enable Shopify SKU matching because catalog.skus contains SKU codes that differ only by case or surrounding whitespace. Correct those ERP SKU codes first.';
                    end if;
                end $$;
                """);
            migrationBuilder.Sql("create unique index if not exists uq_skus_case_insensitive_code on catalog.skus (upper(btrim(sku_code))); ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("drop index if exists catalog.uq_skus_case_insensitive_code;");
        }
    }
}
