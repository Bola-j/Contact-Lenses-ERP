using Lensee.Modules.Payments.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations;

/// <summary>
/// Reclassifies the old, misleading merchant-credit name without changing the
/// amount, approval history, or cash records of any financial event.
/// </summary>
[DbContext(typeof(PaymentsDbContext))]
[Migration("20260909090000_ReclassifyMerchantCreditAsAdditionalCharge")]
public partial class ReclassifyMerchantCreditAsAdditionalCharge : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "uq_cash_records_adjustment",
            schema: "payments",
            table: "cash_records");

        migrationBuilder.CreateIndex(
            name: "idx_cash_records_adjustment",
            schema: "payments",
            table: "cash_records",
            column: "financial_adjustment_id",
            filter: "(financial_adjustment_id IS NOT NULL)");

        migrationBuilder.Sql("""
            alter table payments.financial_adjustments
                drop constraint if exists chk_financial_adjustment_type;
            alter table payments.financial_adjustments
                add constraint chk_financial_adjustment_type
                check (adjustment_type in ('AdditionalCharge','BalanceReduction','CashRefund'));

            update payments.financial_adjustments
            set adjustment_type = 'AdditionalCharge',
                notes = concat_ws(E'\n', notes, '[Reclassified from MerchantCredit]')
            where adjustment_type = 'MerchantCredit';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            update payments.financial_adjustments
            set adjustment_type = 'MerchantCredit'
            where adjustment_type = 'AdditionalCharge'
              and notes like '%[Reclassified from MerchantCredit]%';

            alter table payments.financial_adjustments
                drop constraint if exists chk_financial_adjustment_type;
            alter table payments.financial_adjustments
                add constraint chk_financial_adjustment_type
                check (adjustment_type in ('MerchantCredit','BalanceReduction','CashRefund'));
            """);

        migrationBuilder.DropIndex(
            name: "idx_cash_records_adjustment",
            schema: "payments",
            table: "cash_records");
        migrationBuilder.CreateIndex(
            name: "uq_cash_records_adjustment",
            schema: "payments",
            table: "cash_records",
            column: "financial_adjustment_id",
            unique: true,
            filter: "(financial_adjustment_id IS NOT NULL)");
    }
}
