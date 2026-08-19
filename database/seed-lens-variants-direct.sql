create or replace function pg_temp.seed_uuid(value text)
returns uuid
language sql
immutable
as $$
  select (
    substr(md5(value), 1, 8) || '-' ||
    substr(md5(value), 9, 4) || '-' ||
    substr(md5(value), 13, 4) || '-' ||
    substr(md5(value), 17, 4) || '-' ||
    substr(md5(value), 21, 12)
  )::uuid;
$$;

insert into catalog.categories (id, parent_id, name)
values
  (pg_temp.seed_uuid('category:lenses'), null, 'Lenses'),
  (pg_temp.seed_uuid('category:lenses:medical'), pg_temp.seed_uuid('category:lenses'), 'Medical Lenses'),
  (pg_temp.seed_uuid('category:lenses:colored'), pg_temp.seed_uuid('category:lenses'), 'Colored Lenses'),
  (pg_temp.seed_uuid('category:solution'), null, 'Solution')
on conflict (id) do update
set parent_id = excluded.parent_id,
    name = excluded.name;

insert into catalog.brands (id, name)
values (pg_temp.seed_uuid('brand:clear-vision'), 'Clear Vision')
on conflict (id) do update
set name = excluded.name;

with lens_products as (
  select *
  from (values
    ('Plain Medical Lens Box - 1 Year', 'Medical Lenses', 3, 'SealedPackOnly', '1 year', 'Annual', '{"powerRange":"plainMedical","packaging":"Box","duration":"yearly"}'::jsonb, '{"seed":"medical-lenses-direct-db","packageCode":"BOX3","validity":"1 year"}'::jsonb),
    ('Plain Medical Lens Box - 3 Years', 'Medical Lenses', 3, 'SealedPackOnly', '3 years', 'Annual', '{"powerRange":"plainMedical","packaging":"Box","duration":"yearly"}'::jsonb, '{"seed":"medical-lenses-direct-db","packageCode":"BOX3","validity":"3 years"}'::jsonb),
    ('Plain Medical Lens Box - 5 Years', 'Medical Lenses', 3, 'SealedPackOnly', '5 years', 'Annual', '{"powerRange":"plainMedical","packaging":"Box","duration":"yearly"}'::jsonb, '{"seed":"medical-lenses-direct-db","packageCode":"BOX3","validity":"5 years"}'::jsonb),
    ('Plain Medical Lens Vial - 1 Year', 'Medical Lenses', 1, 'SealedPackOnly', '1 year', 'Annual', '{"powerRange":"plainMedical","packaging":"Vial","duration":"yearly"}'::jsonb, '{"seed":"medical-lenses-direct-db","packageCode":"VIAL1","validity":"1 year"}'::jsonb),
    ('Plain Medical Lens Vial - 3 Years', 'Medical Lenses', 1, 'SealedPackOnly', '3 years', 'Annual', '{"powerRange":"plainMedical","packaging":"Vial","duration":"yearly"}'::jsonb, '{"seed":"medical-lenses-direct-db","packageCode":"VIAL1","validity":"3 years"}'::jsonb),
    ('Plain Medical Lens Vial - 5 Years', 'Medical Lenses', 1, 'SealedPackOnly', '5 years', 'Annual', '{"powerRange":"plainMedical","packaging":"Vial","duration":"yearly"}'::jsonb, '{"seed":"medical-lenses-direct-db","packageCode":"VIAL1","validity":"5 years"}'::jsonb),
    ('Clear Vision Colored Lens Pack - 3 Months', 'Colored Lenses', 2, 'SinglePiece', '3 months', 'Monthly', '{"powerRange":"coloredMedical","duration":"3 months"}'::jsonb, '{"seed":"medical-lenses-direct-db","packageCode":"PACK2","validity":"3 months"}'::jsonb),
    ('Clear Vision Colored Lens Pack - 6 Months', 'Colored Lenses', 2, 'SinglePiece', '6 months', 'Monthly', '{"powerRange":"coloredMedical","duration":"6 months"}'::jsonb, '{"seed":"medical-lenses-direct-db","packageCode":"PACK2","validity":"6 months"}'::jsonb),
    ('Clear Vision Colored Lens Pack - 9 Months', 'Colored Lenses', 2, 'SinglePiece', '9 months', 'Monthly', '{"powerRange":"coloredMedical","duration":"9 months"}'::jsonb, '{"seed":"medical-lenses-direct-db","packageCode":"PACK2","validity":"9 months"}'::jsonb),
    ('Clear Vision Colored Lens Pack - 1 Day', 'Colored Lenses', 2, 'SinglePiece', '1 day', 'Daily', '{"powerRange":"coloredMedical","duration":"1 day"}'::jsonb, '{"seed":"medical-lenses-direct-db","packageCode":"PACK2","validity":"1 day"}'::jsonb),
    ('Clear Vision Colored Lens Pack - 5 Days', 'Colored Lenses', 2, 'SinglePiece', '5 days', 'Daily', '{"powerRange":"coloredMedical","duration":"5 days"}'::jsonb, '{"seed":"medical-lenses-direct-db","packageCode":"PACK2","validity":"5 days"}'::jsonb),
    ('Clear Vision Colored Lens Pack - 7 Days', 'Colored Lenses', 2, 'SinglePiece', '7 days', 'Daily', '{"powerRange":"coloredMedical","duration":"7 days"}'::jsonb, '{"seed":"medical-lenses-direct-db","packageCode":"PACK2","validity":"7 days"}'::jsonb)
  ) as product(name, category_name, pieces_per_pack, sell_mode, duration, rate, clinical_params, extended_attributes)
)
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
  is_active,
  deleted_at
)
select
  pg_temp.seed_uuid('product:' || product.name),
  category.id,
  pg_temp.seed_uuid('brand:clear-vision'),
  product.name,
  'Lens',
  'Batch',
  product.duration,
  product.rate,
  product.duration,
  product.pieces_per_pack,
  product.sell_mode,
  product.clinical_params,
  product.extended_attributes,
  true,
  null
