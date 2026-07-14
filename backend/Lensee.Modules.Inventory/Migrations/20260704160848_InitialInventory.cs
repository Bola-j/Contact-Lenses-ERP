using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Inventory.Migrations
{
    /// <inheritdoc />
    public partial class InitialInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:uuid-ossp", ",,");

            migrationBuilder.CreateTable(
                name: "locations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    location_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("locations_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_batches",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_from = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("inventory_batches_pkey", x => x.id);
                    table.ForeignKey(
                        name: "inventory_batches_location_id_fkey",
                        column: x => x.location_id,
                        principalSchema: "inventory",
                        principalTable: "locations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "opened_piece_lots",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    batch_expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    opened_date = table.Column<DateOnly>(type: "date", nullable: false),
                    piece_expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    loose_piece_quantity = table.Column<int>(type: "integer", nullable: false),
                    created_from = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("opened_piece_lots_pkey", x => x.id);
                    table.ForeignKey(
                        name: "opened_piece_lots_location_id_fkey",
                        column: x => x.location_id,
                        principalSchema: "inventory",
                        principalTable: "locations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "stock_balances",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    available_qty = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    reserved_in_warehouse_qty = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    reserved_with_rep_qty = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    target_qty = table.Column<int>(type: "integer", nullable: true),
                    row_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_updated = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("stock_balances_pkey", x => x.id);
                    table.ForeignKey(
                        name: "stock_balances_location_id_fkey",
                        column: x => x.location_id,
                        principalSchema: "inventory",
                        principalTable: "locations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "stock_transactions",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    quantity_change = table.Column<int>(type: "integer", nullable: false),
                    reference_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("stock_transactions_pkey", x => x.id);
                    table.ForeignKey(
                        name: "stock_transactions_location_id_fkey",
                        column: x => x.location_id,
                        principalSchema: "inventory",
                        principalTable: "locations",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "idx_inv_batches_expiry",
                schema: "inventory",
                table: "inventory_batches",
                column: "expiry_date",
                filter: "(expiry_date IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "idx_inv_batches_location_sku",
                schema: "inventory",
                table: "inventory_batches",
                columns: new[] { "location_id", "sku_id" });

            migrationBuilder.CreateIndex(
                name: "idx_opened_piece_lots_fefo",
                schema: "inventory",
                table: "opened_piece_lots",
                columns: new[] { "location_id", "sku_id", "piece_expiry_date" });

            migrationBuilder.CreateIndex(
                name: "idx_stock_balances_available",
                schema: "inventory",
                table: "stock_balances",
                columns: new[] { "location_id", "available_qty" });

            migrationBuilder.CreateIndex(
                name: "idx_stock_balances_sku",
                schema: "inventory",
                table: "stock_balances",
                column: "sku_id");

            migrationBuilder.CreateIndex(
                name: "uq_location_sku",
                schema: "inventory",
                table: "stock_balances",
                columns: new[] { "location_id", "sku_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_stock_txn_operation",
                schema: "inventory",
                table: "stock_transactions",
                column: "reference_operation_id",
                filter: "(reference_operation_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "idx_stock_txn_sku_location",
                schema: "inventory",
                table: "stock_transactions",
                columns: new[] { "sku_id", "location_id", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "idx_stock_txn_user",
                schema: "inventory",
                table: "stock_transactions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_transactions_location_id",
                schema: "inventory",
                table: "stock_transactions",
                column: "location_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_batches",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "opened_piece_lots",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_balances",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_transactions",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "locations",
                schema: "inventory");
        }
    }
}
