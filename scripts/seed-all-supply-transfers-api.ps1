param(
    [string]$ComposeProjectName = "",
    [string]$DbService = "db",
    [string]$Database = "lensee",
    [string]$DbUser = "lensee_user",
    [int]$MaxSkus = 0,
    [int]$PacksPerDestination = 3000,
    [int]$MainTargetPacks = 3000,
    [decimal]$UnitPrice = 1,
    [string]$SupplierName = "Direct DB seed supplier",
    [string]$LotPrefix = "DB-SEED",
    [string]$ExpiryDate = "2028-12-31",
    [switch]$Preview
)

# Discovers the active catalog and active locations directly from PostgreSQL, then:
#   1. records one received supply shipment into MainWarehouse for every active SKU;
#   2. leaves MainTargetPacks in MainWarehouse;
#   3. creates one completed warehouse-transfer operation per non-main location;
#   4. transfers PacksPerDestination of every SKU to every destination;
#   5. sets stock targets for every seeded SKU/location balance.
#
# This script bypasses the HTTP API on purpose. Use it for local/dev data
# preparation only, because it writes the records and inventory effects directly.

$ErrorActionPreference = "Stop"

if ($MaxSkus -lt 0) { throw "MaxSkus must be zero or greater. Use zero for all active SKUs." }
if ($PacksPerDestination -le 0) { throw "PacksPerDestination must be greater than zero." }
if ($MainTargetPacks -lt 0) { throw "MainTargetPacks must be zero or greater." }
if ($UnitPrice -le 0) { throw "UnitPrice must be greater than zero." }
if ($SupplierName.Contains("'")) { throw "SupplierName cannot contain a single quote." }
if ($LotPrefix.Contains("'")) { throw "LotPrefix cannot contain a single quote." }

$parsedExpiry = [datetime]::MinValue
if (-not [datetime]::TryParse($ExpiryDate, [ref]$parsedExpiry)) {
    throw "ExpiryDate must be a valid date."
}

function Invoke-PostgresScript {
    param(
        [Parameter(Mandatory = $true)] [string]$Sql
    )

    $tempSql = Join-Path ([System.IO.Path]::GetTempPath()) ("lensee-seed-all-supply-transfers-{0}.sql" -f ([Guid]::NewGuid()))
    Set-Content -LiteralPath $tempSql -Value $Sql -Encoding UTF8
    try {
        $composeArgs = @("compose")
        if (-not [string]::IsNullOrWhiteSpace($ComposeProjectName)) {
            $composeArgs += @("--project-name", $ComposeProjectName)
        }
        $composeArgs += @("exec", "-T", $DbService, "psql", "-U", $DbUser, "-d", $Database, "-f", "-")

        Get-Content -LiteralPath $tempSql -Raw | & docker @composeArgs
        if ($LASTEXITCODE -ne 0) {
            throw "psql exited with code $LASTEXITCODE."
        }
    }
    finally {
        Remove-Item -LiteralPath $tempSql -Force -ErrorAction SilentlyContinue
    }
}

$transactionEnd = if ($Preview) { "ROLLBACK;" } else { "COMMIT;" }
$modeText = if ($Preview) { "Preview complete; transaction rolled back." } else { "Direct database seed committed." }
$safeExpiryDate = $parsedExpiry.ToString("yyyy-MM-dd")
$invoiceNumber = "DB-SEED-{0}" -f (Get-Date -Format yyyyMMddHHmmss)
$notes = "Direct database seed for all active SKUs"

$sqlTemplate = @'
\set ON_ERROR_STOP on

BEGIN;

CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

DO $$
DECLARE
    v_actor_id uuid;
    v_actor_name text;
    v_main_location_id uuid;
    v_main_location_name text;
    v_destination_count integer;
    v_sku_count integer;
    v_total_quantity integer;
    v_supply_id uuid;
    v_supply_operation_id uuid;
    v_supply_version_id uuid;
    v_operation_id uuid;
    v_version_id uuid;
    v_destination record;
