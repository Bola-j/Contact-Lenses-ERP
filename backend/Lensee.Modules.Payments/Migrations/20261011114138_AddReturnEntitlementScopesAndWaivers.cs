using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnEntitlementScopesAndWaivers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "merchant_account_settled_amount",
                schema: "payments",
                table: "return_entitlements",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "other_payments_settled_amount",
                schema: "payments",
                table: "return_entitlements",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "reserved_scope",
                schema: "payments",
                table: "return_entitlements",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "scope",
                schema: "payments",
                table: "return_entitlement_valuations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "return_settlement_scope",
                schema: "payments",
                table: "cash_records",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "return_entitlement_waivers",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    return_entitlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "text", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_entitlement_waivers", x => x.id);
                    table.CheckConstraint("chk_return_entitlement_waiver_amount", "amount > 0");
                    table.CheckConstraint("chk_return_entitlement_waiver_scope", "scope in ('SourceOperation','MerchantAccount','OtherPayments')");
                    table.CheckConstraint("chk_return_entitlement_waiver_status", "status in ('PendingReview','Approved','Rejected')");
                    table.ForeignKey(
                        name: "FK_return_entitlement_waivers_return_entitlements_return_entit~",
                        column: x => x.return_entitlement_id,
                        principalSchema: "payments",
                        principalTable: "return_entitlements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_return_entitlement_waivers_return_entitlement_id_idempotenc~",
                schema: "payments",
                table: "return_entitlement_waivers",
                columns: new[] { "return_entitlement_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key is not null");

            migrationBuilder.CreateIndex(
                name: "IX_return_entitlement_waivers_return_entitlement_id_scope_stat~",
                schema: "payments",
                table: "return_entitlement_waivers",
                columns: new[] { "return_entitlement_id", "scope", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "return_entitlement_waivers",
                schema: "payments");

            migrationBuilder.DropColumn(
                name: "merchant_account_settled_amount",
                schema: "payments",
                table: "return_entitlements");

            migrationBuilder.DropColumn(
                name: "other_payments_settled_amount",
                schema: "payments",
                table: "return_entitlements");

            migrationBuilder.DropColumn(
                name: "reserved_scope",
                schema: "payments",
                table: "return_entitlements");

            migrationBuilder.DropColumn(
                name: "scope",
                schema: "payments",
                table: "return_entitlement_valuations");

            migrationBuilder.DropColumn(
                name: "return_settlement_scope",
                schema: "payments",
                table: "cash_records");
        }
    }
}
