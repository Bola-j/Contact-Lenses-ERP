-- Legacy/manual fallback only.
-- The authoritative production path is now EF migrations:
-- - Identity: 20260722171100_AddErpAdminAndSupplyPermissions
-- - Operations: 20260722170716_AddSupplyShipments

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

create table if not exists operations.supply_shipments (
    id uuid primary key default uuid_generate_v4(),
    shipment_number varchar(50) not null unique default ('SUP-' || to_char(nextval('operations.operation_number_seq'), 'FM000000')),
    supplier_name varchar(255) not null,
    invoice_number varchar(100),
    shipment_date timestamp without time zone not null default current_timestamp,
    destination_location_id uuid not null references inventory.locations(id),
    status varchar(50) not null default 'Draft' check (status in ('Draft','Received','Cancelled')),
    notes text,
    product_subtotal numeric(18,4) not null default 0 check (product_subtotal >= 0),
    cost_subtotal numeric(18,4) not null default 0 check (cost_subtotal >= 0),
    landed_total numeric(18,4) not null default 0 check (landed_total >= 0),
    created_by uuid not null references identity.users(id),
    created_at timestamp without time zone not null default current_timestamp,
    updated_by uuid references identity.users(id),
    updated_at timestamp without time zone,
    confirmed_by uuid references identity.users(id),
    confirmed_at timestamp without time zone,
    cancelled_by uuid references identity.users(id),
    cancelled_at timestamp without time zone,
    inventory_receipt_operation_id uuid references operations.operation_logs(id)
);

create table if not exists operations.supply_shipment_lines (
    id uuid primary key default uuid_generate_v4(),
    shipment_id uuid not null references operations.supply_shipments(id) on delete cascade,
    sku_id uuid not null references catalog.skus(id),
    product_name_snapshot varchar(255) not null,
    sku_code_snapshot varchar(100) not null,
    quantity int not null check (quantity > 0),
    unit_price numeric(18,4) check (unit_price is null or unit_price >= 0),
    line_subtotal numeric(18,4) not null check (line_subtotal >= 0),
    allocated_cost numeric(18,4) not null default 0 check (allocated_cost >= 0),
    landed_unit_cost numeric(18,4) not null default 0 check (landed_unit_cost >= 0),
    lot_number varchar(100),
    expiry_date date,
    notes text
);

create table if not exists operations.supply_shipment_costs (
    id uuid primary key default uuid_generate_v4(),
    shipment_id uuid not null references operations.supply_shipments(id) on delete cascade,
    cost_type varchar(50) not null,
    description varchar(255),
    amount numeric(18,4) not null check (amount >= 0)
);

create table if not exists operations.supply_shipment_history (
    id uuid primary key default uuid_generate_v4(),
    shipment_id uuid not null references operations.supply_shipments(id) on delete cascade,
    action varchar(50) not null,
    actor_user_id uuid not null references identity.users(id),
    created_at timestamp without time zone not null default current_timestamp,
    summary text,
    snapshot_data jsonb
);

create index if not exists idx_supply_shipments_created_at on operations.supply_shipments(created_at desc);
create index if not exists idx_supply_shipments_destination on operations.supply_shipments(destination_location_id);
create index if not exists idx_supply_shipments_operation on operations.supply_shipments(inventory_receipt_operation_id);
create index if not exists idx_supply_shipments_status on operations.supply_shipments(status);
create index if not exists idx_supply_lines_shipment on operations.supply_shipment_lines(shipment_id);
create index if not exists idx_supply_lines_sku on operations.supply_shipment_lines(sku_id);
create index if not exists idx_supply_costs_shipment on operations.supply_shipment_costs(shipment_id);
create index if not exists idx_supply_history_shipment_created on operations.supply_shipment_history(shipment_id, created_at desc);

alter table if exists operations.supply_shipment_lines
    alter column unit_price drop not null;
