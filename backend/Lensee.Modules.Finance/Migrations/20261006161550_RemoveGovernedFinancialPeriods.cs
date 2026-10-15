using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Finance.Migrations
{
    /// <inheritdoc />
    public partial class RemoveGovernedFinancialPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // This migration removes an optional/legacy governed-period index.
            // Use IF EXISTS because databases created from different migration
            // histories may legitimately not contain the legacy index.
            migrationBuilder.Sql("DROP INDEX IF EXISTS finance.\"IX_payment_movement_registry_governed_period_id_finance_account\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS finance.\"IX_finance_ledger_entries_governed_period_id_finance_account_id\";");

            migrationBuilder.Sql("ALTER TABLE IF EXISTS finance.payment_movement_registry DROP COLUMN IF EXISTS governed_period_id;");
            migrationBuilder.Sql("ALTER TABLE IF EXISTS finance.finance_ledger_entries DROP COLUMN IF EXISTS governed_period_id;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS finance.governed_finance_openings;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS finance.governed_financial_periods;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "governed_period_id",
                schema: "finance",
                table: "payment_movement_registry",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "governed_period_id",
                schema: "finance",
                table: "finance_ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "governed_finance_openings",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    declared_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    declared_by = table.Column<Guid>(type: "uuid", nullable: false),
                    finance_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    governed_period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    posted_ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_governed_finance_openings", x => x.Id);
                    table.CheckConstraint("chk_governed_finance_opening_amount", "amount >= 0");
                });

            migrationBuilder.CreateTable(
                name: "governed_financial_periods",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    archived_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Draft")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_governed_financial_periods", x => x.Id);
                    table.CheckConstraint("chk_governed_financial_period_dates", "ends_on >= starts_on");
                    table.CheckConstraint("chk_governed_financial_period_status", "status in ('Draft','Active','Archived')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_movement_registry_governed_period_id_finance_account",
                schema: "finance",
                table: "payment_movement_registry",
                columns: new[] { "governed_period_id", "finance_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_ledger_entries_governed_period_id_finance_account_id",
                schema: "finance",
                table: "finance_ledger_entries",
                columns: new[] { "governed_period_id", "finance_account_id" });

            migrationBuilder.CreateIndex(
                name: "IX_governed_finance_openings_governed_period_id_finance_account",
                schema: "finance",
                table: "governed_finance_openings",
                columns: new[] { "governed_period_id", "finance_account_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_governed_finance_openings_posted_ledger_entry_id",
                schema: "finance",
                table: "governed_finance_openings",
                column: "posted_ledger_entry_id",
                unique: true,
                filter: "(posted_ledger_entry_id is not null)");

            migrationBuilder.CreateIndex(
                name: "IX_governed_financial_periods_status",
                schema: "finance",
                table: "governed_financial_periods",
                column: "status",
                unique: true,
                filter: "status = 'Active'");
        }
    }
}
