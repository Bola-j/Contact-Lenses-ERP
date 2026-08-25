using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Lensee.SharedKernel.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Lensee.SharedKernel.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(SharedDbContext))]
    [Migration("20260820090000_AddOutboxMessages")]
    public partial class AddOutboxMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "shared",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    next_attempt_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("outbox_messages_pkey", x => x.id);
                    table.CheckConstraint("chk_outbox_attempts", "attempts >= 0");
                    table.CheckConstraint("chk_outbox_status", "status in ('Pending','Processing','Processed','Failed','DeadLetter')");
                });

            migrationBuilder.CreateTable(
                name: "outbox_delivery_receipts",
                schema: "shared",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    outbox_message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handler_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    processed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("outbox_delivery_receipts_pkey", x => x.id);
                    table.ForeignKey(
                        name: "outbox_delivery_receipts_outbox_message_id_fkey",
                        column: x => x.outbox_message_id,
                        principalSchema: "shared",
                        principalTable: "outbox_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_outbox_messages_occurred_at",
                schema: "shared",
                table: "outbox_messages",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "idx_outbox_messages_ready",
                schema: "shared",
                table: "outbox_messages",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "uq_outbox_delivery_receipts_message_handler",
                schema: "shared",
                table: "outbox_delivery_receipts",
                columns: new[] { "outbox_message_id", "handler_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_delivery_receipts",
                schema: "shared");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "shared");
        }
    }
}
