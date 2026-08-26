# LenseeProduction Production Certification Checklist

## Document control

| Field | Value |
|---|---|
| Status | **NO-GO — Phase 0 application candidate `33f1311` committed; certification incomplete** |
| Current readiness rating | **6.5 / 10** |
| Report date | 2026-08-26 |
| Repository | `D:\LenseeProduction` |
| Branch | `main` |
| Inspected HEAD | `90ee277` (`fix: restore release CI gates`) |
| Scope | Application integrity, PostgreSQL, runtime container, browser workflows, backup/restore, deployment, rollback |
| Excluded | GitHub Actions, organization configuration, and unapproved production deployment |
| Release rule | No production approval until every Critical gate is checked and evidence is recorded |

## Status legend

- `[x]` — completed and supported by evidence.
- `[~]` — planned or in progress; not certification evidence.
- `[ ]` — not completed or must be rerun for this exact release candidate.
- `BLOCKED` — cannot proceed until the named condition is resolved.
- `Critical` — release cannot proceed when open.
- `High` — must be resolved or explicitly accepted before release.
- `Evidence` — command output, report, image digest, database query, screenshot, or signed decision proving completion.

## Executive assessment

The application has materially stronger transaction, concurrency, migration, configuration, and container safeguards. The latest recorded verification included a zero-warning build, 164 passing backend tests, 8 passing PostgreSQL integration tests, a clean fresh/upgrade migration rehearsal, healthy production-shaped API startup, zero restarts, and no high/critical runtime-image findings.

Those results are not yet a production certificate because:

1. The application hardening candidate is committed locally at `33f1311`; its PostgreSQL, container, browser, restore, and deployment evidence must be rerun before certification.
2. Docker Desktop is unavailable on 2026-08-26, so PostgreSQL, container, and browser evidence cannot currently be reproduced.
3. `scripts/deploy-prod.ps1` starts the application stack before explicitly running the migrator even though production auto-migration is disabled.
4. The same deployment script uses `bash` and `/dev/tcp`, while the hardened runtime image is Alpine and does not install Bash.
5. `scripts/e2e-setup.ps1` executes `docker compose down --volumes` and is unsafe unless it is isolated from valued data.
6. The seeded, authenticated, role-based catalog-to-supply-to-payment browser workflow is not complete for this candidate.
7. A real backup/isolated-restore/migration drill has not been recorded.

## Readiness scorecard

| Area | Score | Current assessment |
|---|---:|---|
| Domain and transaction consistency | 8/10 | Stronger; PostgreSQL failure/concurrency tests previously passed |
| Database migration safety | 8/10 | Fresh and upgrade paths previously passed; must rerun from immutable candidate |
| Runtime/container | 8/10 | Alpine image previously healthy and clean; Docker currently unavailable |
| Production configuration | 7/10 | Fail-fast safeguards exist; target environment is unverified |
| Deployment automation | 4/10 | Migration ordering and Bash readiness probe are blockers |
| Browser/business workflows | 4/10 | Automated suites exist but complete seeded acceptance is open |
| Backup and disaster recovery | 4/10 | Runbooks/scripts exist; no completed restore drill evidence |
| Observability/incident operations | 7/10 | Health, telemetry, outbox, and runbooks exist; live alerting is unverified |
| Overall certification | **6.5/10** | **NO-GO** |

## Open blocker register

| ID | Severity | Blocker | Required resolution | Status |
|---|---|---|---|---|
| P0-01 | Critical | No immutable release candidate | Review, commit, and build from a clean tree | Resolved — application candidate `33f1311` committed locally; no push/deployment performed |
| P0-02 | Critical | Production deployment order is incompatible with explicit migrations | Start DB, run migrator, then start API/frontend/proxy | Open |
| P0-03 | Critical | Deployment readiness check requires Bash | Replace with host-side HTTP or an Alpine-compatible check | Open |
| P0-04 | Critical | Docker daemon unavailable | Start Docker Desktop and rerun all Docker/PostgreSQL gates | BLOCKED |
| P0-05 | Critical | E2E setup can delete the default Compose volume | Create isolated E2E Compose project and target guards | Open |
| P0-06 | Critical | Authenticated seeded business-day workflow incomplete | Run all critical role workflows with persisted-state proof | Open |
| P0-07 | Critical | Backup/restore drill incomplete | Back up, restore to isolation, migrate, reconcile, measure RPO/RTO | Open |
| P1-01 | High | Production secrets/TLS/proxy/key storage unverified | Validate actual deployment configuration without exposing secrets | Open |
| P1-02 | High | Failure injection incomplete across all critical workflows | Prove rollback/retry/no duplicate state | Open |

