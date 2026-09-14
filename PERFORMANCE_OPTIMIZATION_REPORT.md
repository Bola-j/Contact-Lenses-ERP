# Performance optimization and verification ledger

Target envelope: 10 simultaneously active users on 2 vCPU / 4 GB RAM. Local, benchmark, and production Compose layers inherit the same active application limits: API 1 vCPU/1536 MiB, PostgreSQL 0.8 vCPU/1536 MiB, frontend 0.05 vCPU/128 MiB, and deployment proxy 0.05 vCPU/64 MiB. Build, migration, and load-generation processes run outside the active application budget.

## Implemented changes

- Catalog SKU selection uses paged server search and direct selected-SKU lookup. Search is debounced by 250 ms and supports product, category, brand, SKU attributes, active state, and deterministic ordering.
- Inventory nonzero balance filtering, counting, ordering, and paging execute in PostgreSQL. Only the selected page is enriched. Zero-stock compatibility remains database-driven from eligible location/SKU sources. Balances, batches, transactions, and expired/blocked batches have working 50-row UI paging.
- Supply lists use lightweight aggregate projections and opt-in `PagedResult` responses. Detail collection loading uses split queries. The editor retains a full JavaScript model with stable line identities while rendering 50 rows at a time. Saving and totals use the full model.
- Supply confirmation validates and loads required inventory rows together, locks the destination deterministically, applies all lines in memory, and saves once inside the existing shared transaction. Whole-document atomicity, audit, operation creation, and optimistic concurrency remain in place.
- Merchant balance reports aggregate all selected account entries in grouped SQL instead of issuing financial-detail queries for each merchant. Statements now honor their bounded `take` value. Merchant-account lists support opt-in paging before enrichment.
- Financial summary totals aggregate in SQL. PDF/XLSX rendering permits one active renderer and two waiting requests; excess requests receive HTTP 429 with `Retry-After: 10`. Existing row-limit validation remains explicit.
- Route changes cancel obsolete GET requests, identical GETs within one render are shared, stale search results are rejected, hidden tabs pause polling, and inventory refreshes run in two bounded waves.
- JavaScript/CSS assets use explicit versions, gzip, and one-year immutable caching. HTML remains revalidated.
- Docker image publishing compiles once. The API connection pool is capped at 20. PostgreSQL starts with `shared_buffers=384MB`, `work_mem=4MB`, `maintenance_work_mem=64MB`, `max_connections=40`, and `max_parallel_workers_per_gather=1`.

## Coverage ledger

Status meanings: **changed** has an implementation in this pass; **verified** has automated or live evidence; **inspected** was reviewed and retained because it was already bounded or no evidence justified a change.

| Surface | Backend families and services | Status | Evidence / disposition |
|---|---|---|---|
| Login and session | Auth, token and cookie session | Inspected, verified | Login route renders in Arabic RTL; health and anonymous boundaries remain covered by contract tests. |
| Dashboard | Operations, inventory, payments and notification summaries | Inspected | Existing bounded summary calls retained; route cancellation prevents stale dashboard work. |
| Catalog | Catalog endpoints and catalog mutation transaction | Changed, verified | Paged SKU search/direct lookup; contract test covers paging, multi-term filtering and lookup. |
| Inventory | Inventory endpoints, stock ledger, target replenishment | Changed, verified | DB-side balance paging; paged growing lists; live seeded counts preserved. |
| Supply | Supply endpoints and stock ledger | Changed, verified | Lightweight lists, split details, 50-row editor, one-save batch receipt; supply operation tests pass. |
| Operations and transfers | Operations endpoints, correction service, replenishment | Changed, verified | Remote SKU/batch selection and cancellation; existing FEFO, reversal, correction and idempotency tests pass. |
| Stocktake | Stocktake endpoints and ledger | Inspected, verified | Endpoint lists were already paged; operation/stocktake contract coverage retained. |
| CRM | CRM endpoints | Inspected, verified | Existing search and first-party merchant picker are bounded; contract suite passes. |
| Payments | Payment endpoints, capacity calculators, merchant accounts and reconciliation | Changed, verified | Paged merchant-account compatibility path, grouped report aggregation, bounded statements; payment/report tests pass. |
| Reports and documents | Report endpoints, catalog, PDF/XLSX/CSV renderers | Changed, verified | SQL financial summary, export capacity gate, explicit row limits; renderer and report contracts pass. |
| Notifications and navigation | Notification and navigation-reference endpoints | Inspected, verified | Existing paging retained; polling pauses while hidden and duplicate requests are shared. |
| Recalls | Recall endpoints, service and worker | Inspected | Existing worker is single-run and scheduled; no speculative index added without a representative query plan. |
| Shopify and outbox | Shopify endpoints/worker and outbox endpoints/service | Inspected, verified | Existing claim, `SKIP LOCKED`, bounded batch, retry and deduplication behavior retained. |
| Audit | Audit endpoints and writer | Inspected, verified | Existing paged audit UI and authorization contracts retained. |
| Administration | User endpoints and location administration | Inspected, verified | Small administrative sets retained; authorization tests pass. |
| Health and hosting | readiness/liveness, proxy forwarding, rate limiting | Changed, verified | Constrained containers healthy; readiness succeeds under effective limits. |
| Frontend delivery | All 13 authenticated routes plus login | Changed, verified | Versioned assets, gzip/cache headers, cancellation, dedupe, stale guards, RTL localization check. |

