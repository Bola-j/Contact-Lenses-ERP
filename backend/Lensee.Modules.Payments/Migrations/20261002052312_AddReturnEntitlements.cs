using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "return_entitlements",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    return_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_sale_operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    beneficiary_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    buyer_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    payment_track = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    original_payment_reference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    normal_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    exception_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    reserved_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    settled_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, defaultValue: 0m),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Held"),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_entitlements", x => x.id);
                    table.CheckConstraint("chk_return_entitlement_amounts", "normal_amount >= 0 and exception_amount >= 0 and reserved_amount >= 0 and settled_amount >= 0 and reserved_amount + settled_amount <= normal_amount + exception_amount");
                    table.CheckConstraint("chk_return_entitlement_beneficiary", "beneficiary_kind in ('Merchant','Anonymous')");
                    table.CheckConstraint("chk_return_entitlement_status", "status in ('PendingValuation','Held','Reserved','Settled','Cancelled')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_return_entitlements_idempotency_key",
                schema: "payments",
                table: "return_entitlements",
                column: "idempotency_key",
                unique: true,
                filter: "(idempotency_key IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_return_entitlements_merchant_id_status",
                schema: "payments",
                table: "return_entitlements",
                columns: new[] { "merchant_id", "status" },
                filter: "(merchant_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_return_entitlements_return_operation_id",
                schema: "payments",
                table: "return_entitlements",
                column: "return_operation_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "return_entitlements",
                schema: "payments");

        }
    }
}