## Previously completed hardening work

- [x] Catalog state, audit record, and durable outbox message share one PostgreSQL transaction.
- [x] Injected catalog failure test proves both catalog and outbox records roll back.
- [x] Warehouse reservation uses an atomic relational update plus ledger transaction.
- [x] Competing stock writes surface a typed conflict and map to RFC ProblemDetails `409`.
- [x] PostgreSQL concurrency test previously proved one reservation succeeds, one conflicts, and one ledger row remains.
- [x] Operations-corrections and Shared outbox metadata migrations are discoverable and apply to a fresh database.
- [x] Upgrade-path test starts from prior migration heads and applies current Operations/Shared heads.
- [x] Production configuration requires strong JWT/database values, exact HTTPS CORS origins, and an absolute Data Protection key path.
- [x] Production application instances default to `Database:AutoMigrate=false`.
- [x] Runtime image moved to supported .NET 8 Alpine and includes fonts plus Cairo timezone data.
- [x] Previous production-shaped rehearsal reached healthy `/health` and `/ready` with restart count `0`.
- [x] Previous runtime-image scan reported no high/critical findings.

All completed items must be revalidated after the release candidate is committed.

---

# Step-by-step execution checklist

## Phase 0 — Preserve and identify the candidate

### 0.0 Plan and execution record

**Objective:** preserve the hardening changes as reviewable, immutable local commits. Phase 0 does not authorize a push, tag, Docker start, deployment, or a production approval.

**Acceptance boundary:** every behavior change must be in a scoped commit with a matching test/evidence path; `Program.cs` must be staged by hunk because it contains inventory, catalog, and production-configuration work; Phase 0 completes only when the working tree is clean and the application candidate SHA is recorded.

| Candidate | Commit | Scope | Review/evidence result |
|---|---|---|---|
| A — inventory reservation atomicity | `67d2a45` | Relational reservation/ledger transaction, typed stock-write conflict, `409` mapping, PostgreSQL concurrency test | Staged by hunk; release build and unit suite pass. PostgreSQL execution remains blocked by Docker Desktop. |
| B — catalog/outbox atomicity | `da9130c` | Catalog mutation transaction, endpoint integration, DI hunk, PostgreSQL rollback test/project reference | Staged by hunk; rollback test is discovered by the PostgreSQL project build. Execution remains blocked by Docker Desktop. |
| C — migration upgrade proof | `a6a1b09` | PostgreSQL upgrade-path migration test | Staged and reviewed; execution remains blocked by Docker Desktop. |
| D — production runtime configuration | `add5556` | Alpine runtime, durable Data Protection configuration, production CORS/key-ring validation, Compose and env defaults | Staged and reviewed; image/runtime rehearsal remains open in Phases 5 and 7. |
| E — catalog audit rollback proof | `33f1311` | PostgreSQL rollback test additionally persists and verifies rollback of an identity audit row | Full solution build passes; PostgreSQL execution remains blocked by Docker Desktop. |
| F — certification record | This commit | This checklist only | Records Phase 0 evidence without marking downstream gates complete. |

**Phase 0 evidence:**

- [x] Starting state recorded at `main` / `90ee277` on 2026-08-26 21:05:17 +03:00: 10 tracked modifications and 4 untracked files.
- [x] Candidate-file secret scan found zero credential-pattern matches; no candidate dump/binary/trace artifact was found.
- [x] `git diff --check` passed before staging and each staged candidate passed `git diff --cached --check`. Git only reported CRLF conversion notices.
- [x] `dotnet restore Lensee.slnx` completed with all projects up to date.
- [x] `dotnet build Lensee.slnx --configuration Release --no-restore -warnaserror` passed with 0 warnings and 0 errors, including a rerun after `33f1311`.
- [x] `dotnet test backend/Lensee.Tests/Lensee.Tests.csproj --configuration Release --no-build --no-restore --logger "console;verbosity=minimal"` passed: 164 passed, 0 failed, 0 skipped.
- [~] PostgreSQL integration tests, container image build/scan, runtime rehearsal, and browser acceptance were not run because `docker info` could not connect to `//./pipe/dockerDesktopLinuxEngine` on 2026-08-26. These remain blocking certification gates, not passing results.
- [x] No push, tag, deployment, Docker reset, or production action was performed.

