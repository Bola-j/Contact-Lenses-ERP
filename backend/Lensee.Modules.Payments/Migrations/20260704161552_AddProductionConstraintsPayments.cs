using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionConstraintsPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("alter table if exists payments.main_payment_logs drop constraint if exists chk_main_payment_amount_paid;");
            migrationBuilder.Sql("alter table if exists payments.main_payment_logs drop constraint if exists chk_main_payment_method;");
            migrationBuilder.Sql("alter table if exists payments.main_payment_logs drop constraint if exists chk_main_payment_paid_lte_total;");
            migrationBuilder.Sql("alter table if exists payments.main_payment_logs drop constraint if exists chk_main_payment_status;");
            migrationBuilder.Sql("alter table if exists payments.main_payment_logs drop constraint if exists chk_main_payment_total_amount;");
            migrationBuilder.Sql("alter table if exists payments.installment_sub_logs drop constraint if exists chk_sub_log_amount;");
            migrationBuilder.Sql("alter table if exists payments.installment_sub_logs drop constraint if exists chk_sub_log_payment_method;");
            migrationBuilder.Sql("alter table if exists payments.installment_sub_logs drop constraint if exists chk_sub_log_status;");
            migrationBuilder.Sql("alter table if exists payments.financial_adjustments drop constraint if exists chk_financial_adjustment_amount;");
            migrationBuilder.Sql("alter table if exists payments.financial_adjustments drop constraint if exists chk_financial_adjustment_status;");
            migrationBuilder.Sql("alter table if exists payments.financial_adjustments drop constraint if exists chk_financial_adjustment_type;");
            migrationBuilder.Sql("alter table if exists payments.cash_records drop constraint if exists chk_cash_amount;");
            migrationBuilder.Sql("alter table if exists payments.cash_records drop constraint if exists chk_cash_payment_type;");
            migrationBuilder.Sql("alter table if exists payments.cash_records drop constraint if exists chk_cash_status;");

            migrationBuilder.AlterColumn<string>(
                name: "payment_type",
                schema: "payments",
                table: "cash_records",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValueSql: "'CashReceived'::character varying",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValueSql: "'Cash'::character varying");

            migrationBuilder.AddCheckConstraint(
                name: "chk_main_payment_amount_paid",
                schema: "payments",
                table: "main_payment_logs",
                sql: "amount_paid >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_main_payment_method",
                schema: "payments",
                table: "main_payment_logs",
                sql: "payment_method in ('CashHandToHand','CashTransaction','Installment')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_main_payment_paid_lte_total",
                schema: "payments",
                table: "main_payment_logs",
                sql: "amount_paid <= total_amount");

            migrationBuilder.AddCheckConstraint(
                name: "chk_main_payment_status",
                schema: "payments",
                table: "main_payment_logs",
                sql: "status in ('PendingAdmin','PendingAccountant','PendingAdminReview','Completed','Rejected','Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_main_payment_total_amount",
                schema: "payments",
                table: "main_payment_logs",
                sql: "total_amount >= 0");

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

            migrationBuilder.AddCheckConstraint(
                name: "chk_sub_log_status",
                schema: "payments",
                table: "installment_sub_logs",
                sql: "sub_log_status in ('Draft','PendingAdminReview','Confirmed','Rejected')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_financial_adjustment_amount",
                schema: "payments",
                table: "financial_adjustments",
                sql: "amount > 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_financial_adjustment_status",
                schema: "payments",
                table: "financial_adjustments",
                sql: "status in ('Completed','Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_financial_adjustment_type",
                schema: "payments",
                table: "financial_adjustments",
                sql: "adjustment_type in ('MerchantCredit','BalanceReduction','CashRefund')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_cash_amount",
                schema: "payments",
                table: "cash_records",
                sql: "amount > 0");

            migrationBuilder.AddCheckConstraint(
                name: "chk_cash_payment_type",
                schema: "payments",
                table: "cash_records",
                sql: "payment_type in ('CashReceived','CashRefund')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_cash_status",
                schema: "payments",
                table: "cash_records",
                sql: "status in ('Completed','Cancelled')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_main_payment_amount_paid",
                schema: "payments",
                table: "main_payment_logs");

            migrationBuilder.DropCheckConstraint(
                name: "chk_main_payment_method",
                schema: "payments",
                table: "main_payment_logs");

            migrationBuilder.DropCheckConstraint(
                name: "chk_main_payment_paid_lte_total",
                schema: "payments",
                table: "main_payment_logs");

            migrationBuilder.DropCheckConstraint(
                name: "chk_main_payment_status",
                schema: "payments",
                table: "main_payment_logs");

            migrationBuilder.DropCheckConstraint(
                name: "chk_main_payment_total_amount",
                schema: "payments",
                table: "main_payment_logs");

            migrationBuilder.DropCheckConstraint(
                name: "chk_sub_log_amount",
                schema: "payments",
                table: "installment_sub_logs");

            migrationBuilder.DropCheckConstraint(
                name: "chk_sub_log_payment_method",
                schema: "payments",
                table: "installment_sub_logs");

            migrationBuilder.DropCheckConstraint(
                name: "chk_sub_log_status",
                schema: "payments",
                table: "installment_sub_logs");

            migrationBuilder.DropCheckConstraint(
                name: "chk_financial_adjustment_amount",
                schema: "payments",
                table: "financial_adjustments");

            migrationBuilder.DropCheckConstraint(
                name: "chk_financial_adjustment_status",
                schema: "payments",
                table: "financial_adjustments");

            migrationBuilder.DropCheckConstraint(
                name: "chk_financial_adjustment_type",
                schema: "payments",
                table: "financial_adjustments");

            migrationBuilder.DropCheckConstraint(
                name: "chk_cash_amount",
                schema: "payments",
                table: "cash_records");

            migrationBuilder.DropCheckConstraint(
                name: "chk_cash_payment_type",
                schema: "payments",
                table: "cash_records");

            migrationBuilder.DropCheckConstraint(
                name: "chk_cash_status",
                schema: "payments",
                table: "cash_records");

            migrationBuilder.AlterColumn<string>(
                name: "payment_type",
                schema: "payments",
                table: "cash_records",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValueSql: "'Cash'::character varying",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldDefaultValueSql: "'CashReceived'::character varying");
        }
    }
}
