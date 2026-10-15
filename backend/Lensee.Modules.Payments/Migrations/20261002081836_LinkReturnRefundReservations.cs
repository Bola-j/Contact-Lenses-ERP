using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class LinkReturnRefundReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "financial_adjustment_id",
                schema: "payments",
                table: "merchant_refund_reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_merchant_refund_reservations_financial_adjustment_id",
                schema: "payments",
                table: "merchant_refund_reservations",
                column: "financial_adjustment_id",
                unique: true,
                filter: "financial_adjustment_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_merchant_refund_reservations_financial_adjustments_financia~",
                schema: "payments",
                table: "merchant_refund_reservations",
                column: "financial_adjustment_id",
                principalSchema: "payments",
                principalTable: "financial_adjustments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_merchant_refund_reservations_financial_adjustments_financia~",
                schema: "payments",
                table: "merchant_refund_reservations");

            migrationBuilder.DropIndex(
                name: "IX_merchant_refund_reservations_financial_adjustment_id",
                schema: "payments",
                table: "merchant_refund_reservations");

            migrationBuilder.DropColumn(
                name: "financial_adjustment_id",
                schema: "payments",
                table: "merchant_refund_reservations");
        }
    }
}
