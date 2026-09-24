using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Finance.Migrations
{
    /// <inheritdoc />
    public partial class LinkFinanceCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_finance_expenses_category_id",
                schema: "finance",
                table: "finance_expenses",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_c_level_withdrawals_category_id",
                schema: "finance",
                table: "c_level_withdrawals",
                column: "category_id");

            migrationBuilder.AddForeignKey(
                name: "FK_c_level_withdrawals_finance_categories_category_id",
                schema: "finance",
                table: "c_level_withdrawals",
                column: "category_id",
                principalSchema: "finance",
                principalTable: "finance_categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_finance_expenses_finance_categories_category_id",
                schema: "finance",
                table: "finance_expenses",
                column: "category_id",
                principalSchema: "finance",
                principalTable: "finance_categories",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_c_level_withdrawals_finance_categories_category_id",
                schema: "finance",
                table: "c_level_withdrawals");

            migrationBuilder.DropForeignKey(
                name: "FK_finance_expenses_finance_categories_category_id",
                schema: "finance",
                table: "finance_expenses");

            migrationBuilder.DropIndex(
                name: "IX_finance_expenses_category_id",
                schema: "finance",
                table: "finance_expenses");

            migrationBuilder.DropIndex(
                name: "IX_c_level_withdrawals_category_id",
                schema: "finance",
                table: "c_level_withdrawals");
        }
    }
}
