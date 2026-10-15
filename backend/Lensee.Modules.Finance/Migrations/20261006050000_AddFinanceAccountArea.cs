using System;
using Lensee.Modules.Finance.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Finance.Migrations;

[DbContext(typeof(FinanceDbContext))]
[Migration("20261006050000_AddFinanceAccountArea")]
public partial class AddFinanceAccountArea : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "location_id",
            schema: "finance",
            table: "finance_accounts",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_finance_accounts_location_id",
            schema: "finance",
            table: "finance_accounts",
            column: "location_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_finance_accounts_location_id",
            schema: "finance",
            table: "finance_accounts");

        migrationBuilder.DropColumn(
            name: "location_id",
            schema: "finance",
            table: "finance_accounts");
    }
}