BEGIN
    SELECT id, COALESCE(NULLIF(full_name, ''), username)
    INTO v_actor_id, v_actor_name
    FROM identity.users
    WHERE is_active = true
    ORDER BY
        CASE
            WHEN is_primary_admin = true THEN 0
            WHEN role IN ('Admin','ERPAdmin','Accountant') THEN 1
            ELSE 2
        END,
        id
    LIMIT 1;

    IF v_actor_id IS NULL THEN
        RAISE EXCEPTION 'No active user was found to own the seed records.';
    END IF;

    SELECT id, name
    INTO v_main_location_id, v_main_location_name
    FROM inventory.locations
    WHERE is_active = true AND location_type = 'MainWarehouse'
    ORDER BY id
    LIMIT 1;

    IF v_main_location_id IS NULL THEN
        RAISE EXCEPTION 'No active MainWarehouse location was found.';
    END IF;

    CREATE TEMP TABLE seed_locations ON COMMIT DROP AS
    SELECT id, name, location_type
    FROM inventory.locations
    WHERE is_active = true
    ORDER BY CASE WHEN id = v_main_location_id THEN 0 ELSE 1 END, id;

    CREATE TEMP TABLE seed_destinations ON COMMIT DROP AS
    SELECT id, name, location_type
    FROM seed_locations
    WHERE id <> v_main_location_id AND location_type <> 'MainWarehouse'
    ORDER BY id;

    SELECT COUNT(*) INTO v_destination_count FROM seed_destinations;
    IF v_destination_count = 0 THEN
        RAISE EXCEPTION 'No active non-main destination locations were found.';
    END IF;

    CREATE TEMP TABLE seed_skus ON COMMIT DROP AS
    SELECT
        sku.id AS sku_id,
        sku.sku_code,
        product.name AS product_name,
        LEFT('__LOT_PREFIX__-' || regexp_replace(sku.sku_code, '[^A-Za-z0-9_-]+', '-', 'g'), 100) AS lot_number,
        (__PACKS_PER_DESTINATION__ * v_destination_count) + __MAIN_TARGET_PACKS__ AS inbound_quantity
    FROM catalog.skus AS sku
    JOIN catalog.products AS product ON product.id = sku.product_id
    WHERE sku.is_active = true
      AND sku.deleted_at IS NULL
      AND product.is_active = true
      AND product.deleted_at IS NULL
    ORDER BY product.name, sku.sku_code, sku.id
    LIMIT CASE WHEN __MAX_SKUS__ > 0 THEN __MAX_SKUS__ ELSE NULL END;

    SELECT COUNT(*) INTO v_sku_count FROM seed_skus;
    IF v_sku_count = 0 THEN
        RAISE EXCEPTION 'No active SKUs were found.';
    END IF;

    SELECT SUM(inbound_quantity) INTO v_total_quantity FROM seed_skus;

    INSERT INTO operations.operation_logs (
        id,
        operation_number,
        operation_type,
        status,
        destination_location_id,
        payment_method,
        notes,
        is_deleted,
        created_by,
        confirmed_by,
        created_at,
        confirmed_at,
        created_actor_name,
        record_kind
    )
    VALUES (
        uuid_generate_v4(),
        'OP-' || to_char(clock_timestamp(), 'YYYYMMDDHH24MISSMS') || '-' || floor(random() * 1000)::int,
        'InventoryReceipt',
        'Received',
        v_main_location_id,
        NULL,
        '__NOTES__',
        false,
        v_actor_id,
        v_actor_id,
        now(),
        now(),
        v_actor_name,
        'Standard'
    )
    RETURNING id INTO v_supply_operation_id;

    INSERT INTO operations.operation_lines (
        operation_id,
        sku_id,
        product_name_snapshot,
        sku_code_snapshot,
        section,
        quantity,
        entry_mode,
        bonus_quantity,
        unit_price,
        line_total,
        expiry_date,
        lot_number,
        unit_cost,
        line_notes
    )
    SELECT
        v_supply_operation_id,
        sku_id,
        product_name,
        sku_code,
        'Standard',
        inbound_quantity,
        'Packs',
        0,
        __UNIT_PRICE__,
        inbound_quantity * __UNIT_PRICE__,
        DATE '__EXPIRY_DATE__',
        lot_number,
        __UNIT_PRICE__,
        'Direct DB seed receipt'
    FROM seed_skus;

    INSERT INTO operations.operation_versions (
        operation_id,
        version_number,
        snapshot_data,
        reason,
        edited_by,
        edited_at,
        edited_actor_name
    )
    VALUES (
        v_supply_operation_id,
        1,
        jsonb_build_object(
            'seed', 'seed-all-supply-transfers-api.ps1',
            'operationType', 'InventoryReceipt',
            'skuCount', v_sku_count,
            'totalQuantity', v_total_quantity,
            'location', v_main_location_name
        ),
        'Direct database seed',
        v_actor_id,
        now(),
        v_actor_name
    )
    RETURNING id INTO v_supply_version_id;

    UPDATE operations.operation_logs
    SET current_version_id = v_supply_version_id
    WHERE id = v_supply_operation_id;

    INSERT INTO operations.supply_shipments (
        shipment_number,
        supplier_name,
        invoice_number,
        shipment_date,
        destination_location_id,
        status,
        notes,
        product_subtotal,
        cost_subtotal,
        landed_total,
        created_by,
        created_at,
        updated_by,
        updated_at,
        confirmed_by,
        confirmed_at,
        inventory_receipt_operation_id
    )
    VALUES (
        'SUP-' || to_char(clock_timestamp(), 'YYYYMMDDHH24MISSMS') || '-' || floor(random() * 1000)::int,
        '__SUPPLIER_NAME__',
        '__INVOICE_NUMBER__',
        now(),
        v_main_location_id,
        'Received',
        '__NOTES__',
        v_total_quantity * __UNIT_PRICE__,
        0,
        v_total_quantity * __UNIT_PRICE__,
        v_actor_id,
        now(),
        v_actor_id,
        now(),
        v_actor_id,
        now(),
        v_supply_operation_id
    )
    RETURNING id INTO v_supply_id;

    INSERT INTO operations.supply_shipment_lines (
        shipment_id,
        sku_id,
        product_name_snapshot,
        sku_code_snapshot,
        quantity,
        unit_price,
        line_subtotal,
        allocated_cost,
        landed_unit_cost,
        lot_number,
        expiry_date,
        notes
    )
    SELECT
        v_supply_id,
        sku_id,
        product_name,
        sku_code,
        inbound_quantity,
        __UNIT_PRICE__,
        inbound_quantity * __UNIT_PRICE__,
        0,
        __UNIT_PRICE__,
        lot_number,
        DATE '__EXPIRY_DATE__',
        'Direct DB seed receipt'
    FROM seed_skus;

    INSERT INTO operations.supply_shipment_history (
        shipment_id,
        action,
        actor_user_id,
        created_at,
        summary,
        snapshot_data
    )
    VALUES (
        v_supply_id,
        'Received',
        v_actor_id,
        now(),
        'Direct DB seed received ' || v_sku_count || ' SKU(s).',
        jsonb_build_object('skuCount', v_sku_count, 'destinationLocationId', v_main_location_id)
    );

    INSERT INTO inventory.stock_balances (
        location_id,
        sku_id,
        available_qty,
        reserved_in_warehouse_qty,
        reserved_with_rep_qty,
        target_qty,
        row_version,
        last_updated
    )
    SELECT
        v_main_location_id,
        sku_id,
        inbound_quantity,
        0,
        0,
        __MAIN_TARGET_PACKS__,
        0,
        now()
    FROM seed_skus
    ON CONFLICT (location_id, sku_id) DO UPDATE
    SET available_qty = inventory.stock_balances.available_qty + EXCLUDED.available_qty,
        target_qty = EXCLUDED.target_qty,
        row_version = inventory.stock_balances.row_version + 1,
        last_updated = now();

    INSERT INTO inventory.inventory_batches (
        sku_id,
        location_id,
        lot_number,
        expiry_date,
        quantity,
        created_from,
        created_by,
        notes,
        created_at
    )
    SELECT
        sku_id,
        v_main_location_id,
        lot_number,
        DATE '__EXPIRY_DATE__',
        inbound_quantity,
        v_supply_operation_id,
        v_actor_id,
        'Direct DB seed receipt',
        now()
    FROM seed_skus;

    INSERT INTO inventory.stock_transactions (
        sku_id,
        location_id,
        transaction_type,
        quantity_change,
        reference_operation_id,
        user_id,
        created_at
    )
    SELECT
        sku_id,
        v_main_location_id,
        'Receipt',
        inbound_quantity,
        v_supply_operation_id,
        v_actor_id,
        now()
    FROM seed_skus;

    FOR v_destination IN SELECT * FROM seed_destinations ORDER BY id LOOP
        INSERT INTO operations.operation_logs (
            id,
            operation_number,
            operation_type,
            status,
            source_location_id,
            destination_location_id,
            payment_method,
            notes,
            is_deleted,
            created_by,
            confirmed_by,
            created_at,
            confirmed_at,
            created_actor_name,
            record_kind
        )
        VALUES (
            uuid_generate_v4(),
            'OP-' || to_char(clock_timestamp(), 'YYYYMMDDHH24MISSMS') || '-' || floor(random() * 1000)::int,
            'WarehouseTransfer',
            'Received',
            v_main_location_id,
            v_destination.id,
            NULL,
            'Direct DB seed transfer to ' || v_destination.name,
            false,
            v_actor_id,
            v_actor_id,
            now(),
            now(),
            v_actor_name,
            'Standard'
        )
        RETURNING id INTO v_operation_id;

        INSERT INTO operations.operation_lines (
            operation_id,
            sku_id,
            product_name_snapshot,
            sku_code_snapshot,
            section,
            quantity,
            entry_mode,
            bonus_quantity,
            unit_price,
            line_total,
            expiry_date,
            lot_number,
            unit_cost,
            line_notes
        )
        SELECT
            v_operation_id,
            sku_id,
            product_name,
            sku_code,
            'Standard',
            __PACKS_PER_DESTINATION__,
            'Packs',
            0,
            0,
            0,
            DATE '__EXPIRY_DATE__',
            lot_number,
            __UNIT_PRICE__,
            'Direct DB seed transfer'
        FROM seed_skus;

        INSERT INTO operations.operation_versions (
            operation_id,
            version_number,
            snapshot_data,
            reason,
            edited_by,
            edited_at,
            edited_actor_name
        )
        VALUES (
            v_operation_id,
            1,
            jsonb_build_object(
                'seed', 'seed-all-supply-transfers-api.ps1',
                'operationType', 'WarehouseTransfer',
                'skuCount', v_sku_count,
                'quantityPerSku', __PACKS_PER_DESTINATION__,
                'sourceLocationId', v_main_location_id,
                'destinationLocationId', v_destination.id
            ),
            'Direct database seed',
            v_actor_id,
            now(),
            v_actor_name
        )
        RETURNING id INTO v_version_id;

        UPDATE operations.operation_logs
        SET current_version_id = v_version_id
        WHERE id = v_operation_id;

        UPDATE inventory.stock_balances AS balance
        SET available_qty = balance.available_qty - __PACKS_PER_DESTINATION__,
            row_version = balance.row_version + 1,
            last_updated = now()
        FROM seed_skus AS sku
        WHERE balance.location_id = v_main_location_id
          AND balance.sku_id = sku.sku_id;

        INSERT INTO inventory.stock_balances (
            location_id,
            sku_id,
            available_qty,
            reserved_in_warehouse_qty,
            reserved_with_rep_qty,
            target_qty,
            row_version,
            last_updated
        )
        SELECT
            v_destination.id,
            sku_id,
            __PACKS_PER_DESTINATION__,
            0,
            0,
            __PACKS_PER_DESTINATION__,
            0,
            now()
        FROM seed_skus
        ON CONFLICT (location_id, sku_id) DO UPDATE
        SET available_qty = inventory.stock_balances.available_qty + EXCLUDED.available_qty,
            target_qty = EXCLUDED.target_qty,
            row_version = inventory.stock_balances.row_version + 1,
            last_updated = now();

        INSERT INTO inventory.inventory_batches (
            sku_id,
            location_id,
            lot_number,
            expiry_date,
            quantity,
            created_from,
            created_by,
            notes,
            created_at
        )
        SELECT
            sku_id,
            v_destination.id,
            lot_number,
            DATE '__EXPIRY_DATE__',
            __PACKS_PER_DESTINATION__,
            v_operation_id,
            v_actor_id,
            'Direct DB seed transfer in',
            now()
        FROM seed_skus;

        INSERT INTO inventory.stock_transactions (
            sku_id,
            location_id,
            transaction_type,
            quantity_change,
            reference_operation_id,
            user_id,
            created_at
        )
        SELECT
            sku_id,
            v_main_location_id,
            'SupplyOut',
            -__PACKS_PER_DESTINATION__,
            v_operation_id,
            v_actor_id,
            now()
        FROM seed_skus;

        INSERT INTO inventory.stock_transactions (
            sku_id,
            location_id,
            transaction_type,
            quantity_change,
            reference_operation_id,
            user_id,
            created_at
        )
        SELECT
            sku_id,
            v_destination.id,
            'SupplyIn',
            __PACKS_PER_DESTINATION__,
            v_operation_id,
            v_actor_id,
            now()
        FROM seed_skus;
    END LOOP;

    UPDATE inventory.stock_balances AS balance
    SET target_qty = CASE
            WHEN balance.location_id = v_main_location_id THEN __MAIN_TARGET_PACKS__
            ELSE __PACKS_PER_DESTINATION__
        END,
        row_version = balance.row_version + 1,
        last_updated = now()
    FROM seed_locations AS location
    JOIN seed_skus AS sku ON true
    WHERE balance.location_id = location.id
      AND balance.sku_id = sku.sku_id;

    RAISE NOTICE 'Seeded % SKU(s), % destination(s), supply shipment %, receipt operation %.',
        v_sku_count, v_destination_count, v_supply_id, v_supply_operation_id;
