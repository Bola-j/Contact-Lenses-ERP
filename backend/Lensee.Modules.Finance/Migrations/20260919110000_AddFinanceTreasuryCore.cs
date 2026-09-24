using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Finance.Migrations;

[DbContext(typeof(Lensee.Modules.Finance.Data.FinanceDbContext))]
[Migration("20260919110000_AddFinanceTreasuryCore")]
public partial class AddFinanceTreasuryCore : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema("finance");
        migrationBuilder.CreateTable(name: "finance_accounts", schema: "finance", columns: table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false),
            name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
            type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
            is_active = table.Column<bool>(type: "boolean", nullable: false),
            reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
            details = table.Column<string>(type: "text", nullable: true),
            created_by = table.Column<Guid>(type: "uuid", nullable: false),
            created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
            updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("pk_finance_accounts", x => x.id);
            table.CheckConstraint("chk_finance_account_type", "type in ('CashOnHand','BankAccount','Wallet')");
        });
        migrationBuilder.CreateTable(name: "finance_ledger_entries", schema: "finance", columns: table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false),
            finance_account_id = table.Column<Guid>(type: "uuid", nullable: false),
            direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
            amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
            category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
            movement_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
            source_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
            source_id = table.Column<Guid>(type: "uuid", nullable: false),
            business_date = table.Column<DateOnly>(type: "date", nullable: false),
            status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Posted"),
            created_by = table.Column<Guid>(type: "uuid", nullable: false),
            created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
            reverses_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
            correlation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
            external_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("pk_finance_ledger_entries", x => x.id);
            table.CheckConstraint("chk_finance_ledger_direction", "direction in ('Debit','Credit')");
            table.CheckConstraint("chk_finance_ledger_amount", "amount > 0");
            table.CheckConstraint("chk_finance_ledger_status", "status in ('Posted','Reversed')");
        });
        migrationBuilder.CreateTable(name: "finance_opening_balances", schema: "finance", columns: table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false),
            finance_account_id = table.Column<Guid>(type: "uuid", nullable: false),
            amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
            direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "Credit"),
            as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
            description = table.Column<string>(type: "text", nullable: false),
            status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Draft"),
            created_by = table.Column<Guid>(type: "uuid", nullable: false),
            created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
            reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
            reviewed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
            posted_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
            reverses_opening_balance_id = table.Column<Guid>(type: "uuid", nullable: true),
            replaced_by_opening_balance_id = table.Column<Guid>(type: "uuid", nullable: true),
            correlation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("pk_finance_opening_balances", x => x.id);
            table.CheckConstraint("chk_finance_opening_amount", "amount > 0");
            table.CheckConstraint("chk_finance_opening_direction", "direction in ('Debit','Credit')");
            table.CheckConstraint("chk_finance_opening_status", "status in ('Draft','PendingReview','Posted','Rejected','Corrected')");
        });
        migrationBuilder.CreateTable(name: "payment_movement_registry", schema: "finance", columns: table => new
        {
            id = table.Column<Guid>(type: "uuid", nullable: false),
            source_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
            source_id = table.Column<Guid>(type: "uuid", nullable: false),
            track = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
            movement_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
            amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
            finance_account_id = table.Column<Guid>(type: "uuid", nullable: false),
            normalized_external_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
            status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Posted"),
            created_by = table.Column<Guid>(type: "uuid", nullable: false),
            created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
            correlation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("pk_payment_movement_registry", x => x.id);
            table.CheckConstraint("chk_payment_movement_amount", "amount > 0");
            table.CheckConstraint("chk_payment_movement_status", "status in ('Posted','Reversed')");
        });
        migrationBuilder.AddForeignKey(name: "fk_finance_ledger_account", schema: "finance", table: "finance_ledger_entries", column: "finance_account_id", principalSchema: "finance", principalTable: "finance_accounts", principalColumn: "id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "fk_finance_opening_account", schema: "finance", table: "finance_opening_balances", column: "finance_account_id", principalSchema: "finance", principalTable: "finance_accounts", principalColumn: "id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "fk_payment_movement_account", schema: "finance", table: "payment_movement_registry", column: "finance_account_id", principalSchema: "finance", principalTable: "finance_accounts", principalColumn: "id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.CreateIndex("uq_finance_account_name_type", "finance_accounts", new[] { "name", "type" }, schema: "finance", unique: true);
        migrationBuilder.CreateIndex("uq_finance_ledger_source", "finance_ledger_entries", new[] { "source_type", "source_id" }, schema: "finance", unique: true);
        migrationBuilder.CreateIndex("uq_finance_ledger_reference", "finance_ledger_entries", "external_reference", schema: "finance", unique: true, filter: "(external_reference is not null)");
        migrationBuilder.CreateIndex("ix_finance_ledger_account_date", "finance_ledger_entries", new[] { "finance_account_id", "business_date" }, schema: "finance");
        migrationBuilder.CreateIndex("uq_finance_opening_posted_entry", "finance_opening_balances", "posted_entry_id", schema: "finance", unique: true, filter: "(posted_entry_id is not null)");
        migrationBuilder.CreateIndex("uq_payment_movement_source", "payment_movement_registry", new[] { "source_type", "source_id" }, schema: "finance", unique: true);
        migrationBuilder.CreateIndex("uq_payment_movement_reference", "payment_movement_registry", "normalized_external_reference", schema: "finance", unique: true, filter: "(normalized_external_reference is not null)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("payment_movement_registry", "finance");
        migrationBuilder.DropTable("finance_opening_balances", "finance");
        migrationBuilder.DropTable("finance_ledger_entries", "finance");
        migrationBuilder.DropTable("finance_accounts", "finance");
    }
}
