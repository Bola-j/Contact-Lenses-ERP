using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Lensee.Modules.Payments.Data;

#nullable disable

namespace Lensee.Modules.Payments.Migrations;

[DbContext(typeof(PaymentsDbContext))]
[Migration("20260915120000_AddMerchantFinancialClosure")]
public partial class AddMerchantFinancialClosure : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // operation_id is already nullable in your current DB,
        // but keeping this makes the migration valid for older databases too.
        migrationBuilder.AlterColumn<Guid>(
            name: "operation_id",
            schema: "payments",
            table: "cash_records",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");

        // merchant_id already exists in your current database.
        // IF NOT EXISTS keeps this safe for both existing and fresh databases.
        migrationBuilder.Sql("""
            ALTER TABLE payments.cash_records
            ADD COLUMN IF NOT EXISTS merchant_id uuid;
            """);

        // This index already exists in your current database.
        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS idx_cash_records_merchant
            ON payments.cash_records (merchant_id)
            WHERE merchant_id IS NOT NULL;
            """);

        // The constraint does NOT currently exist.
        // Add it only if missing.
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1
                    FROM pg_constraint
                    WHERE conname = 'chk_cash_record_scope'
                      AND conrelid = 'payments.cash_records'::regclass
                ) THEN
                    ALTER TABLE payments.cash_records
                    ADD CONSTRAINT chk_cash_record_scope
                    CHECK (
                        operation_id IS NOT NULL
                        OR merchant_id IS NOT NULL
                    );
                END IF;
            END
            $$;
            """);

        migrationBuilder.CreateTable(
            name: "merchant_financial_closure_proposals",
            schema: "payments",
            columns: table => new
            {
                id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false,
                    defaultValueSql: "uuid_generate_v4()"),

                account_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),

                status = table.Column<string>(
                    type: "character varying(40)",
                    maxLength: 40,
                    nullable: false,
                    defaultValue: "PendingAdminReview"),

                submitted_by = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),

                submitted_at = table.Column<DateTime>(
                    type: "timestamp without time zone",
                    nullable: false,
                    defaultValueSql: "CURRENT_TIMESTAMP"),

                reviewed_by = table.Column<Guid>(
                    type: "uuid",
                    nullable: true),

                reviewed_at = table.Column<DateTime>(
                    type: "timestamp without time zone",
                    nullable: true),

                review_reason = table.Column<string>(
                    type: "text",
                    nullable: true),

                notes = table.Column<string>(
                    type: "text",
                    nullable: true),

                idempotency_key = table.Column<string>(
                    type: "character varying(200)",
                    maxLength: 200,
                    nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "pk_merchant_financial_closure_proposals",
                    x => x.id);

                table.CheckConstraint(
                    "chk_financial_closure_proposal_status",
                    "status in ('PendingAdminReview','PartiallyApproved','Approved','Rejected')");
            });

        migrationBuilder.CreateTable(
            name: "merchant_financial_closure_items",
            schema: "payments",
            columns: table => new
            {
                id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false,
                    defaultValueSql: "uuid_generate_v4()"),

                proposal_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),

                operation_id = table.Column<Guid>(
                    type: "uuid",
                    nullable: false),

                operation_number = table.Column<string>(
                    type: "character varying(50)",
                    maxLength: 50,
                    nullable: false),

                settlement_amount = table.Column<decimal>(
                    type: "numeric(18,4)",
                    nullable: false),

                remaining_amount = table.Column<decimal>(
                    type: "numeric(18,4)",
                    nullable: false),

                decision = table.Column<string>(
                    type: "character varying(20)",
                    maxLength: 20,
                    nullable: false,
                    defaultValue: "Pending"),

                rejection_reason = table.Column<string>(
                    type: "text",
                    nullable: true),

                decided_by = table.Column<Guid>(
                    type: "uuid",
                    nullable: true),

                decided_at = table.Column<DateTime>(
                    type: "timestamp without time zone",
                    nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "pk_merchant_financial_closure_items",
                    x => x.id);

                table.ForeignKey(
                    name: "fk_closure_item_proposal",
                    column: x => x.proposal_id,
                    principalSchema: "payments",
                    principalTable: "merchant_financial_closure_proposals",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);

                table.CheckConstraint(
                    "chk_financial_closure_item_decision",
                    "decision in ('Pending','Approved','Rejected')");

                table.CheckConstraint(
                    "chk_financial_closure_item_amount",
                    "settlement_amount >= 0 and remaining_amount >= 0");
            });

        migrationBuilder.CreateIndex(
            name: "ix_closure_proposals_account_status",
            schema: "payments",
            table: "merchant_financial_closure_proposals",
            columns: new[]
            {
                "account_id",
                "status"
            });

        migrationBuilder.CreateIndex(
            name: "uq_closure_proposals_account_idempotency",
            schema: "payments",
            table: "merchant_financial_closure_proposals",
            columns: new[]
            {
                "account_id",
                "idempotency_key"
            },
            unique: true,
            filter: "(idempotency_key IS NOT NULL)");

        migrationBuilder.CreateIndex(
            name: "ix_closure_items_proposal_operation",
            schema: "payments",
            table: "merchant_financial_closure_items",
            columns: new[]
            {
                "proposal_id",
                "operation_id"
            },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_closure_items_operation",
            schema: "payments",
            table: "merchant_financial_closure_items",
            column: "operation_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "merchant_financial_closure_items",
            schema: "payments");

        migrationBuilder.DropTable(
            name: "merchant_financial_closure_proposals",
            schema: "payments");

        migrationBuilder.Sql("""
            ALTER TABLE payments.cash_records
            DROP CONSTRAINT IF EXISTS chk_cash_record_scope;
            """);

        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS payments.idx_cash_records_merchant;
            """);

        migrationBuilder.Sql("""
            ALTER TABLE payments.cash_records
            DROP COLUMN IF EXISTS merchant_id;
            """);

        migrationBuilder.AlterColumn<Guid>(
            name: "operation_id",
            schema: "payments",
            table: "cash_records",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
    }
}