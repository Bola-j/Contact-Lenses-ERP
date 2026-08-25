using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplyShipments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supply_shipments",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    shipment_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    shipment_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    destination_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValueSql: "'Draft'::character varying"),
                    notes = table.Column<string>(type: "text", nullable: true),
                    product_subtotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    cost_subtotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    landed_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    inventory_receipt_operation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("supply_shipments_pkey", x => x.id);
                    table.CheckConstraint("chk_supply_shipments_cost_subtotal", "cost_subtotal >= 0");
                    table.CheckConstraint("chk_supply_shipments_landed_total", "landed_total >= 0");
                    table.CheckConstraint("chk_supply_shipments_product_subtotal", "product_subtotal >= 0");
                    table.CheckConstraint("chk_supply_shipments_status", "status in ('Draft','Received','Cancelled')");
                    table.ForeignKey(
                        name: "supply_shipments_inventory_receipt_operation_id_fkey",
                        column: x => x.inventory_receipt_operation_id,
                        principalSchema: "operations",
                        principalTable: "operation_logs",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "supply_shipment_costs",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cost_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("supply_shipment_costs_pkey", x => x.id);
                    table.CheckConstraint("chk_supply_costs_amount", "amount >= 0");
                    table.ForeignKey(
                        name: "supply_shipment_costs_shipment_id_fkey",
                        column: x => x.shipment_id,
                        principalSchema: "operations",
                        principalTable: "supply_shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supply_shipment_history",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    summary = table.Column<string>(type: "text", nullable: true),
                    snapshot_data = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("supply_shipment_history_pkey", x => x.id);
                    table.ForeignKey(
                        name: "supply_shipment_history_shipment_id_fkey",
                        column: x => x.shipment_id,
                        principalSchema: "operations",
                        principalTable: "supply_shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "supply_shipment_lines",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name_snapshot = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    sku_code_snapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    line_subtotal = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    allocated_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    landed_unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("supply_shipment_lines_pkey", x => x.id);
                    table.CheckConstraint("chk_supply_lines_allocated_cost", "allocated_cost >= 0");
                    table.CheckConstraint("chk_supply_lines_landed_unit_cost", "landed_unit_cost >= 0");
                    table.CheckConstraint("chk_supply_lines_line_subtotal", "line_subtotal >= 0");
                    table.CheckConstraint("chk_supply_lines_quantity", "quantity > 0");
                    table.CheckConstraint("chk_supply_lines_unit_price", "unit_price is null or unit_price >= 0");
                    table.ForeignKey(
                        name: "supply_shipment_lines_shipment_id_fkey",
                        column: x => x.shipment_id,
                        principalSchema: "operations",
                        principalTable: "supply_shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_supply_costs_shipment",
                schema: "operations",
                table: "supply_shipment_costs",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "idx_supply_history_shipment_created",
                schema: "operations",
                table: "supply_shipment_history",
                columns: new[] { "shipment_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_supply_lines_shipment",
                schema: "operations",
                table: "supply_shipment_lines",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "idx_supply_lines_sku",
                schema: "operations",
                table: "supply_shipment_lines",
                column: "sku_id");

            migrationBuilder.CreateIndex(
                name: "idx_supply_shipments_created_at",
                schema: "operations",
                table: "supply_shipments",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_supply_shipments_destination",
                schema: "operations",
                table: "supply_shipments",
                column: "destination_location_id");

            migrationBuilder.CreateIndex(
                name: "idx_supply_shipments_operation",
                schema: "operations",
                table: "supply_shipments",
                column: "inventory_receipt_operation_id");

            migrationBuilder.CreateIndex(
                name: "idx_supply_shipments_status",
                schema: "operations",
                table: "supply_shipments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "supply_shipments_shipment_number_key",
                schema: "operations",
                table: "supply_shipments",
                column: "shipment_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supply_shipment_costs",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "supply_shipment_history",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "supply_shipment_lines",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "supply_shipments",
                schema: "operations");
        }
    }
}
