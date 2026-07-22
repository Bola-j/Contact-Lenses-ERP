-- Report test seed for CRM, operations, payments, inventory, and stocktake outputs.
-- Run after database/seed-dev.sql and the lens/product seed.

insert into crm.merchants (
  id,
  business_name,
  contact_person_name,
  phone_numbers,
  email,
  address,
  business_type,
  status,
  notes
)
values
  ('51000000-0000-0000-0000-000000000001', 'Nile Optics Wholesale', 'Mona Hassan', array['01000000001','0220000001'], 'orders@nile-optics.example', 'Nasr City, Cairo', 'Merchant', 'Active', 'Primary wholesale report-test merchant.'),
  ('51000000-0000-0000-0000-000000000002', 'Roxy Beauty Center', 'Karim Adel', array['01000000002'], 'accounts@roxy-beauty.example', 'Roxy, Heliopolis', 'BeautyCenter', 'Active', 'Retail and cash receipt report-test account.'),
  ('51000000-0000-0000-0000-000000000003', 'Alex Eye Clinic', 'Dina Samir', array['01000000003'], 'finance@alex-eye.example', 'Smouha, Alexandria', 'Oculist', 'Inactive', 'Statement report-test merchant with credit adjustment.')
on conflict (id) do update
set business_name = excluded.business_name,
    contact_person_name = excluded.contact_person_name,
    phone_numbers = excluded.phone_numbers,
    email = excluded.email,
    address = excluded.address,
    business_type = excluded.business_type,
    status = excluded.status,
    notes = excluded.notes,
    is_deleted = false,
    deleted_at = null,
    updated_at = current_timestamp;

