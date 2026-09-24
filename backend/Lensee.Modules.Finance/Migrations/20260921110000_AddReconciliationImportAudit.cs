using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Lensee.Modules.Finance.Data;

#nullable disable

namespace Lensee.Modules.Finance.Migrations;

/// <summary>
/// Stores only signed source evidence and reviewer decisions.  It deliberately
/// does not reinterpret or rewrite historical payment rows.
/// </summary>
[DbContext(typeof(FinanceDbContext))]
[Migration("20260921110000_AddReconciliationImportAudit")]
public partial class AddReconciliationImportAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "reconciliation_import_packages", schema: "finance", columns: table => new
        {
            id = table.Column<Guid>("uuid", nullable: false),
            schema_version = table.Column<string>("character varying(30)", maxLength: 30, nullable: false),
            signer = table.Column<string>("character varying(200)", maxLength: 200, nullable: false),
            signature = table.Column<string>("character varying(1000)", maxLength: 1000, nullable: false),
            payload_sha256 = table.Column<string>("character varying(64)", maxLength: 64, nullable: false),
            source_period_start = table.Column<DateOnly>("date", nullable: false),
            source_period_end = table.Column<DateOnly>("date", nullable: false),
            exported_at_utc = table.Column<DateTime>("timestamp without time zone", nullable: false),
            status = table.Column<string>("character varying(30)", maxLength: 30, nullable: false, defaultValue: "Registered"),
            registered_by_user_id = table.Column<Guid>("uuid", nullable: false),
            registered_at = table.Column<DateTime>("timestamp without time zone", nullable: false),
            applied_by_user_id = table.Column<Guid>("uuid", nullable: true),
            applied_at = table.Column<DateTime>("timestamp without time zone", nullable: true),
            apply_idempotency_key = table.Column<string>("character varying(200)", maxLength: 200, nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_reconciliation_import_packages", x => x.id);
            table.CheckConstraint("chk_reconciliation_package_period", "source_period_start <= source_period_end");
            table.CheckConstraint("chk_reconciliation_package_status", "status in ('Registered','Previewed','Applied','Rejected')");
        });

        migrationBuilder.CreateTable(name: "reconciliation_import_rows", schema: "finance", columns: table => new
        {
            id = table.Column<Guid>("uuid", nullable: false),
            package_id = table.Column<Guid>("uuid", nullable: false),
            row_number = table.Column<int>("integer", nullable: false),
            row_type = table.Column<string>("character varying(50)", maxLength: 50, nullable: false),
            source_reference = table.Column<string>("character varying(200)", maxLength: 200, nullable: false),
            original_payload = table.Column<string>("jsonb", nullable: false),
            decision = table.Column<string>("character varying(30)", maxLength: 30, nullable: false, defaultValue: "PendingReview"),
            canonical_track = table.Column<string>("character varying(30)", maxLength: 30, nullable: true),
            decision_reason = table.Column<string>("character varying(1000)", maxLength: 1000, nullable: true),
            resolved_by_user_id = table.Column<Guid>("uuid", nullable: true),
            resolved_at = table.Column<DateTime>("timestamp without time zone", nullable: true),
            resolution_note = table.Column<string>("character varying(1000)", maxLength: 1000, nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_reconciliation_import_rows", x => x.id);
            table.ForeignKey(name: "FK_reconciliation_import_rows_reconciliation_import_packages_package_id", column: x => x.package_id, principalSchema: "finance", principalTable: "reconciliation_import_packages", principalColumn: "id", onDelete: ReferentialAction.Restrict);
            table.CheckConstraint("chk_reconciliation_row_decision", "decision in ('Approved','PendingReview','Rejected','Applied')");
        });

        migrationBuilder.CreateIndex("IX_reconciliation_import_packages_apply_idempotency_key", "reconciliation_import_packages", "apply_idempotency_key", "finance", unique: true, filter: "(apply_idempotency_key is not null)");
        migrationBuilder.CreateIndex("IX_reconciliation_import_packages_payload_sha256", "reconciliation_import_packages", "payload_sha256", "finance", unique: true);
        migrationBuilder.CreateIndex("IX_reconciliation_import_rows_package_id_decision", "reconciliation_import_rows", new[] { "package_id", "decision" }, "finance");
        migrationBuilder.CreateIndex("IX_reconciliation_import_rows_package_id_row_number", "reconciliation_import_rows", new[] { "package_id", "row_number" }, "finance", unique: true);
        migrationBuilder.CreateIndex("IX_reconciliation_import_rows_package_id_source_reference", "reconciliation_import_rows", new[] { "package_id", "source_reference" }, "finance", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("reconciliation_import_rows", "finance");
        migrationBuilder.DropTable("reconciliation_import_packages", "finance");
    }
}
