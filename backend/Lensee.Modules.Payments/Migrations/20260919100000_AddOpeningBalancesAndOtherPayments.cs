using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Lensee.Modules.Payments.Migrations;

[DbContext(typeof(Lensee.Modules.Payments.Data.PaymentsDbContext))]
[Migration("20260919100000_AddOpeningBalancesAndOtherPayments")]
public partial class AddOpeningBalancesAndOtherPayments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE payments.main_payment_logs DROP CONSTRAINT IF EXISTS chk_main_payment_scope;");
        migrationBuilder.Sql("ALTER TABLE payments.main_payment_logs DISABLE TRIGGER USER;");
        migrationBuilder.Sql("UPDATE payments.main_payment_logs SET scope = 'OtherPayments' WHERE scope = 'DirectOperation';");
        migrationBuilder.Sql("UPDATE payments.main_payment_logs SET scope = 'OtherPayments' WHERE scope IS NULL OR scope NOT IN ('MerchantAccount','OtherPayments');");
        migrationBuilder.Sql("ALTER TABLE payments.main_payment_logs ENABLE TRIGGER USER;");
        migrationBuilder.Sql("ALTER TABLE payments.main_payment_logs ADD CONSTRAINT chk_main_payment_scope CHECK (scope in ('MerchantAccount','OtherPayments'));");
        migrationBuilder.Sql("ALTER TABLE payments.main_payment_logs ALTER COLUMN scope SET DEFAULT 'OtherPayments';");

        migrationBuilder.Sql("ALTER TABLE payments.merchant_operation_obligations ALTER COLUMN operation_id DROP NOT NULL;");
        migrationBuilder.AddColumn<string>(name: "source_type", schema: "payments", table: "merchant_operation_obligations", type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "Operation");
        migrationBuilder.AddColumn<Guid?>(name: "source_id", schema: "payments", table: "merchant_operation_obligations", type: "uuid", nullable: true);
        migrationBuilder.Sql("UPDATE payments.merchant_operation_obligations SET source_id = operation_id WHERE source_id IS NULL;");
        migrationBuilder.Sql("ALTER TABLE payments.merchant_operation_obligations ALTER COLUMN source_id SET NOT NULL;");
        migrationBuilder.Sql("DROP INDEX IF EXISTS payments.ix_merchant_operation_obligations_operation_id;");
        migrationBuilder.CreateIndex("ix_merchant_operation_obligations_operation_id", "merchant_operation_obligations", "operation_id", schema: "payments", unique: true, filter: "(operation_id IS NOT NULL)");
        migrationBuilder.CreateIndex("ix_merchant_operation_obligations_source", "merchant_operation_obligations", new[] { "source_type", "source_id" }, schema: "payments", unique: true);

        migrationBuilder.CreateTable(
            name: "merchant_opening_balance_charges",
            schema: "payments",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                merchant_id = table.Column<Guid>(type: "uuid", nullable: false),
                amount = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                as_of_date = table.Column<DateOnly>(type: "date", nullable: false),
                description = table.Column<string>(type: "text", nullable: false),
                status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Draft"),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                reviewed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                review_reason = table.Column<string>(type: "text", nullable: true),
                posted_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                reverses_charge_id = table.Column<Guid>(type: "uuid", nullable: true),
                replaced_by_charge_id = table.Column<Guid>(type: "uuid", nullable: true),
                correlation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_merchant_opening_balance_charges", x => x.id);
                table.CheckConstraint("chk_opening_balance_amount", "amount > 0");
                table.CheckConstraint("chk_opening_balance_status", "status in ('Draft','PendingReview','Posted','Rejected','Reversed','Corrected')");
            });
        migrationBuilder.CreateIndex("ix_opening_balance_merchant_status", "merchant_opening_balance_charges", new[] { "merchant_id", "status" }, schema: "payments");
        migrationBuilder.CreateIndex("ix_opening_balance_posted_entry", "merchant_opening_balance_charges", "posted_entry_id", schema: "payments", unique: true, filter: "(posted_entry_id IS NOT NULL)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("merchant_opening_balance_charges", "payments");
        migrationBuilder.DropIndex("ix_merchant_operation_obligations_source", "merchant_operation_obligations", "payments");
        migrationBuilder.DropColumn("source_type", "merchant_operation_obligations", "payments");
        migrationBuilder.DropColumn("source_id", "merchant_operation_obligations", "payments");
        migrationBuilder.Sql("ALTER TABLE payments.merchant_operation_obligations ALTER COLUMN operation_id SET NOT NULL;");
        migrationBuilder.Sql("ALTER TABLE payments.main_payment_logs DROP CONSTRAINT IF EXISTS chk_main_payment_scope;");
        migrationBuilder.Sql("ALTER TABLE payments.main_payment_logs ADD CONSTRAINT chk_main_payment_scope CHECK (scope in ('MerchantAccount','DirectOperation'));");
        migrationBuilder.Sql("ALTER TABLE payments.main_payment_logs ALTER COLUMN scope SET DEFAULT 'DirectOperation';");
    }
}
