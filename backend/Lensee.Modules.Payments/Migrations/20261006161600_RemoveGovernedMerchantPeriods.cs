using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGovernedMerchantPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS payments.\"IX_merchant_account_entries_governed_period_id_account_id\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS payments.\"IX_merchant_operation_obligations_governed_period_id_account_id\";");
            migrationBuilder.Sql("ALTER TABLE IF EXISTS payments.merchant_account_entries DROP COLUMN IF EXISTS governed_period_id;");
            migrationBuilder.Sql("ALTER TABLE IF EXISTS payments.merchant_operation_obligations DROP COLUMN IF EXISTS governed_period_id;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS payments.governed_merchant_openings;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The removed governed merchant-period model is retired and has no supported rollback path.
        }
    }
}
