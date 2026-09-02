# Payment idempotency pending-key reconciliation

## Purpose

The migration command refuses to run when it finds a committed `Pending` row in
`payments.payment_idempotency_keys`.  A historical pending row does not prove
whether its corresponding payment mutation committed, so it must never be
deleted, retried, or marked complete automatically.

## Read-only diagnosis

Run the following query against the target PostgreSQL database and retain the
output with the change record.  Do not disclose request hashes or response
bodies outside the incident record.

```sql
select id, key, scope, request_hash, created_at, last_seen_at, expires_at
from payments.payment_idempotency_keys
where status = 'Pending'
order by created_at, id;
```

For each key, correlate its scope and timestamps with the relevant
`payments.main_payment_logs`, `payments.cash_records`,
`payments.financial_adjustments`, identity audit rows, and shared outbox rows.
Use immutable external payment-provider evidence when the command initiated a
cash movement.

## Operator-directed resolution

1. A payments owner records whether the requested business effect is absent,
   present once, or indeterminate.
2. For an absent effect, issue a *new* request with a *new* idempotency key;
   do not reuse the pending key.
3. For a present-once effect, an authorized database operator may update the
   pending row only with an incident-approved, deterministic response that has
   been reconstructed from the committed records. Record the exact command and
   evidence in the incident. If a deterministic response cannot be proven,
   retain the row and keep rollout blocked.
4. For an indeterminate effect, stop and escalate to finance/security. Do not
   retry, delete, or complete the key.
5. Re-run the read-only query. The migration command can proceed only when it
   returns no pending rows.

This runbook intentionally provides no write SQL: resolution is an explicit
operator decision, not an automated recovery path.
