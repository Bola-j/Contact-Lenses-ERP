# Lensee Release E2E Suite

This suite drives the real vanilla JavaScript frontend with Playwright. It also performs focused direct API assertions for authorization, invalid transitions, and race-condition checks where UI-only testing cannot prove backend protection.

## MVP Scope Documents

- `docs/testing/mvp-e2e-coverage-map.md`: MVP feature classification and test ownership.
- `docs/testing/mvp-e2e-test-plan.md`: P0/P1/P2 testing strategy.
- `docs/testing/full-mvp-daily-scenarios.md`: daily scenario source for role, module, workflow, logs, editing, and concurrency coverage.
- `docs/testing/how-to-run-e2e.md`: local commands and environment variables.
- `docs/testing/mvp-out-of-scope.md`: future PRD features intentionally not tested now.
- `docs/testing/mvp-found-bugs.md`: defect register for E2E findings.

## Reset And Run

The full release suite resets only its dedicated `lensee-e2e` Compose project. It never targets the default development project or production volumes.

```powershell
npm run e2e:setup
npm run e2e
```

Useful subsets:

```powershell
npm run e2e:smoke
npm run e2e:business-day
npm run e2e:headed
npm run e2e:ui
npm run e2e:report
```

Alias scripts:

```powershell
npm run test:e2e
npm run test:e2e:mvp
npm run test:e2e:debug
npm run test:e2e:report
```

## Coverage Files

- `auth-permissions.spec.js`: login, role navigation, direct API permission denial.
- `catalog-crm.spec.js`: catalog lifecycle, validation, merchant/rep lifecycle, notes.
- `inventory-transfer.spec.js`: receipt, balances, batches, targets, transfer lifecycle, route preservation.
- `operations-sales-return-change.spec.js`: sales, reserve, return/change warnings, write-off, operation detail/version surfaces.
- `payments-accounting.spec.js`: assignment, accountant draft, admin reject/approve, balance, completed-log assignment rule.
- `notifications-reports.spec.js`: notification filters/read state/action links, report tables, CSV/PDF downloads.
- `stocktake.spec.js`: session, SKU/lot/expiry lines, validation, confirmation.
- `mobile.spec.js`: mobile warehouse clerk shell, inventory, operations.
- `full-business-day.spec.js`: cross-module happy path from catalog/CRM through stock, sales, payment, return/change, stocktake, reports.
- `production-scenarios.spec.js`: last-stock race, double-submit, stale-stock, transfer receive race, payment approval race.
- `record-editing-complex.spec.js`: historical snapshots, draft editing, payment reassignment/rejection loop, stocktake editing locks, stale transitions.
- `scenario-gaps.spec.js`: scenario-derived admin user lifecycle, password/deactivation behavior, user-maintenance authorization, parallel sessions, invalid stored tokens.

## Support Helpers

The existing `e2e/support/helpers.js` remains the shared compatibility layer. Thin concern-based wrappers also exist under `e2e/support/` for new tests: auth, api, catalog, crm, inventory, operations, payments, downloads, and notifications.

## Credentials

Defaults match the dedicated synthetic E2E seed. They are valid only in the isolated `lensee-e2e` database and may be overridden by environment variables when required:

```powershell
$env:LENSEE_E2E_ADMIN_USER = "e2e_admin"
$env:LENSEE_E2E_ADMIN_PASSWORD = "E2E-only-not-production-2026!"
$env:LENSEE_E2E_ERP_ADMIN_USER = "e2e_erp_admin"
$env:LENSEE_E2E_ERP_ADMIN_PASSWORD = "E2E-only-not-production-2026!"
$env:LENSEE_E2E_CLEVEL_USER = "e2e_clevel"
$env:LENSEE_E2E_CLEVEL_PASSWORD = "E2E-only-not-production-2026!"
$env:LENSEE_E2E_ACCOUNTANT_USER = "e2e_accountant"
$env:LENSEE_E2E_ACCOUNTANT_PASSWORD = "E2E-only-not-production-2026!"
$env:LENSEE_E2E_CLERK_USER = "e2e_roxy_clerk"
$env:LENSEE_E2E_CLERK_PASSWORD = "E2E-only-not-production-2026!"
$env:LENSEE_E2E_RETAIL_CLERK_USER = "e2e_retail_clerk"
$env:LENSEE_E2E_ONLINE_CLERK_USER = "e2e_online_clerk"
```

The default isolated ports are PostgreSQL `58181`, API `55000`, and frontend `53001`, all loopback-only. The suite uses `LENSEE_E2E_API_URL` when set, otherwise `http://127.0.0.1:55000`.

## Workload capacity test

The workload harness uses eight dedicated `e2e_load_01` through `e2e_load_08` accounts and only authenticated read routes. It never creates operations, stock movements, payments, or production data.

```powershell
npm run e2e:setup
npm run workload:e2e
```

The default run simulates eight concurrent sessions for 60 seconds, with 500 ms think time. It records a redacted JSON report under `artifacts/workload/` and fails when readiness drops, any request fails above 1%, or p95 latency exceeds 2 seconds. To inspect a planned run without sending requests:

```powershell
node scripts/run-workload-test.mjs --users 8 --duration-seconds 60 --dry-run
```

The default target is the loopback-only E2E API. Any other target, including a VPS, requires both `--allow-live-target` and the environment confirmation `LENSEE_ALLOW_LIVE_WORKLOAD_TEST=I_UNDERSTAND_THIS_HITS_A_LIVE_SYSTEM`, plus a password supplied through `LENSEE_WORKLOAD_PASSWORD`. Do not run a live-target test without a maintenance window and dedicated test accounts.
