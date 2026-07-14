using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations
{
    /// <inheritdoc />
    public partial class InitialPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:uuid-ossp", ",,");

            migrationBuilder.CreateTable(
                name: "cash_records",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValueSql: "'Cash'::character varying"),
                    sub_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValueSql: "'Completed'::character varying"),
                    payment_date = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("cash_records_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "financial_adjustments",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    adjustment_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValueSql: "'Completed'::character varying"),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("financial_adjustments_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "main_payment_logs",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    merchant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    amount_paid = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValueSql: "'Installment'::character varying"),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValueSql: "'PendingAdmin'::character varying"),
                    initialized_by = table.Column<Guid>(type: "uuid", nullable: false),
                    initialized_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    assigned_to = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    last_modified_by = table.Column<Guid>(type: "uuid", nullable: true),
                    last_modified_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    notes = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("main_payment_logs_pkey", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "installment_sub_logs",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    main_log_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    payment_method = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    date_received = table.Column<DateOnly>(type: "date", nullable: false),
                    sub_log_status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValueSql: "'Draft'::character varying"),
                    drafted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    drafted_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("installment_sub_logs_pkey", x => x.id);
                    table.ForeignKey(
                        name: "installment_sub_logs_main_log_id_fkey",
                        column: x => x.main_log_id,
                        principalSchema: "payments",
                        principalTable: "main_payment_logs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_cash_records_date",
                schema: "payments",
                table: "cash_records",
                column: "payment_date",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "idx_cash_records_operation",
                schema: "payments",
                table: "cash_records",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "idx_financial_adjustments_merchant",
                schema: "payments",
                table: "financial_adjustments",
                column: "merchant_id");

            migrationBuilder.CreateIndex(
                name: "idx_financial_adjustments_operation",
                schema: "payments",
                table: "financial_adjustments",
                column: "operation_id",
                filter: "(operation_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "idx_sub_logs_main_log",
                schema: "payments",
                table: "installment_sub_logs",
                column: "main_log_id");

            migrationBuilder.CreateIndex(
                name: "idx_sub_logs_status",
                schema: "payments",
                table: "installment_sub_logs",
                column: "sub_log_status");

            migrationBuilder.CreateIndex(
                name: "idx_main_payment_assigned",
                schema: "payments",
                table: "main_payment_logs",
                column: "assigned_to",
                filter: "(assigned_to IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "idx_main_payment_merchant",
                schema: "payments",
                table: "main_payment_logs",
                column: "merchant_id");

            migrationBuilder.CreateIndex(
                name: "idx_main_payment_operation",
                schema: "payments",
                table: "main_payment_logs",
                column: "operation_id");

            migrationBuilder.CreateIndex(
                name: "idx_main_payment_status",
                schema: "payments",
                table: "main_payment_logs",
                column: "status",
                filter: "(is_deleted = false)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_records",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "financial_adjustments",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "installment_sub_logs",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "main_payment_logs",
                schema: "payments");
        }
    }
}