### 0.1 Record the starting state

- [x] Run `git branch --show-current` and confirm the intended branch.
- [x] Run `git log -1 --oneline` and record the starting commit.
- [x] Run `git status --short` and inventory every modified/untracked file.
- [x] Run `git diff --check` and require no whitespace errors.
- [x] Confirm no `.env`, password, token, database dump, Playwright trace, or generated secret is staged.
- [x] Record the operator and timestamp.

Evidence:

| Field | Result |
|---|---|
| Operator | Codex (local Phase 0 implementation; approver unassigned) |
| Timestamp | 2026-08-26 21:05:17 +03:00 |
| Starting commit | `90ee277` |
| Working-tree review link | Phase 0.0 evidence in this checklist |
| Secret review result | Passed — zero credential-pattern matches; zero candidate dump/binary/trace artifacts |

### 0.2 Split and review the hardening changes

- [x] Commit inventory concurrency and transactional-ledger changes separately (`67d2a45`).
- [x] Commit catalog/outbox atomicity and PostgreSQL rollback evidence separately (`da9130c`), then add direct audit rollback proof (`33f1311`).
- [x] Commit migration-upgrade proof separately (`a6a1b09`).
- [x] Commit runtime image and production configuration changes separately (`add5556`).
- [x] Preserve unrelated worktree changes; all starting changes were included in the scoped candidate map.
- [x] Review each staged commit diff before creating the candidate.
- [x] Do not push or deploy without explicit authorization.

Pass criteria:

- [x] Clean working tree after the documentation commit.
- [x] No unrelated files in the release commits.
- [x] No secrets or generated test artifacts.
- [x] Every behavior change has a corresponding test/evidence item; Docker-gated items are explicitly deferred to Phase 4 rather than claimed as passed.

## Phase 1 — Repair deployment and E2E tooling

### 1.1 Correct the production deployment sequence

Modify `scripts/deploy-prod.ps1` so that it performs this exact order:

1. Validate `.env` and Compose configuration.
2. Build the database, migrator, API, frontend, and proxy images.
3. Start PostgreSQL only.
4. Wait for PostgreSQL readiness.
5. Run the one-shot `migrator` service under the advisory lock.
6. Stop immediately if migration returns non-zero.
7. Start the API.
8. Wait for `/ready` to return HTTP 200 and `Healthy`.
9. Start the frontend and Caddy.
10. Print container status, image digests, health, and migration evidence.

Checklist:

- [ ] Production instances remain `Database:AutoMigrate=false`.
- [ ] The migrator is the only schema-mutating process.
- [ ] A failed migration prevents API promotion.
- [ ] The script captures migration logs on failure.
- [ ] Rerunning the script is idempotent.

### 1.2 Replace the Bash readiness probe

- [ ] Remove the `bash -lc` and `/dev/tcp` dependency from `scripts/deploy-prod.ps1`.
- [ ] Use `Invoke-RestMethod`, a Docker health status, or another installed Alpine-compatible mechanism.
- [ ] Validate response status and JSON `status` value.
- [ ] Print the final 120 API log lines when readiness fails.
- [ ] Return a non-zero exit code on timeout.

Pass criteria:

- The real Alpine release image reaches readiness without Bash.
- Readiness failure is actionable and never reported as success.

### 1.3 Isolate destructive E2E setup

- [ ] Add a dedicated E2E Compose configuration with unique container names, ports, network, and volumes.
- [ ] Use a dedicated Compose project name.
- [ ] Refuse to run when `ASPNETCORE_ENVIRONMENT=Production`.
- [ ] Resolve and print the target project, database, and volume before resetting.
- [ ] Limit `down --volumes` to the dedicated E2E project.
- [ ] Never point `scripts/restore-db.ps1 -Force` at production during E2E.

Pass criteria:

- Existing development and production volumes remain unchanged.
- Two consecutive setup runs produce the same baseline.
- Test credentials are unique, synthetic, and absent from production configuration.

## Phase 2 — Restore the verification environment

