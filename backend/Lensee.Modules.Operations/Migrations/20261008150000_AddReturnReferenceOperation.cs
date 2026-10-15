using System;
using Lensee.Modules.Operations.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    [DbContext(typeof(OperationsDbContext))]
    [Migration("20261008150000_AddReturnReferenceOperation")]
    public partial class AddReturnReferenceOperation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "return_reference_operation_id",
                schema: "operations",
                table: "operation_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_op_logs_return_reference",
                schema: "operations",
                table: "operation_logs",
                column: "return_reference_operation_id");

            migrationBuilder.AddForeignKey(
                name: "operation_logs_return_reference_operation_id_fkey",
                schema: "operations",
                table: "operation_logs",
                column: "return_reference_operation_id",
                principalSchema: "operations",
                principalTable: "operation_logs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "operation_logs_return_reference_operation_id_fkey",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropIndex(
                name: "idx_op_logs_return_reference",
                schema: "operations",
                table: "operation_logs");

            migrationBuilder.DropColumn(
                name: "return_reference_operation_id",
                schema: "operations",
                table: "operation_logs");
        }
    }
}
