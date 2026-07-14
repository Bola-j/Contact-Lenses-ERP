using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations;

public partial class AllowCashHandToHandPaymentLogs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("alter table if exists payments.main_payment_logs drop constraint if exists chk_main_payment_method;");
        migrationBuilder.Sql("alter table if exists payments.main_payment_logs add constraint chk_main_payment_method check (payment_method in ('CashHandToHand','CashTransaction','Installment'));");
        migrationBuilder.Sql("alter table if exists payments.cash_records drop constraint if exists chk_cash_status;");
        migrationBuilder.Sql("alter table if exists payments.cash_records add constraint chk_cash_status check (status in ('PendingAccountant','Completed','Cancelled'));");
        migrationBuilder.Sql("""
            insert into payments.main_payment_logs (
                id, operation_id, merchant_id, total_amount, amount_paid,
                payment_method, status, initialized_by, initialized_at,
                last_modified_by, last_modified_at, notes, is_deleted)
            select
                uuid_generate_v4(), operation.id, operation.client_id,
                sum(record.amount), sum(record.amount), 'CashHandToHand', 'Completed',
                operation.created_by, min(record.payment_date),
                operation.created_by, max(record.payment_date),
                'Backfilled from completed cash sale.', false
            from payments.cash_records record
            join operations.operation_logs operation on operation.id = record.operation_id
            where record.payment_type = 'CashReceived'
              and record.status = 'Completed'
              and operation.operation_type in ('WholesaleSale', 'RetailSale')
              and operation.status = 'Completed'
              and operation.client_id is not null
              and not exists (
                  select 1 from payments.main_payment_logs existing
                  where existing.operation_id = operation.id and existing.is_deleted = false)
            group by operation.id, operation.client_id, operation.created_by;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("alter table if exists payments.main_payment_logs drop constraint if exists chk_main_payment_method;");
        migrationBuilder.Sql("alter table if exists payments.main_payment_logs add constraint chk_main_payment_method check (payment_method in ('CashTransaction','Installment'));");
        migrationBuilder.Sql("alter table if exists payments.cash_records drop constraint if exists chk_cash_status;");
        migrationBuilder.Sql("alter table if exists payments.cash_records add constraint chk_cash_status check (status in ('Completed','Cancelled'));");
    }
}