END $$;

SELECT
    location.name AS location,
    COUNT(*) AS sku_balances,
    SUM(balance.available_qty) AS total_available_packs,
    SUM(balance.target_qty) AS total_target_packs
FROM inventory.stock_balances AS balance
JOIN inventory.locations AS location ON location.id = balance.location_id
WHERE balance.sku_id IN (
    SELECT sku.id
    FROM catalog.skus AS sku
    JOIN catalog.products AS product ON product.id = sku.product_id
    WHERE sku.is_active = true
      AND sku.deleted_at IS NULL
      AND product.is_active = true
      AND product.deleted_at IS NULL
    ORDER BY product.name, sku.sku_code, sku.id
    LIMIT CASE WHEN __MAX_SKUS__ > 0 THEN __MAX_SKUS__ ELSE NULL END
)
GROUP BY location.name
ORDER BY location.name;

__TRANSACTION_END__

\echo __MODE_TEXT__
'@

$sql = $sqlTemplate.
    Replace("__MAX_SKUS__", [string]$MaxSkus).
    Replace("__PACKS_PER_DESTINATION__", [string]$PacksPerDestination).
    Replace("__MAIN_TARGET_PACKS__", [string]$MainTargetPacks).
    Replace("__UNIT_PRICE__", ([string]$UnitPrice).Replace(",", ".")).
    Replace("__SUPPLIER_NAME__", $SupplierName).
    Replace("__LOT_PREFIX__", $LotPrefix).
    Replace("__EXPIRY_DATE__", $safeExpiryDate).
    Replace("__INVOICE_NUMBER__", $invoiceNumber).
    Replace("__NOTES__", $notes).
    Replace("__TRANSACTION_END__", $transactionEnd).
    Replace("__MODE_TEXT__", $modeText)

Invoke-PostgresScript -Sql $sql
