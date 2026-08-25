using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDestinationSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "notification_number",
                schema: "notifications",
                table: "notification_logs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reference_code",
                schema: "notifications",
                table: "notification_logs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reference_context_json",
                schema: "notifications",
                table: "notification_logs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reference_title",
                schema: "notifications",
                table: "notification_logs",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE notifications.notification_logs
                SET notification_number = 'NOT-' || upper(replace(id::text, '-', '')),
                    reference_code = CASE lower(coalesce(reference_type, ''))
                        WHEN 'stockbalance' THEN 'BAL-'
                        WHEN 'inventorybatch' THEN 'BATCH-'
                        WHEN 'operation' THEN 'OP-'
                        WHEN 'paymentlog' THEN 'PAY-'
                        WHEN 'stocktake' THEN 'STK-'
                        WHEN 'supplyshipment' THEN 'SUP-'
                        WHEN 'merchant' THEN 'MER-'
                        WHEN 'exportlog' THEN 'EXP-'
                        ELSE 'REC-'
                    END || upper(replace(reference_id::text, '-', ''))
                WHERE reference_id IS NOT NULL;
                """);
            migrationBuilder.Sql("UPDATE notifications.notification_logs SET notification_number = 'NOT-' || upper(replace(id::text, '-', '')) WHERE notification_number IS NULL;");
            migrationBuilder.AlterColumn<string>(
                name: "notification_number",
                schema: "notifications",
                table: "notification_logs",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_notif_logs_notification_number",
                schema: "notifications",
                table: "notification_logs",
                column: "notification_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_notif_logs_notification_number",
                schema: "notifications",
                table: "notification_logs");

            migrationBuilder.DropColumn(
                name: "notification_number",
                schema: "notifications",
                table: "notification_logs");

            migrationBuilder.DropColumn(
                name: "reference_code",
                schema: "notifications",
                table: "notification_logs");

            migrationBuilder.DropColumn(
                name: "reference_context_json",
                schema: "notifications",
                table: "notification_logs");

            migrationBuilder.DropColumn(
                name: "reference_title",
                schema: "notifications",
                table: "notification_logs");
        }
    }
}
