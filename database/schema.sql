-- ==============================================================================
-- DATABASE CREATION SCRIPT: Lansee Multi-Location Platform (PRD v2.5)
-- Target: PostgreSQL 16+
-- Architecture: Modular Monolith â€” 9 Schemas
-- ==============================================================================

-- ==============================================================================
-- EXTENSIONS
-- ==============================================================================
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- ==============================================================================
-- SCHEMAS
-- ==============================================================================
CREATE SCHEMA IF NOT EXISTS shared;
CREATE SCHEMA IF NOT EXISTS identity;
CREATE SCHEMA IF NOT EXISTS catalog;
CREATE SCHEMA IF NOT EXISTS inventory;
CREATE SCHEMA IF NOT EXISTS crm;
CREATE SCHEMA IF NOT EXISTS operations;
CREATE SCHEMA IF NOT EXISTS payments;
CREATE SCHEMA IF NOT EXISTS notifications;
CREATE SCHEMA IF NOT EXISTS reporting;

-- ==============================================================================
-- SHARED SCHEMA
-- ==============================================================================
CREATE TABLE shared.system_settings (
    key         VARCHAR(100) PRIMARY KEY,
    value       TEXT         NOT NULL,
    description TEXT,
    updated_at  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- ==============================================================================
-- INVENTORY SCHEMA â€” Locations Base Table
-- ==============================================================================
CREATE TABLE inventory.locations (
    id            UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    name          VARCHAR(100) NOT NULL,
    location_type VARCHAR(50)  NOT NULL
        CONSTRAINT chk_location_type CHECK (location_type IN ('MainWarehouse','SubWarehouse','Online')),
    is_active     BOOLEAN      NOT NULL DEFAULT TRUE
);

-- ==============================================================================
-- IDENTITY SCHEMA
-- ==============================================================================
CREATE TABLE identity.users (
    id            UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    username      VARCHAR(100) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    full_name     VARCHAR(255) NOT NULL,
    role          VARCHAR(50)  NOT NULL
        CONSTRAINT chk_user_role CHECK (role IN ('CLevel','Admin','ERPAdmin','Accountant','WarehouseClerk')),
    location_id   UUID         REFERENCES inventory.locations(id),
    is_active     BOOLEAN      NOT NULL DEFAULT TRUE,
    created_at    TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    -- [FIX] Enforce WarehouseClerk explicit location containment invariant
    CONSTRAINT chk_user_location_by_role CHECK (
        (role = 'WarehouseClerk' AND location_id IS NOT NULL) OR
        (role != 'WarehouseClerk' AND location_id IS NULL)
    )
);

CREATE INDEX idx_users_role ON identity.users(role);
CREATE INDEX idx_users_location ON identity.users(location_id);

CREATE TABLE identity.refresh_tokens (
    id            UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id       UUID         NOT NULL REFERENCES identity.users(id) ON DELETE CASCADE,
    token_hash    VARCHAR(512) NOT NULL UNIQUE,
    expires_at    TIMESTAMP    NOT NULL,
    revoked_at    TIMESTAMP,
    replaced_by   UUID         REFERENCES identity.refresh_tokens(id),
    created_at    TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by_ip VARCHAR(45),
    revoked_by_ip VARCHAR(45)
);

CREATE INDEX idx_refresh_tokens_user ON identity.refresh_tokens(user_id);
CREATE INDEX idx_refresh_tokens_hash ON identity.refresh_tokens(token_hash);

CREATE TABLE identity.roles_permissions (
    id         UUID        PRIMARY KEY DEFAULT uuid_generate_v4(),
    role       VARCHAR(50) NOT NULL,
    permission VARCHAR(100) NOT NULL,
    CONSTRAINT uq_role_permission UNIQUE(role, permission)
);

CREATE TABLE identity.audit_logs (
    id                  UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    entity_type         VARCHAR(100) NOT NULL,
    entity_id           UUID         NOT NULL,
    action              VARCHAR(50)  NOT NULL,
    changed_fields      JSONB,
    stock_delta_applied INT,
    user_id             UUID         NOT NULL REFERENCES identity.users(id),
    ip_address          VARCHAR(45),
    created_at          TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_audit_logs_entity ON identity.audit_logs(entity_type, entity_id);
CREATE INDEX idx_audit_logs_user ON identity.audit_logs(user_id);
CREATE INDEX idx_audit_logs_created_at ON identity.audit_logs(created_at DESC);

-- ==============================================================================
-- CATALOG SCHEMA
-- ==============================================================================
CREATE TABLE catalog.categories (
    id         UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    parent_id  UUID         REFERENCES catalog.categories(id),
    name       VARCHAR(255) NOT NULL,
    created_at TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_categories_parent ON catalog.categories(parent_id);

CREATE TABLE catalog.brands (
    id         UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    name       VARCHAR(255) NOT NULL,
    created_at TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE catalog.products (
    id                  UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    category_id         UUID         NOT NULL REFERENCES catalog.categories(id),
    brand_id            UUID         NOT NULL REFERENCES catalog.brands(id),
    name                VARCHAR(255) NOT NULL,
    product_type        VARCHAR(50)  NOT NULL
        CONSTRAINT chk_product_type CHECK (product_type IN ('ColoredLens','MedicalLens','Solution')),
    expiry_type         VARCHAR(50),
    pieces_per_pack     INT          CHECK (pieces_per_pack IS NULL OR pieces_per_pack > 0),
    sell_mode           VARCHAR(50)
        CONSTRAINT chk_sell_mode CHECK (sell_mode IN ('SealedPackOnly','SinglePiece','Both')),
    clinical_params     JSONB,
    extended_attributes JSONB,
    is_active           BOOLEAN      NOT NULL DEFAULT TRUE,
    deleted_at          TIMESTAMP,
    created_at          TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_products_category ON catalog.products(category_id);
CREATE INDEX idx_products_brand ON catalog.products(brand_id);
CREATE INDEX idx_products_active ON catalog.products(is_active);

CREATE TABLE catalog.skus (
    id          UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    product_id  UUID         NOT NULL REFERENCES catalog.products(id),
    sku_code    VARCHAR(100) UNIQUE NOT NULL,
    power_sign  VARCHAR(1),
    power_value DECIMAL,
    color_name  VARCHAR(100),
    size        VARCHAR(50), 
    barcode     VARCHAR(255),
    is_active   BOOLEAN      NOT NULL DEFAULT TRUE,
    deleted_at  TIMESTAMP
);

CREATE INDEX idx_skus_product ON catalog.skus(product_id);
CREATE INDEX idx_skus_sku_code ON catalog.skus(sku_code);

-- ==============================================================================
-- CRM SCHEMA
-- ==============================================================================
CREATE TABLE crm.merchants (
    id                  UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    business_name       VARCHAR(255) NOT NULL,
    contact_person_name VARCHAR(255) NOT NULL,
    phone_numbers       TEXT[]       NOT NULL DEFAULT '{}',
    email               VARCHAR(255),
    address             TEXT,
    business_type       VARCHAR(50)  NOT NULL
        CONSTRAINT chk_merchant_business_type CHECK (business_type IN ('Merchant','Pharmacy','Oculist','BeautyCenter','Other')),
    status              VARCHAR(50)  NOT NULL DEFAULT 'Active'
        CONSTRAINT chk_merchant_status CHECK (status IN ('Active','Inactive','Suspended')),
    notes               TEXT,
    is_deleted          BOOLEAN      NOT NULL DEFAULT FALSE,
    deleted_at          TIMESTAMP,
    created_at          TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_merchants_status ON crm.merchants(status) WHERE is_deleted = FALSE;

CREATE TABLE crm.representatives (
    id                   UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    name                 VARCHAR(255) NOT NULL,
    phone_numbers        TEXT[]       NOT NULL DEFAULT '{}',
    email                VARCHAR(255),
    type                 VARCHAR(50)  NOT NULL DEFAULT 'External'
        CONSTRAINT chk_rep_type CHECK (type IN ('Internal','External')),
    linked_user_id       UUID         REFERENCES identity.users(id),
    assigned_location_id UUID         REFERENCES inventory.locations(id),
    status               VARCHAR(50)  NOT NULL DEFAULT 'Active'
        CONSTRAINT chk_rep_status CHECK (status IN ('Active','Inactive')),
    notes                TEXT,
    is_deleted           BOOLEAN      NOT NULL DEFAULT FALSE,
    deleted_at           TIMESTAMP
);

CREATE INDEX idx_representatives_status ON crm.representatives(status) WHERE is_deleted = FALSE;

CREATE TABLE crm.merchant_notes (
    id          UUID      PRIMARY KEY DEFAULT uuid_generate_v4(),
    merchant_id UUID      NOT NULL REFERENCES crm.merchants(id),
    note        TEXT      NOT NULL,
    added_by    UUID      NOT NULL REFERENCES identity.users(id),
    created_at  TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_merchant_notes_merchant ON crm.merchant_notes(merchant_id);

-- ==============================================================================
-- OPERATIONS SCHEMA
-- ==============================================================================
CREATE SEQUENCE operations.operation_number_seq START 1000 INCREMENT 1;

CREATE TABLE operations.operation_logs (
    id                      UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    operation_number        VARCHAR(50)  UNIQUE NOT NULL
        DEFAULT ('OP-' || TO_CHAR(NEXTVAL('operations.operation_number_seq'), 'FM000000')),
    operation_type          VARCHAR(50)  NOT NULL
        CONSTRAINT chk_op_type CHECK (operation_type IN (
            'WholesaleSale','RetailSale','OnlineSale','Change',
            'Reserve','Supply','InventoryReceipt','WriteOff','StocktakeAdjustment')),
    status                  VARCHAR(50)  NOT NULL DEFAULT 'Draft'
        CONSTRAINT chk_op_status CHECK (status IN ('Draft','Reserved','Confirmed','Cancelled')),
    source_location_id      UUID         REFERENCES inventory.locations(id),
    destination_location_id UUID         REFERENCES inventory.locations(id),
    client_id               UUID         REFERENCES crm.merchants(id),
    client_name             VARCHAR(255),
    representative_id       UUID         REFERENCES crm.representatives(id),
    payment_method          VARCHAR(50)
        CONSTRAINT chk_payment_method CHECK (payment_method IN ('CashHandToHand','CashTransaction','Installment') OR payment_method IS NULL),
    current_version_id      UUID,       -- FK established via ALTER post-table-creation
    notes                   TEXT,
    is_deleted              BOOLEAN      NOT NULL DEFAULT FALSE,
    deleted_at              TIMESTAMP,
    created_by              UUID         NOT NULL REFERENCES identity.users(id),
    confirmed_by            UUID         REFERENCES identity.users(id),
    created_at              TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    confirmed_at            TIMESTAMP
);

CREATE INDEX idx_op_logs_type_status ON operations.operation_logs(operation_type, status);
CREATE INDEX idx_op_logs_created_by ON operations.operation_logs(created_by);
CREATE INDEX idx_op_logs_client ON operations.operation_logs(client_id) WHERE client_id IS NOT NULL;
CREATE INDEX idx_op_logs_source_location ON operations.operation_logs(source_location_id);
CREATE INDEX idx_op_logs_created_at ON operations.operation_logs(created_at DESC);

CREATE TABLE operations.operation_lines (
    id                           UUID          PRIMARY KEY DEFAULT uuid_generate_v4(),
    operation_id                 UUID          NOT NULL REFERENCES operations.operation_logs(id),
    sku_id                       UUID          NOT NULL REFERENCES catalog.skus(id),
    product_name_snapshot        VARCHAR(255)  NOT NULL,
    sku_code_snapshot            VARCHAR(100)  NOT NULL,
    merchant_name_snapshot       VARCHAR(255),
    representative_name_snapshot VARCHAR(255),
    section                      VARCHAR(20)   NOT NULL DEFAULT 'Standard'
        CONSTRAINT chk_line_section CHECK (section IN ('Standard','ChangeOut','ChangeIn')),
    quantity                     INT           NOT NULL CHECK (quantity > 0),
    entry_mode                   VARCHAR(50)   NOT NULL DEFAULT 'Pieces'
        CONSTRAINT chk_entry_mode CHECK (entry_mode IN ('Packs','Pieces')),
    bonus_quantity               INT           NOT NULL DEFAULT 0 CHECK (bonus_quantity >= 0),
    unit_price                   DECIMAL(18,4) NOT NULL CHECK (unit_price >= 0),
    line_total                   DECIMAL(18,4) NOT NULL,
    write_off_reason             VARCHAR(50)
        CONSTRAINT chk_write_off_reason CHECK (write_off_reason IN
            ('Expired','Damaged','Lost','Miscounted','Other') OR write_off_reason IS NULL),
    write_off_reason_text        TEXT,   
    expiry_date                  DATE,
    lot_number                   VARCHAR(100),
    unit_cost                    DECIMAL(18,4),
    line_notes                   TEXT,
    CONSTRAINT chk_line_total CHECK (line_total = quantity * unit_price)
);

CREATE INDEX idx_op_lines_operation ON operations.operation_lines(operation_id);
CREATE INDEX idx_op_lines_sku ON operations.operation_lines(sku_id);

CREATE TABLE operations.operation_versions (
    id             UUID      PRIMARY KEY DEFAULT uuid_generate_v4(),
    operation_id   UUID      NOT NULL REFERENCES operations.operation_logs(id),
    version_number INT       NOT NULL CHECK (version_number >= 1),
    snapshot_data  JSONB     NOT NULL,
    reason         TEXT      NOT NULL DEFAULT 'Initial',
    edited_by      UUID      NOT NULL REFERENCES identity.users(id),
    edited_at      TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_op_version UNIQUE(operation_id, version_number)
);

CREATE INDEX idx_op_versions_operation ON operations.operation_versions(operation_id);

ALTER TABLE operations.operation_logs
    ADD CONSTRAINT fk_current_version
    FOREIGN KEY (current_version_id) REFERENCES operations.operation_versions(id);

-- Receipt-specific header fields (1:1 extension of operation_logs for InventoryReceipt type only)
CREATE TABLE operations.inventory_receipt_headers (
    id              UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    operation_id    UUID         NOT NULL UNIQUE REFERENCES operations.operation_logs(id),
    supplier_name   VARCHAR(255) NOT NULL,
    invoice_number  VARCHAR(100),
    receipt_date    TIMESTAMP    NOT NULL
);

CREATE INDEX idx_receipt_headers_operation ON operations.inventory_receipt_headers(operation_id);

CREATE TABLE operations.supply_shipments (
    id                             UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    shipment_number                VARCHAR(50)  NOT NULL UNIQUE
        DEFAULT ('SUP-' || TO_CHAR(NEXTVAL('operations.operation_number_seq'), 'FM000000')),
    supplier_name                  VARCHAR(255) NOT NULL,
    invoice_number                 VARCHAR(100),
    shipment_date                  TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    destination_location_id        UUID         NOT NULL REFERENCES inventory.locations(id),
    status                         VARCHAR(50)  NOT NULL DEFAULT 'Draft'
        CONSTRAINT chk_supply_shipments_status CHECK (status IN ('Draft','Received','Cancelled')),
    notes                          TEXT,
    product_subtotal               NUMERIC(18,4) NOT NULL DEFAULT 0
        CONSTRAINT chk_supply_shipments_product_subtotal CHECK (product_subtotal >= 0),
    cost_subtotal                  NUMERIC(18,4) NOT NULL DEFAULT 0
        CONSTRAINT chk_supply_shipments_cost_subtotal CHECK (cost_subtotal >= 0),
    landed_total                   NUMERIC(18,4) NOT NULL DEFAULT 0
        CONSTRAINT chk_supply_shipments_landed_total CHECK (landed_total >= 0),
    created_by                     UUID         NOT NULL REFERENCES identity.users(id),
    created_at                     TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_by                     UUID         REFERENCES identity.users(id),
    updated_at                     TIMESTAMP,
    confirmed_by                   UUID         REFERENCES identity.users(id),
    confirmed_at                   TIMESTAMP,
    cancelled_by                   UUID         REFERENCES identity.users(id),
    cancelled_at                   TIMESTAMP,
    inventory_receipt_operation_id UUID         REFERENCES operations.operation_logs(id)
);

CREATE INDEX idx_supply_shipments_created_at ON operations.supply_shipments(created_at DESC);
CREATE INDEX idx_supply_shipments_destination ON operations.supply_shipments(destination_location_id);
CREATE INDEX idx_supply_shipments_operation ON operations.supply_shipments(inventory_receipt_operation_id);
CREATE INDEX idx_supply_shipments_status ON operations.supply_shipments(status);

CREATE TABLE operations.supply_shipment_lines (
    id                    UUID          PRIMARY KEY DEFAULT uuid_generate_v4(),
    shipment_id           UUID          NOT NULL REFERENCES operations.supply_shipments(id) ON DELETE CASCADE,
    sku_id                UUID          NOT NULL REFERENCES catalog.skus(id),
    product_name_snapshot VARCHAR(255)  NOT NULL,
    sku_code_snapshot     VARCHAR(100)  NOT NULL,
    quantity              INTEGER       NOT NULL CONSTRAINT chk_supply_lines_quantity CHECK (quantity > 0),
    unit_price            NUMERIC(18,4) CONSTRAINT chk_supply_lines_unit_price CHECK (unit_price IS NULL OR unit_price >= 0),
    line_subtotal         NUMERIC(18,4) NOT NULL CONSTRAINT chk_supply_lines_line_subtotal CHECK (line_subtotal >= 0),
    allocated_cost        NUMERIC(18,4) NOT NULL DEFAULT 0 CONSTRAINT chk_supply_lines_allocated_cost CHECK (allocated_cost >= 0),
    landed_unit_cost      NUMERIC(18,4) NOT NULL DEFAULT 0 CONSTRAINT chk_supply_lines_landed_unit_cost CHECK (landed_unit_cost >= 0),
    lot_number            VARCHAR(100),
    expiry_date           DATE,
    notes                 TEXT
);

CREATE INDEX idx_supply_lines_shipment ON operations.supply_shipment_lines(shipment_id);
CREATE INDEX idx_supply_lines_sku ON operations.supply_shipment_lines(sku_id);

CREATE TABLE operations.supply_shipment_costs (
    id          UUID          PRIMARY KEY DEFAULT uuid_generate_v4(),
    shipment_id UUID          NOT NULL REFERENCES operations.supply_shipments(id) ON DELETE CASCADE,
    cost_type   VARCHAR(50)   NOT NULL,
    description VARCHAR(255),
    amount      NUMERIC(18,4) NOT NULL CONSTRAINT chk_supply_costs_amount CHECK (amount >= 0)
);

CREATE INDEX idx_supply_costs_shipment ON operations.supply_shipment_costs(shipment_id);

CREATE TABLE operations.supply_shipment_history (
    id            UUID        PRIMARY KEY DEFAULT uuid_generate_v4(),
    shipment_id   UUID        NOT NULL REFERENCES operations.supply_shipments(id) ON DELETE CASCADE,
    action        VARCHAR(50) NOT NULL,
    actor_user_id UUID        NOT NULL REFERENCES identity.users(id),
    created_at    TIMESTAMP   NOT NULL DEFAULT CURRENT_TIMESTAMP,
    summary       TEXT,
    snapshot_data JSONB
);

CREATE INDEX idx_supply_history_shipment_created ON operations.supply_shipment_history(shipment_id, created_at DESC);

CREATE TABLE operations.stocktake_sessions (
    id                      UUID      PRIMARY KEY DEFAULT uuid_generate_v4(),
    location_id             UUID      NOT NULL REFERENCES inventory.locations(id),
    session_date            TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    performed_by            UUID      NOT NULL REFERENCES identity.users(id),
    confirmed_by            UUID      REFERENCES identity.users(id),
    products_counted        INT       CHECK (products_counted IS NULL OR products_counted >= 0),
    total_discrepancy_units INT,
    notes                   TEXT,
    status                  VARCHAR(50) NOT NULL DEFAULT 'Open'
        CONSTRAINT chk_stocktake_status CHECK (status IN ('Open','Confirmed')),
    created_at              TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    confirmed_at            TIMESTAMP
);

CREATE INDEX idx_stocktake_location ON operations.stocktake_sessions(location_id);

CREATE TABLE operations.stocktake_adjustment_lines (
    id                UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    session_id        UUID NOT NULL REFERENCES operations.stocktake_sessions(id) ON DELETE RESTRICT,
    sku_id            UUID NOT NULL REFERENCES catalog.skus(id),
    system_qty_before INT NOT NULL CHECK (system_qty_before >= 0),
    physical_count    INT NOT NULL CHECK (physical_count >= 0),
    delta             INT NOT NULL,   
    line_note         TEXT,
    CONSTRAINT chk_delta_consistency CHECK (delta = physical_count - system_qty_before)
);

CREATE INDEX idx_stocktake_adj_session ON operations.stocktake_adjustment_lines(session_id);

-- ==============================================================================
-- INVENTORY SCHEMA â€” Balances, Batches, & Transactions
-- ==============================================================================
CREATE TABLE inventory.stock_balances (
    id                        UUID      PRIMARY KEY DEFAULT uuid_generate_v4(),
    location_id               UUID      NOT NULL REFERENCES inventory.locations(id),
    sku_id                    UUID      NOT NULL REFERENCES catalog.skus(id),
    available_qty             INT       NOT NULL DEFAULT 0 CHECK (available_qty >= 0),
    reserved_in_warehouse_qty INT       NOT NULL DEFAULT 0 CHECK (reserved_in_warehouse_qty >= 0),
    reserved_with_rep_qty     INT       NOT NULL DEFAULT 0 CHECK (reserved_with_rep_qty >= 0),
    target_qty                INT       CHECK (target_qty IS NULL OR target_qty >= 0),
    row_version               INT       NOT NULL DEFAULT 0,
    last_updated              TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT uq_location_sku UNIQUE(location_id, sku_id)
);

CREATE INDEX idx_stock_balances_sku ON inventory.stock_balances(sku_id);
CREATE INDEX idx_stock_balances_available ON inventory.stock_balances(location_id, available_qty);

CREATE TABLE inventory.inventory_batches (
    id           UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    sku_id       UUID         NOT NULL REFERENCES catalog.skus(id),
    location_id  UUID         NOT NULL REFERENCES inventory.locations(id),
    lot_number   VARCHAR(100),
    expiry_date  DATE,
    quantity     INT          NOT NULL DEFAULT 0 CHECK (quantity >= 0),
    created_from UUID         REFERENCES operations.operation_logs(id),
    created_by   UUID         REFERENCES identity.users(id),
    notes        TEXT,
    created_at   TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_inv_batches_expiry ON inventory.inventory_batches(expiry_date) WHERE expiry_date IS NOT NULL;
CREATE INDEX idx_inv_batches_location_sku ON inventory.inventory_batches(location_id, sku_id);

CREATE TABLE inventory.stock_transactions (
    id                     UUID      PRIMARY KEY DEFAULT uuid_generate_v4(),
    sku_id                 UUID      NOT NULL REFERENCES catalog.skus(id),
    location_id            UUID      NOT NULL REFERENCES inventory.locations(id),
    transaction_type       VARCHAR(50) NOT NULL
        CONSTRAINT chk_txn_type CHECK (transaction_type IN (
            'Receipt','Sale','SupplyOut','SupplyIn',
            'ReserveInWarehouse','ReserveWithRep',
            'ReserveReleaseInWarehouse','ReserveReleaseWithRep',
            'WriteOff','StocktakeAdjustment',
            'ChangeOut','ChangeIn')),
    quantity_change        INT       NOT NULL CHECK (quantity_change != 0),
    reference_operation_id UUID      REFERENCES operations.operation_logs(id),
    user_id                UUID      NOT NULL REFERENCES identity.users(id),
    created_at             TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_stock_txn_sku_location ON inventory.stock_transactions(sku_id, location_id, created_at DESC);
CREATE INDEX idx_stock_txn_operation ON inventory.stock_transactions(reference_operation_id) WHERE reference_operation_id IS NOT NULL;
CREATE INDEX idx_stock_txn_user ON inventory.stock_transactions(user_id);

-- ==============================================================================
-- PAYMENTS SCHEMA
-- ==============================================================================
CREATE TABLE payments.cash_records (
    id           UUID          PRIMARY KEY DEFAULT uuid_generate_v4(),
    operation_id UUID          NOT NULL REFERENCES operations.operation_logs(id),
    payment_type VARCHAR(50)   NOT NULL DEFAULT 'Cash',
    sub_type     VARCHAR(50)
        CONSTRAINT chk_cash_sub_type CHECK (sub_type IN ('HandToHand','BankTransaction')),
    amount       DECIMAL(18,4) NOT NULL CHECK (amount > 0),
    status       VARCHAR(50)   NOT NULL DEFAULT 'Completed',
    payment_date TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by   UUID          NOT NULL REFERENCES identity.users(id),
    notes        TEXT
);

CREATE INDEX idx_cash_records_operation ON payments.cash_records(operation_id);
CREATE INDEX idx_cash_records_date ON payments.cash_records(payment_date DESC);

CREATE TABLE payments.main_payment_logs (
    id               UUID          PRIMARY KEY DEFAULT uuid_generate_v4(),
    operation_id     UUID          NOT NULL REFERENCES operations.operation_logs(id),
    merchant_id      UUID          NOT NULL REFERENCES crm.merchants(id),
    total_amount     DECIMAL(18,4) NOT NULL CHECK (total_amount > 0),
    amount_paid      DECIMAL(18,4) NOT NULL DEFAULT 0 CHECK (amount_paid >= 0),
    payment_method   VARCHAR(50)   NOT NULL DEFAULT 'Installment',
    status           VARCHAR(50)   NOT NULL DEFAULT 'PendingAdmin'
        CONSTRAINT chk_payment_log_status CHECK (status IN (
            'PendingAdmin','PendingAccountant','PendingAdminReview','Completed')),
    initialized_by   UUID          NOT NULL REFERENCES identity.users(id),
    initialized_at   TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    assigned_to      UUID          REFERENCES identity.users(id),
    assigned_at      TIMESTAMP,
    last_modified_by UUID          REFERENCES identity.users(id),
    last_modified_at TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    notes            TEXT,
    is_deleted       BOOLEAN       NOT NULL DEFAULT FALSE
);

CREATE INDEX idx_main_payment_status ON payments.main_payment_logs(status) WHERE is_deleted = FALSE;
CREATE INDEX idx_main_payment_merchant ON payments.main_payment_logs(merchant_id);
CREATE INDEX idx_main_payment_assigned ON payments.main_payment_logs(assigned_to) WHERE assigned_to IS NOT NULL;
CREATE INDEX idx_main_payment_operation ON payments.main_payment_logs(operation_id);

CREATE TABLE payments.installment_sub_logs (
    id               UUID          PRIMARY KEY DEFAULT uuid_generate_v4(),
    main_log_id      UUID          NOT NULL REFERENCES payments.main_payment_logs(id) ON DELETE RESTRICT,
    amount           DECIMAL(18,4) NOT NULL CHECK (amount > 0),
    payment_method   VARCHAR(50)
        CONSTRAINT chk_sub_log_method CHECK (payment_method IN ('CashHandToHand','CashTransaction')),
    date_received    DATE          NOT NULL,
    sub_log_status   VARCHAR(50)   NOT NULL DEFAULT 'Draft'
        CONSTRAINT chk_sub_log_status CHECK (sub_log_status IN ('Draft','Confirmed','Rejected')),
    drafted_by       UUID          NOT NULL REFERENCES identity.users(id),
    drafted_at       TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    confirmed_by     UUID          REFERENCES identity.users(id),
    confirmed_at     TIMESTAMP,
    rejection_reason TEXT,
    notes            TEXT
);

CREATE INDEX idx_sub_logs_main_log ON payments.installment_sub_logs(main_log_id);
CREATE INDEX idx_sub_logs_status ON payments.installment_sub_logs(sub_log_status);

-- ==============================================================================
-- NOTIFICATIONS SCHEMA
-- ==============================================================================
CREATE TABLE notifications.alert_configs (
    id              UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    alert_type      VARCHAR(100) NOT NULL,
    threshold_value INT,
    threshold_unit  VARCHAR(50),
    is_active       BOOLEAN      NOT NULL DEFAULT TRUE
);

CREATE TABLE notifications.notification_logs (
    id             UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    alert_type     VARCHAR(100) NOT NULL,
    message        TEXT         NOT NULL,
    reference_id   UUID,       
    reference_type VARCHAR(100),
    target_user_id UUID         REFERENCES identity.users(id),
    target_role    VARCHAR(50),
    channel        VARCHAR(50)  NOT NULL DEFAULT 'InApp'
        CONSTRAINT chk_channel CHECK (channel IN ('InApp','WhatsApp')),
    is_read        BOOLEAN      NOT NULL DEFAULT FALSE,
    created_at     TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX idx_notif_logs_user_unread ON notifications.notification_logs(target_user_id, is_read) WHERE is_read = FALSE;
CREATE INDEX idx_notif_logs_created_at ON notifications.notification_logs(created_at DESC);

-- ==============================================================================
-- REPORTING SCHEMA
-- ==============================================================================
CREATE TABLE reporting.export_logs (
    id            UUID         PRIMARY KEY DEFAULT uuid_generate_v4(),
    report_type   VARCHAR(50)  NOT NULL,
    requested_by  UUID         REFERENCES identity.users(id),
    generated_url VARCHAR(500),
    created_at    TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- ==============================================================================
-- PROCEDURAL TRIGGERS & BUSINESS RULES ENFORCEMENT
-- ==============================================================================

-- ------------------------------------------------------------------------------
-- 1. IMMUTABILITY RULE: unit_price on operation_lines (PRD Section 7.5)
-- ------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION operations.prevent_unit_price_update()
RETURNS TRIGGER AS $$
BEGIN
    IF OLD.unit_price != NEW.unit_price THEN
        RAISE EXCEPTION 'unit_price is completely immutable after creation on operation_lines (line_id: %). PRD Section 7.5.', OLD.id;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_prevent_unit_price_update
    BEFORE UPDATE OF unit_price ON operations.operation_lines
    FOR EACH ROW EXECUTE FUNCTION operations.prevent_unit_price_update();

-- ------------------------------------------------------------------------------
-- 2. IMMUTABILITY RULE: stock_transactions append-only context (PRD Section 11.2)
-- ------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION inventory.prevent_stock_transaction_mutation()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'stock_transactions records are strictly insert-only and cannot be mutated or deleted (txn_id: %). PRD Section 11.2.', OLD.id;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_prevent_stock_txn_update
    BEFORE UPDATE ON inventory.stock_transactions
    FOR EACH ROW EXECUTE FUNCTION inventory.prevent_stock_transaction_mutation();

CREATE TRIGGER trg_prevent_stock_txn_delete
    BEFORE DELETE ON inventory.stock_transactions
    FOR EACH ROW EXECUTE FUNCTION inventory.prevent_stock_transaction_mutation();

-- ------------------------------------------------------------------------------
-- 3. STATE SYNCHRONIZATION: amount_paid & status machine logic on main_payment_logs
-- ------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION payments.refresh_amount_paid()
RETURNS TRIGGER AS $$
DECLARE
    v_main_log_id  UUID;
    v_total_paid   DECIMAL(18,4);
    v_total_amount DECIMAL(18,4);
    v_old_status   VARCHAR(50);
BEGIN
    -- Safely acquire target scope across INSERT, UPDATE, or DELETE operations
    v_main_log_id := COALESCE(NEW.main_log_id, OLD.main_log_id);

    -- Compute confirmed financial flows 
    SELECT COALESCE(SUM(amount), 0)
    INTO v_total_paid
    FROM payments.installment_sub_logs
    WHERE main_log_id = v_main_log_id
      AND sub_log_status = 'Confirmed';

    -- Retrieve structural baseline configuration flags
    SELECT total_amount, status 
    INTO v_total_amount, v_old_status
    FROM payments.main_payment_logs
    WHERE id = v_main_log_id;

    -- Update parents properties and correct historical status traps dynamically
    UPDATE payments.main_payment_logs
    SET amount_paid      = v_total_paid,
        last_modified_at = CURRENT_TIMESTAMP,
        status = CASE
            WHEN v_total_paid >= v_total_amount THEN 'Completed'
            WHEN v_old_status = 'Completed' AND v_total_paid < v_total_amount THEN 'PendingAdminReview'
            ELSE status
        END
    WHERE id = v_main_log_id;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_refresh_amount_paid
    AFTER INSERT OR UPDATE OR DELETE ON payments.installment_sub_logs
    FOR EACH ROW EXECUTE FUNCTION payments.refresh_amount_paid();

-- ------------------------------------------------------------------------------
-- 4. HARD-DELETE PROTECTION: installment_sub_logs absolute history (PRD Section 12.4)
-- ------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION payments.prevent_installment_sub_log_deletion()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'installment_sub_logs records are append-only logs and cannot be hard deleted (sub_log_id: %). PRD Section 12.4.', OLD.id;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_prevent_sub_log_delete
    BEFORE DELETE ON payments.installment_sub_logs
    FOR EACH ROW EXECUTE FUNCTION payments.prevent_installment_sub_log_deletion();

-- ------------------------------------------------------------------------------
-- 5. CONCURRENCY CONTROL: row_version optimistic sequencing (PRD Section 11.2)
-- ------------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION inventory.increment_row_version()
RETURNS TRIGGER AS $$
BEGIN
    NEW.row_version  := OLD.row_version + 1;
    NEW.last_updated := CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_stock_balance_version
    BEFORE UPDATE ON inventory.stock_balances
    FOR EACH ROW EXECUTE FUNCTION inventory.increment_row_version();

-- ==============================================================================
-- SEED DATA CONFIGURATIONS
-- ==============================================================================

-- 1. Core Operating Locations
INSERT INTO inventory.locations (id, name, location_type, is_active) VALUES
    ('11111111-1111-1111-1111-111111111111', 'Roxy (Main)',          'MainWarehouse', TRUE),
    ('22222222-2222-2222-2222-222222222222', 'Mohamed Naguib (Retail)', 'SubWarehouse', TRUE),
    ('33333333-3333-3333-3333-333333333333', 'Online',               'Online',        TRUE);

-- 2. System Settings Baseline Parameters
INSERT INTO shared.system_settings (key, value, description) VALUES
    ('low_stock_threshold_default',   '10',   'Default low stock alert threshold (pieces)'),
    ('reserve_unresolved_days',       '7',    'Days before an unresolved reserve triggers alert'),
    ('in_warehouse_expiry_months',    '3',    'Months before expiry to fire in-warehouse alert'),
    ('merchant_held_expiry_months',   '18',   'Months before expiry to fire merchant-held alert'),
    ('outstanding_balance_days',      '30',   'Days since last payment to fire balance notification flags');
