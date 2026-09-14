using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class EnsureMerchantAccountPaymentMethodConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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
                sql: "payment_method in ('CashHandToHand','CashTransaction','MerchantAccount','Installment')");
        }
    }
}
