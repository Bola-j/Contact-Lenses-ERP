using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplyPaymentsAndCorrectionLineage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supply_payments",
                schema: "operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    shipment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    movement_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    finance_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "text", nullable: true),
                    posted_finance_ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reverses_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    replaced_by_payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supply_payments", x => x.id);
                    table.CheckConstraint("chk_supply_payment_amount", "amount > 0");
                    table.CheckConstraint("chk_supply_payment_category", "category in ('SupplierPurchase','Freight','Customs','Transport','OtherShipmentCost')");
                    table.CheckConstraint("chk_supply_payment_method", "movement_method in ('CashHandToHand','CashTransaction','BankTransfer','Wallet')");
                    table.CheckConstraint("chk_supply_payment_status", "status in ('Draft','PendingReview','Posted','Rejected','Corrected')");
                    table.ForeignKey(
                        name: "FK_supply_payments_supply_shipments_shipment_id",
                        column: x => x.shipment_id,
                        principalSchema: "operations",
                        principalTable: "supply_shipments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_supply_payments_shipment",
                schema: "operations",
                table: "supply_payments",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "IX_supply_payments_posted_finance_ledger_entry_id",
                schema: "operations",
                table: "supply_payments",
                column: "posted_finance_ledger_entry_id",
                unique: true,
                filter: "(posted_finance_ledger_entry_id is not null)");

            migrationBuilder.CreateIndex(
                name: "IX_supply_payments_replaced_by_payment_id",
                schema: "operations",
                table: "supply_payments",
                column: "replaced_by_payment_id",
                unique: true,
                filter: "(replaced_by_payment_id is not null)");

            migrationBuilder.Sql("""
                ALTER TABLE operations.supply_payments
                ADD CONSTRAINT fk_supply_payments_finance_account
                FOREIGN KEY (finance_account_id)
                REFERENCES finance.finance_accounts (id)
                ON DELETE RESTRICT;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE operations.supply_payments DROP CONSTRAINT IF EXISTS fk_supply_payments_finance_account;");
            migrationBuilder.DropTable(
                name: "supply_payments",
                schema: "operations");
        }
    }
}
