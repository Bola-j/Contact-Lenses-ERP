using Lensee.Modules.Finance.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Finance.Migrations;

[DbContext(typeof(FinanceDbContext))]
[Migration("20260927110000_AllowPostedSupplyInstallmentStatus")]
public sealed class AllowPostedSupplyInstallmentStatus : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
        => FinanceDbContextModelSnapshot.BuildCurrentModel(modelBuilder);

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "chk_supply_installment_status",
            schema: "finance",
            table: "supply_supplier_installments");

        migrationBuilder.AddCheckConstraint(
            name: "chk_supply_installment_status",
            schema: "finance",
            table: "supply_supplier_installments",
            sql: "\"Status\" in ('Draft','Posted','Corrected','Cancelled')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "chk_supply_installment_status",
            schema: "finance",
            table: "supply_supplier_installments");

        migrationBuilder.AddCheckConstraint(
            name: "chk_supply_installment_status",
            schema: "finance",
            table: "supply_supplier_installments",
            sql: "\"Status\" in ('Draft','Paid','Corrected','Cancelled')");
    }
}
