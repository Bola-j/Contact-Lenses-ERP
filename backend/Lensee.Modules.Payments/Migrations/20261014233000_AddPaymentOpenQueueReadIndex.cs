using Lensee.Modules.Payments.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations;

/// <inheritdoc />
[DbContext(typeof(PaymentsDbContext))]
[Migration("20261014233000_AddPaymentOpenQueueReadIndex")]
public partial class AddPaymentOpenQueueReadIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "idx_main_payment_open_queue_last_modified",
            schema: "payments",
            table: "main_payment_logs",
            column: "last_modified_at",
            descending: new[] { true },
            filter: "(is_deleted = false AND status <> 'Completed' AND status <> 'Rejected')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "idx_main_payment_open_queue_last_modified",
            schema: "payments",
            table: "main_payment_logs");
    }
}
