using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplyReceivingLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "inventory_receipt_operation_id",
                schema: "operations",
                table: "stocktake_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "purpose",
                schema: "operations",
                table: "stocktake_sessions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "CycleCount");

            migrationBuilder.AddColumn<Guid>(
                name: "supply_finance_log_id",
                schema: "operations",
                table: "stocktake_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "supply_shipment_id",
                schema: "operations",
                table: "stocktake_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "stocktake_session_id",
                schema: "operations",
                table: "inventory_receipt_headers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "supply_finance_log_id",
                schema: "operations",
                table: "inventory_receipt_headers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "supply_shipment_id",
                schema: "operations",
                table: "inventory_receipt_headers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_stocktake_supply_shipment",
                schema: "operations",
                table: "stocktake_sessions",
                column: "supply_shipment_id");

            migrationBuilder.AddCheckConstraint(
                name: "chk_stocktake_purpose",
                schema: "operations",
                table: "stocktake_sessions",
                sql: "purpose in ('CycleCount','SupplyReceiving')");

            migrationBuilder.CreateIndex(
                name: "idx_receipt_headers_supply_shipment",
                schema: "operations",
                table: "inventory_receipt_headers",
                column: "supply_shipment_id");

            migrationBuilder.CreateIndex(
                name: "ux_receipt_headers_stocktake_session",
                schema: "operations",
                table: "inventory_receipt_headers",
                column: "stocktake_session_id",
                unique: true,
                filter: "(stocktake_session_id is not null)");

            migrationBuilder.Sql(@"update operations.inventory_receipt_headers h
                set supply_shipment_id = s.id, supply_finance_log_id = f.""Id""
                from operations.supply_shipments s left join finance.supply_finance_logs f on f.""SupplyShipmentId"" = s.id
                where s.inventory_receipt_operation_id = h.operation_id and h.supply_shipment_id is null;");
        }
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "idx_stocktake_supply_shipment",
                schema: "operations",
                table: "stocktake_sessions");

            migrationBuilder.DropCheckConstraint(
                name: "chk_stocktake_purpose",
                schema: "operations",
                table: "stocktake_sessions");

            migrationBuilder.DropIndex(
                name: "idx_receipt_headers_supply_shipment",
                schema: "operations",
                table: "inventory_receipt_headers");

            migrationBuilder.DropIndex(
                name: "ux_receipt_headers_stocktake_session",
                schema: "operations",
                table: "inventory_receipt_headers");

            migrationBuilder.DropColumn(
                name: "inventory_receipt_operation_id",
                schema: "operations",
                table: "stocktake_sessions");

            migrationBuilder.DropColumn(
                name: "purpose",
                schema: "operations",
                table: "stocktake_sessions");

            migrationBuilder.DropColumn(
                name: "supply_finance_log_id",
                schema: "operations",
                table: "stocktake_sessions");

            migrationBuilder.DropColumn(
                name: "supply_shipment_id",
                schema: "operations",
                table: "stocktake_sessions");

            migrationBuilder.DropColumn(
                name: "stocktake_session_id",
                schema: "operations",
                table: "inventory_receipt_headers");

            migrationBuilder.DropColumn(
                name: "supply_finance_log_id",
                schema: "operations",
                table: "inventory_receipt_headers");

            migrationBuilder.DropColumn(
                name: "supply_shipment_id",
                schema: "operations",
                table: "inventory_receipt_headers");
        }
    }
}