## Current live evidence

The constrained local stack was rebuilt against the preserved database. Effective Docker limits were read from container inspection, PostgreSQL settings were read from the running server, and the seeded row counts remained 12 products, 4,698 SKUs, 14,094 balances, 14,094 batches, and 4,698 supply lines. The browser rendered the Arabic login route with `lang=ar-EG`, `dir=rtl`, 54 DOM nodes, and versioned JavaScript/CSS assets. Nginx returned gzip for JavaScript, `Cache-Control: public, max-age=31536000, immutable` for versioned assets, and `no-cache` for HTML.

The representative nonzero-balance page query used the existing unique `(location_id, sku_id)` index, returned 50 rows in 1.255 ms, used a 29 kB in-memory incremental sort, and did not spill. This plan did not add a speculative index.

Validation completed on 2026-09-13:

- Release build succeeded.
- 211/211 application tests passed.
- 24/24 real PostgreSQL integration tests passed in isolated Testcontainers.
- Frontend syntax, encoding, localization (1,283 Arabic translations / 648 checked strings), and unsafe-DOM guards passed.
- All local, E2E/benchmark, production, and deployment Compose combinations rendered successfully.
- Live readiness reported PostgreSQL reachable and no pending EF migrations.
- Browser console contained no warnings or errors on the rebuilt Arabic login route.
- A read-only constrained smoke workload over SKU search, inventory pages, supply pages, financial summary, notifications, and session lookup completed with zero request/readiness failures: 1 user p95 125 ms (15 requests), 5 users p95 115 ms (75 requests), and 10 users p95 129 ms / p99 265 ms (150 requests). Evidence: `artifacts/workload/constrained-read-smoke-valid-20260913.json`.

The repository-wide endpoint-boundary guard remains red because the pre-existing payment workflow worktree has 30 direct persistence calls against its frozen baseline of 22. This performance pass added no payment persistence call and did not weaken the guard. The affected payment workflow should be extracted into command services before treating the aggregate `npm run check` as green.

Run `scripts/verify-performance-profile.ps1` after every deployment. Run `scripts/run-workload-test.mjs` only against an isolated database copy; it records p50/p95/p99 latency, response bytes, status counts, error rate, and readiness failures at configurable 1/5/10-user stages.

## Acceptance evidence still environment-dependent

The repository now contains the workload and resource-verification tooling, but the short read-only smoke is not the requested full acceptance run. A truthful before/after comparison, 60-minute 10-user mixed-workload soak, 4,698-line confirm timing, 10x database run, and production disk/network measurements require an isolated cloned dataset with disposable write fixtures and the production host. Do not interpret the local container results as VPS disk or network proof. Record those results beside this ledger without replacing the local evidence.

`artifacts/workload/invalid-auth-probe-20260913.json` records a deliberately invalid credential probe (401 followed by rate-limit 429 responses). It is diagnostic evidence only and must not be used as latency or capacity evidence.

## Deployment and rollback

1. Back up PostgreSQL and run migrations outside the active application budget.
2. Render and validate the Compose configuration, deploy one application stack, then run `scripts/verify-performance-profile.ps1`.
3. Warm health, SKU search, inventory, dashboard and one representative document before admitting users.
4. Run 1/5/10-user stages against the isolated clone, then the soak. Monitor container CPU throttling/memory and PostgreSQL waits/temp files alongside the JSON workload report.
5. Roll back by deploying the preceding application/frontend images. These performance changes add no schema migration; the prior database remains compatible. Restore the previous Compose files only if the old resource settings are intentionally required.
