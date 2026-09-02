using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    public partial class AddFinancialAdjustmentRefundLineage : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "financial_adjustment_id",
                schema: "payments",
                table: "cash_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_cash_records_adjustment",
                schema: "payments",
                table: "cash_records",
                column: "financial_adjustment_id",
                unique: true,
                filter: "(financial_adjustment_id IS NOT NULL)");

            migrationBuilder.Sql("""
                UPDATE payments.financial_adjustments
                SET status = 'Completed'
                WHERE status = 'Approved'
                  AND adjustment_type = 'MerchantCredit'
                  AND notes LIKE 'Approved correction %';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_cash_records_adjustment",
                schema: "payments",
                table: "cash_records");
            migrationBuilder.DropColumn(
                name: "financial_adjustment_id",
                schema: "payments",
                table: "cash_records");
        }
    }
}
