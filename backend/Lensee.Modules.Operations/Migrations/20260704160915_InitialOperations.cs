using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class InitialOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "operations");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:uuid-ossp", ",,");

            migrationBuilder.CreateSequence(
                name: "operation_number_seq",
                schema: "operations",
                startValue: 1000L);

            migrationBuilder.CreateTable(
                name: "stocktake_sessions",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    performed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    products_counted = table.Column<int>(type: "integer", nullable: true),
                    total_discrepancy_units = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValueSql: "'Draft'::character varying"),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    confirmed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("stocktake_sessions_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stocktake_adjustment_lines",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    system_qty_before = table.Column<int>(type: "integer", nullable: false),
                    physical_count = table.Column<int>(type: "integer", nullable: false),
                    delta = table.Column<int>(type: "integer", nullable: false),
                    line_note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("stocktake_adjustment_lines_pkey", x => x.id);
                    table.ForeignKey(
                        name: "stocktake_adjustment_lines_session_id_fkey",
                        column: x => x.session_id,
                        principalSchema: "operations",
                        principalTable: "stocktake_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_receipt_headers",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    receipt_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("inventory_receipt_headers_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "operation_lines",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name_snapshot = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    sku_code_snapshot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    merchant_name_snapshot = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    representative_name_snapshot = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    section = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValueSql: "'Standard'::character varying"),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    entry_mode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValueSql: "'Pieces'::character varying"),
                    bonus_quantity = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    unit_price = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    write_off_reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    write_off_reason_text = table.Column<string>(type: "text", nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    line_notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("operation_lines_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "operation_logs",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    operation_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValueSql: "('OP-'::text || to_char(nextval('operations.operation_number_seq'::regclass), 'FM000000'::text))"),
                    operation_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValueSql: "'Draft'::character varying"),
                    source_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    destination_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    representative_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    current_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    confirmed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("operation_logs_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "operation_versions",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    snapshot_data = table.Column<string>(type: "jsonb", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'Initial'::text"),
                    edited_by = table.Column<Guid>(type: "uuid", nullable: false),
                    edited_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("operation_versions_pkey", x => x.id);
                    table.ForeignKey(
                        name: "operation_versions_operation_id_fkey",
                        column: x => x.operation_id,
                        principalSchema: "operations",
                        principalTable: "operation_logs",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "idx_receipt_headers_operation",
                schema: "operations",
                table: "inventory_receipt_headers",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "inventory_receipt_headers_operation_id_key",
                schema: "operations",
                table: "inventory_receipt_headers",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_op_lines_operation",
                schema: "operations",
                table: "operation_lines",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "idx_op_lines_sku",
                schema: "operations",
                table: "operation_lines",
                column: "sku_id");

            migrationBuilder.CreateIndex(
                name: "idx_op_logs_client",
                schema: "operations",
                table: "operation_logs",
                column: "client_id",
                filter: "(client_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "idx_op_logs_created_at",
                schema: "operations",
                table: "operation_logs",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_op_logs_created_by",
                schema: "operations",
                table: "operation_logs",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "idx_op_logs_source_location",
                schema: "operations",
                table: "operation_logs",
                column: "source_location_id");

            migrationBuilder.CreateIndex(
                name: "idx_op_logs_type_status",
                schema: "operations",
                table: "operation_logs",
                columns: new[] { "operation_type", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_operation_logs_current_version_id",
                schema: "operations",
                table: "operation_logs",
                column: "current_version_id");

            migrationBuilder.CreateIndex(
                name: "operation_logs_operation_number_key",
                schema: "operations",
                table: "operation_logs",
                column: "operation_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_op_versions_operation",
                schema: "operations",
                table: "operation_versions",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "uq_op_version",
                schema: "operations",
                table: "operation_versions",
                columns: new[] { "operation_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_stocktake_adj_session",
                schema: "operations",
                table: "stocktake_adjustment_lines",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "idx_stocktake_location",
                schema: "operations",
                table: "stocktake_sessions",
                column: "location_id");

            migrationBuilder.AddForeignKey(
                name: "inventory_receipt_headers_operation_id_fkey",
                schema: "operations",
                table: "inventory_receipt_headers",
                column: "operation_id",
                principalSchema: "operations",
                principalTable: "operation_logs",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "operation_lines_operation_id_fkey",
                schema: "operations",
                table: "operation_lines",
                column: "operation_id",
                principalSchema: "operations",
                principalTable: "operation_logs",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "fk_current_version",
                schema: "operations",
                table: "operation_logs",
                column: "current_version_id",
                principalSchema: "operations",
                principalTable: "operation_versions",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "operation_versions_operation_id_fkey",
                schema: "operations",
                table: "operation_versions");

            migrationBuilder.DropTable(
                name: "inventory_receipt_headers",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "operation_lines",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "stocktake_adjustment_lines",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "stocktake_sessions",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "operation_logs",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "operation_versions",
                schema: "operations");

            migrationBuilder.DropSequence(
                name: "operation_number_seq",
                schema: "operations");
        }
    }
}
