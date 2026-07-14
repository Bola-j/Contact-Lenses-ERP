-- Idempotent patch for the Sprint 3 catalog expiry model.
-- Products store configured expiry durations/rates; inventory batches store concrete expiry dates.

alter table catalog.products
add column if not exists sealed_expiry_duration varchar(50);

alter table catalog.products
add column if not exists sealed_expiry_rate varchar(20);

alter table catalog.products
add column if not exists opened_expiry_duration varchar(50);

alter table catalog.products
drop constraint if exists chk_product_type;

update catalog.products
set product_type = 'Lens'
where product_type in ('ColoredLens', 'MedicalLens', 'PlainMedical', 'ColoredMedical', 'ContactLens');

update catalog.products
set product_type = 'Solution'
where product_type in ('LensSolution', 'CareSolution');

alter table catalog.products
add constraint chk_product_type check (product_type in ('Lens', 'Solution'));

alter table catalog.products
drop column if exists opened_expiry_days;

alter table catalog.products
drop column if exists sealed_expiry_days;

alter table catalog.products
drop column if exists opened_expiry_duration_value;

alter table catalog.products
drop column if exists opened_expiry_duration_unit;

alter table catalog.products
drop column if exists sealed_expiry_duration_value;

alter table catalog.products
drop column if exists sealed_expiry_duration_unit;

alter table inventory.inventory_batches
drop column if exists sealed_expiry_days;
