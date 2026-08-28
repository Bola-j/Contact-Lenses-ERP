# Production certification and release boundary

## What CI/local automation may do

- `powershell -NoProfile -File scripts/verify-phase4-10.ps1` records static, PostgreSQL, image-policy, and configuration evidence only.
- Add `-IncludeRuntime`, `-IncludeBrowser`, and `-IncludeRecovery` only on a disposable Docker host. These modes use `lensee-certification-*` and `lensee-e2e` projects; they never target the default stack.
- Review the PR workflow for the exact commit before treating any local evidence as release evidence.

## VPS-only operator runbook

1. During an approved maintenance window, record `git rev-parse HEAD`, `docker compose images`, `docker compose ps`, `docker stats --no-stream`, and the current backup metadata. Keep all output outside source control and redact secrets.
2. Run the staged, read-only workload with dedicated synthetic users: 2, then 4, then 8 sessions. Stop if readiness fails, errors exceed 1%, p95 exceeds 2 seconds, a container restarts, or OOM is reported.
3. Create and hash a fresh backup before migration. Copy it to the approved encrypted off-host destination, verify its age is at most 15 minutes, and record the storage reference without recording credentials.
4. Validate the deployed TLS certificate, exact CORS origin, proxy network, persistent Data Protection keys, alert delivery, and non-public database listener.
5. Run the explicit migrator once. If it fails, stop promotion, preserve logs, and follow `migration-rollback.md`; do not start application containers as a workaround.
6. After the API is healthy, start frontend and Caddy. Verify `/health`, `/ready`, one valid login, one read-only authorization check, one controlled catalog-to-stock transaction, and its audit/outbox records.
7. Record image digest, timestamps, restart counts, migration log, smoke-test result, rollback owner, and the PR workflow URL in the certification checklist.

## Non-negotiable safety rules

- Never run `scripts/e2e-setup.ps1`, `scripts/verify-recovery-drill.ps1`, or `scripts/restore-db.ps1 -Force` against the default production Compose project.
- Never put `.env` contents, database dumps, passwords, JWT secrets, storage keys, or Shopify secrets in GitHub artifacts or the checklist.
- A completed local/CI gate does not complete a VPS/TLS/backup/deployment gate.
