using Microsoft.EntityFrameworkCore.Migrations;
using Lensee.Modules.Identity.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Lensee.Modules.Identity.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(IdentityDbContext))]
    [Migration("20260820092000_AddFinancialIntegrityPermissions")]
    public partial class AddFinancialIntegrityPermissions : Migration
    {
        /// <inheritdoc />
        
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                insert into identity.roles_permissions (id, role, permission)
                select uuid_generate_v4(), values_to_insert.role, values_to_insert.permission
                from (values
                    ('Admin', 'payments.adjustments.request'),
                    ('Admin', 'payments.adjustments.approve'),
                    ('Admin', 'operations.corrections.request'),
                    ('Admin', 'operations.corrections.approve'),
                    ('ERPAdmin', 'payments.adjustments.request'),
                    ('ERPAdmin', 'payments.adjustments.approve'),
                    ('ERPAdmin', 'operations.corrections.request'),
                    ('ERPAdmin', 'operations.corrections.approve'),
                    ('Accountant', 'payments.adjustments.request'),
                    ('Accountant', 'operations.corrections.request')
                ) as values_to_insert(role, permission)
                where not exists (
                    select 1
                    from identity.roles_permissions existing
                    where existing.role = values_to_insert.role
                      and existing.permission = values_to_insert.permission
                );

                delete from identity.roles_permissions
                where role = 'Accountant'
                  and permission = 'payments.approve';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                delete from identity.roles_permissions
                where (role, permission) in (
                    ('Admin', 'payments.adjustments.request'),
                    ('Admin', 'payments.adjustments.approve'),
                    ('Admin', 'operations.corrections.request'),
                    ('Admin', 'operations.corrections.approve'),
                    ('ERPAdmin', 'payments.adjustments.request'),
                    ('ERPAdmin', 'payments.adjustments.approve'),
                    ('ERPAdmin', 'operations.corrections.request'),
                    ('ERPAdmin', 'operations.corrections.approve'),
                    ('Accountant', 'payments.adjustments.request'),
                    ('Accountant', 'operations.corrections.request')
                );

                insert into identity.roles_permissions (id, role, permission)
                select uuid_generate_v4(), 'Accountant', 'payments.approve'
                where not exists (
                    select 1
                    from identity.roles_permissions
                    where role = 'Accountant'
                      and permission = 'payments.approve'
                );
                """);
        }
    }
}
