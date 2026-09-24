using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationLineSourceAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "operation_line_source_allocations",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    target_operation_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_operation_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_opened_piece_lot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operation_line_source_allocations", x => x.id);
                    table.CheckConstraint("chk_operation_source_allocation_quantity", "quantity > 0");
                    table.CheckConstraint("chk_operation_source_allocation_entry_mode", "entry_mode in ('Packs','Pieces')");
                    table.CheckConstraint("chk_operation_source_allocation_piece_lot", "entry_mode = 'Packs' or source_opened_piece_lot_id is not null");
                    table.ForeignKey(
                        name: "FK_operation_source_allocations_target_line",
                        column: x => x.target_operation_line_id,
                        principalSchema: "operations",
                        principalTable: "operation_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_operation_source_allocations_source_operation",
                        column: x => x.source_operation_id,
                        principalSchema: "operations",
                        principalTable: "operation_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_operation_source_allocations_source_line",
                        column: x => x.source_operation_line_id,
                        principalSchema: "operations",
                        principalTable: "operation_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_operation_line_source_allocations_target_operation_line_id",
                schema: "operations",
                table: "operation_line_source_allocations",
                column: "target_operation_line_id",
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_operation_line_source_allocations_source_operation_line_id_entry_mode",
                schema: "operations",
                table: "operation_line_source_allocations",
                columns: new[] { "source_operation_line_id", "entry_mode" });
            migrationBuilder.CreateIndex(
                name: "IX_operation_line_source_allocations_source_operation_id_sku_id_lot_number_expiry_date",
                schema: "operations",
                table: "operation_line_source_allocations",
                columns: new[] { "source_operation_id", "sku_id", "lot_number", "expiry_date" });

            migrationBuilder.Sql("""
                ALTER TABLE operations.operation_line_source_allocations
                ADD CONSTRAINT fk_operation_source_allocations_source_batch
                FOREIGN KEY (source_batch_id)
                REFERENCES inventory.inventory_batches (id)
                ON DELETE RESTRICT;
                ALTER TABLE operations.operation_line_source_allocations
                ADD CONSTRAINT fk_operation_source_allocations_source_piece_lot
                FOREIGN KEY (source_opened_piece_lot_id)
                REFERENCES inventory.opened_piece_lots (id)
                ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "operation_line_source_allocations",
                schema: "operations");
        }
    }
}
