using Lensee.Modules.Operations.Data;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

public static class DatabaseCompatibility
{
    public static async Task EnsureSchemaAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();

        var logger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("DatabaseCompatibility");

        var operationsDbContext = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();

        try
        {
            await operationsDbContext.Database.ExecuteSqlRawAsync("""
                create extension if not exists "uuid-ossp";

                create schema if not exists identity;
                create schema if not exists catalog;
                create schema if not exists inventory;
                create schema if not exists crm;
                create schema if not exists operations;
                create schema if not exists payments;
                create schema if not exists notifications;
                create schema if not exists reporting;

                do $$
                begin
                    if to_regclass('identity.users') is not null then
                        alter table identity.users
                            drop constraint if exists chk_user_role;

                        alter table identity.users
                            add constraint chk_user_role
                            check (role in ('CLevel','Admin','ERPAdmin','Accountant','WarehouseClerk'));
                    end if;

                    if to_regclass('identity.roles_permissions') is not null then
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
                    end if;
                end $$;

                create table if not exists operations.supply_shipments (
                    id uuid primary key default uuid_generate_v4(),
                    shipment_number varchar(50) not null unique default ('SUP-' || to_char(nextval('operations.operation_number_seq'), 'FM000000')),
                    supplier_name varchar(255) not null,
                    invoice_number varchar(100),
                    shipment_date timestamp without time zone not null default current_timestamp,
                    destination_location_id uuid not null,
                    status varchar(50) not null default 'Draft',
                    notes text,
                    product_subtotal numeric(18,4) not null default 0,
                    cost_subtotal numeric(18,4) not null default 0,
                    landed_total numeric(18,4) not null default 0,
                    created_by uuid not null,
                    created_at timestamp without time zone not null default current_timestamp,
                    updated_by uuid,
                    updated_at timestamp without time zone,
                    confirmed_by uuid,
                    confirmed_at timestamp without time zone,
                    cancelled_by uuid,
                    cancelled_at timestamp without time zone,
                    inventory_receipt_operation_id uuid
                );

                create table if not exists operations.supply_shipment_lines (
                    id uuid primary key default uuid_generate_v4(),
                    shipment_id uuid not null references operations.supply_shipments(id) on delete cascade,
                    sku_id uuid not null,
                    product_name_snapshot varchar(255) not null,
                    sku_code_snapshot varchar(100) not null,
                    quantity int not null,
                    unit_price numeric(18,4),
                    line_subtotal numeric(18,4) not null,
                    allocated_cost numeric(18,4) not null default 0,
                    landed_unit_cost numeric(18,4) not null default 0,
                    lot_number varchar(100),
                    expiry_date date,
                    notes text
                );

                create table if not exists operations.supply_shipment_costs (
                    id uuid primary key default uuid_generate_v4(),
                    shipment_id uuid not null references operations.supply_shipments(id) on delete cascade,
                    cost_type varchar(50) not null,
                    description varchar(255),
                    amount numeric(18,4) not null
                );

                create table if not exists operations.supply_shipment_history (
                    id uuid primary key default uuid_generate_v4(),
                    shipment_id uuid not null references operations.supply_shipments(id) on delete cascade,
                    action varchar(50) not null,
                    actor_user_id uuid not null,
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

                do $$
                begin
                    if to_regclass('payments.main_payment_logs') is not null then
                        alter table payments.main_payment_logs
                            drop constraint if exists chk_main_payment_method;

                        alter table payments.main_payment_logs
                            add constraint chk_main_payment_method
                            check (payment_method in ('CashHandToHand','CashTransaction','Installment'));

                        alter table payments.cash_records
                            drop constraint if exists chk_cash_status;

                        alter table payments.cash_records
                            add constraint chk_cash_status
                            check (status in ('PendingAccountant','Completed','Cancelled'));

                        insert into payments.main_payment_logs (
                            id, operation_id, merchant_id, total_amount, amount_paid,
                            payment_method, status, initialized_by, initialized_at,
                            last_modified_by, last_modified_at, notes, is_deleted)
                        select
                            uuid_generate_v4(),
                            operation.id,
                            operation.client_id,
                            sum(record.amount),
                            sum(record.amount),
                            'CashHandToHand',
                            'Completed',
                            operation.created_by,
                            min(record.payment_date),
                            operation.created_by,
                            max(record.payment_date),
                            'Backfilled from completed cash sale.',
                            false
                        from payments.cash_records record
                        join operations.operation_logs operation on operation.id = record.operation_id
                        where record.payment_type = 'CashReceived'
                          and record.status = 'Completed'
                          and operation.operation_type in ('WholesaleSale', 'RetailSale')
                          and operation.status = 'Completed'
                          and operation.client_id is not null
                          and not exists (
                              select 1 from payments.main_payment_logs existing
                              where existing.operation_id = operation.id and existing.is_deleted = false)
                        group by operation.id, operation.client_id, operation.created_by;
                    end if;
                end $$;

                do $$
                begin
                    if to_regclass('crm.merchants') is not null
                       and to_regclass('operations.operation_logs') is not null
                       and to_regclass('payments.main_payment_logs') is not null
                       and to_regclass('payments.cash_records') is not null then
                        with anonymous_sales as (
                            select
                                operation.id,
                                operation.client_name,
                                operation.created_by,
                                operation.created_at,
                                operation.confirmed_at,
                                coalesce((
                                    select sum(line.line_total)
                                    from operations.operation_lines line
                                    where line.operation_id = operation.id
                                ), 0) as total_amount
                            from operations.operation_logs operation
                            where operation.operation_type = 'RetailSale'
                              and operation.status = 'Completed'
                              and operation.payment_method in ('CashHandToHand', 'CashTransaction')
                              and operation.client_id is null
                              and coalesce(nullif(btrim(operation.client_name), ''), '') <> ''
                        ),
                        inserted_merchants as (
                            insert into crm.merchants (
                                id, business_name, contact_person_name, phone_numbers,
                                business_type, status, notes, is_deleted, created_at, updated_at
                            )
                            select
                                uuid_generate_v4(),
                                sale.client_name,
                                sale.client_name,
                                '{{}}'::text[],
                                'Other',
                                'Active',
                                'Auto-created from anonymous cash sale backfill.',
                                false,
                                sale.created_at,
                                sale.created_at
                            from anonymous_sales sale
                            where not exists (
                                select 1
                                from crm.merchants merchant
                                where merchant.is_deleted = false
                                  and merchant.business_type = 'Other'
                                  and merchant.business_name = sale.client_name
                            )
                            returning id, business_name
                        ),
                        merchant_lookup as (
                            select merchant.id, merchant.business_name
                            from crm.merchants merchant
                            where merchant.is_deleted = false
                              and merchant.business_type = 'Other'

                            union all

                            select id, business_name
                            from inserted_merchants
                        )
                        update operations.operation_logs operation
                        set client_id = merchant.id
                        from merchant_lookup merchant
                        where operation.operation_type = 'RetailSale'
                          and operation.status = 'Completed'
                          and operation.payment_method in ('CashHandToHand', 'CashTransaction')
                          and operation.client_id is null
                          and operation.client_name = merchant.business_name;

                        insert into payments.cash_records (
                            id, operation_id, payment_type, sub_type, amount, status,
                            payment_date, created_by, notes
                        )
                        select
                            uuid_generate_v4(),
                            operation.id,
                            'CashReceived',
                            'CashHandToHand',
                            coalesce((
                                select sum(line.line_total)
                                from operations.operation_lines line
                                where line.operation_id = operation.id
                            ), 0),
                            'PendingAccountant',
                            coalesce(operation.confirmed_at, operation.created_at),
                            operation.created_by,
                            'Backfilled from anonymous completed cash sale.'
                        from operations.operation_logs operation
                        where operation.operation_type = 'RetailSale'
                          and operation.status = 'Completed'
                          and operation.payment_method = 'CashHandToHand'
                          and operation.client_id is not null
                          and not exists (
                              select 1
                              from payments.cash_records record
                              where record.operation_id = operation.id
                          )
                          and coalesce((
                              select sum(line.line_total)
                              from operations.operation_lines line
                              where line.operation_id = operation.id
                          ), 0) > 0;

                        insert into payments.main_payment_logs (
                            id, operation_id, merchant_id, total_amount, amount_paid,
                            payment_method, status, initialized_by, initialized_at,
                            last_modified_by, last_modified_at, notes, is_deleted
                        )
                        select
                            uuid_generate_v4(),
                            operation.id,
                            operation.client_id,
                            coalesce((
                                select sum(line.line_total)
                                from operations.operation_lines line
                                where line.operation_id = operation.id
                            ), 0),
                            0,
                            operation.payment_method,
                            case
                                when operation.payment_method = 'CashHandToHand' then 'PendingAccountant'
                                else 'PendingAdmin'
                            end,
                            operation.created_by,
                            operation.created_at,
                            operation.created_by,
                            coalesce(operation.confirmed_at, operation.created_at),
                            'Backfilled from anonymous completed cash sale.',
                            false
                        from operations.operation_logs operation
                        where operation.operation_type = 'RetailSale'
                          and operation.status = 'Completed'
                          and operation.payment_method in ('CashHandToHand', 'CashTransaction')
                          and operation.client_id is not null
                          and not exists (
                              select 1 from payments.main_payment_logs existing
                              where existing.operation_id = operation.id and existing.is_deleted = false
                          )
                          and coalesce((
                              select sum(line.line_total)
                              from operations.operation_lines line
                              where line.operation_id = operation.id
                          ), 0) > 0;
                    end if;
                end $$;

                do $$
                begin
                    if to_regclass('operations.operation_logs') is not null then
                        alter table operations.operation_logs
                            drop constraint if exists chk_op_type;

                        alter table operations.operation_logs
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

                        alter table operations.operation_logs
                            drop constraint if exists chk_op_status;

                        alter table operations.operation_logs
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
                    end if;
                end $$;

                do $$
                begin
                    if to_regclass('inventory.stock_transactions') is not null then
                        alter table inventory.stock_transactions
                            drop constraint if exists chk_txn_type;

                        alter table inventory.stock_transactions
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
                    end if;
                end $$;

                do $$
                begin
                    if to_regclass('inventory.locations') is not null then
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
                    end if;
                end $$;

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

                create index if not exists idx_cash_records_operation
                    on payments.cash_records(operation_id);

                create index if not exists idx_main_payment_operation
                    on payments.main_payment_logs(operation_id);

                create index if not exists idx_main_payment_merchant
                    on payments.main_payment_logs(merchant_id);

                create index if not exists idx_sub_logs_main_log
                    on payments.installment_sub_logs(main_log_id);
            """);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not update database compatibility objects.");
        }
    }
}
