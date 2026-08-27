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

1. The implementation candidate through `0514711` is committed locally; PostgreSQL, container, browser, restore, and deployment acceptance evidence must still be completed before certification.
2. Docker Desktop is now available, but its 7.41 GiB allocation is below the Phase 2 target of 8 GiB; full container, runtime, and browser evidence remains incomplete.
3. `scripts/deploy-prod.ps1` has been corrected to run the migrator before promotion, but the production-shaped ordering and idempotence acceptance run remains open.
4. The Alpine-compatible Docker health check is implemented, but its runtime readiness/failure evidence remains open.
5. `scripts/e2e-setup.ps1` now targets an isolated project, but the required two-run isolation acceptance is still open.
6. The seeded, authenticated, role-based catalog-to-supply-to-payment browser workflow is not complete for this candidate.
7. A real backup/isolated-restore/migration drill has not been recorded.

## Readiness scorecard

| Area | Score | Current assessment |
|---|---:|---|
| Domain and transaction consistency | 8/10 | Stronger; PostgreSQL failure/concurrency tests previously passed |
| Database migration safety | 8/10 | Fresh and upgrade paths previously passed; must rerun from immutable candidate |
| Runtime/container | 8/10 | Alpine image previously healthy and clean; Docker smoke and one disposable PostgreSQL upgrade test now pass, but allocation is below the planned target |
| Production configuration | 7/10 | Fail-fast safeguards exist; target environment is unverified |
| Deployment automation | 6/10 | Migration ordering and Alpine-compatible readiness are implemented; runtime acceptance is still a blocker |
| Browser/business workflows | 4/10 | Automated suites exist but complete seeded acceptance is open |
| Backup and disaster recovery | 4/10 | Runbooks/scripts exist; no completed restore drill evidence |
| Observability/incident operations | 7/10 | Health, telemetry, outbox, and runbooks exist; live alerting is unverified |
| Overall certification | **6.5/10** | **NO-GO** |

## Open blocker register

| ID | Severity | Blocker | Required resolution | Status |
|---|---|---|---|---|
| P0-01 | Critical | No immutable release candidate | Review, commit, and build from a clean tree | Resolved — application candidate `33f1311` committed locally; no push/deployment performed |
| P0-02 | Critical | Production deployment acceptance is incomplete | Prove DB → migrator → API → frontend/proxy order and idempotence against production-shaped Compose | Open — implementation `83b7975`; acceptance unrun |
| P0-03 | Critical | Alpine readiness acceptance is incomplete | Prove Docker health transitions and actionable failure handling in the release image | Open — implementation `83b7975`; acceptance unrun |
| P0-04 | Critical | Docker Desktop allocation is below the Phase 2 target | Allocate at least 8 GiB to Docker Desktop, then rerun the Phase 2 environment-safety runbook before complete Docker/PostgreSQL gates | BLOCKED — Engine and disposable upgrade test pass, but 7.41 GiB is below target on 2026-08-26 |
| P0-05 | Critical | E2E isolation acceptance is incomplete | Prove two isolated reset/seed runs leave default-project containers and volumes unchanged | Open — implementation `eb93b99`; acceptance unrun |
| P0-06 | Critical | Authenticated seeded business-day workflow incomplete | Run all critical role workflows with persisted-state proof | Open |
| P0-07 | Critical | Backup/restore drill incomplete | Back up, restore to isolation, migrate, reconcile, measure RPO/RTO | Open |
| P1-01 | High | Production secrets/TLS/proxy/key storage unverified | Validate actual deployment configuration without exposing secrets | Open |
| P1-02 | High | Failure injection incomplete across all critical workflows | Prove rollback/retry/no duplicate state | Open |
| P1-03 | High | Static format gate fails on existing line endings | Remediate CRLF `ENDOLINE` findings in a reviewed candidate, then rerun Phase 3 | Open |

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

### 1.0 Implementation state

Phase 1 code is implemented locally but remains unverified at runtime because Docker was unavailable during its implementation run. Phase 2 later restored Docker access, but did not run the production-shaped or two-run E2E acceptance. The changes remove production seed switches, use the explicit Compose migrator, replace the Bash probe with an API Docker health check, and move E2E reset/seed activity to `lensee-e2e` with loopback-only ports `58181`, `55000`, and `53001`.

