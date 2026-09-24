using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Lensee.Modules.Payments.Migrations;

[DbContext(typeof(Lensee.Modules.Payments.Data.PaymentsDbContext))]
[Migration("20260919153000_AddLegacyPaymentFinanceAccount")]
public partial class AddLegacyPaymentFinanceAccount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "finance_account_id",
            schema: "payments",
            table: "cash_records",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "finance_account_id",
            schema: "payments",
            table: "installment_sub_logs",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex("ix_cash_records_finance_account_id", "cash_records", "finance_account_id", "payments");
        migrationBuilder.CreateIndex("ix_installment_sub_logs_finance_account_id", "installment_sub_logs", "finance_account_id", "payments");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("ix_cash_records_finance_account_id", "cash_records", "payments");
        migrationBuilder.DropIndex("ix_installment_sub_logs_finance_account_id", "installment_sub_logs", "payments");
        migrationBuilder.DropColumn("finance_account_id", "cash_records", "payments");
        migrationBuilder.DropColumn("finance_account_id", "installment_sub_logs", "payments");
    }
}
