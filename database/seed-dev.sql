-- PRD v2.5 development seed.
-- Passwords:
--   all users / 12121212


-- Schema changes belong to the one-shot migrator. This seed intentionally
-- contains data only, so it can run after a fresh or upgraded migration.

insert into inventory.locations (id, name, location_type, is_active)
values
  ('11111111-1111-1111-1111-111111111111', 'Roxy (Main)', 'MainWarehouse', true),
  ('22222222-2222-2222-2222-222222222222', 'Mohamed Naguib (Retail)', 'SubWarehouse', true),
  ('33333333-3333-3333-3333-333333333333', 'Online', 'Online', true)
on conflict (id) do update
set name = excluded.name,
    location_type = excluded.location_type,
    is_active = excluded.is_active;

insert into shared.system_settings (key, value, description)
values
  ('low_stock_threshold_default', '10', 'Default low stock alert threshold (pieces)'),
  ('reserve_unresolved_days', '7', 'Days before an unresolved reserve triggers alert'),
  ('in_warehouse_expiry_months', '3', 'Months before expiry to fire in-warehouse alert'),
  ('merchant_held_expiry_months', '18', 'Months before expiry to fire merchant-held alert'),
  ('outstanding_balance_days', '30', 'Days since last payment to fire balance notification flags')
on conflict (key) do update
set value = excluded.value,
    description = excluded.description,
    updated_at = current_timestamp;

insert into identity.roles_permissions (id, role, permission)
values
  (uuid_generate_v4(), 'CLevel', 'catalog.read'),
  (uuid_generate_v4(), 'CLevel', 'inventory.read'),
  (uuid_generate_v4(), 'CLevel', 'operations.read'),
  (uuid_generate_v4(), 'CLevel', 'payments.read'),
  (uuid_generate_v4(), 'CLevel', 'reports.read'),
  (uuid_generate_v4(), 'CLevel', 'supply.read'),
  (uuid_generate_v4(), 'Admin', 'users.read'),
  (uuid_generate_v4(), 'Admin', 'users.write'),
  (uuid_generate_v4(), 'Admin', 'users.password.write'),
  (uuid_generate_v4(), 'Admin', 'catalog.read'),
  (uuid_generate_v4(), 'Admin', 'catalog.write'),
  (uuid_generate_v4(), 'Admin', 'inventory.read'),
  (uuid_generate_v4(), 'Admin', 'inventory.write'),
  (uuid_generate_v4(), 'Admin', 'operations.read'),
  (uuid_generate_v4(), 'Admin', 'operations.write'),
  (uuid_generate_v4(), 'Admin', 'payments.read'),
  (uuid_generate_v4(), 'Admin', 'payments.write'),
  (uuid_generate_v4(), 'Admin', 'payments.draft'),
  (uuid_generate_v4(), 'Admin', 'payments.approve'),
  (uuid_generate_v4(), 'Admin', 'reports.read'),
  (uuid_generate_v4(), 'Admin', 'supply.read'),
  (uuid_generate_v4(), 'Admin', 'supply.write'),
  (uuid_generate_v4(), 'Admin', 'audit.read'),
  (uuid_generate_v4(), 'Admin', 'settings.write'),
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
  (uuid_generate_v4(), 'ERPAdmin', 'settings.write'),
  (uuid_generate_v4(), 'Accountant', 'operations.read'),
  (uuid_generate_v4(), 'Accountant', 'payments.read'),
  (uuid_generate_v4(), 'Accountant', 'payments.draft'),
  (uuid_generate_v4(), 'Accountant', 'reports.read'),
  (uuid_generate_v4(), 'WarehouseClerk', 'catalog.read'),
  (uuid_generate_v4(), 'WarehouseClerk', 'inventory.read'),
  (uuid_generate_v4(), 'WarehouseClerk', 'operations.read'),
  (uuid_generate_v4(), 'WarehouseClerk', 'operations.write')
on conflict do nothing;

insert into identity.users (id, username, password_hash, full_name, role, location_id, is_active)
values
  (
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1',
    'admin',
    '<hash:12121212>',
    'Lansee Admin',
    'Admin',
    null,
    true
  ),
  (
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2',
    'clevel',
    '<hash:12121212>',
    'C-Level Executive',
    'CLevel',
    null,
    true
  ),
  (
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3',
    'accountant',
    '<hash:12121212>',
    'Lansee Accountant',
    'Accountant',
    null,
    true
  ),
  (
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa7',
    'erp_admin',
    '<hash:12121212>',
    'ERP Admin',
    'ERPAdmin',
    null,
    true
  ),
  (
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4',
    'roxy_clerk',
    '<hash:12121212>',
    'Roxy Warehouse Clerk',
    'WarehouseClerk',
    '11111111-1111-1111-1111-111111111111',
    true
  ),
  (
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa5',
    'retail_clerk',
    '<hash:12121212>',
    'Retail Warehouse Clerk',
    'WarehouseClerk',
    '22222222-2222-2222-2222-222222222222',
    true
  ),
  (
    'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa6',
    'online_clerk',
    '<hash:12121212>',
    'Online Warehouse Clerk',
    'WarehouseClerk',
    '33333333-3333-3333-3333-333333333333',
    true
  )
