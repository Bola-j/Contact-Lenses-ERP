using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations;

public partial class AllowReturnApprovalStatuses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("alter table if exists operations.operation_logs drop constraint if exists chk_op_status;");
        migrationBuilder.AddCheckConstraint(
            name: "chk_op_status",
            schema: "operations",
            table: "operation_logs",
            sql: "status in ('Draft','PendingApproval','Approved','Confirmed','Completed','Reserved','Shipped','Received','Cancelled','Rejected','Dispatched','Delivered','FailedDelivery')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("alter table if exists operations.operation_logs drop constraint if exists chk_op_status;");
        migrationBuilder.AddCheckConstraint(
            name: "chk_op_status",
            schema: "operations",
            table: "operation_logs",
            sql: "status in ('Draft','Confirmed','Completed','Reserved','Shipped','Received','Cancelled')");
    }
}
