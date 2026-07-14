using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class InitialNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:uuid-ossp", ",,");

            migrationBuilder.CreateTable(
                name: "alert_configs",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    alert_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    threshold_value = table.Column<int>(type: "integer", nullable: true),
                    threshold_unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("alert_configs_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_logs",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    alert_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reference_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValueSql: "'InApp'::character varying"),
                    is_read = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("notification_logs_pkey", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_notif_logs_created_at",
                schema: "notifications",
                table: "notification_logs",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_notif_logs_user_unread",
                schema: "notifications",
                table: "notification_logs",
                columns: new[] { "target_user_id", "is_read" },
                filter: "(is_read = false)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_configs",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_logs",
                schema: "notifications");
        }
    }
}
