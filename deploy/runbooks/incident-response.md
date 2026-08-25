# Financial integrity incident response

1. Preserve application, PostgreSQL, and outbox evidence; do not mutate financial rows to make dashboards look healthy.
2. Disable the affected workflow through deployment controls if a duplicate or integrity error is still active.
3. Identify impacted operations from immutable audit/outbox/correction lineage, reconcile net stock and financial effects, and use new compensating workflows for corrections.
4. Require a peer review, a clean restore verification, and a post-incident action before reopening production promotion.