insert into crm.representatives (
  id,
  name,
  phone_numbers,
  email,
  type,
  linked_user_id,
  assigned_location_id,
  status,
  notes
)
values
  ('52000000-0000-0000-0000-000000000001', 'Salma Field Rep', array['01000000011'], 'salma.rep@example.com', 'External', null, '11111111-1111-1111-1111-111111111111', 'Active', 'Wholesale route representative.'),
  ('52000000-0000-0000-0000-000000000002', 'Online Desk', array['01000000012'], 'online.rep@example.com', 'Internal', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa6', '33333333-3333-3333-3333-333333333333', 'Active', 'Online and cash operations representative.')
on conflict (id) do update
set name = excluded.name,
    phone_numbers = excluded.phone_numbers,
    email = excluded.email,
    type = excluded.type,
    linked_user_id = excluded.linked_user_id,
    assigned_location_id = excluded.assigned_location_id,
    status = excluded.status,
    notes = excluded.notes,
    is_deleted = false,
    deleted_at = null;

insert into crm.merchant_notes (id, merchant_id, note, added_by, created_at)
values
  ('53000000-0000-0000-0000-000000000001', '51000000-0000-0000-0000-000000000001', 'Prefers monthly consolidated statements with SKU detail.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', current_timestamp - interval '14 days'),
  ('53000000-0000-0000-0000-000000000002', '51000000-0000-0000-0000-000000000002', 'Cash hand-to-hand receipts must be reviewed by accountant.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3', current_timestamp - interval '5 days'),
  ('53000000-0000-0000-0000-000000000003', '51000000-0000-0000-0000-000000000003', 'Credit note issued for returned expired lot.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', current_timestamp - interval '2 days')
on conflict (id) do update
set merchant_id = excluded.merchant_id,
    note = excluded.note,
    added_by = excluded.added_by,
    created_at = excluded.created_at;

insert into inventory.stock_balances (
  id,
  location_id,
  sku_id,
  available_qty,
  reserved_in_warehouse_qty,
  reserved_with_rep_qty,
  target_qty,
  last_updated
)
values
  ('54000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', '40000000-0000-0000-0000-000000000001', 120, 8, 4, 150, current_timestamp - interval '1 day'),
  ('54000000-0000-0000-0000-000000000002', '11111111-1111-1111-1111-111111111111', '40000000-0000-0000-0000-000000000003', 72, 5, 2, 90, current_timestamp - interval '2 days'),
  ('54000000-0000-0000-0000-000000000003', '22222222-2222-2222-2222-222222222222', '40000000-0000-0000-0000-000000000002', 34, 0, 1, 45, current_timestamp - interval '6 hours'),
  ('54000000-0000-0000-0000-000000000004', '33333333-3333-3333-3333-333333333333', '40000000-0000-0000-0000-000000000004', 56, 3, 0, 60, current_timestamp - interval '3 hours')
on conflict (location_id, sku_id) do update
set available_qty = excluded.available_qty,
    reserved_in_warehouse_qty = excluded.reserved_in_warehouse_qty,
    reserved_with_rep_qty = excluded.reserved_with_rep_qty,
    target_qty = excluded.target_qty,
    last_updated = excluded.last_updated;

insert into inventory.inventory_batches (
  id,
  sku_id,
  location_id,
  lot_number,
  expiry_date,
  quantity,
  created_by,
  notes,
  created_at
)
values
  ('55000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', 'REP-FRE-BLUE-2601', current_date + interval '18 months', 80, 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4', 'Report seed wholesale batch.', current_timestamp - interval '20 days'),
  ('55000000-0000-0000-0000-000000000002', '40000000-0000-0000-0000-000000000003', '11111111-1111-1111-1111-111111111111', 'REP-LAN-CLEAR-2602', current_date + interval '20 months', 60, 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4', 'Report seed medical lens batch.', current_timestamp - interval '18 days'),
  ('55000000-0000-0000-0000-000000000003', '40000000-0000-0000-0000-000000000004', '33333333-3333-3333-3333-333333333333', 'REP-OPT-120-2603', current_date + interval '10 months', 45, 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa6', 'Report seed online solution batch.', current_timestamp - interval '10 days')
on conflict (id) do update
set sku_id = excluded.sku_id,
    location_id = excluded.location_id,
    lot_number = excluded.lot_number,
    expiry_date = excluded.expiry_date,
    quantity = excluded.quantity,
    created_by = excluded.created_by,
    notes = excluded.notes,
    created_at = excluded.created_at;

insert into operations.operation_logs (
  id,
  operation_number,
  operation_type,
  status,
  source_location_id,
  destination_location_id,
  client_id,
  client_name,
  representative_id,
  payment_method,
  notes,
  created_by,
  confirmed_by,
  created_at,
  confirmed_at
)
values
  ('56000000-0000-0000-0000-000000000001', 'REP-OP-0001', 'WholesaleSale', 'Confirmed', '11111111-1111-1111-1111-111111111111', null, '51000000-0000-0000-0000-000000000001', 'Nile Optics Wholesale', '52000000-0000-0000-0000-000000000001', 'Installment', 'Report seed wholesale invoice with partial installment collection.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', current_timestamp - interval '12 days', current_timestamp - interval '12 days' + interval '2 hours'),
  ('56000000-0000-0000-0000-000000000002', 'REP-OP-0002', 'RetailSale', 'Confirmed', '22222222-2222-2222-2222-222222222222', null, '51000000-0000-0000-0000-000000000002', 'Roxy Beauty Center', '52000000-0000-0000-0000-000000000002', 'CashHandToHand', 'Report seed retail cash sale awaiting accountant trail.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa5', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', current_timestamp - interval '8 days', current_timestamp - interval '8 days' + interval '1 hour'),
  ('56000000-0000-0000-0000-000000000003', 'REP-OP-0003', 'Return', 'Confirmed', '11111111-1111-1111-1111-111111111111', null, '51000000-0000-0000-0000-000000000003', 'Alex Eye Clinic', '52000000-0000-0000-0000-000000000001', null, 'Report seed return for merchant statement credit behavior.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', current_timestamp - interval '4 days', current_timestamp - interval '4 days' + interval '45 minutes'),
  ('56000000-0000-0000-0000-000000000004', 'REP-OP-0004', 'Change', 'Confirmed', '11111111-1111-1111-1111-111111111111', null, '51000000-0000-0000-0000-000000000001', 'Nile Optics Wholesale', '52000000-0000-0000-0000-000000000001', 'CashTransaction', 'Report seed exchange with out and in lines.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', current_timestamp - interval '2 days', current_timestamp - interval '2 days' + interval '30 minutes')
on conflict (id) do update
set operation_number = excluded.operation_number,
    operation_type = excluded.operation_type,
    status = excluded.status,
    source_location_id = excluded.source_location_id,
    destination_location_id = excluded.destination_location_id,
    client_id = excluded.client_id,
    client_name = excluded.client_name,
    representative_id = excluded.representative_id,
    payment_method = excluded.payment_method,
    notes = excluded.notes,
    created_by = excluded.created_by,
    confirmed_by = excluded.confirmed_by,
    created_at = excluded.created_at,
    confirmed_at = excluded.confirmed_at,
    is_deleted = false,
    deleted_at = null;

insert into operations.operation_lines (
  id,
  operation_id,
  sku_id,
  product_name_snapshot,
  sku_code_snapshot,
  merchant_name_snapshot,
  representative_name_snapshot,
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
values
  ('57000000-0000-0000-0000-000000000001', '56000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000001', 'FreshLook Color Monthly', 'FRE-CL-P0-BLUE', 'Nile Optics Wholesale', 'Salma Field Rep', 'Standard', 10, 'Pieces', 1, 250.0000, 2500.0000, current_date + interval '18 months', 'REP-FRE-BLUE-2601', 115.0000, 'Wholesale blue colored lens line.'),
  ('57000000-0000-0000-0000-000000000002', '56000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000003', 'Lansee Clear Medical', 'LAN-PM-M125-CLEAR', 'Nile Optics Wholesale', 'Salma Field Rep', 'Standard', 6, 'Pieces', 0, 320.0000, 1920.0000, current_date + interval '20 months', 'REP-LAN-CLEAR-2602', 140.0000, 'Medical lens wholesale line.'),
  ('57000000-0000-0000-0000-000000000003', '56000000-0000-0000-0000-000000000002', '40000000-0000-0000-0000-000000000002', 'FreshLook Color Monthly', 'FRE-CL-P0-HAZEL', 'Roxy Beauty Center', 'Online Desk', 'Standard', 3, 'Pieces', 0, 310.0000, 930.0000, current_date + interval '14 months', 'REP-FRE-HAZEL-2604', 120.0000, 'Retail cash line.'),
  ('57000000-0000-0000-0000-000000000004', '56000000-0000-0000-0000-000000000003', '40000000-0000-0000-0000-000000000004', 'OptiCare Solution 120ml', 'OPT-PCS-120ML', 'Alex Eye Clinic', 'Salma Field Rep', 'Standard', 2, 'Pieces', 0, 180.0000, 360.0000, current_date + interval '10 months', 'REP-OPT-120-2603', 70.0000, 'Returned solution line.'),
  ('57000000-0000-0000-0000-000000000005', '56000000-0000-0000-0000-000000000004', '40000000-0000-0000-0000-000000000001', 'FreshLook Color Monthly', 'FRE-CL-P0-BLUE', 'Nile Optics Wholesale', 'Salma Field Rep', 'ChangeOut', 2, 'Pieces', 0, 250.0000, 500.0000, current_date + interval '18 months', 'REP-FRE-BLUE-2601', 115.0000, 'Exchange item sent out.'),
  ('57000000-0000-0000-0000-000000000006', '56000000-0000-0000-0000-000000000004', '40000000-0000-0000-0000-000000000003', 'Lansee Clear Medical', 'LAN-PM-M125-CLEAR', 'Nile Optics Wholesale', 'Salma Field Rep', 'ChangeIn', 1, 'Pieces', 0, 320.0000, 320.0000, current_date + interval '20 months', 'REP-LAN-CLEAR-2602', 140.0000, 'Exchange item received in.')
on conflict (id) do update
set operation_id = excluded.operation_id,
    sku_id = excluded.sku_id,
    product_name_snapshot = excluded.product_name_snapshot,
    sku_code_snapshot = excluded.sku_code_snapshot,
    merchant_name_snapshot = excluded.merchant_name_snapshot,
    representative_name_snapshot = excluded.representative_name_snapshot,
    section = excluded.section,
    quantity = excluded.quantity,
    entry_mode = excluded.entry_mode,
    bonus_quantity = excluded.bonus_quantity,
    unit_price = excluded.unit_price,
    line_total = excluded.line_total,
    expiry_date = excluded.expiry_date,
    lot_number = excluded.lot_number,
    unit_cost = excluded.unit_cost,
    line_notes = excluded.line_notes;

insert into operations.operation_versions (id, operation_id, version_number, snapshot_data, reason, edited_by, edited_at)
values
  ('58000000-0000-0000-0000-000000000001', '56000000-0000-0000-0000-000000000001', 1, '{"seed":"reports","operation":"REP-OP-0001"}', 'Initial report seed invoice.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', current_timestamp - interval '12 days'),
  ('58000000-0000-0000-0000-000000000002', '56000000-0000-0000-0000-000000000002', 1, '{"seed":"reports","operation":"REP-OP-0002"}', 'Initial report seed cash sale.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa5', current_timestamp - interval '8 days'),
  ('58000000-0000-0000-0000-000000000003', '56000000-0000-0000-0000-000000000003', 1, '{"seed":"reports","operation":"REP-OP-0003"}', 'Initial report seed return.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', current_timestamp - interval '4 days'),
  ('58000000-0000-0000-0000-000000000004', '56000000-0000-0000-0000-000000000004', 1, '{"seed":"reports","operation":"REP-OP-0004"}', 'Initial report seed change.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', current_timestamp - interval '2 days')
on conflict (operation_id, version_number) do update
set snapshot_data = excluded.snapshot_data,
    reason = excluded.reason,
    edited_by = excluded.edited_by,
    edited_at = excluded.edited_at;

update operations.operation_logs operation
set current_version_id = version.id
from operations.operation_versions version
where version.operation_id = operation.id
  and version.version_number = 1
  and operation.id in (
    '56000000-0000-0000-0000-000000000001',
    '56000000-0000-0000-0000-000000000002',
    '56000000-0000-0000-0000-000000000003',
    '56000000-0000-0000-0000-000000000004'
  );

insert into payments.main_payment_logs (
  id,
  operation_id,
  merchant_id,
  total_amount,
  amount_paid,
  payment_method,
  status,
  initialized_by,
  initialized_at,
  assigned_to,
  assigned_at,
  last_modified_by,
  last_modified_at,
  notes
)
values
  ('59000000-0000-0000-0000-000000000001', '56000000-0000-0000-0000-000000000001', '51000000-0000-0000-0000-000000000001', 4420.0000, 2500.0000, 'Installment', 'PendingAdminReview', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', current_timestamp - interval '12 days', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3', current_timestamp - interval '11 days', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3', current_timestamp - interval '3 days', 'Partial installment collection for report testing.'),
  ('59000000-0000-0000-0000-000000000002', '56000000-0000-0000-0000-000000000002', '51000000-0000-0000-0000-000000000002', 930.0000, 930.0000, 'CashHandToHand', 'Completed', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa5', current_timestamp - interval '8 days', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3', current_timestamp - interval '8 days', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3', current_timestamp - interval '7 days', 'Completed cash hand-to-hand receipt for PDF testing.'),
  ('59000000-0000-0000-0000-000000000003', '56000000-0000-0000-0000-000000000004', '51000000-0000-0000-0000-000000000001', 180.0000, 180.0000, 'CashTransaction', 'Completed', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', current_timestamp - interval '2 days', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3', current_timestamp - interval '2 days', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3', current_timestamp - interval '1 day', 'Change price difference settled by bank transaction.')
on conflict (id) do update
set operation_id = excluded.operation_id,
    merchant_id = excluded.merchant_id,
    total_amount = excluded.total_amount,
    amount_paid = excluded.amount_paid,
    payment_method = excluded.payment_method,
    status = excluded.status,
    initialized_by = excluded.initialized_by,
    initialized_at = excluded.initialized_at,
    assigned_to = excluded.assigned_to,
    assigned_at = excluded.assigned_at,
    last_modified_by = excluded.last_modified_by,
    last_modified_at = excluded.last_modified_at,
    notes = excluded.notes,
    is_deleted = false;

insert into payments.installment_sub_logs (
  id,
  main_log_id,
  amount,
  payment_method,
  date_received,
  sub_log_status,
  drafted_by,
  drafted_at,
  confirmed_by,
  confirmed_at,
  notes
)
values
  ('5a000000-0000-0000-0000-000000000001', '59000000-0000-0000-0000-000000000001', 1500.0000, 'CashTransaction', current_date - 10, 'Confirmed', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3', current_timestamp - interval '10 days', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', current_timestamp - interval '10 days' + interval '2 hours', 'Bank deposit confirmed.'),
  ('5a000000-0000-0000-0000-000000000002', '59000000-0000-0000-0000-000000000001', 1000.0000, 'CashHandToHand', current_date - 3, 'PendingAdminReview', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3', current_timestamp - interval '3 days', null, null, 'Second installment awaiting admin review.'),
  ('5a000000-0000-0000-0000-000000000003', '59000000-0000-0000-0000-000000000002', 930.0000, 'CashHandToHand', current_date - 8, 'Confirmed', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3', current_timestamp - interval '8 days', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', current_timestamp - interval '7 days', 'Cash sale confirmed.')
on conflict (id) do update
set main_log_id = excluded.main_log_id,
    amount = excluded.amount,
    payment_method = excluded.payment_method,
    date_received = excluded.date_received,
    sub_log_status = excluded.sub_log_status,
    drafted_by = excluded.drafted_by,
    drafted_at = excluded.drafted_at,
    confirmed_by = excluded.confirmed_by,
    confirmed_at = excluded.confirmed_at,
    rejection_reason = null,
    notes = excluded.notes;

insert into payments.cash_records (
  id,
  operation_id,
  payment_type,
  sub_type,
  amount,
  status,
  payment_date,
  created_by,
  notes
)
values
  ('5b000000-0000-0000-0000-000000000001', '56000000-0000-0000-0000-000000000002', 'CashReceived', 'HandToHand', 930.0000, 'Completed', current_timestamp - interval '8 days', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa5', 'Cash received from Roxy Beauty Center.'),
  ('5b000000-0000-0000-0000-000000000002', '56000000-0000-0000-0000-000000000004', 'CashReceived', 'BankTransaction', 180.0000, 'Completed', current_timestamp - interval '1 day', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3', 'Bank transaction for change difference.'),
  ('5b000000-0000-0000-0000-000000000003', '56000000-0000-0000-0000-000000000003', 'CashRefund', 'HandToHand', 120.0000, 'Completed', current_timestamp - interval '4 days', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3', 'Partial cash refund attached to returned stock.')
on conflict (id) do update
set operation_id = excluded.operation_id,
    payment_type = excluded.payment_type,
    sub_type = excluded.sub_type,
    amount = excluded.amount,
    status = excluded.status,
    payment_date = excluded.payment_date,
    created_by = excluded.created_by,
    notes = excluded.notes;

insert into payments.financial_adjustments (
  id,
  merchant_id,
  operation_id,
  adjustment_type,
  amount,
  status,
  notes,
  created_by,
  created_at
)
values
  ('5c000000-0000-0000-0000-000000000001', '51000000-0000-0000-0000-000000000003', '56000000-0000-0000-0000-000000000003', 'MerchantCredit', 240.0000, 'Completed', 'Credit for returned expired lot.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', current_timestamp - interval '4 days'),
  ('5c000000-0000-0000-0000-000000000002', '51000000-0000-0000-0000-000000000001', '56000000-0000-0000-0000-000000000001', 'BalanceReduction', 100.0000, 'Completed', 'Manual balance reduction for negotiated discount.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', current_timestamp - interval '2 days'),
  ('5c000000-0000-0000-0000-000000000003', '51000000-0000-0000-0000-000000000002', '56000000-0000-0000-0000-000000000002', 'CashRefund', 50.0000, 'Completed', 'Small cash refund correction.', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3', current_timestamp - interval '6 days')
on conflict (id) do update
set merchant_id = excluded.merchant_id,
    operation_id = excluded.operation_id,
    adjustment_type = excluded.adjustment_type,
    amount = excluded.amount,
    status = excluded.status,
    notes = excluded.notes,
    created_by = excluded.created_by,
    created_at = excluded.created_at;

insert into operations.stocktake_sessions (
  id,
  location_id,
  session_date,
  performed_by,
  confirmed_by,
  products_counted,
  total_discrepancy_units,
  notes,
  status,
  created_at,
  confirmed_at
)
values
  ('5d000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', current_timestamp - interval '6 days', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1', 3, -2, 'Report seed confirmed stocktake for main warehouse.', 'Confirmed', current_timestamp - interval '6 days', current_timestamp - interval '5 days')
on conflict (id) do update
set location_id = excluded.location_id,
    session_date = excluded.session_date,
    performed_by = excluded.performed_by,
    confirmed_by = excluded.confirmed_by,
    products_counted = excluded.products_counted,
    total_discrepancy_units = excluded.total_discrepancy_units,
    notes = excluded.notes,
    status = excluded.status,
    created_at = excluded.created_at,
    confirmed_at = excluded.confirmed_at;

insert into operations.stocktake_adjustment_lines (
  id,
  session_id,
  sku_id,
  lot_number,
  expiry_date,
  system_qty_before,
  physical_count,
  delta,
  line_note
)
values
  ('5e000000-0000-0000-0000-000000000001', '5d000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000001', 'REP-FRE-BLUE-2601', current_date + interval '18 months', 122, 120, -2, 'Two blue lenses missing from bin count.'),
  ('5e000000-0000-0000-0000-000000000002', '5d000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000003', 'REP-LAN-CLEAR-2602', current_date + interval '20 months', 72, 72, 0, 'Medical lens count matched.')
on conflict (id) do update
set session_id = excluded.session_id,
    sku_id = excluded.sku_id,
    lot_number = excluded.lot_number,
    expiry_date = excluded.expiry_date,
    system_qty_before = excluded.system_qty_before,
    physical_count = excluded.physical_count,
    delta = excluded.delta,
    line_note = excluded.line_note;