on conflict (id) do update
set password_hash = excluded.password_hash,
    full_name = excluded.full_name,
    role = excluded.role,
    location_id = excluded.location_id,
    is_active = excluded.is_active;

insert into catalog.categories (id, parent_id, name)
values
  ('10000000-0000-0000-0000-000000000001', null, 'Products'),
  ('10000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001', 'Lenses'),
  ('10000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000002', 'Colored Lenses'),
  ('10000000-0000-0000-0000-000000000004', '10000000-0000-0000-0000-000000000002', 'Medical Lenses'),
  ('10000000-0000-0000-0000-000000000005', '10000000-0000-0000-0000-000000000004', 'Plain Medical'),
  ('10000000-0000-0000-0000-000000000006', '10000000-0000-0000-0000-000000000004', 'Colored Medical'),
  ('10000000-0000-0000-0000-000000000007', '10000000-0000-0000-0000-000000000001', 'Solutions'),
  ('10000000-0000-0000-0000-000000000008', '10000000-0000-0000-0000-000000000007', 'Preservation / Conservative Solution')
on conflict (id) do update
set parent_id = excluded.parent_id,
    name = excluded.name;

insert into catalog.brands (id, name)
values
  ('20000000-0000-0000-0000-000000000001', 'Lansee'),
  ('20000000-0000-0000-0000-000000000002', 'FreshLook'),
  ('20000000-0000-0000-0000-000000000003', 'OptiCare')
on conflict (id) do update
set name = excluded.name;

insert into catalog.products (
  id,
  category_id,
  brand_id,
  name,
  product_type,
  expiry_type,
  sealed_expiry_duration,
  opened_expiry_rate,
  opened_expiry_duration,
  pieces_per_pack,
  sell_mode,
  clinical_params,
  extended_attributes,
  is_active
)
values
  (
    '30000000-0000-0000-0000-000000000001',
    '10000000-0000-0000-0000-000000000003',
    '20000000-0000-0000-0000-000000000002',
    'FreshLook Color Monthly',
    'Lens',
    'Batch',
    '3 years',
    'Annual',
    null,
    1,
    'SinglePiece',
    '{"duration":"monthly","diameter":"14.2"}',
    '{"target":"cosmetic"}',
    true
  ),
  (
    '30000000-0000-0000-0000-000000000002',
    '10000000-0000-0000-0000-000000000005',
    '20000000-0000-0000-0000-000000000001',
    'Lansee Clear Medical',
    'Lens',
    'Batch',
    '3 years',
    'Annual',
    null,
    1,
    'SinglePiece',
    '{"duration":"monthly","baseCurve":"8.6"}',
    '{"material":"hydrogel"}',
    true
  ),
  (
    '30000000-0000-0000-0000-000000000003',
    '10000000-0000-0000-0000-000000000008',
    '20000000-0000-0000-0000-000000000003',
    'OptiCare Solution 120ml',
    'Solution',
    'Product',
    '2 years',
    'Annual',
    null,
    1,
    'SinglePiece',
    null,
    '{"volumeMl":120}',
    true
  )
on conflict (id) do update
set category_id = excluded.category_id,
    brand_id = excluded.brand_id,
    name = excluded.name,
    product_type = excluded.product_type,
    expiry_type = excluded.expiry_type,
    sealed_expiry_duration = excluded.sealed_expiry_duration,
    opened_expiry_rate = excluded.opened_expiry_rate,
    opened_expiry_duration = excluded.opened_expiry_duration,
    pieces_per_pack = excluded.pieces_per_pack,
    sell_mode = excluded.sell_mode,
    clinical_params = excluded.clinical_params,
    extended_attributes = excluded.extended_attributes,
    is_active = excluded.is_active;

insert into catalog.skus (
  id,
  product_id,
  sku_code,
  power_sign,
  power_value,
  color_name,
  size,
  barcode,
  is_active
)
values
  ('40000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000001', 'FRE-CL-P0-BLUE', '+', 0.00, 'Blue', 'Monthly', '622000000001', true),
  ('40000000-0000-0000-0000-000000000002', '30000000-0000-0000-0000-000000000001', 'FRE-CL-P0-HAZEL', '+', 0.00, 'Hazel', 'Monthly', '622000000002', true),
  ('40000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000002', 'LAN-PM-M125-CLEAR', '-', 1.25, 'Clear', 'Monthly', '622000000003', true),
  ('40000000-0000-0000-0000-000000000004', '30000000-0000-0000-0000-000000000003', 'OPT-PCS-120ML', null, null, null, '120ml', '622000000004', true)
on conflict (id) do update
set product_id = excluded.product_id,
    sku_code = excluded.sku_code,
    power_sign = excluded.power_sign,
    power_value = excluded.power_value,
    color_name = excluded.color_name,
    size = excluded.size,
    barcode = excluded.barcode,
    is_active = excluded.is_active;
