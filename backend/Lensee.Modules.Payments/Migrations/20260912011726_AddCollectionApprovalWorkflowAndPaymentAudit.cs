using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionApprovalWorkflowAndPaymentAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "merchant_account_collection_drafts",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    transaction_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    allocations_json = table.Column<string>(type: "jsonb", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    drafted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    drafted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    assigned_to = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_merchant_account_collection_drafts", x => x.Id);
                    table.CheckConstraint("chk_merchant_collection_draft_amount", "amount > 0");
                    table.CheckConstraint("chk_merchant_collection_draft_method", "payment_method in ('CashHandToHand','CashTransaction','BankTransfer','Wallet')");
                    table.CheckConstraint("chk_merchant_collection_draft_status", "status in ('PendingAdminReview','Confirmed','Rejected')");
                    table.ForeignKey(
                        name: "FK_merchant_account_collection_drafts_merchant_receivable_acco~",
                        column: x => x.account_id,
                        principalSchema: "payments",
                        principalTable: "merchant_receivable_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_audit_events",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    previous_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    new_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_log_id = table.Column<Guid>(type: "uuid", nullable: true),
                    collection_draft_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    payment_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    data_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_audit_events", x => x.Id);
                    table.CheckConstraint("chk_payment_audit_amount", "amount is null or amount >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_merchant_account_collection_drafts_account_id_status_drafte~",
                schema: "payments",
                table: "merchant_account_collection_drafts",
                columns: new[] { "account_id", "status", "drafted_at" });

            migrationBuilder.CreateIndex(
                name: "IX_merchant_account_collection_drafts_assigned_to",
                schema: "payments",
                table: "merchant_account_collection_drafts",
                column: "assigned_to",
                filter: "(assigned_to IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_payment_audit_events_collection_draft_id_occurred_at",
                schema: "payments",
                table: "payment_audit_events",
                columns: new[] { "collection_draft_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_audit_events_merchant_id_occurred_at",
                schema: "payments",
                table: "payment_audit_events",
                columns: new[] { "merchant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_payment_audit_events_occurred_at",
                schema: "payments",
                table: "payment_audit_events",
                column: "occurred_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_payment_audit_events_payment_log_id_occurred_at",
                schema: "payments",
                table: "payment_audit_events",
                columns: new[] { "payment_log_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "merchant_account_collection_drafts",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payment_audit_events",
                schema: "payments");
        }
    }
}
