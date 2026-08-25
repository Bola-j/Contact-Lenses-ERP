using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Lensee.SharedKernel.Data;

#nullable disable

namespace Lensee.SharedKernel.Migrations;

/// <inheritdoc />
[DbContext(typeof(SharedDbContext))]
[Migration("20260825091000_AddOutboxContractMetadata")]
public partial class AddOutboxContractMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "event_version", schema: "shared", table: "outbox_messages", type: "integer", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<string>(name: "correlation_id", schema: "shared", table: "outbox_messages", type: "character varying(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<string>(name: "causation_id", schema: "shared", table: "outbox_messages", type: "character varying(128)", maxLength: 128, nullable: true);
        migrationBuilder.CreateIndex(name: "idx_outbox_messages_correlation", schema: "shared", table: "outbox_messages", column: "correlation_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "idx_outbox_messages_correlation", schema: "shared", table: "outbox_messages");
        migrationBuilder.DropColumn(name: "event_version", schema: "shared", table: "outbox_messages");
        migrationBuilder.DropColumn(name: "correlation_id", schema: "shared", table: "outbox_messages");
        migrationBuilder.DropColumn(name: "causation_id", schema: "shared", table: "outbox_messages");
    }
}
