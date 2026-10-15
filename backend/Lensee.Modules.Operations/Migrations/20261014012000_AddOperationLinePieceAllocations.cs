using Lensee.Modules.Operations.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations;

[DbContext(typeof(OperationsDbContext))]
[Migration("20261014012000_AddOperationLinePieceAllocations")]
public partial class AddOperationLinePieceAllocations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "operation_line_piece_allocations", schema: "operations",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false, defaultValueSql: "uuid_generate_v4()"),
                operation_line_id = table.Column<Guid>(nullable: false),
                opened_piece_lot_id = table.Column<Guid>(nullable: false),
                source_batch_id = table.Column<Guid>(nullable: true),
                issued_piece_quantity = table.Column<int>(nullable: false),
                pieces_per_pack_snapshot = table.Column<int>(nullable: false),
                piece_issue_transaction_id = table.Column<Guid>(nullable: false),
                pack_open_transaction_id = table.Column<Guid>(nullable: true),
                lot_number = table.Column<string>(maxLength: 100, nullable: true),
                expiry_date = table.Column<DateOnly>(nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            }, constraints: table =>
            {
                table.PrimaryKey("pk_operation_line_piece_allocations", value => value.id);
                table.ForeignKey(
                    name: "fk_operation_line_piece_allocations_operation_lines_operation_line_id",
                    column: value => value.operation_line_id,
                    principalSchema: "operations",
                    principalTable: "operation_lines",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.CheckConstraint("chk_operation_piece_allocation_quantity", "issued_piece_quantity > 0");
                table.CheckConstraint("chk_operation_piece_allocation_pack_size", "pieces_per_pack_snapshot > 0");
            });
        migrationBuilder.CreateIndex(name: "ux_operation_line_piece_allocation_issue", schema: "operations", table: "operation_line_piece_allocations", columns: new[] { "operation_line_id", "opened_piece_lot_id", "piece_issue_transaction_id" }, unique: true);
        migrationBuilder.CreateIndex(
            name: "ix_operation_line_piece_allocations_source_batch_id",
            schema: "operations",
            table: "operation_line_piece_allocations",
            column: "source_batch_id");
        migrationBuilder.CreateIndex(
            name: "ix_operation_line_piece_allocations_piece_issue_transaction_id",
            schema: "operations",
            table: "operation_line_piece_allocations",
            column: "piece_issue_transaction_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("operation_line_piece_allocations", "operations");
}
