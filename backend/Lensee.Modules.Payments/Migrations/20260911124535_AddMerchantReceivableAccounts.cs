using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class AddMerchantReceivableAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "transaction_reference",
                schema: "payments",
                table: "installment_sub_logs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "confirmed_at",
                schema: "payments",
                table: "cash_records",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "confirmed_by",
                schema: "payments",
                table: "cash_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "transaction_reference",
                schema: "payments",
                table: "cash_records",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "merchant_account_classification_snapshots",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    calendar_year = table.Column<int>(type: "integer", nullable: false),
                    is_partial_year = table.Column<bool>(type: "boolean", nullable: false),
                    grade = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    score = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    flags_json = table.Column<string>(type: "jsonb", nullable: false),
                    settings_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    calculated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchant_account_classification_snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "merchant_receivable_accounts",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    next_sequence = table.Column<long>(type: "bigint", nullable: false),
                    opened_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    opened_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchant_receivable_accounts", x => x.Id);
                    table.CheckConstraint("chk_merchant_account_next_sequence", "next_sequence > 0");
                    table.CheckConstraint("chk_merchant_account_status", "status in ('Open','Frozen','Closed')");
                });

            migrationBuilder.CreateTable(
                name: "merchant_refund_reservations",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    payout_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchant_refund_reservations", x => x.Id);
                    table.CheckConstraint("chk_merchant_refund_reservation_amount", "amount > 0");
                    table.CheckConstraint("chk_merchant_refund_reservation_status", "status in ('PendingApproval','Approved','Paid','Rejected','Cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "merchant_account_entries",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    entry_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    debit_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    credit_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    transaction_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    source_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reverses_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    posted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    posted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchant_account_entries", x => x.Id);
                    table.CheckConstraint("chk_merchant_account_entry_amount", "(debit_amount > 0 and credit_amount = 0) or (credit_amount > 0 and debit_amount = 0)");
                    table.CheckConstraint("chk_merchant_account_entry_method", "payment_method is null or payment_method in ('CashHandToHand','CashTransaction','BankTransfer','Wallet')");
                    table.CheckConstraint("chk_merchant_account_entry_status", "status in ('Posted','Reversed')");
                    table.ForeignKey(
                        name: "FK_merchant_account_entries_merchant_receivable_accounts_accou~",
                        column: x => x.account_id,
                        principalSchema: "payments",
                        principalTable: "merchant_receivable_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "merchant_operation_obligations",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    posted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchant_operation_obligations", x => x.Id);
                    table.CheckConstraint("chk_merchant_obligation_amount", "original_amount >= 0");
                    table.ForeignKey(
                        name: "FK_merchant_operation_obligations_merchant_receivable_accounts~",
                        column: x => x.account_id,
                        principalSchema: "payments",
                        principalTable: "merchant_receivable_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "merchant_entry_allocations",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    obligation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    allocated_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    allocated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchant_entry_allocations", x => x.Id);
                    table.CheckConstraint("chk_merchant_entry_allocation_amount", "amount > 0");
                    table.ForeignKey(
                        name: "FK_merchant_entry_allocations_merchant_account_entries_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "payments",
                        principalTable: "merchant_account_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_merchant_entry_allocations_merchant_operation_obligations_o~",
                        column: x => x.obligation_id,
                        principalSchema: "payments",
                        principalTable: "merchant_operation_obligations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_merchant_account_classification_snapshots_account_id_calend~",
                schema: "payments",
                table: "merchant_account_classification_snapshots",
                columns: new[] { "account_id", "calendar_year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_merchant_account_entries_account_id_sequence",
                schema: "payments",
                table: "merchant_account_entries",
                columns: new[] { "account_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_merchant_account_entries_operation_id",
                schema: "payments",
                table: "merchant_account_entries",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "IX_merchant_account_entries_source_type_source_id_entry_type",
                schema: "payments",
                table: "merchant_account_entries",
                columns: new[] { "source_type", "source_id", "entry_type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_merchant_entry_allocations_entry_id_obligation_id",
                schema: "payments",
                table: "merchant_entry_allocations",
                columns: new[] { "entry_id", "obligation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_merchant_entry_allocations_obligation_id",
                schema: "payments",
                table: "merchant_entry_allocations",
                column: "obligation_id");

            migrationBuilder.CreateIndex(
                name: "IX_merchant_operation_obligations_account_id_status_posted_at",
                schema: "payments",
                table: "merchant_operation_obligations",
                columns: new[] { "account_id", "status", "posted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_merchant_operation_obligations_operation_id",
                schema: "payments",
                table: "merchant_operation_obligations",
                column: "operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_merchant_receivable_accounts_merchant_id",
                schema: "payments",
                table: "merchant_receivable_accounts",
                column: "merchant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_merchant_refund_reservations_account_id_status",
                schema: "payments",
                table: "merchant_refund_reservations",
                columns: new[] { "account_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "merchant_account_classification_snapshots",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "merchant_entry_allocations",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "merchant_refund_reservations",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "merchant_account_entries",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "merchant_operation_obligations",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "merchant_receivable_accounts",
                schema: "payments");

            migrationBuilder.DropColumn(
                name: "transaction_reference",
                schema: "payments",
                table: "installment_sub_logs");

            migrationBuilder.DropColumn(
                name: "confirmed_at",
                schema: "payments",
                table: "cash_records");

            migrationBuilder.DropColumn(
                name: "confirmed_by",
                schema: "payments",
                table: "cash_records");

            migrationBuilder.DropColumn(
                name: "transaction_reference",
                schema: "payments",
                table: "cash_records");
        }
    }
}