- [ ] Start Docker Desktop.
- [ ] Run `docker info` and record client/server versions.
- [ ] Run `docker compose version`.
- [ ] Confirm adequate disk, memory, and CPU.
- [ ] Confirm no stale E2E containers or conflicting ports.
- [ ] Confirm ports `8181`, `5000`, and `3001` are either free or intentionally owned by the test stack.

Pass criteria:

- Docker Engine responds.
- Linux containers can run.
- PostgreSQL Testcontainers can start.
- No valued container/volume will be reset by certification tests.

## Phase 3 — Build and static validation

Run these commands sequentially from the repository root:

```powershell
dotnet restore Lensee.slnx
dotnet build Lensee.slnx --configuration Release --no-restore -warnaserror
dotnet format Lensee.slnx --verify-no-changes --no-restore
npm ci
npm run check
node --check frontend/app.js
npm --prefix frontend run build
```

Checklist:

- [ ] Restore succeeds without changing locked dependencies unexpectedly.
- [ ] Release build succeeds with zero warnings/errors.
- [ ] Formatting verification passes.
- [ ] Encoding check passes.
- [ ] Localization check passes.
- [ ] Unsafe-DOM-sink guard passes.
- [ ] Endpoint-boundary guard passes.
- [ ] Frontend JavaScript syntax passes.
- [ ] Frontend production build succeeds.

Evidence:

| Check | Result | Evidence path/link |
|---|---|---|
| Release build | | |
| Format | | |
| Frontend checks | | |
| Frontend build | | |

## Phase 4 — Backend and PostgreSQL tests

### 4.1 Unit and contract tests

```powershell
dotnet test backend/Lensee.Tests/Lensee.Tests.csproj `
  --configuration Release `
  --no-build `
  --no-restore `
  --logger "console;verbosity=minimal"
```

- [ ] All tests pass.
- [ ] No critical test is skipped.
- [ ] Authorization, validation, catalog, inventory, operations, payments, reports, notifications, and stocktake contracts pass.
- [ ] Stale stock writes return ProblemDetails `409`.

### 4.2 Real PostgreSQL integration tests

```powershell
$env:LENSEE_RUN_POSTGRES_TESTS = "true"
try {
    dotnet test backend/Lensee.PostgresIntegrationTests/Lensee.PostgresIntegrationTests.csproj `
      --configuration Release `
      --no-restore `
      --logger "console;verbosity=minimal"
} finally {
    Remove-Item Env:LENSEE_RUN_POSTGRES_TESTS -ErrorAction SilentlyContinue
}
```

- [ ] All PostgreSQL tests pass.
- [ ] Concurrent reservation produces one success and one conflict.
- [ ] Exactly one balance/ledger mutation persists.
- [ ] Catalog failure rolls back catalog/audit/outbox state.
- [ ] Payment constraints reject invalid aggregate state.
- [ ] Operation-correction uniqueness constraints pass.
- [ ] Fresh migration test passes.
- [ ] Prior-release upgrade test passes.
- [ ] No pending migration remains.

Pass criteria:

- Zero failed tests.
- Zero skipped PostgreSQL tests.
- Database assertions prove persisted state, not only HTTP responses.

## Phase 5 — Dependency and container security

### 5.1 Dependency review

```powershell
dotnet list Lensee.slnx package --vulnerable --include-transitive
dotnet list Lensee.slnx package --outdated
npm audit --omit=dev
```

- [ ] No unresolved critical vulnerability.
- [ ] No exploitable high vulnerability without documented mitigation.
- [ ] Fixed versions are used where available.
- [ ] Dependency updates rerun Phases 3 and 4.

### 5.2 Build and scan the exact release image

```powershell
docker build --pull `
  --tag lenseehost:release-candidate `
  --file backend/Lensee.Host/Dockerfile .
