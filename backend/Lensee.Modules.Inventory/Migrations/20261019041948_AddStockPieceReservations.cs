using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Inventory.Migrations
{
    /// <inheritdoc />
    public partial class AddStockPieceReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stock_piece_reservations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_piece_quantity = table.Column<int>(type: "integer", nullable: false),
                    pieces_per_pack_snapshot = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    released_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("stock_piece_reservations_pkey", x => x.id);
                    table.CheckConstraint("chk_stock_piece_reservation_quantity", "requested_piece_quantity > 0 and pieces_per_pack_snapshot > 0");
                    table.CheckConstraint("chk_stock_piece_reservation_status", "status in ('Active','Consumed','Released')");
                    table.CheckConstraint("chk_stock_piece_reservation_terminal", "(status = 'Active' and consumed_at is null and released_at is null) or (status = 'Consumed' and consumed_at is not null and released_at is null) or (status = 'Released' and consumed_at is null and released_at is not null)");
                });

            migrationBuilder.CreateTable(
                name: "stock_piece_reservation_allocations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_opened_piece_lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    piece_quantity = table.Column<int>(type: "integer", nullable: false),
                    physical_packs_held = table.Column<int>(type: "integer", nullable: false),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("stock_piece_reservation_allocations_pkey", x => x.id);
                    table.CheckConstraint("chk_stock_piece_reservation_allocation_quantities", "piece_quantity > 0 and physical_packs_held >= 0");
                    table.CheckConstraint("chk_stock_piece_reservation_allocation_source", "(source_kind = 'OpenedPieceLot' and source_opened_piece_lot_id is not null and physical_packs_held = 0) or (source_kind = 'SealedBatch' and source_opened_piece_lot_id is null and physical_packs_held > 0)");
                    table.ForeignKey(
                        name: "stock_piece_reservation_allocations_reservation_id_fkey",
                        column: x => x.reservation_id,
                        principalSchema: "inventory",
                        principalTable: "stock_piece_reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "stock_piece_reservation_allocations_source_batch_id_fkey",
                        column: x => x.source_batch_id,
                        principalSchema: "inventory",
                        principalTable: "inventory_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "stock_piece_reservation_allocations_source_opened_piece_lot_id_fkey",
                        column: x => x.source_opened_piece_lot_id,
                        principalSchema: "inventory",
                        principalTable: "opened_piece_lots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_stock_piece_reservation_allocations_batch",
                schema: "inventory",
                table: "stock_piece_reservation_allocations",
                column: "source_batch_id");

            migrationBuilder.CreateIndex(
                name: "idx_stock_piece_reservation_allocations_opened_lot",
                schema: "inventory",
                table: "stock_piece_reservation_allocations",
                column: "source_opened_piece_lot_id",
                filter: "(source_opened_piece_lot_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "idx_stock_piece_reservation_allocations_reservation_source",
                schema: "inventory",
                table: "stock_piece_reservation_allocations",
                columns: new[] { "reservation_id", "source_kind", "source_batch_id" });

            migrationBuilder.CreateIndex(
                name: "idx_stock_piece_reservations_stock_status",
                schema: "inventory",
                table: "stock_piece_reservations",
                columns: new[] { "location_id", "sku_id", "status" });

            migrationBuilder.CreateIndex(
                name: "uq_stock_piece_reservations_operation_line",
                schema: "inventory",
                table: "stock_piece_reservations",
                column: "operation_line_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_piece_reservation_allocations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_piece_reservations",
                schema: "inventory");

        }
    }
}
