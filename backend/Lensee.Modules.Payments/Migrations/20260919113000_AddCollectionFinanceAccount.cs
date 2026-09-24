using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Lensee.Modules.Payments.Data;

#nullable disable

namespace Lensee.Modules.Payments.Migrations;

[Migration("20260919113000_AddCollectionFinanceAccount")]
[DbContext(typeof(PaymentsDbContext))]
public partial class AddCollectionFinanceAccount : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "finance_account_id", schema: "payments", table: "merchant_account_collection_drafts", type: "uuid", nullable: true);
        migrationBuilder.CreateIndex(name: "ix_merchant_collection_drafts_finance_account_id", schema: "payments", table: "merchant_account_collection_drafts", column: "finance_account_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "ix_merchant_collection_drafts_finance_account_id", schema: "payments", table: "merchant_account_collection_drafts");
        migrationBuilder.DropColumn(name: "finance_account_id", schema: "payments", table: "merchant_account_collection_drafts");
    }
}
