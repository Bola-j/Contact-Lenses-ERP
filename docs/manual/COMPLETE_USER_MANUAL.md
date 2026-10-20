# Payments user manual (candidate; role verification incomplete)

This guide describes the Payments workspace using the visible bilingual SPA, not endpoint names. The focused browser run used mocked data; confirm each step in the dedicated QA environment before treating this guide as operationally complete. Never infer a merchant from a buyer name, phone number, or payment method. Select a real active receiving Finance account for every actual movement.

## Admin

### Role purpose

Review and post submitted payment work, and manage permitted Payments workflows. Finance-module account administration is a separate permission boundary.

### Login and navigation

Sign in with the assigned company account, then choose Payments from the application navigation or a Payments link. The route is available in English and Arabic. No credentials are included here.

### Daily workflow: collections

1. Open Payments and choose Merchant account payments or Other payments.
2. Inspect the queue and open details before acting.
3. Approve only a valid submitted item whose amount, movement method, reference (for electronic methods), and receiving account are correct.
4. For rejection, choose Reject, enter a specific reason in the required field, then confirm. The rejected record remains in history; correct it with a new linked workflow rather than editing posted/rejected history.
5. Refresh the queue and check the merchant balance, audit, and relevant ledger view.

Expected downstream effect: approval posts the merchant allocation and Finance movement together; the submitter should see the status change after the workspace refreshes. This downstream chain was not verified against live role accounts in this pass.

DO verify the selected merchant/operation, amount, and receiving account. DO wait for the success acknowledgement. DO use the stated rejection reason when rejecting.

DON'T approve a duplicate request, infer an identity from buyer details, or assume a failed refresh means the mutation failed. If acknowledgement is uncertain, refresh/search the existing record before submitting another request.

### Other supported Payments work

The workspace exposes collection queues, history, audit, merchant balances/statements/orders, adjustments/refunds, opening balances, and financial-closure work according to the account’s permissions. This pass did not certify every control or downstream result. Before operating refund or financial-closure work, verify the exact requested amount, source sale, current available credit, and review status in QA.

## ERPAdmin

### Role purpose

ERP administration. Payments authority is intended to match Admin; Finance-module administration remains separately governed.

### Daily workflow

Use the same Payments collection review steps described for Admin. If an Admin-accessible Payments action is missing or forbidden, record it as a role-parity defect and stop that operation pending verification. Independent ERPAdmin UI certification was not completed.

## Accountant

### Role purpose

Prepare and submit collection work for review.

### Daily workflow

1. Open Payments and choose the correct Merchant account or Other payments scope.
2. Select the operation by its visible operation number/payment reference; for OtherPayments, enter a reference and let the workspace resolve it.
3. Enter the received amount, date, supported movement method, and a real active receiving account. Add the transaction reference for electronic methods.
4. Submit for approval and wait for the acknowledgement. Repeated submission with the same unchanged request is deduplicated by the same idempotency key in the current browser form.
5. Revisit the queue/history to see the reviewer’s decision.

Expected downstream effect: the reviewer sees pending work; merchant balance and Finance ledger change only after approval. This complete handoff is not yet verified against real accounts.

DO check the operation and payment amount. DON'T resubmit as a new item just because a refresh is slow; search for the original request first.

### OtherPayments refund request (candidate flow; reviewer/Finance posting not yet certified)

The OtherPayments collection form now includes a movement-type choice for a cash refund. Select the source operation, choose Cash refund, enter the approved refund amount, movement method, and active Finance account, then send it for review. The reviewer uses the pending cash-record action to approve or reject; approval is intended to post a Finance debit. Do not use this path for merchant refunds, which remain in the merchant adjustment/refund workflow. The request form passed a mocked browser check; real approval, Finance ledger posting, reservation concurrency, and downstream status still require QA verification.

## CLevel

### Role purpose

Executive review of the Payments adjustments/refund capabilities granted to this role.

### Daily workflow

Open Payments and review only the work and action buttons shown for the account. Verify the linked source and reason before approval or rejection. Refund approval is subject to available-credit revalidation. This role’s exact UI and permission boundary were not independently certified in this pass.

## WarehouseClerk

### Role purpose

Physical warehouse and stock workflows.

### Payments access

Payments is not listed as a WarehouseClerk route in the current UI allowlist. Do not use a bookmarked Payments link to bypass that restriction. If a warehouse task appears to need a financial action, hand it to the authorized Payments employee.

## Common recovery

- Required receiving account unavailable: verify the Finance account selector’s loading/error state and ask an authorized administrator; do not choose an implicit cash account.
- Operation reference not found: check the full operation number or payment reference; do not create a merchant based on the buyer’s name.
- Rejection reason missing: complete the required reason field before continuing.
- Refresh failed after success: the mutation may already be committed. Reopen the queue/history and search for the existing request before retrying.
- Permission denied: stop and ask the system administrator to check the intended role boundary; do not repeatedly submit.

