# Backup and clean-environment restore

1. Run `pgbackrest --stanza=lensee stanza-create` once after a fresh database initialization, then confirm WAL archiving is healthy and the newest repository backup is no older than 15 minutes.
2. Restore into an isolated, empty PostgreSQL data volume using `pgbackrest --stanza=lensee restore` and the deployment’s recovery Compose overlay.
3. Start the migration command, then start an application instance. Verify `/ready`, migration status, and a sampled operation/payment/audit lineage.
4. Record start/end timestamps, newest recovered WAL timestamp, result, operator, and evidence link. The drill passes only when RPO is at most 15 minutes and RTO is at most 60 minutes.
