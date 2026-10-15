using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnEntitlementSettlementLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "return_entitlement_id",
                schema: "payments",
                table: "financial_adjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "return_entitlement_id",
                schema: "payments",
                table: "cash_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_financial_adjustments_return_entitlement_id",
                schema: "payments",
                table: "financial_adjustments",
                column: "return_entitlement_id",
                unique: true,
                filter: "(return_entitlement_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_cash_records_return_entitlement_id",
                schema: "payments",
                table: "cash_records",
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

            migrationBuilder.DropIndex(
                name: "IX_cash_records_return_entitlement_id",
                schema: "payments",
                table: "cash_records");

            migrationBuilder.DropColumn(
                name: "return_entitlement_id",
                schema: "payments",
                table: "financial_adjustments");

            migrationBuilder.DropColumn(
                name: "return_entitlement_id",
                schema: "payments",
                table: "cash_records");
        }
    }
}
