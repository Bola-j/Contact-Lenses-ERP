-- Dedicated users for the isolated lensee-e2e Compose project only.
-- Passwords are transformed from <hash:...> placeholders by scripts/e2e-setup.ps1.

update identity.users
set is_primary_admin = false
where is_primary_admin and username <> 'e2e_admin';

insert into identity.users (id, username, password_hash, full_name, role, location_id, is_active, is_primary_admin)
values
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1', 'e2e_admin', '<hash:E2E-only-not-production-2026!>', 'E2E Primary Admin', 'Admin', null, true, true),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2', 'e2e_clevel', '<hash:E2E-only-not-production-2026!>', 'E2E C-Level Executive', 'CLevel', null, true, false),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb3', 'e2e_accountant', '<hash:E2E-only-not-production-2026!>', 'E2E Accountant', 'Accountant', null, true, false),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb4', 'e2e_erp_admin', '<hash:E2E-only-not-production-2026!>', 'E2E ERP Admin', 'ERPAdmin', null, true, false),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb5', 'e2e_roxy_clerk', '<hash:E2E-only-not-production-2026!>', 'E2E Roxy Warehouse Clerk', 'WarehouseClerk', '11111111-1111-1111-1111-111111111111', true, false),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb6', 'e2e_retail_clerk', '<hash:E2E-only-not-production-2026!>', 'E2E Retail Warehouse Clerk', 'WarehouseClerk', '22222222-2222-2222-2222-222222222222', true, false),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb7', 'e2e_online_clerk', '<hash:E2E-only-not-production-2026!>', 'E2E Online Warehouse Clerk', 'WarehouseClerk', '33333333-3333-3333-3333-333333333333', true, false)
on conflict (username) do update
set password_hash = excluded.password_hash,
    full_name = excluded.full_name,
    role = excluded.role,
    location_id = excluded.location_id,
    is_active = excluded.is_active,
    is_primary_admin = excluded.is_primary_admin;
