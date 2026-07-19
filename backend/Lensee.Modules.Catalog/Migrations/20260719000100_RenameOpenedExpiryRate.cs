using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Catalog.Migrations
{
    /// <inheritdoc />
    public partial class RenameOpenedExpiryRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("alter table if exists catalog.products drop constraint if exists chk_products_sealed_expiry_rate;");
            migrationBuilder.Sql("alter table if exists catalog.products drop constraint if exists chk_products_opened_expiry_rate;");
            migrationBuilder.Sql(
                """
                do $$
                begin
                    if exists (
                        select 1
                        from information_schema.columns
                        where table_schema = 'catalog'
                          and table_name = 'products'
                          and column_name = 'sealed_expiry_rate'
                    ) and not exists (
                        select 1
                        from information_schema.columns
                        where table_schema = 'catalog'
                          and table_name = 'products'
                          and column_name = 'opened_expiry_rate'
                    ) then
                        alter table catalog.products rename column sealed_expiry_rate to opened_expiry_rate;
                    end if;
                end $$;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "chk_products_opened_expiry_rate",
                schema: "catalog",
                table: "products",
                sql: "opened_expiry_rate is null or opened_expiry_rate in ('Daily','Monthly','Annual')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_products_opened_expiry_rate",
                schema: "catalog",
                table: "products");

            migrationBuilder.Sql(
                """
                do $$
                begin
                    if exists (
                        select 1
                        from information_schema.columns
                        where table_schema = 'catalog'
                          and table_name = 'products'
                          and column_name = 'opened_expiry_rate'
                    ) and not exists (
                        select 1
                        from information_schema.columns
                        where table_schema = 'catalog'
                          and table_name = 'products'
                          and column_name = 'sealed_expiry_rate'
                    ) then
                        alter table catalog.products rename column opened_expiry_rate to sealed_expiry_rate;
                    end if;
                end $$;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "chk_products_sealed_expiry_rate",
                schema: "catalog",
                table: "products",
                sql: "sealed_expiry_rate is null or sealed_expiry_rate in ('Daily','Monthly','Annual')");
        }
    }
}
