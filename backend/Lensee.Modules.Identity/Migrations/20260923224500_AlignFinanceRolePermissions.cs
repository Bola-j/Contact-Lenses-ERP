using Lensee.Modules.Identity.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Lensee.Modules.Identity.Migrations;

[DbContext(typeof(IdentityDbContext))]
[Migration("20260923224500_AlignFinanceRolePermissions")]
public sealed class AlignFinanceRolePermissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            delete from identity.roles_permissions
            where role = 'ERPAdmin' and permission like 'finance.%';
            delete from identity.roles_permissions
            where role = 'Accountant' and permission in
                ('finance.expense.create', 'finance.expense.approve', 'finance.withdrawal.create',
                 'finance.withdrawal.assign', 'finance.withdrawal.approve', 'finance.accounts.manage', 'finance.reconcile');
            insert into identity.roles_permissions (id, role, permission)
            select uuid_generate_v4(), item.role, item.permission
            from (values
                ('Admin', 'finance.opening.create'), ('Admin', 'finance.opening.correct'),
                ('Accountant', 'finance.opening.create'), ('Accountant', 'finance.opening.correct'),
                ('Admin', 'reports.executive.read'), ('CLevel', 'reports.executive.read')
            ) as item(role, permission)
            on conflict (role, permission) do nothing;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Role grants removed by this remediation are intentionally not
        // reinstated by a schema rollback. Access must be granted deliberately.
        migrationBuilder.Sql("""
            delete from identity.roles_permissions
            where (role in ('Admin', 'Accountant') and permission in ('finance.opening.create', 'finance.opening.correct'))
               or (role in ('Admin', 'CLevel') and permission = 'reports.executive.read');
            """);
    }
}