from lens_products product
join catalog.categories category on category.name = product.category_name
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
    is_active = true,
    deleted_at = null;

with generic_lens_products as (
  select id
  from catalog.products
  where name in (
    'Clear Vision Colored Medical Lens Pack',
    'Plain Medical Lens Box',
    'Plain Medical Lens Vial'
  )
)
update catalog.skus sku
set is_active = false,
    deleted_at = coalesce(sku.deleted_at, current_timestamp)
from generic_lens_products product
where sku.product_id = product.id;

update catalog.products
set is_active = false,
    deleted_at = coalesce(deleted_at, current_timestamp)
where name in (
  'Clear Vision Colored Medical Lens Pack',
  'Plain Medical Lens Box',
  'Plain Medical Lens Vial'
);

update catalog.skus sku
set is_active = false,
    deleted_at = coalesce(sku.deleted_at, current_timestamp)
from catalog.products product
where sku.product_id = product.id
  and (
    product.name like 'Clear Vision Colored Lens Pack - %'
    or product.name like 'Plain Medical Lens Box - %'
    or product.name like 'Plain Medical Lens Vial - %'
  );

with
colors(color_name, color_code) as (
  values
    ('Green', 'GREEN'),
    ('Green 2T', 'GREEN2T'),
    ('Marine', 'MARINE'),
    ('Blue', 'BLUE'),
    ('True Sapphire', 'TRUESAPPHIRE'),
    ('Gray', 'GRAY'),
    ('Galaxy Gray', 'GALAXYGRAY'),
    ('Selena Gray', 'SELENAGRAY'),
    ('Hazel', 'HAZEL'),
    ('Pure Hazel', 'PUREHAZEL'),
    ('Sunset', 'SUNSET'),
    ('Jewel Brown', 'JEWELBROWN')
),
plain_powers as (
  select '-' as power_sign, value::numeric(5,2) as power_value
  from (
    select generate_series(0.50, 5.00, 0.25) as value
    union all
    select generate_series(5.50, 20.00, 0.50) as value
  ) values
  union all
  select '+' as power_sign, value::numeric(5,2) as power_value
  from (
    select generate_series(0.50, 5.00, 0.25) as value
    union all
    select generate_series(5.50, 10.00, 0.50) as value
  ) values
),
colored_powers as (
  select '-' as power_sign, value::numeric(5,2) as power_value
  from (
    select generate_series(0.50, 5.00, 0.25) as value
    union all
    select generate_series(5.50, 10.00, 0.50) as value
  ) values
  union all
  select '+' as power_sign, 0.00::numeric(5,2) as power_value
  union all
  select '+' as power_sign, value::numeric(5,2) as power_value
  from (
    select generate_series(0.50, 5.00, 0.25) as value
    union all
    select generate_series(5.50, 10.00, 0.50) as value
  ) values
),
sku_rows as (
  select
    pg_temp.seed_uuid('sku:' || product.name || ':' || power.power_sign || ':' || power.power_value || ':Plain:' || product.size) as id,
    product.id as product_id,
    concat(
      'CV-', product.category_code, '-',
      case when power.power_sign = '-' then 'M' else 'P' end,
      replace(regexp_replace(power.power_value::text, '\.0+$|(?<=\.\d)0$', '', 'g'), '.', ''),
      '-PLAIN-', replace(upper(product.size), ' ', ''),
      '-', product.duration_code,
      '-', product.rate_code
    ) as sku_code,
    power.power_sign,
    power.power_value,
    'Plain' as color_name,
    product.size,
    product.sort_order
  from (
    select id, name,
      'ML' as category_code,
      case
        when opened_expiry_duration like '%day%' then 'D' || lpad(split_part(opened_expiry_duration, ' ', 1), 2, '0')
        when opened_expiry_duration like '%month%' then 'M' || lpad(split_part(opened_expiry_duration, ' ', 1), 2, '0')
        when opened_expiry_duration like '%year%' then 'Y' || lpad(split_part(opened_expiry_duration, ' ', 1), 2, '0')
        else regexp_replace(upper(coalesce(opened_expiry_duration, 'NA')), '[^A-Z0-9]', '', 'g')
      end as duration_code,
      regexp_replace(upper(coalesce(opened_expiry_rate, 'NA')), '[^A-Z0-9]', '', 'g') as rate_code,
      case when name like '%Box%' then 'Box 3' else 'Vial 1' end as size,
      case
        when name like '%1 Year%' then 1
        when name like '%3 Years%' then 2
        when name like '%5 Years%' then 3
        else 99
      end as sort_order
    from catalog.products
    where name like 'Plain Medical Lens Box - %'
       or name like 'Plain Medical Lens Vial - %'
  ) product
  cross join plain_powers power

  union all

  select
    pg_temp.seed_uuid('sku:' || product.name || ':' || power.power_sign || ':' || power.power_value || ':' || color.color_name || ':Pack 2') as id,
    product.id as product_id,
    concat(
      'CV-CL-',
      case when power.power_sign = '-' then 'M' else 'P' end,
      replace(regexp_replace(power.power_value::text, '\.0+$|(?<=\.\d)0$', '', 'g'), '.', ''),
      '-', color.color_code,
      '-PACK2',
      '-', product.duration_code,
      '-', product.rate_code
    ) as sku_code,
    power.power_sign,
    power.power_value,
    color.color_name,
    'Pack 2',
    product.sort_order
  from (
    select id, name,
      case
        when opened_expiry_duration like '%day%' then 'D' || lpad(split_part(opened_expiry_duration, ' ', 1), 2, '0')
        when opened_expiry_duration like '%month%' then 'M' || lpad(split_part(opened_expiry_duration, ' ', 1), 2, '0')
        when opened_expiry_duration like '%year%' then 'Y' || lpad(split_part(opened_expiry_duration, ' ', 1), 2, '0')
        else regexp_replace(upper(coalesce(opened_expiry_duration, 'NA')), '[^A-Z0-9]', '', 'g')
      end as duration_code,
      regexp_replace(upper(coalesce(opened_expiry_rate, 'NA')), '[^A-Z0-9]', '', 'g') as rate_code,
      case
        when name like '%3 Months%' then 1
        when name like '%6 Months%' then 2
        when name like '%9 Months%' then 3
        when name like '%1 Day%' then 4
        when name like '%5 Days%' then 5
        when name like '%7 Days%' then 6
        else 99
      end as sort_order
    from catalog.products
    where name like 'Clear Vision Colored Lens Pack - %'
  ) product
  cross join colored_powers power
  cross join colors color
),
deduped_sku_rows as (
  select *
  from (
    select sku_rows.*, row_number() over (partition by sku_code order by sort_order, product_id) as duplicate_rank
    from sku_rows
  ) ranked
  where duplicate_rank = 1
)
insert into catalog.skus (
  id,
  product_id,
  sku_code,
  power_sign,
  power_value,
  color_name,
  size,
  barcode,
  is_active,
  deleted_at
)
select
  pg_temp.seed_uuid('sku-code:' || sku_code),
  product_id,
  sku_code,
  power_sign,
  power_value,
  color_name,
  size,
  null,
  true,
  null
from deduped_sku_rows
on conflict (sku_code) do update
set product_id = excluded.product_id,
    power_sign = excluded.power_sign,
    power_value = excluded.power_value,
    color_name = excluded.color_name,
    size = excluded.size,
    is_active = true,
    deleted_at = null;

select
  count(*) filter (where product_type = 'Lens' and is_active and name like 'Clear Vision Colored Lens Pack - %') as active_colored_validity_products,
  count(*) filter (where product_type = 'Lens' and is_active and (name like 'Plain Medical Lens Box - %' or name like 'Plain Medical Lens Vial - %')) as active_medical_validity_products,
  count(*) filter (where product_type = 'Lens' and is_active and name in ('Clear Vision Colored Medical Lens Pack', 'Plain Medical Lens Box', 'Plain Medical Lens Vial')) as active_generic_lens_products
from catalog.products;
