using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Finance.Migrations
{
    /// <inheritdoc />
    public partial class EnforceOneFinanceOpeningRoot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS finance.\"IX_finance_opening_balances_finance_account_id\";");

            migrationBuilder.CreateIndex(
                name: "ux_finance_opening_one_root_per_account",
                schema: "finance",
                table: "finance_opening_balances",
                column: "finance_account_id",
                unique: true,
                filter: "(reverses_opening_balance_id is null and status <> 'Rejected')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_finance_opening_one_root_per_account",
                schema: "finance",
                table: "finance_opening_balances");

            migrationBuilder.CreateIndex(
                name: "IX_finance_opening_balances_finance_account_id",
                schema: "finance",
                table: "finance_opening_balances",
                column: "finance_account_id");
        }
    }
}
