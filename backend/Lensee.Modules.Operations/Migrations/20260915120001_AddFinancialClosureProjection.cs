using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Lensee.Modules.Operations.Data;

#nullable disable

namespace Lensee.Modules.Operations.Migrations;

[DbContext(typeof(OperationsDbContext))]
[Migration("20260915120001_AddFinancialClosureProjection")]
public partial class AddFinancialClosureProjection : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "financial_closure_status", table: "operation_logs", schema: "operations", type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "Open");
        migrationBuilder.AddColumn<Guid>(name: "financial_closure_proposal_id", table: "operation_logs", schema: "operations", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "financially_closed_by", table: "operation_logs", schema: "operations", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "financially_closed_at", table: "operation_logs", schema: "operations", type: "timestamp without time zone", nullable: true);
        migrationBuilder.AddCheckConstraint(name: "chk_op_financial_closure_status", schema: "operations", table: "operation_logs", sql: "financial_closure_status in ('Open','FinanciallyClosed')");
    }
    protected override void Down(MigrationBuilder migrationBuilder) { migrationBuilder.DropCheckConstraint(name: "chk_op_financial_closure_status", schema: "operations", table: "operation_logs"); migrationBuilder.DropColumn(name: "financially_closed_at", schema: "operations", table: "operation_logs"); migrationBuilder.DropColumn(name: "financially_closed_by", schema: "operations", table: "operation_logs"); migrationBuilder.DropColumn(name: "financial_closure_proposal_id", schema: "operations", table: "operation_logs"); migrationBuilder.DropColumn(name: "financial_closure_status", schema: "operations", table: "operation_logs"); }
}
