using Lensee.Modules.Identity.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Identity.Migrations;

[DbContext(typeof(IdentityDbContext))]
[Migration("20260811180000_AddPrimaryAdmin")]
public partial class AddPrimaryAdmin : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "is_primary_admin",
            schema: "identity",
            table: "users",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.Sql("""
            alter table identity.users
                add constraint chk_users_primary_admin_role
                check (not is_primary_admin or role = 'Admin');

            create unique index uq_users_primary_admin
                on identity.users (is_primary_admin)
                where is_primary_admin;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("drop index if exists identity.uq_users_primary_admin;");
        migrationBuilder.DropCheckConstraint(
            name: "chk_users_primary_admin_role",
            schema: "identity",
            table: "users");
        migrationBuilder.DropColumn(
            name: "is_primary_admin",
            schema: "identity",
            table: "users");
    }
}
