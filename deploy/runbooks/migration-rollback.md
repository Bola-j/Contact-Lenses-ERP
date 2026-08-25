# Migration rollback

1. Stop application instances and determine the last known-safe migration from the migration history tables.
2. Prefer an expand-first forward repair. Only run a down migration against an isolated restored copy after verifying it preserves financial lineage.
3. If rollback is required, restore the pre-migration encrypted backup, validate totals/lineage, and record the decision and evidence in the incident timeline.
