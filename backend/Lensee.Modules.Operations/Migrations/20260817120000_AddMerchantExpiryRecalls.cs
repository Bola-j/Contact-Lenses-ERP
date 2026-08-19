using System;
using Lensee.Modules.Operations.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations;

[DbContext(typeof(OperationsDbContext))]
[Migration("20260817120000_AddMerchantExpiryRecalls")]
public partial class AddMerchantExpiryRecalls : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "merchant_expiry_recalls",
            schema: "operations",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                merchant_id = table.Column<Guid>(type: "uuid", nullable: false),
                sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                lot_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, defaultValue: ""),
                expiry_date = table.Column<DateOnly>(type: "date", nullable: false),
                status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Active"),
                sold_quantity = table.Column<int>(type: "integer", nullable: false),
                returned_quantity = table.Column<int>(type: "integer", nullable: false),
                resolved_sold_quantity = table.Column<int>(type: "integer", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                resolved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                resolution_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("merchant_expiry_recalls_pkey", x => x.id);
                table.CheckConstraint("chk_merchant_expiry_recall_quantities", "sold_quantity >= 0 and returned_quantity >= 0");
                table.CheckConstraint("chk_merchant_expiry_recall_status", "status in ('Active','Completed','NoStock')");
            });

        migrationBuilder.CreateIndex(
            name: "idx_merchant_expiry_recall_merchant",
            schema: "operations",
            table: "merchant_expiry_recalls",
            column: "merchant_id");

        migrationBuilder.CreateIndex(
            name: "idx_merchant_expiry_recall_status_expiry",
            schema: "operations",
            table: "merchant_expiry_recalls",
            columns: new[] { "status", "expiry_date" });

        migrationBuilder.CreateIndex(
            name: "uq_merchant_expiry_recall_batch",
            schema: "operations",
            table: "merchant_expiry_recalls",
            columns: new[] { "merchant_id", "sku_id", "lot_number", "expiry_date" },
            unique: true);

        migrationBuilder.AddColumn<Guid>(
            name: "merchant_expiry_recall_id",
            schema: "operations",
            table: "operation_logs",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "idx_op_logs_merchant_expiry_recall",
            schema: "operations",
            table: "operation_logs",
            column: "merchant_expiry_recall_id");

        migrationBuilder.AddForeignKey(
            name: "operation_logs_merchant_expiry_recall_id_fkey",
            schema: "operations",
            table: "operation_logs",
            column: "merchant_expiry_recall_id",
            principalSchema: "operations",
            principalTable: "merchant_expiry_recalls",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "operation_logs_merchant_expiry_recall_id_fkey",
            schema: "operations",
            table: "operation_logs");

        migrationBuilder.DropIndex(
            name: "idx_op_logs_merchant_expiry_recall",
            schema: "operations",
            table: "operation_logs");

        migrationBuilder.DropColumn(
            name: "merchant_expiry_recall_id",
            schema: "operations",
            table: "operation_logs");

        migrationBuilder.DropTable(
            name: "merchant_expiry_recalls",
            schema: "operations");
    }
}
