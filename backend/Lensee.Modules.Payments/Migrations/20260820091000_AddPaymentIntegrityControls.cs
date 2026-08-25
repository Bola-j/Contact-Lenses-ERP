using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Lensee.Modules.Payments.Data;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />

    [DbContext(typeof(PaymentsDbContext))]
    [Migration("20260820091000_AddPaymentIntegrityControls")]
    public partial class AddPaymentIntegrityControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                do $$
                declare duplicate_count integer;
                begin
                    select count(*) into duplicate_count
                    from (
                        select operation_id
                        from payments.main_payment_logs
                        where is_deleted = false
                        group by operation_id
                        having count(*) > 1
                    ) duplicates;

                    if duplicate_count > 0 then
                        raise exception 'Cannot apply payment integrity migration: % operation(s) have multiple active main payment logs.', duplicate_count;
                    end if;
                end $$;
                """);

            migrationBuilder.AddColumn<decimal>(
                name: "pending_amount",
                schema: "payments",
                table: "main_payment_logs",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                update payments.main_payment_logs log
                set pending_amount = coalesce((
                    select sum(sub.amount)
                    from payments.installment_sub_logs sub
                    where sub.main_log_id = log.id
                      and sub.sub_log_status = 'Draft'
                ), 0);
                """);

            migrationBuilder.Sql("alter table if exists payments.main_payment_logs drop constraint if exists chk_main_payment_paid_lte_total;");
            migrationBuilder.AddCheckConstraint(
                name: "chk_main_payment_pending_amount",
                schema: "payments",
                table: "main_payment_logs",
                sql: "pending_amount >= 0");
            migrationBuilder.AddCheckConstraint(
                name: "chk_main_payment_paid_lte_total",
                schema: "payments",
                table: "main_payment_logs",
                sql: "amount_paid + pending_amount <= total_amount");

            migrationBuilder.CreateIndex(
                name: "uq_main_payment_operation_active",
                schema: "payments",
                table: "main_payment_logs",
                column: "operation_id",
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateTable(
                name: "payment_idempotency_keys",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    key = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    response_status_code = table.Column<int>(type: "integer", nullable: true),
                    response_body = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("payment_idempotency_keys_pkey", x => x.id);
                    table.CheckConstraint("chk_payment_idempotency_status", "status in ('Pending','Completed')");
                });

            migrationBuilder.CreateIndex(
                name: "idx_payment_idempotency_expires_at",
                schema: "payments",
                table: "payment_idempotency_keys",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "uq_payment_idempotency_key_scope",
                schema: "payments",
                table: "payment_idempotency_keys",
                columns: new[] { "key", "scope" },
                unique: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by",
                schema: "payments",
                table: "financial_adjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reviewed_at",
                schema: "payments",
                table: "financial_adjustments",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                schema: "payments",
                table: "financial_adjustments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "payment_log_id",
                schema: "payments",
                table: "financial_adjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reverses_adjustment_id",
                schema: "payments",
                table: "financial_adjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "lineage_kind",
                schema: "payments",
                table: "financial_adjustments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValueSql: "'LegacyUnlinked'::character varying");

            migrationBuilder.Sql("""
                alter table if exists payments.financial_adjustments drop constraint if exists chk_financial_adjustment_status;
                alter table payments.financial_adjustments
                    add constraint chk_financial_adjustment_status
                    check (status in ('PendingApproval','Approved','Rejected','Completed','Cancelled','LegacyUnlinked'));

                update payments.financial_adjustments
                set lineage_kind = case when operation_id is null then 'LegacyUnlinked' else 'SourceLinked' end;
                """);

            migrationBuilder.Sql("""
                create or replace function payments.verify_payment_log_aggregates()
                returns trigger
                language plpgsql
                as $$
                declare affected_main_log_id uuid;
                declare paid numeric(18,4);
                declare pending numeric(18,4);
                begin
                    affected_main_log_id := coalesce(new.main_log_id, old.main_log_id, new.id, old.id);

                    if affected_main_log_id is null then
                        return null;
                    end if;

                    if not exists (
                        select 1
                        from payments.main_payment_logs
                        where id = affected_main_log_id
                          and payment_method = 'Installment'
                          and is_deleted = false
                    ) then
                        return null;
                    end if;

                    select
                        coalesce(sum(amount) filter (where sub_log_status = 'Confirmed'), 0),
                        coalesce(sum(amount) filter (where sub_log_status = 'Draft'), 0)
                    into paid, pending
                    from payments.installment_sub_logs
                    where main_log_id = affected_main_log_id;

                    if exists (
                        select 1
                        from payments.main_payment_logs
                        where id = affected_main_log_id
                          and (amount_paid <> paid or pending_amount <> pending)
                    ) then
                        raise exception 'Payment aggregate mismatch for main log %', affected_main_log_id;
                    end if;

                    return null;
                end $$;

                drop trigger if exists trg_installment_sub_logs_verify_aggregates on payments.installment_sub_logs;
                create constraint trigger trg_installment_sub_logs_verify_aggregates
                after insert or update or delete on payments.installment_sub_logs
                deferrable initially deferred
                for each row execute function payments.verify_payment_log_aggregates();

                drop trigger if exists trg_main_payment_logs_verify_aggregates on payments.main_payment_logs;
                create constraint trigger trg_main_payment_logs_verify_aggregates
                after insert or update on payments.main_payment_logs
                deferrable initially deferred
                for each row execute function payments.verify_payment_log_aggregates();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop trigger if exists trg_installment_sub_logs_verify_aggregates on payments.installment_sub_logs;
                drop trigger if exists trg_main_payment_logs_verify_aggregates on payments.main_payment_logs;
                drop function if exists payments.verify_payment_log_aggregates();
                """);

            migrationBuilder.DropTable(
                name: "payment_idempotency_keys",
                schema: "payments");

            migrationBuilder.DropColumn(
                name: "reviewed_by",
                schema: "payments",
                table: "financial_adjustments");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                schema: "payments",
                table: "financial_adjustments");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                schema: "payments",
                table: "financial_adjustments");

            migrationBuilder.DropColumn(
                name: "payment_log_id",
                schema: "payments",
                table: "financial_adjustments");

            migrationBuilder.DropColumn(
                name: "reverses_adjustment_id",
                schema: "payments",
                table: "financial_adjustments");

            migrationBuilder.DropColumn(
                name: "lineage_kind",
                schema: "payments",
                table: "financial_adjustments");

            migrationBuilder.DropIndex(
                name: "uq_main_payment_operation_active",
                schema: "payments",
                table: "main_payment_logs");

            migrationBuilder.DropCheckConstraint(
                name: "chk_main_payment_pending_amount",
                schema: "payments",
                table: "main_payment_logs");

            migrationBuilder.Sql("alter table if exists payments.main_payment_logs drop constraint if exists chk_main_payment_paid_lte_total;");
            migrationBuilder.AddCheckConstraint(
                name: "chk_main_payment_paid_lte_total",
                schema: "payments",
                table: "main_payment_logs",
                sql: "amount_paid <= total_amount");

            migrationBuilder.DropColumn(
                name: "pending_amount",
                schema: "payments",
                table: "main_payment_logs");
        }
    }
}
