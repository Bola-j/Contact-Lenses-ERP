using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Finance.Migrations;

/// <summary>
/// Synchronizes EF metadata after the preceding reconciliation-import migration
/// added its physical schema through a manually authored migration.
/// </summary>
public partial class SynchronizeFinanceModelSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Snapshot-only: 20260921110000_AddReconciliationImportAudit owns the schema.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No physical schema operation was performed by this metadata migration.
    }
}