The following remain uncertified: persistent company users, full UI coverage for every role/operation, ERPAdmin differential parity, shared-company cross-role chains, and real PostgreSQL concurrency/upgrade behavior. See `artifacts/overnight/SCENARIO_MATRIX.md` and `artifacts/overnight/OVERNIGHT_REPORT.md`.

## Shopify Online intake — verified isolated QA behavior (Admin/ERPAdmin and WarehouseClerk)

### Role purpose

Admin/ERPAdmin maintain exception mappings and monitor Shopify intake. The Online Warehouse Clerk allocates stock and fulfills eligible drafts. Shopify owns the commercial order; ERP owns stock and fulfillment. Finance/provider verification remains an independent process.

### Daily workflow

1. The catalog owner copies the ERP SKU into each Shopify variant before orders are created. The integration does not change Shopify SKUs.
2. Open **Online intake** and review queue age, attempts and actionable status. A routine active unique SKU match is remembered by store and variant; unresolved or conflicting items stay visible for correction.
3. To correct a mismatch, open the variant mapping, select the verified active ERP SKU, enter a reason and save. Retry/replay the existing delivery. Do not create another order because processing is delayed.
4. A valid order arrives as an unallocated Draft. Open it in **Operations**, choose the correct SubWarehouse and each line's batch/expiry, save the allocation, and confirm to reserve stock.
5. Before dispatch, inspect the payment facts. A trusted Shopify-reported **paid** status allows dispatch when normal stock and fulfillment checks pass, even if independent provider evidence and weekend Finance reconciliation remain pending. A pending/unpaid order remains reserved but cannot dispatch.
6. Dispatch never creates provider verification, collection or Finance reconciliation. Use the established Finance and provider-verification workflows when evidence arrives.

### Quantity and commercial facts

Shopify quantities are pieces for ERP `SinglePiece`/`Both` products and packs for `SealedPackOnly` products. Review the displayed unit, exact quantity, price and line total. Do not convert pieces to packs or re-enter Shopify commercial amounts. ERP chooses batches and controls reservations/expiry.

### Do and don't

**Do:** verify SKU, unit, quantity, buyer delivery facts, source location and batch before confirming; wait for the save/confirmation message; use the existing queued event for retries; review reported payment, provider verification and Finance reconciliation separately.

**Don't:** resolve an ambiguous SKU by guessing; alter historical lines to repair a mapping; dispatch while Shopify still reports pending; treat a gateway label, checkout ID or Shopify paid notice as independent provider proof; create a payment/collection/reconciliation during dispatch; or automatically reverse dispatched stock on cancellation/refund.

### Cancellation, refunds and recovery

A pre-dispatch cancellation releases the actual held pieces/packs once. After dispatch (including delivered/failed-delivery statuses), cancellation and refund notices create review work; do not reverse stock or accounting from the notice. Physical goods return and financial adjustments use the existing governed return/Finance workflows. Reconciliation/manual recovery schedules durable recovery; it does not claim the order was already imported. Escalate unresolved events rather than recreating orders.

### Verification status

ERP UI simulation covered mapping/replay, allocation, paid dispatch and unpaid dispatch blocking. This is implementation evidence, not a requirement to maintain a separate Shopify test store. Actual production Shopify delivery and Paymob certification are separate; follow `docs/SHOPIFY_INTEGRATION_CONFIGURATION.md` for the one-store production setup.

### Verified Online Warehouse Clerk company handoff — 2026-10-19

In the isolated company workflow, the Online Warehouse Clerk can search using the Shopify order number. Open **Edit** on its unallocated Shopify draft; the Shopify retail type and commercial line are shown read-only. Choose an active SubWarehouse and the line's available batch/expiry, save the allocation, then confirm to reserve. On an unpaid order, Ship is disabled and a message explains that Shopify must report paid and the order must have no open review; the order remains Reserved. On an eligible paid order, dispatch is available when normal stock and fulfillment checks pass. The UI confirms each transition and the queue reflects the saved status after refresh. This path was verified in Chrome against the persistent isolated company QA database. It does not certify actual Shopify-origin delivery or production permissions.

The selected SubWarehouse's batches are visible only for the linked Shopify draft and SKU being allocated; ordinary Warehouse Clerk inventory access remains assigned-location scoped. If no batch appears, check that the target SKU has active, non-quarantined, unexpired stock at the selected SubWarehouse and ask an administrator to correct the stock rather than creating a replacement order.

### Verified Accountant handoff

The Accountant can inspect a dispatched order's Shopify-reported payment state, independent provider verification and Finance reconciliation separately. A Shopify-paid order can be dispatched while the other two facts remain pending. The Accountant review path displayed those states without a financial mutation. Provider verification and Finance reconciliation continue only through their existing authorized workflows.
### Production activation

Use the existing production Shopify store and the ERP's production HTTPS address. Enter the store domain, active Online location ID and signing secret as described in `docs/SHOPIFY_INTEGRATION_CONFIGURATION.md`; then release the five app-specific webhook subscriptions and enable intake. No QA store, tunnel or QA catalog copy is part of this setup. Preserve the existing ERP SKU codes in Shopify and leave COD blank unless it is separately approved.