- [~] Deployment sequence implementation is complete; production-shaped rehearsal and idempotence proof remain required.
- [~] Alpine-compatible readiness implementation is complete; runtime health proof remains required.
- [~] Dedicated E2E project/volumes/seeds implementation is complete; two-run isolation proof remains required.
- [~] CI log collection is aligned to the dedicated E2E Compose project.

Static evidence recorded on 2026-08-26: PowerShell parsing passed for both changed scripts; production and E2E Compose merges passed with non-secret dummy production values; `dotnet build Lensee.slnx --configuration Release --no-restore -warnaserror` passed with 0 warnings/errors; unit/contract tests passed 164/164; and `npm run check` passed. Docker Engine was unavailable, so no migrator, container, or reset command was executed.

Evidence to collect when Docker is available: merged Compose output, migrator log, API Docker health status, service/image status, two consecutive E2E setup results, and proof that default-project volumes are unchanged.

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

- [~] Production instances remain `Database:AutoMigrate=false`.
- [~] The migrator is the only schema-mutating process.
- [~] A failed migration prevents API promotion.
- [~] The script captures migration logs on failure.
- [~] Rerunning the script is idempotent.

### 1.2 Replace the Bash readiness probe

- [~] Remove the `bash -lc` and `/dev/tcp` dependency from `scripts/deploy-prod.ps1`.
- [~] Use `Invoke-RestMethod`, a Docker health status, or another installed Alpine-compatible mechanism.
- [~] Validate response status and JSON `status` value.
- [~] Print the final 120 API log lines when readiness fails.
- [~] Return a non-zero exit code on timeout.

Pass criteria:

- The real Alpine release image reaches readiness without Bash.
- Readiness failure is actionable and never reported as success.

### 1.3 Isolate destructive E2E setup

- [~] Add a dedicated E2E Compose configuration with unique container names, ports, network, and volumes.
- [~] Use a dedicated Compose project name.
- [~] Refuse to run when `ASPNETCORE_ENVIRONMENT=Production`.
- [~] Resolve and print the target project, database, and volume before resetting.
- [~] Limit `down --volumes` to the dedicated E2E project.
- [~] Never point `scripts/restore-db.ps1 -Force` at production during E2E.

Pass criteria:

- Existing development and production volumes remain unchanged.
- Two consecutive setup runs produce the same baseline.
- Test credentials are unique, synthetic, and absent from production configuration.

## Phase 2 — Restore the verification environment

### 2.0 Execution plan and current baseline

**Objective:** establish a Docker-capable, evidence-backed verification environment without disturbing valued local, E2E, or production-like data. Phase 2 authorizes only disposable container checks and read-only inspection; application deployment, database reset, migration rehearsal, and browser testing remain in their later phases.

**Acceptance criteria:**

- WHEN Docker Desktop is started THEN `docker info` SHALL return both client and server information using the Linux-container engine.
- IF a certification or E2E port is already listening THEN the operator SHALL identify its owner and preserve it unless it is explicitly confirmed as disposable test infrastructure.
- WHEN a disposable Docker container runs successfully THEN the environment SHALL record the command, engine version, resource allocation, and result before any PostgreSQL test is attempted.
- WHEN the Testcontainers smoke test completes THEN the container SHALL be automatically removed and the full PostgreSQL suite SHALL still be rerun in Phase 4.
- IF Docker startup, capacity, port ownership, or Testcontainers fails THEN Phase 2 SHALL remain blocked and no default Compose volume may be reset as a workaround.

**Planned sequence:**

1. [~] Start Docker Desktop in Linux-container mode; record Docker Desktop version, engine server version, active context, and its configured allocation of at least 4 CPUs and 8 GiB memory — started and recorded, but memory is below target.
2. [x] Run `docker info`, `docker compose version`, and `docker run --rm hello-world`; record output without secrets.
3. [x] Inspect default and `lensee-e2e` Compose projects, named volumes, and ports before any stack command. Do not use `docker compose down --volumes` for the default project.
4. [x] Resolve port conflicts: `8181`, `5000`, and `3001` are intentionally attributed to the pre-existing default stack; E2E ports `58181`, `55000`, and `53001` are free for `lensee-e2e`.
5. [x] Run one disposable Testcontainers smoke test with `LENSEE_RUN_POSTGRES_TESTS=true` and filter `MigrationUpgradePostgresTests`; record the result, then proceed to the complete Phase 4 suite only after this environment gate passes.