```

- [ ] Record image ID and digest.
- [ ] Scan OS and .NET packages for High/Critical findings.
- [ ] Do not scan a stale `latest` image.
- [ ] Review every finding rather than suppressing it automatically.
- [ ] Confirm `Africa/Cairo` timezone is present.
- [ ] Confirm application runs as the non-root application user.

Pass criteria:

- Zero unresolved critical vulnerability.
- Zero exploitable high vulnerability.
- Release image digest matches the candidate recorded for deployment.

## Phase 6 — Fresh and upgrade database rehearsal

### 6.1 Fresh database

- [ ] Start an empty PostgreSQL 17 instance.
- [ ] Run the release image with `--migrate`.
- [ ] Confirm advisory lock acquisition and release.
- [ ] Confirm all nine context migration chains apply.
- [ ] Start the API with auto-migration disabled.
- [ ] Verify `/health` and `/ready` are `Healthy`.
- [ ] Confirm restart count remains `0`.

### 6.2 Upgrade database

- [ ] Create a database at the documented prior-release migration heads.
- [ ] Seed representative catalog, stock, operation, payment, audit, and outbox rows.
- [ ] Back up the pre-upgrade database.
- [ ] Run the release migrator.
- [ ] Confirm new columns, constraints, and indexes.
- [ ] Reconcile pre/post row counts and critical totals.
- [ ] Start the API and verify readiness.

Pass criteria:

- Fresh and upgrade migrations both succeed.
- No pending migrations.
- No lost business record or broken lineage.
- Application instances never mutate schema at startup.

## Phase 7 — Production-shaped runtime rehearsal

- [ ] Validate merged Compose files with production-shaped dummy values.
- [ ] Start DB, migrator, API, frontend, and Caddy using the corrected deployment order.
- [ ] Verify HTTP-to-HTTPS redirect.
- [ ] Verify TLS in the target environment.
- [ ] Verify exact CORS origin behavior.
- [ ] Verify untrusted origins are rejected.
- [ ] Verify Data Protection key persistence across API container replacement.
- [ ] Trigger the operational scheduler once.
- [ ] Confirm `/health` and `/ready` bypass rate limiting.
- [ ] Observe API, PostgreSQL, worker, and outbox logs.

Pass criteria:

- Health and readiness remain green.
- Restart count remains `0`.
- No startup exception or recurring worker failure.
- No development placeholder is accepted in Production.
- Authentication remains valid across an API restart when expected.

## Phase 8 — Seeded browser acceptance

### 8.1 Seed the isolated environment

- [ ] Seed locations.
- [ ] Seed a unique Primary Admin.
- [ ] Seed Admin, ERPAdmin, WarehouseClerk, Accountant, and CLevel users.
- [ ] Seed catalog categories, brand, products, SKUs, and batches.
- [ ] Seed a merchant and representative.
- [ ] Verify every credential before workflow execution.
- [ ] Record all generated business identifiers.

### 8.2 Execute critical workflows

Run the repository business-day suite:

```powershell
npm run e2e:business-day
```

Then run the targeted critical suites:

```powershell
npx playwright test `
  e2e/catalog-crm.spec.js `
  e2e/inventory-transfer.spec.js `
  e2e/operations-sales-return-change.spec.js `
  e2e/payments-accounting.spec.js `
  e2e/stocktake.spec.js `
  e2e/notifications-reports.spec.js `
  --project=chromium
```

Finally run the complete suite:

```powershell
npm run e2e
```

Workflow checklist:

- [ ] Primary Admin can administer users safely.
- [ ] Admin can create/update catalog records.
- [ ] ERPAdmin actions visible in the UI are authorized by the API.
- [ ] WarehouseClerk can receive supply into inventory.
- [ ] Supply receipt creates the correct operation, version, stock balance, batch, and ledger rows.
- [ ] Warehouse reservation succeeds and can be released/completed.
- [ ] Transfer decrements source and increments destination exactly once.
- [ ] Retail sale moves stock and payment state exactly once.
- [ ] Wholesale sale moves stock and payment state exactly once.
- [ ] Accountant can process permitted payment states.
- [ ] Return/change workflow creates compensating stock effects.
- [ ] Stocktake adjustment creates the correct ledger and audit records.
- [ ] Reports reflect persisted downstream data.
- [ ] Notifications reference real business identifiers.
- [ ] Audit History uses operation numbers rather than opaque UUID fragments.
- [ ] Mobile critical login/navigation workflow passes.

Browser pass criteria:

- 100% critical-task success.
- No unhandled console error.
- No unexpected API `4xx` or `5xx`.
- No visible action returns an unexpected `403`.
- No open Sev-1 or Sev-2 defect.
- Database queries confirm every downstream effect.

## Phase 9 — Failure injection and consistency proof

