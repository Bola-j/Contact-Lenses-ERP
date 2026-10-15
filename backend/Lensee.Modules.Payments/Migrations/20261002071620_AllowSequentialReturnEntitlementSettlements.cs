using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class AllowSequentialReturnEntitlementSettlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_financial_adjustments_return_entitlement_id",
                schema: "payments",
                table: "financial_adjustments");

            migrationBuilder.CreateIndex(
                name: "IX_financial_adjustments_return_entitlement_id",
                schema: "payments",
                table: "financial_adjustments",
                column: "return_entitlement_id",
                filter: "(return_entitlement_id IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_financial_adjustments_return_entitlement_id",
                schema: "payments",
                table: "financial_adjustments");

            migrationBuilder.CreateIndex(
                name: "IX_financial_adjustments_return_entitlement_id",
                schema: "payments",
                table: "financial_adjustments",
                column: "return_entitlement_id",
                unique: true,
                filter: "(return_entitlement_id IS NOT NULL)");
        }
    }
}
