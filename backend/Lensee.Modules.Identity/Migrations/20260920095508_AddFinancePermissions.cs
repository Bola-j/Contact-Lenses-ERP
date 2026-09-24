using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lensee.Modules.Identity.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                insert into identity.roles_permissions (id, role, permission)
                select uuid_generate_v4(), values_to_insert.role, values_to_insert.permission
                from (values
                    ('CLevel', 'finance.read'),
                    ('Admin', 'finance.read'), ('Admin', 'finance.expense.create'), ('Admin', 'finance.expense.approve'),
                    ('Admin', 'finance.withdrawal.create'), ('Admin', 'finance.withdrawal.assign'), ('Admin', 'finance.withdrawal.approve'), ('Admin', 'finance.accounts.manage'), ('Admin', 'finance.reconcile'),
                    ('ERPAdmin', 'finance.read'), ('ERPAdmin', 'finance.expense.create'), ('ERPAdmin', 'finance.expense.approve'),
                    ('ERPAdmin', 'finance.withdrawal.create'), ('ERPAdmin', 'finance.withdrawal.approve'), ('ERPAdmin', 'finance.accounts.manage'), ('ERPAdmin', 'finance.reconcile'),
                    ('Accountant', 'finance.read'), ('Accountant', 'finance.expense.create'), ('Accountant', 'finance.withdrawal.create')
                ) as values_to_insert(role, permission)
                on conflict (role, permission) do nothing;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                delete from identity.roles_permissions
                where permission like 'finance.%';
                """);
        }
    }
}
