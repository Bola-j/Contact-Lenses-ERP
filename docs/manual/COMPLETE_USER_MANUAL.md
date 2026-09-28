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
