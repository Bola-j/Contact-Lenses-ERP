using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Lensee.Modules.Payments.Data;

#nullable disable

namespace Lensee.Modules.Payments.Migrations;

[DbContext(typeof(PaymentsDbContext))]
[Migration("20260829180000_FixMainPaymentAggregateTrigger")]
public partial class FixMainPaymentAggregateTrigger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            create or replace function payments.verify_main_payment_log_aggregates()
            returns trigger
            language plpgsql
            as $$
            declare paid numeric(18,4);
            declare pending numeric(18,4);
            begin
                if new.payment_method <> 'Installment' or new.is_deleted then
                    return null;
                end if;

                select
                    coalesce(sum(amount) filter (where sub_log_status = 'Confirmed'), 0),
                    coalesce(sum(amount) filter (where sub_log_status = 'Draft'), 0)
                into paid, pending
                from payments.installment_sub_logs
                where main_log_id = new.id;

                if new.amount_paid <> paid or new.pending_amount <> pending then
                    raise exception 'Payment aggregate mismatch for main log %', new.id;
                end if;

                return null;
            end $$;

            drop trigger if exists trg_main_payment_logs_verify_aggregates on payments.main_payment_logs;
            create constraint trigger trg_main_payment_logs_verify_aggregates
            after insert or update on payments.main_payment_logs
            deferrable initially deferred
            for each row execute function payments.verify_main_payment_log_aggregates();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            drop trigger if exists trg_main_payment_logs_verify_aggregates on payments.main_payment_logs;
            create constraint trigger trg_main_payment_logs_verify_aggregates
            after insert or update on payments.main_payment_logs
            deferrable initially deferred
            for each row execute function payments.verify_payment_log_aggregates();
            drop function if exists payments.verify_main_payment_log_aggregates();
            """);
    }
}
