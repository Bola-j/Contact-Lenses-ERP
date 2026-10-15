using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class AddGovernedReturnSettlementAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "account_level_settled_amount",
                schema: "payments",
                table: "return_entitlements",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "source_settled_amount",
                schema: "payments",
                table: "return_entitlements",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "return_settlement_scope",
                schema: "payments",
                table: "financial_adjustments",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "transfer_fee_amount",
                schema: "payments",
                table: "cash_records",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "return_entitlement_valuations",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    return_entitlement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    principal_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valued_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_return_entitlement_valuations", x => x.id);
                    table.CheckConstraint("chk_return_entitlement_valuation_amount", "amount > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_return_entitlement_valuations_ReturnEntitlementId_idempoten~",
                schema: "payments",
                table: "return_entitlement_valuations",
                columns: new[] { "return_entitlement_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key is not null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "return_entitlement_valuations",
                schema: "payments");

            migrationBuilder.DropColumn(
                name: "account_level_settled_amount",
                schema: "payments",
                table: "return_entitlements");

            migrationBuilder.DropColumn(
                name: "source_settled_amount",
                schema: "payments",
                table: "return_entitlements");

            migrationBuilder.DropColumn(
                name: "return_settlement_scope",
                schema: "payments",
                table: "financial_adjustments");

            migrationBuilder.DropColumn(
                name: "transfer_fee_amount",
                schema: "payments",
                table: "cash_records");
        }
    }
}
