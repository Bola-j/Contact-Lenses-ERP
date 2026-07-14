-- Idempotent patch for CRM + sales/reserve operations.

create table if not exists inventory.opened_piece_lots (
    id uuid primary key default uuid_generate_v4(),
    location_id uuid not null references inventory.locations(id),
    sku_id uuid not null,
    source_batch_id uuid not null,
    lot_number varchar(100),
    batch_expiry_date date,
    opened_date date not null,
    piece_expiry_date date,
    loose_piece_quantity int not null default 0 check (loose_piece_quantity >= 0),
    created_from uuid,
    created_by uuid not null,
    created_at timestamp without time zone not null default current_timestamp
);

create index if not exists idx_opened_piece_lots_fefo
    on inventory.opened_piece_lots(location_id, sku_id, piece_expiry_date);

alter table if exists operations.operation_logs
    drop constraint if exists chk_op_type;

alter table if exists operations.operation_logs
    add constraint chk_op_type
    check (operation_type in (
        'InventoryReceipt',
        'WarehouseTransfer',
        'WholesaleSale',
        'RetailSale',
        'Reserve',
        'Supply',
        'WriteOff',
        'Change',
        'Return'
    ));

alter table if exists operations.operation_logs
    drop constraint if exists chk_op_status;

alter table if exists operations.operation_logs
    add constraint chk_op_status
    check (status in (
        'Draft',
        'Confirmed',
        'Completed',
        'Reserved',
        'Shipped',
        'Received',
        'Cancelled'
    ));

alter table if exists inventory.stock_transactions
    drop constraint if exists chk_txn_type;

alter table if exists inventory.stock_transactions
    add constraint chk_txn_type
    check (transaction_type in (
        'Receipt',
        'Sale',
        'ReserveInWarehouse',
        'ReserveWithRep',
        'ReserveReleaseInWarehouse',
        'ReserveReleaseWithRep',
        'WriteOff',
        'SupplyOut',
        'SupplyIn',
        'StocktakeAdjustment',
        'ChangeOut',
        'ChangeIn',
        'ReturnIn'
    ));

create schema if not exists payments;

create table if not exists payments.cash_records (
    id uuid primary key default uuid_generate_v4(),
    operation_id uuid not null,
    payment_type varchar(50) not null default 'CashReceived',
    sub_type varchar(50),
    amount numeric(18,4) not null,
    status varchar(50) not null default 'Completed',
    payment_date timestamp without time zone not null default current_timestamp,
    created_by uuid not null,
    notes text
);

create table if not exists payments.main_payment_logs (
    id uuid primary key default uuid_generate_v4(),
    operation_id uuid not null,
    merchant_id uuid not null,
    total_amount numeric(18,4) not null,
    amount_paid numeric(18,4) not null default 0,
    payment_method varchar(50) not null default 'Installment',
    status varchar(50) not null default 'PendingAdmin',
    initialized_by uuid not null,
    initialized_at timestamp without time zone not null default current_timestamp,
    assigned_to uuid,
    assigned_at timestamp without time zone,
    last_modified_by uuid,
    last_modified_at timestamp without time zone not null default current_timestamp,
    notes text,
    is_deleted boolean not null default false
);

create table if not exists payments.installment_sub_logs (
    id uuid primary key default uuid_generate_v4(),
    main_log_id uuid not null references payments.main_payment_logs(id) on delete restrict,
    amount numeric(18,4) not null,
    payment_method varchar(50),
    date_received date not null,
    sub_log_status varchar(50) not null default 'Draft',
    drafted_by uuid not null,
    drafted_at timestamp without time zone not null default current_timestamp,
    confirmed_by uuid,
    confirmed_at timestamp without time zone,
    rejection_reason text,
    notes text
);

create index if not exists idx_cash_records_operation on payments.cash_records(operation_id);
create index if not exists idx_cash_records_date on payments.cash_records(payment_date desc);
create index if not exists idx_main_payment_operation on payments.main_payment_logs(operation_id);
create index if not exists idx_main_payment_merchant on payments.main_payment_logs(merchant_id);
create index if not exists idx_main_payment_assigned on payments.main_payment_logs(assigned_to) where assigned_to is not null;
create index if not exists idx_main_payment_status on payments.main_payment_logs(status) where is_deleted = false;
create index if not exists idx_sub_logs_main_log on payments.installment_sub_logs(main_log_id);
create index if not exists idx_sub_logs_status on payments.installment_sub_logs(sub_log_status);
