using Lensee.Modules.Payments.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations;

[DbContext(typeof(PaymentsDbContext))]
[Migration("20260922015900_ValidateDeferredPaymentAggregates")]
public partial class ValidateDeferredPaymentAggregates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            create or replace function payments.verify_main_payment_log_aggregates()
            returns trigger language plpgsql as $$
            declare paid numeric(18,4); declare pending numeric(18,4);
            begin
                if new.payment_method is not null or new.is_deleted then return null; end if;
                select coalesce(sum(amount) filter (where sub_log_status = 'Confirmed'), 0),
                       coalesce(sum(amount) filter (where sub_log_status = 'Draft'), 0)
                into paid, pending from payments.installment_sub_logs where main_log_id = new.id;
                if new.amount_paid <> paid or new.pending_amount <> pending then
                    raise exception 'Payment aggregate mismatch for main log %', new.id;
                end if;
                return null;
            end $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            create or replace function payments.verify_main_payment_log_aggregates()
            returns trigger language plpgsql as $$
            declare paid numeric(18,4); declare pending numeric(18,4);
            begin
                if new.payment_method <> 'Installment' or new.is_deleted then return null; end if;
                select coalesce(sum(amount) filter (where sub_log_status = 'Confirmed'), 0),
                       coalesce(sum(amount) filter (where sub_log_status = 'Draft'), 0)
                into paid, pending from payments.installment_sub_logs where main_log_id = new.id;
                if new.amount_paid <> paid or new.pending_amount <> pending then
                    raise exception 'Payment aggregate mismatch for main log %', new.id;
                end if;
                return null;
            end $$;
            """);
    }
}
