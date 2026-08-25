# Correction reconciliation

1. Locate the original operation and its correction lineage at `GET /api/v1/operations/{id}/corrections`.
2. Verify requester and reviewer differ, the approved proposal has one reversal, and any replacement is a Draft.
3. Reconcile stock transactions for the reversal ID, the settlement cash/merchant-credit entry, the audit envelope, and its outbox message.
4. Do not delete or edit the source, reversal, settlement, or audit rows. Create a new controlled correction only when a new business fact exists.
