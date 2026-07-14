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

The full release suite is destructive to dev/demo data by design.

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

Defaults match the deterministic dev seed:

```powershell
$env:LENSEE_E2E_ADMIN_USER = "admin"
$env:LENSEE_E2E_ADMIN_PASSWORD = "12121212"
$env:LENSEE_E2E_CLEVEL_USER = "clevel"
$env:LENSEE_E2E_CLEVEL_PASSWORD = "12121212"
$env:LENSEE_E2E_ACCOUNTANT_USER = "accountant"
$env:LENSEE_E2E_ACCOUNTANT_PASSWORD = "12121212"
$env:LENSEE_E2E_CLERK_USER = "roxy_clerk"
$env:LENSEE_E2E_CLERK_PASSWORD = "12121212"
$env:LENSEE_E2E_RETAIL_CLERK_USER = "retail_clerk"
$env:LENSEE_E2E_ONLINE_CLERK_USER = "online_clerk"
```

The suite uses `LENSEE_E2E_API_URL` when set, otherwise `http://localhost:5000`.
