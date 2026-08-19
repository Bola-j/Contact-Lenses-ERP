using Lensee.Modules.Identity.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Identity.Migrations;

[DbContext(typeof(IdentityDbContext))]
[Migration("20260809120000_AddUserCreationProvenanceAndNormalizedUsername")]
public partial class AddUserCreationProvenanceAndNormalizedUsername : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            do $$
            begin
                if exists (
                    select 1
                    from identity.users
                    group by upper(btrim(username))
                    having count(*) > 1
                ) then
                    raise exception 'Cannot enable case-insensitive usernames because identity.users contains names that differ only by case or surrounding whitespace. Resolve those account names before applying this migration.';
                end if;
            end $$;
            """);

        migrationBuilder.AddColumn<Guid>(
            name: "created_by_admin_id",
            schema: "identity",
            table: "users",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "idx_users_created_by_admin",
            schema: "identity",
            table: "users",
            column: "created_by_admin_id");

        migrationBuilder.AddForeignKey(
            name: "users_created_by_admin_id_fkey",
            schema: "identity",
            table: "users",
            column: "created_by_admin_id",
            principalSchema: "identity",
            principalTable: "users",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.DropIndex(
            name: "users_username_key",
            schema: "identity",
            table: "users");
        migrationBuilder.Sql("create unique index uq_users_normalized_username on identity.users (upper(btrim(username))); ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("drop index if exists identity.uq_users_normalized_username;");
        migrationBuilder.CreateIndex(
            name: "users_username_key",
            schema: "identity",
            table: "users",
            column: "username",
            unique: true);
        migrationBuilder.DropForeignKey("users_created_by_admin_id_fkey", "identity", "users");
        migrationBuilder.DropIndex("idx_users_created_by_admin", "identity", "users");
        migrationBuilder.DropColumn("created_by_admin_id", "identity", "users");
    }
}
