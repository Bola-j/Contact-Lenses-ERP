# Outbox replay

1. Confirm the originating command committed and inspect only the metadata at `GET /api/v1/outbox/dead-letters` as an Admin or ERPAdmin.
2. Correct the unavailable downstream dependency; never edit an outbox payload in the database.
3. Retry one item with `POST /api/v1/outbox/dead-letters/{id}/retry` and observe the delivery receipt and notification result.
4. If it dead-letters again, preserve the error, open an incident, and reconcile the immutable audit entry with the source operation/payment.