- [ ] Fail catalog mutation after catalog save; verify catalog/audit/outbox all roll back.
- [ ] Fail replenishment after Operations writes; verify Operations/Notifications all roll back.
- [ ] Fail payment initialization after payment write; verify payment/audit all roll back.
- [ ] Interrupt outbox delivery; verify retry and delivery receipt behavior.
- [ ] Exhaust outbox attempts; verify dead-letter state and controlled replay.
- [ ] Throw an unexpected scheduled-job exception; verify API remains alive and next pass retries.
- [ ] Cancel each worker; verify graceful cancellation without error reporting.
- [ ] Run parallel stock mutation; verify one winner, one `409`, no negative or partial state.
- [ ] Retry an idempotent command; verify no duplicate stock or money movement.

Pass criteria:

- No partial multi-context state.
- No duplicate ledger or payment effect.
- No lost committed event.
- Host health remains available.
- Retry behavior is bounded, observable, and safe.

## Phase 10 — Backup and restore certification

### 10.1 Backup

```powershell
.\scripts\backup-db.ps1
```

- [ ] Record backup path, size, timestamp, database version, and SHA-256.
- [ ] Verify the backup is non-empty and readable.
- [ ] Copy backup to storage outside the application host.
- [ ] Confirm retention and encryption.
- [ ] Confirm newest recoverable point meets RPO <= 15 minutes.

### 10.2 Restore to isolation

- [ ] Create an empty, isolated PostgreSQL instance/volume.
- [ ] Resolve and print the target before using `-Force`.
- [ ] Confirm the target is not production.
- [ ] Restore the backup.
- [ ] Run the release migrator.
- [ ] Start the release API.
- [ ] Verify `/ready`.
- [ ] Reconcile stock totals, payment totals, operation lineage, audit rows, corrections, and outbox state.
- [ ] Record restore start/end times.

Warning: `scripts/restore-db.ps1` runs `pg_restore --clean --if-exists` and is destructive to its target.

Pass criteria:

- RPO <= 15 minutes.
- RTO <= 60 minutes.
- No missing or inconsistent critical record.
- No pending migration.
- Restored API reaches readiness.

## Phase 11 — Production configuration certification

- [ ] `ASPNETCORE_ENVIRONMENT=Production`.
- [ ] Unique strong JWT secret supplied from protected secret storage.
- [ ] Unique strong database password supplied from protected secret storage.
- [ ] Exact HTTPS frontend origin configured.
- [ ] Trusted proxy network matches the actual Caddy/container network.
- [ ] Data Protection key path is an absolute persistent volume.
- [ ] `DATABASE_AUTO_MIGRATE=false`.
- [ ] `DATABASE_BASELINE_EXISTING_SCHEMA=false`.
- [ ] Database is not publicly exposed.
- [ ] TLS certificate issuance and renewal are verified.
- [ ] Shopify secrets exist only when Shopify is enabled.
- [ ] Rate limits support normal browser background refresh without blocking health.
- [ ] OTLP/metrics endpoint is reachable when configured.
- [ ] Backup age, outbox backlog, health, restarts, and database errors are alertable.
- [ ] No secret appears in Git, command output, logs, screenshots, or reports.

Pass criteria:

- Missing/weak required configuration fails startup.
- Valid production configuration starts cleanly.
- Key material survives container replacement.
- TLS, proxy headers, CORS, authentication, and health behave as intended.

## Phase 12 — Release decision and controlled deployment

### 12.1 Pre-deployment decision

- [ ] Release commit recorded.
- [ ] Image digest recorded.
- [ ] Build/static checks green.
- [ ] Backend/PostgreSQL tests green.
- [ ] Container scan green.
- [ ] Fresh/upgrade rehearsal green.
- [ ] Seeded browser acceptance green.
- [ ] Failure-injection suite green.
- [ ] Backup/restore drill green.
- [ ] Production configuration review green.
- [ ] Rollback owner assigned.
- [ ] Maintenance window approved.
- [ ] Incident, migration, correction, and outbox runbooks available.

### 12.2 Deployment

- [ ] Record deployment start time.
- [ ] Confirm newest verified backup.
- [ ] Start/confirm PostgreSQL.
- [ ] Run explicit migrator.
- [ ] Stop on migration failure.
- [ ] Start API and wait for readiness.
- [ ] Start frontend and proxy.
- [ ] Record container image digests and statuses.

