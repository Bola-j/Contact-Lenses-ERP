using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Inventory.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryReceiptCommand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_receipt_commands",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    key = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    stock_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    response_batch_quantity = table.Column<int>(type: "integer", nullable: true),
                    response_status_code = table.Column<int>(type: "integer", nullable: true),
                    response_body = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("inventory_receipt_commands_pkey", x => x.id);
                    table.CheckConstraint("chk_inventory_receipt_command_status", "status in ('Pending','Completed')");
                    table.ForeignKey(
                        name: "FK_inventory_receipt_commands_inventory_batches_batch_id",
                        column: x => x.batch_id,
                        principalSchema: "inventory",
                        principalTable: "inventory_batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_receipt_commands_stock_transactions_stock_transac~",
                        column: x => x.stock_transaction_id,
                        principalSchema: "inventory",
                        principalTable: "stock_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_receipt_commands_batch_id",
                schema: "inventory",
                table: "inventory_receipt_commands",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "uq_inventory_receipt_commands_key",
                schema: "inventory",
                table: "inventory_receipt_commands",
                column: "key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_inventory_receipt_commands_stock_transaction",
                schema: "inventory",
                table: "inventory_receipt_commands",
                column: "stock_transaction_id",
                unique: true,
                filter: "(stock_transaction_id IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "inventory_receipt_commands",
                schema: "inventory");
        }
    }
}
