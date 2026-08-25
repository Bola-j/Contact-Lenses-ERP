using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Lensee.Modules.Identity.Data;
#nullable disable

namespace Lensee.Modules.Identity.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(IdentityDbContext))]
    [Migration("20260722171100_AddErpAdminAndSupplyPermissions")]
    public partial class AddErpAdminAndSupplyPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create extension if not exists "uuid-ossp";

                alter table if exists identity.users
                    drop constraint if exists chk_user_role;

                alter table if exists identity.users
                    add constraint chk_user_role
                    check (role in ('CLevel','Admin','ERPAdmin','Accountant','WarehouseClerk'));

                insert into identity.roles_permissions (id, role, permission)
                values
                    (uuid_generate_v4(), 'Admin', 'users.password.write'),
                    (uuid_generate_v4(), 'Admin', 'supply.read'),
                    (uuid_generate_v4(), 'Admin', 'supply.write'),
                    (uuid_generate_v4(), 'CLevel', 'supply.read'),
                    (uuid_generate_v4(), 'ERPAdmin', 'users.read'),
                    (uuid_generate_v4(), 'ERPAdmin', 'users.write'),
                    (uuid_generate_v4(), 'ERPAdmin', 'catalog.read'),
                    (uuid_generate_v4(), 'ERPAdmin', 'catalog.write'),
                    (uuid_generate_v4(), 'ERPAdmin', 'inventory.read'),
                    (uuid_generate_v4(), 'ERPAdmin', 'inventory.write'),
                    (uuid_generate_v4(), 'ERPAdmin', 'operations.read'),
                    (uuid_generate_v4(), 'ERPAdmin', 'operations.write'),
                    (uuid_generate_v4(), 'ERPAdmin', 'payments.read'),
                    (uuid_generate_v4(), 'ERPAdmin', 'payments.write'),
                    (uuid_generate_v4(), 'ERPAdmin', 'payments.draft'),
                    (uuid_generate_v4(), 'ERPAdmin', 'payments.approve'),
                    (uuid_generate_v4(), 'ERPAdmin', 'reports.read'),
                    (uuid_generate_v4(), 'ERPAdmin', 'audit.read'),
                    (uuid_generate_v4(), 'ERPAdmin', 'settings.write')
                on conflict (role, permission) do nothing;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                delete from identity.roles_permissions
                where (role = 'Admin' and permission in ('users.password.write', 'supply.read', 'supply.write'))
                   or (role = 'CLevel' and permission = 'supply.read')
                   or role = 'ERPAdmin';

                update identity.users
                set role = 'Admin'
                where role = 'ERPAdmin';

                alter table if exists identity.users
                    drop constraint if exists chk_user_role;

                alter table if exists identity.users
                    add constraint chk_user_role
                    check (role in ('CLevel','Admin','Accountant','WarehouseClerk'));
                """);
        }
    }
}
