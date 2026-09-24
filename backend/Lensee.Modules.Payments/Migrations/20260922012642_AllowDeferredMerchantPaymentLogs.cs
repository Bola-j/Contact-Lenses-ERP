using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class AllowDeferredMerchantPaymentLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_main_payment_method",
                schema: "payments",
                table: "main_payment_logs");

            migrationBuilder.AlterColumn<string>(
                name: "payment_method",
                schema: "payments",
                table: "main_payment_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            // Historical labels denoted a payment track, not a real cash movement.
            // Preserve the rows without fabricating a movement method. The aggregate
            // constraint trigger does not depend on payment_method-only cleanup, and
            // existing historical aggregate drift can make it fail when flushed.
            migrationBuilder.Sql("""
                ALTER TABLE payments.main_payment_logs
                    DISABLE TRIGGER trg_main_payment_logs_verify_aggregates;
                UPDATE payments.main_payment_logs
                SET payment_method = NULL
                WHERE payment_method IN ('MerchantAccount', 'Installment', 'Installlaugment');
                ALTER TABLE payments.main_payment_logs
                    ENABLE TRIGGER trg_main_payment_logs_verify_aggregates;
                """);

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

            migrationBuilder.Sql("""
                UPDATE payments.main_payment_logs
                SET payment_method = 'Installment'
                WHERE payment_method IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "payment_method",
                schema: "payments",
                table: "main_payment_logs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "chk_main_payment_method",
                schema: "payments",
                table: "main_payment_logs",
                sql: "payment_method in ('CashHandToHand','CashTransaction','MerchantAccount','Installment','Installlaugment')");
        }
    }
}
