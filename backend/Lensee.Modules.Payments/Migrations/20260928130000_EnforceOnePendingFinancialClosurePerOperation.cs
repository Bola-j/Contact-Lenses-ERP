using Lensee.Modules.Payments.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lensee.Modules.Payments.Migrations;

[DbContext(typeof(PaymentsDbContext))]
[Migration("20260928130000_EnforceOnePendingFinancialClosurePerOperation")]
public sealed class EnforceOnePendingFinancialClosurePerOperation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $$
        BEGIN
            IF EXISTS (
                SELECT operation_id
                FROM payments.merchant_financial_closure_items
                WHERE decision = 'Pending'
                GROUP BY operation_id
                HAVING COUNT(*) > 1
            ) THEN
                RAISE EXCEPTION 'Cannot enforce one pending financial-closure request per operation while duplicate pending items exist; reconcile the affected proposals first.';
            END IF;
        END $$;

        CREATE UNIQUE INDEX ux_financial_closure_one_pending_operation
            ON payments.merchant_financial_closure_items (operation_id)
            WHERE decision = 'Pending';
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP INDEX IF EXISTS payments.ux_financial_closure_one_pending_operation;
        """);
}
