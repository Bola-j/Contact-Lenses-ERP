using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class PersistCollectionScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "scope",
                schema: "payments",
                table: "main_payment_logs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "DirectOperation");

            migrationBuilder.AddColumn<string>(
                name: "scope",
                schema: "payments",
                table: "merchant_account_collection_drafts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "MerchantAccount");

            // The aggregate integrity trigger on main_payment_logs is a deferred
            // constraint trigger. Updating the newly-added scope column queues
            // trigger events, and PostgreSQL then rejects the following ALTER
            // TABLE with 55006 (pending trigger events). Scope is metadata only,
            // so suspend that trigger for the one-time backfill and restore it
            // before the migration continues.
            migrationBuilder.Sql("""
                ALTER TABLE payments.main_payment_logs
                    DISABLE TRIGGER trg_main_payment_logs_verify_aggregates;
                UPDATE payments.main_payment_logs AS payment_log
                SET scope = 'MerchantAccount'
                FROM operations.operation_logs AS operation
                WHERE operation.id = payment_log.operation_id
                  AND operation.client_id IS NOT NULL;
                ALTER TABLE payments.main_payment_logs
                    ENABLE TRIGGER trg_main_payment_logs_verify_aggregates;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "chk_main_payment_scope",
                schema: "payments",
                table: "main_payment_logs",
                sql: "scope in ('MerchantAccount','DirectOperation')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_merchant_collection_draft_scope",
                schema: "payments",
                table: "merchant_account_collection_drafts",
                sql: "scope = 'MerchantAccount'");

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION payments.prevent_collection_scope_change()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF NEW.scope IS DISTINCT FROM OLD.scope THEN
                        RAISE EXCEPTION 'Collection scope is immutable';
                    END IF;
                    RETURN NEW;
                END;
                $function$;
                CREATE TRIGGER trg_main_payment_logs_scope_immutable
                    BEFORE UPDATE OF scope ON payments.main_payment_logs
                    FOR EACH ROW EXECUTE FUNCTION payments.prevent_collection_scope_change();
                CREATE TRIGGER trg_merchant_collection_drafts_scope_immutable
                    BEFORE UPDATE OF scope ON payments.merchant_account_collection_drafts
                    FOR EACH ROW EXECUTE FUNCTION payments.prevent_collection_scope_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint("chk_main_payment_scope", "payments", "main_payment_logs");
            migrationBuilder.DropCheckConstraint("chk_merchant_collection_draft_scope", "payments", "merchant_account_collection_drafts");
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_main_payment_logs_scope_immutable ON payments.main_payment_logs;
                DROP TRIGGER IF EXISTS trg_merchant_collection_drafts_scope_immutable ON payments.merchant_account_collection_drafts;
                DROP FUNCTION IF EXISTS payments.prevent_collection_scope_change();
                """);
            migrationBuilder.DropColumn("scope", "payments", "main_payment_logs");
            migrationBuilder.DropColumn("scope", "payments", "merchant_account_collection_drafts");
        }
    }
}
