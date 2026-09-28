using Lensee.Modules.Payments.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lensee.Modules.Payments.Migrations;

[DbContext(typeof(PaymentsDbContext))]
[Migration("20260928120000_NormalizeMerchantAllocationIdColumn")]
public sealed class NormalizeMerchantAllocationIdColumn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'payments'
                  AND table_name = 'merchant_entry_allocations'
                  AND column_name = 'Id'
            ) AND NOT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'payments'
                  AND table_name = 'merchant_entry_allocations'
                  AND column_name = 'id'
            ) THEN
                ALTER TABLE payments.merchant_entry_allocations RENAME COLUMN "Id" TO id;
            END IF;
        END $$;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DO $$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'payments'
                  AND table_name = 'merchant_entry_allocations'
                  AND column_name = 'id'
            ) AND NOT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'payments'
                  AND table_name = 'merchant_entry_allocations'
                  AND column_name = 'Id'
            ) THEN
                ALTER TABLE payments.merchant_entry_allocations RENAME COLUMN id TO "Id";
            END IF;
        END $$;
        """);
}
