using Lensee.Modules.Operations.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Operations.Migrations;

/// <summary>
/// Repairs databases whose migration history is current but whose legacy
/// compatibility initialization recreated the operation-status constraint
/// with an obsolete lifecycle.
/// </summary>
[DbContext(typeof(OperationsDbContext))]
[Migration("20261011093000_RepairOperationStatusConstraintDrift")]
public partial class RepairOperationStatusConstraintDrift : Migration
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
            sql: "status in ('Draft','PendingApproval','Approved','Confirmed','Completed','Reserved','Shipped','Received','Cancelled','Rejected','Dispatched','Delivered','FailedDelivery')");
    }
}
