using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    public partial class SeparatePaymentTrackFromMovementMethod : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "chk_op_payment_method", schema: "operations", table: "operation_logs");
            migrationBuilder.DropIndex(name: "uq_operation_active_correction", schema: "operations", table: "operation_correction_proposals");
            migrationBuilder.DropCheckConstraint(name: "chk_operation_correction_status", schema: "operations", table: "operation_correction_proposals");
            migrationBuilder.AddColumn<Guid>(name: "finance_account_id", schema: "operations", table: "operation_logs", type: "uuid", nullable: true);
            migrationBuilder.Sql("UPDATE operations.operation_logs SET payment_method = NULL WHERE payment_method IN ('MerchantAccount','Installment','Installlaugment');");
            // The earlier AddOperationLineSourceAllocations migration owns that table.
            migrationBuilder.AddCheckConstraint(name: "chk_op_payment_method", schema: "operations", table: "operation_logs", sql: "payment_method is null or payment_method in ('CashHandToHand','CashTransaction','BankTransfer','Wallet')");
            migrationBuilder.CreateIndex(name: "uq_operation_active_correction", schema: "operations", table: "operation_correction_proposals", column: "operation_id", unique: true, filter: "(status in ('Draft','PendingReview'))");
            migrationBuilder.AddCheckConstraint(name: "chk_operation_correction_status", schema: "operations", table: "operation_correction_proposals", sql: "status in ('Draft','PendingReview','Posted','Rejected')");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "chk_op_payment_method", schema: "operations", table: "operation_logs");
            migrationBuilder.DropIndex(name: "uq_operation_active_correction", schema: "operations", table: "operation_correction_proposals");
            migrationBuilder.DropCheckConstraint(name: "chk_operation_correction_status", schema: "operations", table: "operation_correction_proposals");
            migrationBuilder.DropColumn(name: "finance_account_id", schema: "operations", table: "operation_logs");
            migrationBuilder.AddCheckConstraint(name: "chk_op_payment_method", schema: "operations", table: "operation_logs", sql: "payment_method is null or payment_method in ('CashHandToHand','CashTransaction','MerchantAccount','Installment')");
            migrationBuilder.CreateIndex(name: "uq_operation_active_correction", schema: "operations", table: "operation_correction_proposals", column: "operation_id", unique: true, filter: "(status = 'PendingApproval')");
            migrationBuilder.AddCheckConstraint(name: "chk_operation_correction_status", schema: "operations", table: "operation_correction_proposals", sql: "status in ('PendingApproval','Approved','Rejected')");
        }
    }
}
