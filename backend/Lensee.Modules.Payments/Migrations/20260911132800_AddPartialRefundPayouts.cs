using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class AddPartialRefundPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_merchant_refund_reservation_status",
                schema: "payments",
                table: "merchant_refund_reservations");

            migrationBuilder.AddColumn<decimal>(
                name: "paid_amount",
                schema: "payments",
                table: "merchant_refund_reservations",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "chk_merchant_refund_reservation_paid_amount",
                schema: "payments",
                table: "merchant_refund_reservations",
                sql: "paid_amount >= 0 and paid_amount <= amount");

            migrationBuilder.AddCheckConstraint(
                name: "chk_merchant_refund_reservation_status",
                schema: "payments",
                table: "merchant_refund_reservations",
                sql: "status in ('PendingApproval','Approved','PartiallyPaid','Paid','Rejected','Cancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_merchant_refund_reservation_paid_amount",
                schema: "payments",
                table: "merchant_refund_reservations");

            migrationBuilder.DropCheckConstraint(
                name: "chk_merchant_refund_reservation_status",
                schema: "payments",
                table: "merchant_refund_reservations");

            migrationBuilder.DropColumn(
                name: "paid_amount",
                schema: "payments",
                table: "merchant_refund_reservations");

            migrationBuilder.AddCheckConstraint(
                name: "chk_merchant_refund_reservation_status",
                schema: "payments",
                table: "merchant_refund_reservations",
                sql: "status in ('PendingApproval','Approved','Paid','Rejected','Cancelled')");
        }
    }
}
