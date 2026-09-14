using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddMerchantAccountSettlementMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_op_payment_method",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.AddCheckConstraint(
                name: "chk_op_payment_method",
                schema: "operations",
                table: "operation_logs",
                sql: "payment_method is null or payment_method in ('CashHandToHand','CashTransaction','MerchantAccount','Installment')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_op_payment_method",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.AddCheckConstraint(
                name: "chk_op_payment_method",
                schema: "operations",
                table: "operation_logs",
                sql: "payment_method is null or payment_method in ('CashHandToHand','CashTransaction','Installment')");
        }
    }
}
