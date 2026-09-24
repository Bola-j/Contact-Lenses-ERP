using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeCorrectionWorkflowStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "uq_operation_active_correction", schema: "operations", table: "operation_correction_proposals");
            migrationBuilder.DropCheckConstraint(name: "chk_operation_correction_status", schema: "operations", table: "operation_correction_proposals");
            migrationBuilder.Sql("UPDATE operations.operation_correction_proposals SET status = 'PendingReview' WHERE status = 'PendingApproval';");
            migrationBuilder.Sql("UPDATE operations.operation_correction_proposals SET status = 'Posted' WHERE status = 'Approved';");
            migrationBuilder.AddCheckConstraint(name: "chk_operation_correction_status", schema: "operations", table: "operation_correction_proposals", sql: "status in ('Draft','PendingReview','Posted','Rejected')");
            migrationBuilder.CreateIndex(name: "uq_operation_active_correction", schema: "operations", table: "operation_correction_proposals", column: "operation_id", unique: true, filter: "(status in ('Draft','PendingReview'))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "uq_operation_active_correction", schema: "operations", table: "operation_correction_proposals");
            migrationBuilder.DropCheckConstraint(name: "chk_operation_correction_status", schema: "operations", table: "operation_correction_proposals");
            migrationBuilder.Sql("UPDATE operations.operation_correction_proposals SET status = 'PendingApproval' WHERE status = 'PendingReview';");
            migrationBuilder.Sql("UPDATE operations.operation_correction_proposals SET status = 'Approved' WHERE status = 'Posted';");
            migrationBuilder.AddCheckConstraint(name: "chk_operation_correction_status", schema: "operations", table: "operation_correction_proposals", sql: "status in ('PendingApproval','Approved','Rejected')");
            migrationBuilder.CreateIndex(name: "uq_operation_active_correction", schema: "operations", table: "operation_correction_proposals", column: "operation_id", unique: true, filter: "(status = 'PendingApproval')");
        }
    }
}
