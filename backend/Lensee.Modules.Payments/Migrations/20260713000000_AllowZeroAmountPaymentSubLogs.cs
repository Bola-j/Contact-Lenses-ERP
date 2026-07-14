using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class AllowZeroAmountPaymentSubLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("alter table if exists payments.installment_sub_logs drop constraint if exists chk_sub_log_amount;");
            migrationBuilder.Sql("alter table if exists payments.installment_sub_logs drop constraint if exists chk_sub_log_payment_method;");

            migrationBuilder.AddCheckConstraint(
                name: "chk_sub_log_amount",
                schema: "payments",
                table: "installment_sub_logs",
                sql: "amount >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_sub_log_payment_method",
                schema: "payments",
                table: "installment_sub_logs",
                sql: "payment_method is null or payment_method in ('CashTransaction','CashHandToHand','BankTransfer','Wallet','Installment')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("alter table if exists payments.installment_sub_logs drop constraint if exists chk_sub_log_amount;");
            migrationBuilder.Sql("alter table if exists payments.installment_sub_logs drop constraint if exists chk_sub_log_payment_method;");

            migrationBuilder.AddCheckConstraint(
                name: "chk_sub_log_amount",
                schema: "payments",
                table: "installment_sub_logs",
                sql: "amount > 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_sub_log_payment_method",
                schema: "payments",
                table: "installment_sub_logs",
                sql: "payment_method is null or payment_method in ('CashTransaction','CashHandToHand','BankTransfer','Wallet')");
        }
    }
}
