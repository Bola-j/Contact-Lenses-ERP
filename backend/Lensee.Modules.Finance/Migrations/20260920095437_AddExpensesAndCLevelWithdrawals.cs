using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Finance.Migrations;

public partial class AddExpensesAndCLevelWithdrawals : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "finance_expenses", schema: "finance", columns: table => new
        {
            id = table.Column<Guid>("uuid", nullable: false),
            finance_account_id = table.Column<Guid>("uuid", nullable: false),
            amount = table.Column<decimal>("numeric(18,4)", nullable: false),
            category = table.Column<string>("character varying(60)", maxLength: 60, nullable: false),
            movement_method = table.Column<string>("character varying(50)", maxLength: 50, nullable: false),
            business_date = table.Column<DateOnly>("date", nullable: false),
            description = table.Column<string>("character varying(1000)", maxLength: 1000, nullable: true),
            status = table.Column<string>("character varying(30)", maxLength: 30, nullable: false, defaultValue: "Draft"),
            created_by_user_id = table.Column<Guid>("uuid", nullable: false),
            created_at = table.Column<DateTime>("timestamp without time zone", nullable: false),
            approved_by_user_id = table.Column<Guid?>("uuid", nullable: true),
            approved_at = table.Column<DateTime?>("timestamp without time zone", nullable: true),
            posted_finance_ledger_entry_id = table.Column<Guid?>("uuid", nullable: true),
            reverses_expense_id = table.Column<Guid?>("uuid", nullable: true),
            replaced_by_expense_id = table.Column<Guid?>("uuid", nullable: true),
            external_reference = table.Column<string>("character varying(200)", maxLength: 200, nullable: true),
            correlation_id = table.Column<string>("character varying(100)", maxLength: 100, nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_finance_expenses", x => x.id); table.ForeignKey(name: "FK_finance_expenses_finance_accounts_finance_account_id", column: x => x.finance_account_id, principalSchema: "finance", principalTable: "finance_accounts", principalColumn: "id", onDelete: ReferentialAction.Restrict); table.CheckConstraint("chk_finance_expense_amount", "amount > 0"); table.CheckConstraint("chk_finance_expense_category", "category in ('Salary','Rent','SocialMedia','SoftwareAndTechnologySubscriptions','Other')"); table.CheckConstraint("chk_finance_expense_status", "status in ('Draft','PendingReview','Posted','Rejected','Corrected')"); table.CheckConstraint("chk_finance_expense_other_description", "category <> 'Other' or length(trim(coalesce(description, ''))) > 0"); });
        migrationBuilder.CreateTable(name: "c_level_withdrawals", schema: "finance", columns: table => new
        {
            id = table.Column<Guid>("uuid", nullable: false),
            assigned_to_c_level_user_id = table.Column<Guid>("uuid", nullable: false),
            assigned_by_user_id = table.Column<Guid>("uuid", nullable: false),
            finance_account_id = table.Column<Guid>("uuid", nullable: false),
            amount = table.Column<decimal>("numeric(18,4)", nullable: false),
            movement_method = table.Column<string>("character varying(50)", maxLength: 50, nullable: false),
            business_date = table.Column<DateOnly>("date", nullable: false),
            reason = table.Column<string>("character varying(1000)", maxLength: 1000, nullable: false),
            status = table.Column<string>("character varying(30)", maxLength: 30, nullable: false, defaultValue: "Draft"),
            created_by_user_id = table.Column<Guid>("uuid", nullable: false),
            created_at = table.Column<DateTime>("timestamp without time zone", nullable: false),
            approved_by_user_id = table.Column<Guid?>("uuid", nullable: true),
            approved_at = table.Column<DateTime?>("timestamp without time zone", nullable: true),
            posted_finance_ledger_entry_id = table.Column<Guid?>("uuid", nullable: true),
            reverses_withdrawal_id = table.Column<Guid?>("uuid", nullable: true),
            replaced_by_withdrawal_id = table.Column<Guid?>("uuid", nullable: true),
            external_reference = table.Column<string>("character varying(200)", maxLength: 200, nullable: true),
            correlation_id = table.Column<string>("character varying(100)", maxLength: 100, nullable: true)
        }, constraints: table => { table.PrimaryKey("PK_c_level_withdrawals", x => x.id); table.ForeignKey(name: "FK_c_level_withdrawals_finance_accounts_finance_account_id", column: x => x.finance_account_id, principalSchema: "finance", principalTable: "finance_accounts", principalColumn: "id", onDelete: ReferentialAction.Restrict); table.CheckConstraint("chk_c_level_withdrawal_amount", "amount > 0"); table.CheckConstraint("chk_c_level_withdrawal_status", "status in ('Draft','PendingReview','Posted','Rejected','Corrected')"); table.CheckConstraint("chk_c_level_withdrawal_reason", "length(trim(reason)) > 0"); });
        migrationBuilder.CreateIndex("IX_finance_expenses_finance_account_id", "finance_expenses", "finance_account_id", "finance");
        migrationBuilder.CreateIndex("IX_finance_expenses_posted_finance_ledger_entry_id", "finance_expenses", "posted_finance_ledger_entry_id", "finance", unique: true, filter: "(posted_finance_ledger_entry_id is not null)");
        migrationBuilder.CreateIndex("IX_finance_expenses_replaced_by_expense_id", "finance_expenses", "replaced_by_expense_id", "finance", unique: true, filter: "(replaced_by_expense_id is not null)");
        migrationBuilder.CreateIndex("IX_c_level_withdrawals_assigned_to_c_level_user_id_status", "c_level_withdrawals", new[] { "assigned_to_c_level_user_id", "status" }, "finance");
        migrationBuilder.CreateIndex("IX_c_level_withdrawals_finance_account_id", "c_level_withdrawals", "finance_account_id", "finance");
        migrationBuilder.CreateIndex("IX_c_level_withdrawals_posted_finance_ledger_entry_id", "c_level_withdrawals", "posted_finance_ledger_entry_id", "finance", unique: true, filter: "(posted_finance_ledger_entry_id is not null)");
        migrationBuilder.CreateIndex("IX_c_level_withdrawals_replaced_by_withdrawal_id", "c_level_withdrawals", "replaced_by_withdrawal_id", "finance", unique: true, filter: "(replaced_by_withdrawal_id is not null)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("c_level_withdrawals", "finance");
        migrationBuilder.DropTable("finance_expenses", "finance");
    }
}
