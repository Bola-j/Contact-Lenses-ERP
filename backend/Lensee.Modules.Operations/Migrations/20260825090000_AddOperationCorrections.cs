using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Lensee.Modules.Operations.Data;

#nullable disable

namespace Lensee.Modules.Operations.Migrations;

/// <inheritdoc />
[DbContext(typeof(OperationsDbContext))]
[Migration("20260825090000_AddOperationCorrections")]
public partial class AddOperationCorrections : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "record_kind", schema: "operations", table: "operation_logs", type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Standard");
        migrationBuilder.AddColumn<Guid>(name: "reverses_operation_id", schema: "operations", table: "operation_logs", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "replaced_operation_id", schema: "operations", table: "operation_logs", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "correction_proposal_id", schema: "operations", table: "operation_logs", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(name: "correction_reason", schema: "operations", table: "operation_logs", type: "text", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "corrected_by", schema: "operations", table: "operation_logs", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "corrected_at", schema: "operations", table: "operation_logs", type: "timestamp without time zone", nullable: true);

        migrationBuilder.CreateTable(
            name: "operation_correction_proposals",
            schema: "operations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                reason = table.Column<string>(type: "text", nullable: false),
                settlement_method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                settlement_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                create_replacement_draft = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                requester_id = table.Column<Guid>(type: "uuid", nullable: false),
                requested_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                reviewer_id = table.Column<Guid>(type: "uuid", nullable: true),
                reviewed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                rejection_reason = table.Column<string>(type: "text", nullable: true),
                reversal_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                replacement_operation_id = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("operation_correction_proposals_pkey", x => x.id);
                table.ForeignKey("operation_correction_proposals_operation_id_fkey", x => x.operation_id, principalSchema: "operations", principalTable: "operation_logs", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("chk_operation_correction_status", "status in ('PendingApproval','Approved','Rejected')");
                table.CheckConstraint("chk_operation_correction_settlement", "settlement_method is null or settlement_method in ('CashRefund','MerchantCredit')");
                table.CheckConstraint("chk_operation_correction_amount", "settlement_amount is null or settlement_amount > 0");
            });

        migrationBuilder.CreateIndex(name: "idx_operation_corrections_operation", schema: "operations", table: "operation_correction_proposals", column: "operation_id");
        migrationBuilder.CreateIndex(name: "uq_operation_active_correction", schema: "operations", table: "operation_correction_proposals", column: "operation_id", unique: true, filter: "status = 'PendingApproval'");
        migrationBuilder.CreateIndex(name: "uq_operation_active_reversal", schema: "operations", table: "operation_logs", column: "reverses_operation_id", unique: true, filter: "record_kind = 'Reversal' AND is_deleted = false");
        migrationBuilder.Sql("ALTER TABLE operations.operation_logs ADD CONSTRAINT chk_operation_record_kind CHECK (record_kind in ('Standard','Reversal','Replacement'));");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "operation_correction_proposals", schema: "operations");
        migrationBuilder.DropIndex(name: "uq_operation_active_reversal", schema: "operations", table: "operation_logs");
        migrationBuilder.Sql("ALTER TABLE operations.operation_logs DROP CONSTRAINT IF EXISTS chk_operation_record_kind;");
        migrationBuilder.DropColumn(name: "record_kind", schema: "operations", table: "operation_logs");
        migrationBuilder.DropColumn(name: "reverses_operation_id", schema: "operations", table: "operation_logs");
        migrationBuilder.DropColumn(name: "replaced_operation_id", schema: "operations", table: "operation_logs");
        migrationBuilder.DropColumn(name: "correction_proposal_id", schema: "operations", table: "operation_logs");
        migrationBuilder.DropColumn(name: "correction_reason", schema: "operations", table: "operation_logs");
        migrationBuilder.DropColumn(name: "corrected_by", schema: "operations", table: "operation_logs");
        migrationBuilder.DropColumn(name: "corrected_at", schema: "operations", table: "operation_logs");
    }
}