### 12.3 Post-deployment smoke test

- [ ] `/health` is Healthy.
- [ ] `/ready` is Healthy.
- [ ] Restart count is `0`.
- [ ] Frontend loads through production TLS.
- [ ] One valid user can authenticate.
- [ ] One read-only role check succeeds.
- [ ] One controlled catalog-to-stock transaction succeeds.
- [ ] Corresponding audit and outbox records exist.
- [ ] No unexpected API/browser error appears.
- [ ] Outbox backlog is stable/draining.

### 12.4 Rollback triggers

Rollback immediately when any of these occurs:

- [ ] Migration failure or unexpected schema drift.
- [ ] Persistent health/readiness failure.
- [ ] Repeated API restart.
- [ ] Duplicate/missing stock movement.
- [ ] Duplicate/missing payment movement.
- [ ] Authentication failure for valid users.
- [ ] Growing unrecoverable outbox backlog.
- [ ] Any open Sev-1/Sev-2 production defect.

Rollback method:

1. Stop application instances.
2. Preserve API/PostgreSQL/outbox evidence.
3. Prefer a safe forward repair when data lineage remains intact.
4. When database rollback is required, restore the verified pre-migration backup.
5. Reconcile stock, payment, operation, audit, and correction lineage.
6. Reopen only after peer review and clean health/readiness checks.

---

# Evidence register

Fill one row for every certification execution. Do not mark a gate complete without evidence.

| Gate | Command/test | Candidate SHA/image digest | Date/operator | Result | Evidence path/link |
|---|---|---|---|---|---|
| Release build | `dotnet build Lensee.slnx --configuration Release --no-restore -warnaserror` | `33f1311` | 2026-08-26 / Codex | Pass — 0 warnings / 0 errors | Local command output |
| Unit/contract tests | `dotnet test backend/Lensee.Tests/Lensee.Tests.csproj --configuration Release --no-build --no-restore --logger "console;verbosity=minimal"` | `33f1311` | 2026-08-26 / Codex | Pass — 164 passed, 0 failed, 0 skipped; rerun not required because `33f1311` changes only PostgreSQL test code | Local command output |
| PostgreSQL tests | | | | | |
| Frontend checks/build | | | | | |
| Dependency review | | | | | |
| Image scan | | | | | |
| Fresh migration | | | | | |
| Upgrade migration | | | | | |
| Runtime health/readiness | | | | | |
| Seeded browser workflow | | | | | |
| Failure injection | | | | | |
| Backup | | | | | |
| Isolated restore | | | | | |
| Production configuration | | | | | |
| Post-deploy smoke | | | | | |

# Defect register

| Defect ID | Severity | Area | Reproduction/evidence | Owner | Status | Release impact |
|---|---|---|---|---|---|---|
| P0-01 | Critical | Release candidate | Application hardening candidate committed locally at `33f1311` | Codex | Resolved | Does not waive downstream certification gates |
| P0-02 | Critical | Deployment | API starts before explicit migration in current script | | Open | Blocks release |
| P0-03 | Critical | Runtime | Deployment readiness check requires Bash on Alpine | | Open | Blocks release |
| P0-04 | Critical | Test environment | Docker daemon unavailable on 2026-08-26 | | BLOCKED | Blocks revalidation |
| P0-05 | Critical | E2E safety | Default E2E setup deletes Compose volumes | | Open | Blocks seeded acceptance |
| P0-06 | Critical | Browser acceptance | Critical business-day workflow is incomplete | | Open | Blocks certification |
| P0-07 | Critical | Recovery | Restore drill is incomplete | | Open | Blocks certification |

# Final certification record

| Decision field | Value |
|---|---|
| Final decision | `NO-GO` / `GO WITH CONDITIONS` / `GO` |
| Approved candidate SHA | |
| Approved image digest | |
| Database backup ID/hash | |
| Migration result | |
| Browser acceptance result | |
| Restore RPO/RTO | |
| Open Sev-1 defects | |
| Open Sev-2 defects | |
| Rollback owner | |
| Approver | |
| Decision timestamp | |

The release can move to `GO` only when all Critical checklist items are checked, the evidence register is complete, no Sev-1/Sev-2 defect remains, and the exact committed candidate has passed the complete process.
