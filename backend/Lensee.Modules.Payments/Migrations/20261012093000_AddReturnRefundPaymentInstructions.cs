using System;
using Lensee.Modules.Payments.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations;

/// <summary>
/// Stores payout instructions on the return-refund adjustment itself. Existing
/// approved refunds deliberately remain nullable and use the payout fallback.
/// </summary>
[DbContext(typeof(PaymentsDbContext))]
[Migration("20261012093000_AddReturnRefundPaymentInstructions")]
public partial class AddReturnRefundPaymentInstructions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "refund_finance_account_id", schema: "payments", table: "financial_adjustments", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "refund_payment_method", schema: "payments", table: "financial_adjustments", type: "character varying(50)", maxLength: 50, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "refund_transaction_reference", schema: "payments", table: "financial_adjustments", type: "character varying(200)", maxLength: 200, nullable: true);
        migrationBuilder.AddColumn<decimal>(
            name: "refund_transfer_fee_amount", schema: "payments", table: "financial_adjustments", type: "numeric(18,4)", precision: 18, scale: 4, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "refund_finance_account_id", schema: "payments", table: "financial_adjustments");
        migrationBuilder.DropColumn(name: "refund_payment_method", schema: "payments", table: "financial_adjustments");
        migrationBuilder.DropColumn(name: "refund_transaction_reference", schema: "payments", table: "financial_adjustments");
        migrationBuilder.DropColumn(name: "refund_transfer_fee_amount", schema: "payments", table: "financial_adjustments");
    }
}
