using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.CRM.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionConstraintsCrm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("alter table if exists crm.representatives drop constraint if exists chk_representatives_status;");
            migrationBuilder.Sql("alter table if exists crm.representatives drop constraint if exists chk_representatives_type;");
            migrationBuilder.Sql("alter table if exists crm.merchants drop constraint if exists chk_merchants_status;");

            migrationBuilder.AddCheckConstraint(
                name: "chk_representatives_status",
                schema: "crm",
                table: "representatives",
                sql: "status in ('Active','Inactive')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_representatives_type",
                schema: "crm",
                table: "representatives",
                sql: "type in ('Internal','External')");

            migrationBuilder.AddCheckConstraint(
                name: "chk_merchants_status",
                schema: "crm",
                table: "merchants",
                sql: "status in ('Active','Inactive')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "chk_representatives_status",
                schema: "crm",
                table: "representatives");

            migrationBuilder.DropCheckConstraint(
                name: "chk_representatives_type",
                schema: "crm",
                table: "representatives");

            migrationBuilder.DropCheckConstraint(
                name: "chk_merchants_status",
                schema: "crm",
                table: "merchants");
        }
    }
}