**Execution result (2026-08-26 / Codex):** Docker Desktop was started in the background. The active CLI context was `desktop-linux`; Docker reported Linux containers, client/server `29.4.0`, 16 CPUs, and `7,956,238,336` bytes (7.41 GiB) memory. The resource allocation misses the planned 8 GiB minimum, so Phase 2 remains incomplete even though the disposable container and Testcontainers upgrade smoke test passed. Docker Desktop startup also resumed the pre-existing default stack; it was inspected only and was not reset or otherwise changed by a certification command.

| Check | Result | Phase 2 action |
|---|---|---|
| Docker Engine | Linux Engine available; client/server `29.4.0`; CLI context `desktop-linux` | Passed — retain Engine evidence. |
| Docker Compose CLI | `v5.1.1` | Passed. |
| Host/Docker capacity | Host: 16 logical processors, 15.31 GiB physical memory, 73.59 GiB free on `D:`. Docker: 16 CPUs, 7.41 GiB memory. | **Blocked** — increase Docker allocation to at least 8 GiB before declaring Phase 2 complete. |
| Default project and ports | Pre-existing `lensee_api`, `lensee_db` (healthy), and `lensee_web` were running. Ports `8181`, `5000`, and `3001` were intentionally owned by that stack; no default-project command was run. | Passed — preserved. |
| E2E project and ports | `lensee-e2e` had no containers or project-labelled volumes; ports `58181`, `55000`, and `53001` were free. | Passed — reserved for later E2E acceptance. |
| Disposable checks | `docker run --rm hello-world` passed. Filtered `MigrationUpgradePostgresTests` passed 1/1 with `LENSEE_RUN_POSTGRES_TESTS=true`. | Passed — Phase 4 full suite remains required. |

**Superseded pre-start baseline (2026-08-26):**

| Check | Result | Phase 2 action |
|---|---|---|
| Docker Engine | Unreachable at `//./pipe/dockerDesktopLinuxEngine` | Start Docker Desktop; do not claim container verification yet. |
| Docker Compose CLI | `v5.1.1` | Re-record after Engine startup. |
| Host capacity | 16 logical processors; 15.31 GiB physical memory; 73.60 GiB free on `D:` | Confirm Docker Desktop allocation is at least 4 CPUs / 8 GiB before testing. |
| Default ports | `5000` and `3001` had no listener; `8181` is owned by PID `8112` (`postgres`) | Preserve and identify this PostgreSQL instance before using the default stack. |
| E2E ports | `58181`, `55000`, and `53001` had no listener | Reserve for the dedicated `lensee-e2e` project. |

**Safe command set after Docker Desktop is available:**

