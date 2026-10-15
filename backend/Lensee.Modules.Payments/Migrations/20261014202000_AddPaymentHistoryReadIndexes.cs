using Lensee.Modules.Payments.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations;

/// <inheritdoc />
[DbContext(typeof(PaymentsDbContext))]
[Migration("20261014202000_AddPaymentHistoryReadIndexes")]
public partial class AddPaymentHistoryReadIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "idx_financial_adjustments_created_at",
            schema: "payments",
            table: "financial_adjustments",
            column: "created_at",
            descending: new[] { true });

        migrationBuilder.CreateIndex(
            name: "idx_main_payment_active_last_modified",
            schema: "payments",
            table: "main_payment_logs",
            column: "last_modified_at",
            descending: new[] { true },
            filter: "(is_deleted = false)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "idx_financial_adjustments_created_at",
            schema: "payments",
            table: "financial_adjustments");

        migrationBuilder.DropIndex(
            name: "idx_main_payment_active_last_modified",
            schema: "payments",
            table: "main_payment_logs");
    }
}
