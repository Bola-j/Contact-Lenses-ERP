-- Dedicated users for the isolated lensee-e2e Compose project only.
-- Passwords are transformed from <hash:...> placeholders by scripts/e2e-setup.ps1.

-- The isolated project is reset before this seed runs. Remove any prior E2E
-- fixture users as a defensive measure because username uniqueness is enforced
-- by a normalized unique index, not a named table constraint.
delete from identity.users
where username like 'e2e\_%' escape '\';

insert into identity.users (id, username, password_hash, full_name, role, location_id, is_active, is_primary_admin)
values
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1', 'e2e_admin', '<hash:E2E-only-not-production-2026!>', 'E2E Primary Admin', 'Admin', null, true, true),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2', 'e2e_clevel', '<hash:E2E-only-not-production-2026!>', 'E2E C-Level Executive', 'CLevel', null, true, false),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb3', 'e2e_accountant', '<hash:E2E-only-not-production-2026!>', 'E2E Accountant', 'Accountant', null, true, false),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb4', 'e2e_erp_admin', '<hash:E2E-only-not-production-2026!>', 'E2E ERP Admin', 'ERPAdmin', null, true, false),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb5', 'e2e_roxy_clerk', '<hash:E2E-only-not-production-2026!>', 'E2E Roxy Warehouse Clerk', 'WarehouseClerk', '11111111-1111-1111-1111-111111111111', true, false),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb6', 'e2e_retail_clerk', '<hash:E2E-only-not-production-2026!>', 'E2E Retail Warehouse Clerk', 'WarehouseClerk', '22222222-2222-2222-2222-222222222222', true, false),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb7', 'e2e_online_clerk', '<hash:E2E-only-not-production-2026!>', 'E2E Online Warehouse Clerk', 'WarehouseClerk', '33333333-3333-3333-3333-333333333333', true, false),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb101', 'e2e_load_01', '<hash:E2E-only-not-production-2026!>', 'E2E Load User 01', 'ERPAdmin', null, true, false),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb102', 'e2e_load_02', '<hash:E2E-only-not-production-2026!>', 'E2E Load User 02', 'ERPAdmin', null, true, false),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb103', 'e2e_load_03', '<hash:E2E-only-not-production-2026!>', 'E2E Load User 03', 'ERPAdmin', null, true, false),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb104', 'e2e_load_04', '<hash:E2E-only-not-production-2026!>', 'E2E Load User 04', 'ERPAdmin', null, true, false),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb105', 'e2e_load_05', '<hash:E2E-only-not-production-2026!>', 'E2E Load User 05', 'ERPAdmin', null, true, false),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb106', 'e2e_load_06', '<hash:E2E-only-not-production-2026!>', 'E2E Load User 06', 'ERPAdmin', null, true, false),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb107', 'e2e_load_07', '<hash:E2E-only-not-production-2026!>', 'E2E Load User 07', 'ERPAdmin', null, true, false),
  ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbb108', 'e2e_load_08', '<hash:E2E-only-not-production-2026!>', 'E2E Load User 08', 'ERPAdmin', null, true, false)
;
