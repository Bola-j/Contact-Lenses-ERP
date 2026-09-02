using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Notifications.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleNotificationReadIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "idx_notif_logs_role_unread_created",
                schema: "notifications",
                table: "notification_logs",
                columns: new[] { "target_role", "is_read", "created_at" },
                descending: new[] { false, false, true },
                filter: "(target_role IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_notif_logs_role_unread_created",
                schema: "notifications",
                table: "notification_logs");
        }
    }
}
