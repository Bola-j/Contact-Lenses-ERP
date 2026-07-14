insert into inventory.locations (id, name, location_type, is_active)
values
  ('11111111-1111-1111-1111-111111111111', 'Roxy (Main)', 'MainWarehouse', true),
  ('22222222-2222-2222-2222-222222222222', 'Mohamed Naguib (Retail)', 'SubWarehouse', true),
  ('33333333-3333-3333-3333-333333333333', 'Online', 'Online', true)
on conflict (id) do update
set name = excluded.name,
    location_type = excluded.location_type,
    is_active = excluded.is_active;
