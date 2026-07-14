-- Idempotent PRD-MVP gap bridge migration.
-- Aligns durable schema with current operations/payments/stocktake/reporting behavior.

create schema if not exists payments;
create schema if not exists reporting;

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
        'StocktakeAdjustment',
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

alter table if exists operations.stocktake_sessions
    drop constraint if exists chk_stocktake_status;

update operations.stocktake_sessions
set status = 'Draft'
where status = 'Open';

alter table if exists operations.stocktake_sessions
    alter column status set default 'Draft';

alter table if exists operations.stocktake_sessions
    add constraint chk_stocktake_status
    check (status in ('Draft', 'Confirmed'));

alter table if exists operations.stocktake_adjustment_lines
    add column if not exists lot_number varchar(100);

alter table if exists operations.stocktake_adjustment_lines
    add column if not exists expiry_date date;

alter table if exists operations.stocktake_adjustment_lines
    drop constraint if exists uq_stocktake_line_batch;

alter table if exists operations.stocktake_adjustment_lines
    add constraint uq_stocktake_line_batch
    unique (session_id, sku_id, lot_number, expiry_date);

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

alter table if exists payments.cash_records
    drop constraint if exists chk_cash_payment_type;

update payments.cash_records
set payment_type = 'CashReceived'
where payment_type in ('Cash', 'HandToHand');

alter table if exists payments.cash_records
    alter column payment_type set default 'CashReceived';

alter table if exists payments.cash_records
    add constraint chk_cash_payment_type
    check (payment_type in ('CashReceived', 'CashRefund'));

create table if not exists payments.financial_adjustments (
    id uuid primary key default uuid_generate_v4(),
    merchant_id uuid not null references crm.merchants(id),
    operation_id uuid references operations.operation_logs(id),
    adjustment_type varchar(50) not null,
    amount numeric(18,4) not null check (amount > 0),
    status varchar(50) not null default 'Completed',
    notes text,
    created_by uuid not null references identity.users(id),
    created_at timestamp without time zone not null default current_timestamp,
    constraint chk_financial_adjustment_type check (adjustment_type in ('MerchantCredit','BalanceReduction','CashRefund')),
    constraint chk_financial_adjustment_status check (status in ('Completed','Cancelled'))
);

create index if not exists idx_financial_adjustments_merchant
    on payments.financial_adjustments(merchant_id);

create index if not exists idx_financial_adjustments_operation
    on payments.financial_adjustments(operation_id)
    where operation_id is not null;
