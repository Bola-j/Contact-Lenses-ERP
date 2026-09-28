using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class SyncSupplyReceivingHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_supply_shipments_status",
                schema: "operations",
                table: "supply_shipments");

            migrationBuilder.AddColumn<Guid>(
                name: "supply_receiving_session_id",
                schema: "operations",
                table: "inventory_receipt_headers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "supply_receiving_sessions",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Draft"),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    inventory_receipt_operation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supply_receiving_sessions", x => x.id);
                    table.CheckConstraint("chk_supply_receiving_status", "status in ('Draft','Confirmed')");
                    table.ForeignKey(
                        name: "FK_supply_receiving_sessions_supply_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalSchema: "operations",
                        principalTable: "supply_shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supply_receiving_lines",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    receiving_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shipment_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_quantity = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supply_receiving_lines", x => x.id);
                    table.CheckConstraint("chk_supply_receiving_line_quantity", "received_quantity >= 0");
                    table.ForeignKey(
                        name: "FK_supply_receiving_lines_supply_receiving_sessions_receiving_~",
                        column: x => x.receiving_session_id,
                        principalSchema: "operations",
                        principalTable: "supply_receiving_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supply_receiving_lines_supply_shipment_lines_shipment_line_~",
                        column: x => x.shipment_line_id,
                        principalSchema: "operations",
                        principalTable: "supply_shipment_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "chk_supply_shipments_status",
                schema: "operations",
                table: "supply_shipments",
                sql: "status in ('Draft','Arrived','PartiallyReceived','Received','Cancelled')");

            migrationBuilder.CreateIndex(
                name: "ux_receipt_headers_supply_receiving_session",
                schema: "operations",
                table: "inventory_receipt_headers",
                column: "supply_receiving_session_id",
                unique: true,
                filter: "(supply_receiving_session_id is not null)");

            migrationBuilder.CreateIndex(
                name: "IX_supply_receiving_lines_receiving_session_id_shipment_line_id",
                schema: "operations",
                table: "supply_receiving_lines",
                columns: new[] { "receiving_session_id", "shipment_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supply_receiving_lines_shipment_line_id",
                schema: "operations",
                table: "supply_receiving_lines",
                column: "shipment_line_id");

            migrationBuilder.CreateIndex(
                name: "idx_supply_receiving_shipment_status",
                schema: "operations",
                table: "supply_receiving_sessions",
                columns: new[] { "shipment_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_supply_receiving_operation",
                schema: "operations",
                table: "supply_receiving_sessions",
                column: "inventory_receipt_operation_id",
                unique: true,
                filter: "(inventory_receipt_operation_id is not null)");

            migrationBuilder.Sql("""
                create or replace function operations.verify_supply_shipment_status_transition()
                returns trigger language plpgsql as $$
                begin
                    if new.status is not distinct from old.status then return new; end if;
                    if old.status = 'Draft' and new.status in ('Arrived', 'Cancelled') then return new; end if;
                    if old.status = 'Arrived' and new.status in ('PartiallyReceived', 'Received') then return new; end if;
                    if old.status = 'PartiallyReceived' and new.status in ('PartiallyReceived', 'Received') then return new; end if;
                    raise exception 'Invalid supply shipment status transition from % to %', old.status, new.status;
                end $$;
                create trigger trg_supply_shipment_status_transition
                before update of status on operations.supply_shipments
                for each row execute function operations.verify_supply_shipment_status_transition();

                create or replace function operations.verify_supply_receiving_confirmation()
                returns trigger language plpgsql as $$
                begin
                    if new.status <> 'Confirmed' or old.status = 'Confirmed' then return new; end if;
                    perform 1 from operations.supply_shipment_lines line
                    join operations.supply_receiving_lines receipt on receipt.shipment_line_id = line.id
                    where receipt.receiving_session_id = new.id
                    for update of line;
                    if not exists (select 1 from operations.supply_receiving_lines where receiving_session_id = new.id and received_quantity > 0) then
                        raise exception 'A confirmed supply receiving session must contain a positive quantity';
                    end if;
                    if exists (
                        select 1
                        from operations.supply_receiving_lines receipt
                        join operations.supply_receiving_sessions session on session.id = receipt.receiving_session_id
                        join operations.supply_shipment_lines line on line.id = receipt.shipment_line_id
                        where session.shipment_id = new.shipment_id
                          and (session.status = 'Confirmed' or session.id = new.id)
                        group by receipt.shipment_line_id, line.shipment_id, line.quantity
                        having line.shipment_id <> new.shipment_id or sum(receipt.received_quantity) > line.quantity
                    ) then
                        raise exception 'Supply receipt exceeds its shipment-line outstanding quantity';
                    end if;
                    if exists (
                        select 1 from operations.supply_receiving_lines receipt
                        join operations.supply_shipment_lines line on line.id = receipt.shipment_line_id
                        where receipt.receiving_session_id = new.id and line.shipment_id <> new.shipment_id
                    ) then
                        raise exception 'Supply receiving lines must belong to the session shipment';
                    end if;
                    return new;
                end $$;
                create trigger trg_supply_receiving_confirmation
                before update of status on operations.supply_receiving_sessions
                for each row execute function operations.verify_supply_receiving_confirmation();

                create or replace function operations.prevent_confirmed_supply_receiving_line_change()
                returns trigger language plpgsql as $$
                declare session_status text;
                begin
                    if tg_op = 'DELETE' then
                        select status into session_status from operations.supply_receiving_sessions
                        where id = old.receiving_session_id;
                    else
                        select status into session_status from operations.supply_receiving_sessions
                        where id = new.receiving_session_id;
                    end if;
                    if session_status = 'Confirmed' then raise exception 'Confirmed supply receiving lines are immutable'; end if;
                    if tg_op = 'DELETE' then return old; end if;
                    return new;
                end $$;
                create trigger trg_supply_receiving_line_immutable
                before insert or update or delete on operations.supply_receiving_lines
                for each row execute function operations.prevent_confirmed_supply_receiving_line_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop trigger if exists trg_supply_receiving_line_immutable on operations.supply_receiving_lines;
                drop function if exists operations.prevent_confirmed_supply_receiving_line_change();
                drop trigger if exists trg_supply_receiving_confirmation on operations.supply_receiving_sessions;
                drop function if exists operations.verify_supply_receiving_confirmation();
                drop trigger if exists trg_supply_shipment_status_transition on operations.supply_shipments;
                drop function if exists operations.verify_supply_shipment_status_transition();
                """);
            migrationBuilder.DropTable(
                name: "supply_receiving_lines",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "supply_receiving_sessions",
                schema: "operations");

            migrationBuilder.DropCheckConstraint(
                name: "chk_supply_shipments_status",
                schema: "operations",
                table: "supply_shipments");

            migrationBuilder.DropIndex(
                name: "ux_receipt_headers_supply_receiving_session",
                schema: "operations",
                table: "inventory_receipt_headers");

            migrationBuilder.DropColumn(
                name: "supply_receiving_session_id",
                schema: "operations",
                table: "inventory_receipt_headers");

            migrationBuilder.AddCheckConstraint(
                name: "chk_supply_shipments_status",
                schema: "operations",
                table: "supply_shipments",
                sql: "status in ('Draft','Arrived','Received','Cancelled')");
        }
    }
}
