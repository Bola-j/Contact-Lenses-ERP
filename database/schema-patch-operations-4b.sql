-- Idempotent patch for Sprint 4B operations constraints.
-- Keeps operation_logs aligned with inventory receipt and warehouse transfer lifecycles.

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
