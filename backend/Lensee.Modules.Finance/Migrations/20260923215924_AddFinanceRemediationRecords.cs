using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Finance.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceRemediationRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_finance_expense_category",
                schema: "finance",
                table: "finance_expenses");

            migrationBuilder.DropCheckConstraint(
                name: "chk_finance_expense_status",
                schema: "finance",
                table: "finance_expenses");

            migrationBuilder.AddColumn<Guid>(
                name: "category_id",
                schema: "finance",
                table: "finance_expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correction_note",
                schema: "finance",
                table: "finance_expenses",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "transfer_id",
                schema: "finance",
                table: "finance_expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "category_id",
                schema: "finance",
                table: "c_level_withdrawals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correction_note",
                schema: "finance",
                table: "c_level_withdrawals",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "c_level_withdrawal_repayments",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    withdrawal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    finance_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    movement_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    external_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    posted_entry_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_c_level_withdrawal_repayments", x => x.id);
                    table.CheckConstraint("chk_withdrawal_repayment_amount", "amount > 0");
                    table.ForeignKey(
                        name: "FK_c_level_withdrawal_repayments_c_level_withdrawals_withdrawa~",
                        column: x => x.withdrawal_id,
                        principalSchema: "finance",
                        principalTable: "c_level_withdrawals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_c_level_withdrawal_repayments_finance_accounts_finance_acco~",
                        column: x => x.finance_account_id,
                        principalSchema: "finance",
                        principalTable: "finance_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "finance_categories",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    english_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    arabic_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_categories", x => x.id);
                    table.CheckConstraint("chk_finance_category_kind", "kind in ('Expense','Withdrawal')");
                    table.CheckConstraint("chk_finance_category_order", "sort_order >= 0");
                    table.ForeignKey(
                        name: "FK_finance_categories_finance_categories_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "finance",
                        principalTable: "finance_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "finance_transfers",
                schema: "finance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    fee_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    business_date = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    external_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    source_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fee_expense_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_finance_transfers", x => x.id);
                    table.CheckConstraint("chk_finance_transfer_accounts", "source_account_id <> destination_account_id");
                    table.CheckConstraint("chk_finance_transfer_amount", "amount > 0 and fee_amount >= 0");
                    table.ForeignKey(
                        name: "FK_finance_transfers_finance_accounts_destination_account_id",
                        column: x => x.destination_account_id,
                        principalSchema: "finance",
                        principalTable: "finance_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_finance_transfers_finance_accounts_source_account_id",
                        column: x => x.source_account_id,
                        principalSchema: "finance",
                        principalTable: "finance_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "chk_finance_expense_status",
                schema: "finance",
                table: "finance_expenses",
                sql: "status in ('Draft','PendingReview','Posted','Rejected','Corrected','Pending','Paid')");

            migrationBuilder.CreateIndex(
                name: "IX_c_level_withdrawal_repayments_external_reference",
                schema: "finance",
                table: "c_level_withdrawal_repayments",
                column: "external_reference",
                unique: true,
                filter: "(external_reference is not null)");

            migrationBuilder.CreateIndex(
                name: "IX_c_level_withdrawal_repayments_finance_account_id",
                schema: "finance",
                table: "c_level_withdrawal_repayments",
                column: "finance_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_c_level_withdrawal_repayments_withdrawal_id",
                schema: "finance",
                table: "c_level_withdrawal_repayments",
                column: "withdrawal_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_categories_kind_code",
                schema: "finance",
                table: "finance_categories",
                columns: new[] { "kind", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_finance_categories_kind_sort_order",
                schema: "finance",
                table: "finance_categories",
                columns: new[] { "kind", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_finance_categories_parent_id",
                schema: "finance",
                table: "finance_categories",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_transfers_destination_account_id",
                schema: "finance",
                table: "finance_transfers",
                column: "destination_account_id");

            migrationBuilder.CreateIndex(
                name: "IX_finance_transfers_external_reference",
                schema: "finance",
                table: "finance_transfers",
                column: "external_reference",
                unique: true,
                filter: "(external_reference is not null)");

            migrationBuilder.CreateIndex(
                name: "IX_finance_transfers_source_account_id",
                schema: "finance",
                table: "finance_transfers",
                column: "source_account_id");

            migrationBuilder.Sql(@"
                INSERT INTO finance.finance_categories
                    (id, kind, code, english_name, arabic_name, sort_order, is_active, created_by, created_at)
                VALUES
                    (uuid_generate_v4(), 'Expense', 'Supply', 'Supply', 'التوريد', 10, true, '00000000-0000-0000-0000-000000000000', now()::timestamp),
                    (uuid_generate_v4(), 'Expense', 'Salaries', 'Salaries', 'الرواتب', 20, true, '00000000-0000-0000-0000-000000000000', now()::timestamp),
                    (uuid_generate_v4(), 'Expense', 'SystemServer', 'System Server', 'خادم النظام', 30, true, '00000000-0000-0000-0000-000000000000', now()::timestamp),
                    (uuid_generate_v4(), 'Expense', 'Media', 'Media', 'الإعلام', 40, true, '00000000-0000-0000-0000-000000000000', now()::timestamp),
                    (uuid_generate_v4(), 'Expense', 'ShopifySubscription', 'Shopify Subscription', 'اشتراك شوبيفاي', 50, true, '00000000-0000-0000-0000-000000000000', now()::timestamp),
                    (uuid_generate_v4(), 'Expense', 'WarehouseRental', 'Warehouse Rental', 'إيجار المخزن', 60, true, '00000000-0000-0000-0000-000000000000', now()::timestamp),
                    (uuid_generate_v4(), 'Expense', 'Bills', 'Bills', 'الفواتير', 70, true, '00000000-0000-0000-0000-000000000000', now()::timestamp),
                    (uuid_generate_v4(), 'Expense', 'TransferFee', 'Transfer Fee', 'رسوم التحويل', 80, true, '00000000-0000-0000-0000-000000000000', now()::timestamp),
                    (uuid_generate_v4(), 'Withdrawal', 'CLevelWithdrawal', 'C-level Withdrawal', 'سحب الإدارة العليا', 10, true, '00000000-0000-0000-0000-000000000000', now()::timestamp)
                ON CONFLICT (kind, code) DO NOTHING;
                INSERT INTO finance.finance_categories
                    (id, kind, code, english_name, arabic_name, parent_id, sort_order, is_active, created_by, created_at)
                SELECT uuid_generate_v4(), 'Expense', child.code, child.english_name, child.arabic_name,
                       parent.id, child.sort_order, true, '00000000-0000-0000-0000-000000000000', now()::timestamp
                FROM finance.finance_categories parent
                CROSS JOIN (VALUES
                    ('Electricity', 'Electricity', 'الكهرباء', 71),
                    ('Gas', 'Gas', 'الغاز', 72),
                    ('Internet', 'Internet', 'الإنترنت', 73),
                    ('Water', 'Water', 'المياه', 74)
                ) AS child(code, english_name, arabic_name, sort_order)
                WHERE parent.kind = 'Expense' AND parent.code = 'Bills'
                ON CONFLICT (kind, code) DO NOTHING;
            ");

            migrationBuilder.Sql(@"
                DO $$ BEGIN
                    IF EXISTS (
                        SELECT 1 FROM finance.finance_opening_balances
                        WHERE reverses_opening_balance_id IS NULL AND status <> 'Rejected'
                        GROUP BY finance_account_id HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Multiple Finance opening roots exist for one account; review and resolve before this migration.';
                    END IF;
                END $$;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "c_level_withdrawal_repayments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "finance_categories",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "finance_transfers",
                schema: "finance");

            migrationBuilder.DropCheckConstraint(
                name: "chk_finance_expense_status",
                schema: "finance",
                table: "finance_expenses");

            migrationBuilder.DropColumn(
                name: "category_id",
                schema: "finance",
                table: "finance_expenses");

            migrationBuilder.DropColumn(
                name: "correction_note",
                schema: "finance",
                table: "finance_expenses");

            migrationBuilder.DropColumn(
                name: "transfer_id",
                schema: "finance",
                table: "finance_expenses");

            migrationBuilder.DropColumn(
                name: "category_id",
                schema: "finance",
                table: "c_level_withdrawals");

            migrationBuilder.DropColumn(
                name: "correction_note",
                schema: "finance",
                table: "c_level_withdrawals");

            migrationBuilder.AddCheckConstraint(
                name: "chk_finance_expense_category",
                schema: "finance",
                table: "finance_expenses",
                sql: "category in ('Salary','Rent','SocialMedia','SoftwareAndTechnologySubscriptions','Other')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_finance_expense_status",
                schema: "finance",
                table: "finance_expenses",
                sql: "status in ('Draft','PendingReview','Posted','Rejected','Corrected')");
        }
    }
}
