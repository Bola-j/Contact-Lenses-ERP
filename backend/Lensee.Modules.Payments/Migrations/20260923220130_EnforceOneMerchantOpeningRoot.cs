using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class EnforceOneMerchantOpeningRoot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    IF EXISTS (
                        SELECT 1 FROM payments.merchant_opening_balance_charges
                        WHERE reverses_charge_id IS NULL AND status <> 'Rejected'
                        GROUP BY merchant_id HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Multiple merchant opening roots exist; review each merchant before applying this migration.';
                    END IF;
                END $$;
            ");
            migrationBuilder.CreateIndex(
                name: "ux_merchant_opening_one_root",
                schema: "payments",
                table: "merchant_opening_balance_charges",
                column: "merchant_id",
                unique: true,
                filter: "(reverses_charge_id IS NULL AND status <> 'Rejected')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_merchant_opening_one_root",
                schema: "payments",
                table: "merchant_opening_balance_charges");
        }
    }
}
