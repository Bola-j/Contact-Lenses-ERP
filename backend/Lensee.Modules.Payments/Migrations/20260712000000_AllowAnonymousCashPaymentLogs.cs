using Lensee.Modules.Payments.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Payments.Migrations;

[DbContext(typeof(PaymentsDbContext))]
[Migration("20260712000000_AllowAnonymousCashPaymentLogs")]
public partial class AllowAnonymousCashPaymentLogs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "merchant_id",
            schema: "payments",
            table: "main_payment_logs",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            delete from payments.main_payment_logs
            where merchant_id is null;
            """);

        migrationBuilder.AlterColumn<Guid>(
            name: "merchant_id",
            schema: "payments",
            table: "main_payment_logs",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
    }
}
