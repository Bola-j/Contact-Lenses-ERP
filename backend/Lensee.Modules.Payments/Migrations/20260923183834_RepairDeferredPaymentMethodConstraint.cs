using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class RepairDeferredPaymentMethodConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing databases that recorded 20260922012642 before its repair retain
            // the legacy constraint. Do not rewrite history: converge them here.
            migrationBuilder.DropCheckConstraint(
                name: "chk_main_payment_method",
                schema: "payments",
                table: "main_payment_logs");

            migrationBuilder.AddCheckConstraint(
                name: "chk_main_payment_method",
                schema: "payments",
                table: "main_payment_logs",
                sql: "payment_method is null or payment_method in ('CashHandToHand','CashTransaction','BankTransfer','Wallet')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_main_payment_method",
                schema: "payments",
                table: "main_payment_logs");

            migrationBuilder.AddCheckConstraint(
                name: "chk_main_payment_method",
                schema: "payments",
                table: "main_payment_logs",
                sql: "payment_method in ('CashHandToHand','CashTransaction','MerchantAccount','Installment','Installlaugment')");
        }
    }
}
