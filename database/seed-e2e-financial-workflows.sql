-- Additive, idempotent fixtures for the isolated lensee-e2e database only.
--
-- Purpose: provide merchants and inventory prerequisites for UI-led financial
-- sales and return scenarios. This file intentionally does NOT create a sale,
-- return, payment, entitlement, or financial ledger entry: those must be
-- created through the application under test.
--
-- It never updates an existing fixture row. Re-running it therefore preserves
-- stock changes made while executing an E2E scenario.

begin;

insert into crm.merchants (
  id, business_name, contact_person_name, phone_numbers, email, address,
  business_type, status, notes
)
values
  ('f1000000-0000-0000-0000-000000000001', 'E2E Finance Wholesale Merchant', 'E2E Wholesale Contact', array['01000001001'], 'e2e-wholesale@example.test', 'E2E Test District, Cairo', 'Merchant', 'Active', 'E2E fixture: use for Main Warehouse wholesale pack sales and collection testing.'),
  ('f1000000-0000-0000-0000-000000000002', 'E2E Finance Retail Merchant', 'E2E Retail Contact', array['01000001002'], 'e2e-retail@example.test', 'E2E Test District, Cairo', 'BeautyCenter', 'Active', 'E2E fixture: use for SubWarehouse retail pack or loose-piece sales.'),
  ('f1000000-0000-0000-0000-000000000003', 'E2E Finance Return Merchant', 'E2E Return Contact', array['01000001003'], 'e2e-returns@example.test', 'E2E Test District, Cairo', 'Pharmacy', 'Active', 'E2E fixture: create a sale first, then use that sale for a return and refund or credit test.')
on conflict (id) do nothing;

-- The two CV SKU fixtures below each contain two pieces per sealed pack.
-- Main Warehouse stock supports packs-only wholesale. Retail stock includes
-- separate opened lots so piece sales/returns can be exercised without opening
-- sealed stock during fixture setup.
insert into inventory.inventory_batches (
  id, sku_id, location_id, lot_number, expiry_date, quantity, created_by,
  notes, created_at, is_quarantined
)
values
  ('f2000000-0000-0000-0000-000000000001', 'a501a99d-f036-4a20-8e98-d38b0bd3aacf', '11111111-1111-1111-1111-111111111111', 'E2E-FIN-MAIN-BROWN-A', current_date + interval '24 months', 80, 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb4', 'E2E fixture: 80 sealed packs for wholesale sales.', current_timestamp, false),
  ('f2000000-0000-0000-0000-000000000002', '5f91895a-a2cc-48ab-8bec-b6b7c48c356e', '11111111-1111-1111-1111-111111111111', 'E2E-FIN-MAIN-HAZEL-A', current_date + interval '22 months', 60, 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb4', 'E2E fixture: 60 sealed packs for wholesale sales.', current_timestamp, false),
  ('f2000000-0000-0000-0000-000000000003', 'a501a99d-f036-4a20-8e98-d38b0bd3aacf', '22222222-2222-2222-2222-222222222222', 'E2E-FIN-RETAIL-BROWN-A', current_date + interval '20 months', 35, 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb4', 'E2E fixture: sealed packs for retail pack sales.', current_timestamp, false),
  ('f2000000-0000-0000-0000-000000000004', '5f91895a-a2cc-48ab-8bec-b6b7c48c356e', '22222222-2222-2222-2222-222222222222', 'E2E-FIN-RETAIL-HAZEL-A', current_date + interval '19 months', 25, 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb4', 'E2E fixture: sealed packs alongside loose-piece stock.', current_timestamp, false)
on conflict (id) do nothing;

insert into inventory.stock_balances (
  id, location_id, sku_id, available_qty, reserved_in_warehouse_qty,
  reserved_with_rep_qty, target_qty, row_version, last_updated
)
values
  ('f3000000-0000-0000-0000-000000000001', '11111111-1111-1111-1111-111111111111', 'a501a99d-f036-4a20-8e98-d38b0bd3aacf', 80, 0, 0, 100, 0, current_timestamp),
  ('f3000000-0000-0000-0000-000000000002', '11111111-1111-1111-1111-111111111111', '5f91895a-a2cc-48ab-8bec-b6b7c48c356e', 60, 0, 0, 80, 0, current_timestamp),
  ('f3000000-0000-0000-0000-000000000003', '22222222-2222-2222-2222-222222222222', 'a501a99d-f036-4a20-8e98-d38b0bd3aacf', 35, 0, 0, 45, 0, current_timestamp),
  ('f3000000-0000-0000-0000-000000000004', '22222222-2222-2222-2222-222222222222', '5f91895a-a2cc-48ab-8bec-b6b7c48c356e', 25, 0, 0, 35, 0, current_timestamp)
on conflict (location_id, sku_id) do nothing;

insert into inventory.opened_piece_lots (
  id, location_id, sku_id, source_batch_id, lot_number, batch_expiry_date,
  opened_date, piece_expiry_date, loose_piece_quantity, created_by, created_at
)
values
  ('f4000000-0000-0000-0000-000000000001', '22222222-2222-2222-2222-222222222222', 'a501a99d-f036-4a20-8e98-d38b0bd3aacf', 'f2000000-0000-0000-0000-000000000003', 'E2E-FIN-RETAIL-BROWN-A', current_date + interval '20 months', current_date - 1, current_date + interval '20 months', 20, 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb4', current_timestamp),
  ('f4000000-0000-0000-0000-000000000002', '22222222-2222-2222-2222-222222222222', '5f91895a-a2cc-48ab-8bec-b6b7c48c356e', 'f2000000-0000-0000-0000-000000000004', 'E2E-FIN-RETAIL-HAZEL-A', current_date + interval '19 months', current_date - 1, current_date + interval '19 months', 12, 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb4', current_timestamp)
on conflict (id) do nothing;

commit;
