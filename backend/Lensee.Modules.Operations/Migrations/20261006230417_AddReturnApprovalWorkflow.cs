using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnApprovalWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_op_status",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.AddColumn<string>(
                name: "return_approval_reason",
                schema: "operations",
                table: "operation_logs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "return_approved_at",
                schema: "operations",
                table: "operation_logs",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "return_approved_by",
                schema: "operations",
                table: "operation_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "return_rejected_at",
                schema: "operations",
                table: "operation_logs",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "return_rejected_by",
                schema: "operations",
                table: "operation_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "return_approval_lines",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_quantity = table.Column<int>(type: "integer", nullable: false),
                    eligible_quantity = table.Column<int>(type: "integer", nullable: true),
                    batch_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    expiry_date = table.Column<DateOnly>(type: "date", nullable: true),
                    mismatch_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("return_approval_lines_pkey", x => x.id);
                    table.ForeignKey(
                        name: "FK_return_approval_lines_operation_lines_operation_line_id",
                        column: x => x.operation_line_id,
                        principalSchema: "operations",
                        principalTable: "operation_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_return_approval_lines_operation_logs_operation_id",
                        column: x => x.operation_id,
                        principalSchema: "operations",
                        principalTable: "operation_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "chk_op_status",
                schema: "operations",
                table: "operation_logs",
                sql: "status in ('Draft','PendingApproval','Approved','Confirmed','Completed','Reserved','Shipped','Received','Cancelled','Rejected','Dispatched','Delivered','FailedDelivery')");

            migrationBuilder.CreateIndex(
                name: "idx_return_approval_lines_operation",
                schema: "operations",
                table: "return_approval_lines",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "IX_return_approval_lines_operation_line_id",
                schema: "operations",
                table: "return_approval_lines",
                column: "operation_line_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "return_approval_lines",
                schema: "operations");

            migrationBuilder.DropCheckConstraint(
                name: "chk_op_status",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "return_approval_reason",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "return_approved_at",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "return_approved_by",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "return_rejected_at",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "return_rejected_by",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.AddCheckConstraint(
                name: "chk_op_status",
                schema: "operations",
                table: "operation_logs",
                sql: "status in ('Draft','Confirmed','Completed','Reserved','Shipped','Received','Cancelled','Dispatched','Delivered','FailedDelivery')");
        }
    }
}