```powershell
docker info
docker context ls
docker compose version
docker run --rm hello-world

docker compose ps -a
docker compose -p lensee-e2e -f docker-compose.yml -f docker-compose.e2e.yml ps -a
docker volume ls
Get-NetTCPConnection -State Listen | Where-Object { $_.LocalPort -in 8181,5000,3001,58181,55000,53001 }

$env:LENSEE_RUN_POSTGRES_TESTS = "true"
try {
    dotnet test backend/Lensee.PostgresIntegrationTests/Lensee.PostgresIntegrationTests.csproj `
      --configuration Release --no-build --no-restore `
      --filter "FullyQualifiedName~MigrationUpgradePostgresTests" `
      --logger "console;verbosity=minimal"
} finally {
    Remove-Item Env:LENSEE_RUN_POSTGRES_TESTS -ErrorAction SilentlyContinue
}
```

Evidence:

| Item | Result | Date/operator | Evidence path/link |
|---|---|---|---|
| Docker Engine/client-server versions | Pass — Linux Engine, client/server `29.4.0`; active CLI context `desktop-linux` | 2026-08-26 / Codex | Local command output: `docker version`, `docker info`, `docker context ls` |
| Docker Desktop CPU/memory allocation | Blocked — 16 CPUs, 7.41 GiB memory; below 8 GiB phase target | 2026-08-26 / Codex | Local `docker info` output |
| Port/project/volume ownership review | Pass — default stack attributed and preserved; `lensee-e2e` has no containers or labelled volumes; E2E ports free | 2026-08-26 / Codex | Local Compose, volume, and port inspection |
| Disposable container smoke test | Pass — `hello-world` completed and removed | 2026-08-26 / Codex | Local command output |
| Testcontainers smoke test | Pass — `MigrationUpgradePostgresTests`: 1 passed, 0 failed, 0 skipped | 2026-08-26 / Codex | Local `dotnet test` output |

- [x] Start Docker Desktop in Linux-container mode.
- [x] Run `docker info` and record client/server versions.
- [x] Run `docker compose version`.
- [~] Confirm adequate disk, memory, and CPU — Docker memory is 7.41 GiB, below the 8 GiB target.
- [x] Confirm no stale E2E containers or conflicting ports.
- [x] Confirm ports `8181`, `5000`, and `3001` are intentionally owned by the pre-existing default stack and preserve them.

Pass criteria:

- Docker Engine responds.
- Linux containers can run.
- PostgreSQL Testcontainers can start.
- No valued container/volume will be reset by certification tests.

## Phase 3 — Build and static validation

### 3.0 Execution plan and evidence boundary

**Objective:** prove that the exact committed candidate builds and passes static quality gates without changing tracked source, lockfiles, or generated frontend configuration.

**Preconditions:** Phase 2 does not need to be complete for these local checks, but the candidate SHA must be recorded immediately before execution. Preserve any unrelated untracked files, do not stage them, and require no tracked or staged difference before starting. The 2026-08-26 execution began clean at `0514711`.

**Acceptance criteria:**

- WHEN restore or `npm ci` changes a tracked lockfile THEN Phase 3 SHALL stop and record the diff; dependency changes require a separate reviewed candidate.
- WHEN formatting verification reports a difference THEN Phase 3 SHALL fail without running an auto-format command.
- WHEN a static guard fails THEN Phase 3 SHALL retain its command output and open a defect; it SHALL not suppress or raise a guard baseline merely to pass.
- WHEN the frontend build runs THEN it SHALL use `LENSEE_API_BASE_URL=http://localhost:5000` and SHALL leave tracked `frontend/config.js` unchanged.
- WHEN every command succeeds and `git diff --check`, tracked-diff, and staged-diff checks are clean THEN Phase 3 SHALL record the candidate SHA and command results in the evidence register.

**Planned sequential runbook:**

```powershell
# 0. Capture the exact candidate and preserve unrelated untracked files.
git rev-parse HEAD
git status --short
git diff --check
git diff --exit-code
git diff --cached --exit-code

# 1. Run .NET commands one at a time to avoid CS2012 file locks.
dotnet restore Lensee.slnx
dotnet build Lensee.slnx --configuration Release --no-restore -warnaserror
dotnet format Lensee.slnx --verify-no-changes --no-restore

# 2. Recreate root JavaScript dependencies from the lockfile, then run guards.
npm ci
npm run check
node --check frontend/app.js

# 3. Build deterministic local frontend config and prove it did not change tracked output.
$env:LENSEE_API_BASE_URL = "http://localhost:5000"
try {
    npm --prefix frontend run build
} finally {
    Remove-Item Env:LENSEE_API_BASE_URL -ErrorAction SilentlyContinue
}
git diff --exit-code -- frontend/config.js

# 4. Final candidate-integrity checks.
git diff --check
git diff --exit-code
git diff --cached --exit-code
```

**Failure handling:** capture the failing command and candidate SHA in the evidence register. Do not run `dotnet format` without `--verify-no-changes`, `npm update`, `npm install`, `git restore`, or a dependency upgrade as a corrective shortcut. Investigate and commit any legitimate remediation as a new candidate, then rerun the complete sequence.

**Planned evidence:**

