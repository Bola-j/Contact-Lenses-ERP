# CI Test Inventory

The current Playwright discovery inventory is **75 tests in 25 files** (`npx playwright test --list --reporter=json`, 2026-10-15). Nothing in this refactor deletes or changes business assertions. CI selection is reduced by tiering, not by removing coverage.

| Test file | Cases | Primary coverage | Risk / tier |
| --- | ---: | --- | --- |
| `audit-account-safeguards.spec.js` | 1 | Duplicate usernames, protected accounts, audit traceability | High / full |
| `auth-permissions.spec.js` | 4 | Login, role navigation, permission denials and role API matrix | High / one login-role journey in PR smoke; all cases full |
| `catalog-crm.spec.js` | 3 | Product/SKU lifecycle, validation, merchant lifecycle and Arabic history | High / full |
| `erp-admin-document-access.spec.js` | 1 | ERPAdmin operational-document access boundary | High / full |
| `finance-workspace.spec.js` | 3 | Finance withdrawal workspace by role | High / full |
| `friendly-identifiers.spec.js` | 2 | UUID presentation masking and report search picker behavior | Medium / full |
| `full-business-day.spec.js` | 1 | Cross-module business lifecycle | High / full certification |
| `inventory-transfer.spec.js` | 3 | Receipt, stock/batches, transfer, editor route and data | High / full |
| `language-prefix.spec.js` | 4 | Localized routes, URL state/history and static assets | Medium / full |
| `lensee-ui-comprehensive-headed.spec.js` | 8 | Broad role and multi-module UI certification | High / full |
| `localization.spec.js` | 7 | Arabic/English labels, direction, forms and language switching | Medium / full |
| `notifications-reports.spec.js` | 2 | Notification state/actions and CSV/PDF reports | High / full |
| `operations-return-flow.spec.js` | 7 | Return, entitlement settlement, waiver and payment-method workflows | High / full |
| `operations-sales-return-change.spec.js` | 5 | Sale-line kinds, retail checkout, wholesale lifecycle, write-off and detail | High / anonymous retail checkout in PR smoke; all cases full |
| `payment-notification-presentation.spec.js` | 2 | Localized payment notifications and audit presentation | Medium / full |
| `payments-accounting.spec.js` | 1 | Assignment, draft/review, balance and terminal-state rules | High / full |
| `payments-operation-allocation.spec.js` | 1 | Operation-reference collection allocation | High / full |
| `payments-workflow-ui.spec.js` | 1 | Unified payments collection workspace | High / full |
| `presentation-safety.spec.js` | 2 | Unknown-route recovery, UUID/JSON presentation safety | Medium / full |
| `production-scenarios.spec.js` | 5 | Concurrent stock, double-submit, stale stock, transfer and approval races | Critical / full |
| `record-editing-complex.spec.js` | 5 | Historical snapshots, revisions, payment and stocktake edits, stale transitions | High / full |
| `scenario-gaps.spec.js` | 4 | User lifecycle, authorization, sessions and invalid tokens | High / full |
| `stocktake.spec.js` | 1 | Stocktake creation, count validation and confirmation | High / full |
| `localization-mobile.spec.js` | 1 | Arabic RTL mobile login and overflow | Medium / full mobile project |
| `mobile.spec.js` | 1 | Mobile warehouse navigation and operational usability | High / full mobile project |

## Runtime and overlap measurement

- The source configuration uses one Playwright worker and allows up to 90 seconds per test. This is a per-test ceiling, not an observed suite runtime.
- The old CI workflow serially ran the full browser suite after its core job. The earlier reported 69-test run and current 75-test discovery are different snapshots; the current discovered test list is the refactor baseline.
- Per-file elapsed durations and overlap are not available from discovery alone. The new workflows should be observed for their first successful PR, nightly, and release runs; use workflow job/test timing output before further pruning or raising parallelism.
- The existing business-day, headed-comprehensive, production-race, and editing cases intentionally overlap some module operations but cover distinct end-to-end sequencing, presentation, concurrency, or revision risks. Keep them in full certification unless timing evidence and a documented equivalent test justify a later change.
