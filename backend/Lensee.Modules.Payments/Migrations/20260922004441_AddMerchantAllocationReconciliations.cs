using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations;

public partial class AddMerchantAllocationReconciliations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "merchant_allocation_reconciliations",
            schema: "payments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                source_allocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                replacement_allocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                correction_charge_id = table.Column<Guid>(type: "uuid", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                correlation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_merchant_allocation_reconciliations", x => x.Id);
                table.ForeignKey(name: "fk_allocation_reconciliation_source", column: x => x.source_allocation_id, principalSchema: "payments", principalTable: "merchant_entry_allocations", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey(name: "fk_allocation_reconciliation_replacement", column: x => x.replacement_allocation_id, principalSchema: "payments", principalTable: "merchant_entry_allocations", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey(name: "fk_allocation_reconciliation_charge", column: x => x.correction_charge_id, principalSchema: "payments", principalTable: "merchant_opening_balance_charges", principalColumn: "id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "ux_allocation_reconciliation_source", schema: "payments", table: "merchant_allocation_reconciliations", column: "source_allocation_id", unique: true);
        migrationBuilder.CreateIndex(name: "ux_allocation_reconciliation_replacement", schema: "payments", table: "merchant_allocation_reconciliations", column: "replacement_allocation_id", unique: true);
        migrationBuilder.CreateIndex(name: "ux_allocation_reconciliation_correlation", schema: "payments", table: "merchant_allocation_reconciliations", columns: new[] { "correction_charge_id", "correlation_id" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "merchant_allocation_reconciliations", schema: "payments");
}