| Item | Required result | Candidate SHA | Date/operator | Evidence path/link |
|---|---|---|---|---|
| Baseline status and diff checks | Pass — clean before execution; final content and staged diffs clean | `0514711` | 2026-08-26 / Codex | Local `git status`, diff, cached-diff, and hash output |
| Restore and Release build | Pass — restore up to date; build 0 warnings / 0 errors | `0514711` | 2026-08-26 / Codex | Local command output |
| Format verification | **Fail** — `ENDOFLINE` requires CRLF in existing backend files, including `CatalogEndpoints.cs` and `PaymentIntegrityPostgresTests.cs`; no auto-format was run | `0514711` | 2026-08-26 / Codex | Local `dotnet format --verify-no-changes` output |
| Dependency reproducibility | Pass — `npm ci` added 3 packages, audited 4, found 0 vulnerabilities; no lockfile content drift | `0514711` | 2026-08-26 / Codex | Local command and final diff output |
| Static guards and syntax | Pass — encoding (71 files), localization (1,071 Arabic translations / 554 UI strings), DOM sink (157/157 legacy uses), endpoint boundary, and JS syntax | `0514711` | 2026-08-26 / Codex | Local `npm run check` and `node --check` output |
| Frontend build | Pass — deterministic build used `http://localhost:5000`; `frontend/config.js` content hash equalled the index and final diff was clean | `0514711` | 2026-08-26 / Codex | Local build, hash, and diff output |

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

- [x] Restore succeeds without changing locked dependencies unexpectedly.
- [x] Release build succeeds with zero warnings/errors.
- [~] Formatting verification passes — blocked by existing `ENDOLINE` findings; no auto-format was run.
- [x] Encoding check passes.
- [x] Localization check passes.
- [x] Unsafe-DOM-sink guard passes.
- [x] Endpoint-boundary guard passes.
- [x] Frontend JavaScript syntax passes.
- [x] Frontend production build succeeds.

Evidence:

| Check | Result | Evidence path/link |
|---|---|---|
| Release build | Pass — 0 warnings / 0 errors at `0514711` | Local command output |
| Format | Fail — existing CRLF `ENDOLINE` violations; remediation requires a reviewed candidate | Local command output |
| Frontend checks | Pass — all configured checks and JS syntax passed | Local command output |
| Frontend build | Pass — deterministic config content unchanged | Local command/hash/diff output |

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
| Release build | `dotnet build Lensee.slnx --configuration Release --no-restore -warnaserror` | `0514711` | 2026-08-26 / Codex | Pass — 0 warnings / 0 errors | Local command output |
| Unit/contract tests | `dotnet test backend/Lensee.Tests/Lensee.Tests.csproj --configuration Release --no-build --no-restore --logger "console;verbosity=minimal"` | `33f1311` | 2026-08-26 / Codex | Pass — 164 passed, 0 failed, 0 skipped; rerun not required because `33f1311` changes only PostgreSQL test code | Local command output |
| PostgreSQL Testcontainers smoke | Filtered `MigrationUpgradePostgresTests` with `LENSEE_RUN_POSTGRES_TESTS=true` | `0514711` | 2026-08-26 / Codex | Pass — 1 passed, 0 failed, 0 skipped; not a substitute for Phase 4 full suite | Local command output |
| Docker environment smoke | `docker run --rm hello-world` | N/A | 2026-08-26 / Codex | Pass — Linux engine client/server 29.4.0; Docker allocation 7.41 GiB remains below Phase 2 target | Local command output |
| Format verification | `dotnet format Lensee.slnx --verify-no-changes --no-restore` | `0514711` | 2026-08-26 / Codex | Fail — existing `ENDOFLINE` CRLF violations; no automatic remediation performed | Local command output |
| Frontend checks/build | `npm ci`; `npm run check`; `node --check frontend/app.js`; deterministic frontend build | `0514711` | 2026-08-26 / Codex | Pass — checks passed; config content unchanged | Local command output |
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
| P0-02 | Critical | Deployment | Implementation `83b7975` is pending production-shaped ordering/idempotence evidence | | Open | Blocks release |
| P0-03 | Critical | Runtime | Implementation `83b7975` is pending Alpine Docker-health/failure evidence | | Open | Blocks release |
| P0-04 | Critical | Test environment | Docker Engine and filtered Testcontainers smoke pass, but 7.41 GiB allocation is below 8 GiB Phase 2 target | | BLOCKED | Blocks complete Docker/PostgreSQL revalidation |
| P0-05 | Critical | E2E safety | Implementation `eb93b99` is pending two-run default-volume isolation proof | | Open | Blocks seeded acceptance |
| P1-03 | High | Static validation | Existing CRLF `ENDOLINE` violations fail `dotnet format --verify-no-changes` at `0514711` | | Open | Blocks Phase 3 completion |
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
