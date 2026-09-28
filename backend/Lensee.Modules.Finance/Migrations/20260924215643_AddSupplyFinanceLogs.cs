using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Finance.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplyFinanceLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supply_finance_logs",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplyShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShipmentNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SupplierName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supply_finance_logs", x => x.Id);
                    table.CheckConstraint("chk_supply_finance_log_status", "\"Status\" in ('Open','Closed','Cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "supply_finance_cost_entries",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplyFinanceLogId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Finance"),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    ReversesCostEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReplacedByCostEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrectionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supply_finance_cost_entries", x => x.Id);
                    table.CheckConstraint("chk_supply_finance_cost_amount", "\"Amount\" > 0");
                    table.CheckConstraint("chk_supply_finance_cost_status", "\"Status\" in ('Active','Corrected','Voided')");
                    table.ForeignKey(
                        name: "FK_supply_finance_cost_entries_supply_finance_logs_SupplyFinan~",
                        column: x => x.SupplyFinanceLogId,
                        principalSchema: "finance",
                        principalTable: "supply_finance_logs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supply_supplier_installments",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplyFinanceLogId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinanceAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    MovementMethod = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    PaidByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    PostedFinanceLedgerEntryId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversesInstallmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReplacedByInstallmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrectionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supply_supplier_installments", x => x.Id);
                    table.CheckConstraint("chk_supply_installment_amount", "\"Amount\" > 0");
                    table.CheckConstraint("chk_supply_installment_status", "\"Status\" in ('Draft','Paid','Corrected','Cancelled')");
                    table.ForeignKey(
                        name: "FK_supply_supplier_installments_finance_accounts_FinanceAccoun~",
                        column: x => x.FinanceAccountId,
                        principalSchema: "finance",
                        principalTable: "finance_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supply_supplier_installments_supply_finance_logs_SupplyFina~",
                        column: x => x.SupplyFinanceLogId,
                        principalSchema: "finance",
                        principalTable: "supply_finance_logs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_supply_finance_cost_entries_ReplacedByCostEntryId",
                schema: "finance",
                table: "supply_finance_cost_entries",
                column: "ReplacedByCostEntryId",
                unique: true,
                filter: "(\"ReplacedByCostEntryId\" is not null)");

            migrationBuilder.CreateIndex(
                name: "IX_supply_finance_cost_entries_SupplyFinanceLogId_BusinessDate",
                schema: "finance",
                table: "supply_finance_cost_entries",
                columns: new[] { "SupplyFinanceLogId", "BusinessDate" });

            migrationBuilder.CreateIndex(
                name: "IX_supply_finance_logs_SupplyShipmentId",
                schema: "finance",
                table: "supply_finance_logs",
                column: "SupplyShipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_supply_supplier_installments_ExternalReference",
                schema: "finance",
                table: "supply_supplier_installments",
                column: "ExternalReference",
                unique: true,
                filter: "(\"ExternalReference\" is not null)");

            migrationBuilder.CreateIndex(
                name: "IX_supply_supplier_installments_FinanceAccountId",
                schema: "finance",
                table: "supply_supplier_installments",
                column: "FinanceAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_supply_supplier_installments_PostedFinanceLedgerEntryId",
                schema: "finance",
                table: "supply_supplier_installments",
                column: "PostedFinanceLedgerEntryId",
                unique: true,
                filter: "(\"PostedFinanceLedgerEntryId\" is not null)");

            migrationBuilder.CreateIndex(
                name: "IX_supply_supplier_installments_ReplacedByInstallmentId",
                schema: "finance",
                table: "supply_supplier_installments",
                column: "ReplacedByInstallmentId",
                unique: true,
                filter: "(\"ReplacedByInstallmentId\" is not null)");

            migrationBuilder.CreateIndex(
                name: "IX_supply_supplier_installments_SupplyFinanceLogId_BusinessDate",
                schema: "finance",
                table: "supply_supplier_installments",
                columns: new[] { "SupplyFinanceLogId", "BusinessDate" });

            // Operations is migrated after Finance. The compatibility backfill lives in
            // its own Operations migration so a fresh Finance migration never depends
            // on an Operations table that has not been created yet.
        }
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "supply_finance_cost_entries",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "supply_supplier_installments",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "supply_finance_logs",
                schema: "finance");
        }
    }
}


