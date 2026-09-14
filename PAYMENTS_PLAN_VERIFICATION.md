# Payments separation verification

## Automated backend

| Area | Evidence |
| --- | --- |
| Operations, payments, reporting, and migration contracts | Full rerun after workflow and statement changes: dotnet test backend/Lensee.Tests/Lensee.Tests.csproj --no-build --no-restore — 208/208 passed |
| PostgreSQL payment integrity and quoted identifiers | PaymentIntegrityPostgresTests — 9/9 passed |
| PostgreSQL payment suite current rerun | With `LENSEE_RUN_POSTGRES_TESTS=true`, PaymentIntegrityPostgresTests executed against Testcontainers PostgreSQL and passed 9/9 (1 minute 1 second). |
| PostgreSQL migration upgrade | MigrationUpgradePostgresTests — 1/1 passed |
| Migration discovery metadata | PaymentMigrationDiscoveryTests — 2/2 passed |
| Document rendering | DocumentRendererTests — 8/8 passed |
| Registered-merchant cash sale draft | CompletedCashSale_RequiresExplicitCollectionAndAdminApproval — passed; source-linked draft asserted |
| Shared collection scope guards | Registered-merchant cash receipts are rejected by the legacy cash adapter and must use MerchantAccount workflow; direct-operation requests carrying a merchant ID are rejected. Direct pending cash records are included in the review inbox with the parent workflow status and permitted actions. |
| History workspace separation | Cash receipt history derives merchant identity from its source operation before applying scope filters, so registered-merchant receipts stay in Merchant account history and cannot leak into Other payments. |
| Period statement closing balance | Closing balance is calculated from all posted entries through the selected end date, independently of the displayed row page limit. |
| Full-period statement rows | Merchant statement retrieval no longer silently truncates the posted ledger; all posted rows in the selected period are returned in sequence, while pending/rejected work remains excluded. Host build, full application tests 208/208, and PaymentIntegrityPostgresTests 9/9 passed after this change. |
| Multi-receipt review identity | Shared approve/reject routes resolve the parent log from the concrete cash-record ID, preventing one receipt from approving a different receipt on the same operation; PostgreSQL payment integrity rerun remained 9/9 passed. |
| Collection authorization policy | Collection-create routes use `payments.draft`, which Accountants/Admins/ERPAdmins possess; broader `payments.write` remains restricted, and `payments.approve` is limited to Admin/ERPAdmin with C-Level read access preserved. |
| Assignment capacity | Least-loaded accountant calculation counts both saved Draft and submitted PendingAdminReview merchant collections, plus open payment logs, with deterministic user-ID tie breaking. |
| Pending review balance reservation | `PendingAdminReview` installment and cash collections now contribute to pending totals and capacity reservations without contributing to confirmed/finalized balances. |
| Merchant-account pending totals | Merchant-account snapshots now include both saved `Draft` and submitted `PendingAdminReview` collections, plus both pending cash workflow states. |
| Pending balance unit coverage | `PaymentFinancialCapacityTests` — 6/6 passed, including submitted-collection pending neutrality. |
| Migration discovery | `PaymentMigrationDiscoveryTests` — 2/2 passed. |
| Document renderer | `DocumentRendererTests` — 8/8 passed. |
| Reset script syntax | PowerShell parser check passed; script remains preview-by-default and no reset was executed. |
| Reset script preview | Local preview completed read-only: enumerated 44 business tables, showed before/after counts unchanged after rollback, and preserved users, permissions, catalog tables, locations, and settings. |
| Reset writer guard | Local Compose service inventory confirms the reset guard targets the actual `lensee.host` API writer service; apply mode refuses it while running. |
| Legacy adapter scope guard | Sub-log creation is rejected for registered-merchant movement logs, while legacy MerchantAccount/Installment obligations remain supported through the adapter. |
| Direct collection scope guard | Shared collection requests now require a completed anonymous RetailSale source for `DirectOperation`; merchant-linked, incomplete, inventory, and non-retail operations are rejected before a collection draft is created. Host build and full application tests 208/208 passed. |
| Posted-only statements | Merchant statements now exclude every non-posted ledger entry from period rows, matching opening/closing and account totals. |
| PDF closing balance | Merchant statement PDF now uses the same complete-ledger closing calculation as the API, rather than the final rendered row. |
| Merchant balance report source | Merchant balance JSON/CSV report now reads breakdowns and amount due from `MerchantAccountService`; it no longer falls back to the legacy `FinancialProjection` calculation. |
| Operation/payment PDF balance source | Operation bills and payment receipts now read registered-merchant balance from `MerchantAccountService`; legacy `MerchantBalanceService` is no longer used for those financial document balances. |
| CRM merchant balance source | Normal CRM merchant detail summaries now use the live merchant-account amount due; warehouse-clerk location-scoped summaries retain their intentional location filter. |
| Collection policy copy | Removed stale automatic-confirmation wording; localization now describes assignment and approval for every collection. |
| Registered-merchant cash workflow contract | Updated cash-sale and refund-correction scenarios to use the shared merchant-account collection route; targeted operation tests 2/2 passed. |
| Source-operation linkage | Generic merchant-account collections accept an optional source operation, validate CRM ownership, persist `SourceOperationId`, and the UI preselects it when opened from a merchant payment. |
| Source-operation eligibility | Linked merchant collections now require a completed sale or confirmed payable change; unrelated or incomplete operations are rejected before drafting. |
| Postman financial values | Generated collection now uses canonical `MerchantAccount` and `AdditionalCharge` values; legacy `Installment`/`MerchantCredit` values are absent from the generated financial flow. |
| Postman shared collection workflow | Generated collection now records a merchant-account collection through `POST /api/v1/payments/collections` and approves it through the shared collection route; no legacy `/sublogs` request remains. |
| Explicit sale settlement method | Completed financial sales now fail fast when no settlement method is present instead of silently defaulting to `MerchantAccount`. |
| Friendly movement methods | Payment, history, report, operation detail, and stage/sub-log tables now render readable method labels (for example, “Cash hand to hand” and “Bank transfer”) while preserving canonical API values. `node --check frontend/app.js` and localization checks passed. |
| Plain-language account grade | Merchant account health now shows the configured grade label, score, and an explicit provisional marker when history is insufficient; Arabic labels are localized. |
| Statement document references | Merchant statement PDF now uses full `PAY-...` references, readable operation/payment labels, and readable cash movement labels in its appendix; build and document/report tests passed (15/15). |
| Review-inbox status filtering | Collection-work status filters now retain pending cash receipts by their own status even when the parent payment log has a different status, preserving exact source linkage; host build and 73 focused tests passed. |
| Cash receipt status authority | Review rows now expose the concrete cash receipt status, preventing a parent-log status from mislabeling or misrouting approval/rejection; host build and full application tests 208/208 passed. |
| Current-month statement default | API and merchant-statement PDF now default an omitted period to the current Egypt month; explicit `from`/`to` periods remain supported. Build plus document/report tests passed (15/15). |
| Consistent collection contracts | Merchant draft, sub-log, and cash-record responses now expose `scope`, movement method, approval details, and computed permitted actions consistently; host build and 73 focused tests passed. |
| Migration metadata | Added generated EF designer metadata for `20260912190000_LinkCollectionDraftToSourceOperation`; Payments module build and migration-discovery tests 2/2 passed. |
| Grade-code fallback | Frontend maps legacy `A`–`E` classification responses to plain labels instead of exposing raw grade codes; browser workflow now asserts `Good · 72.00` and passed 1/1. |
| Action-opened collection workspace | Unified collection entry is hidden until opened from Merchant account or Other payments, with direct/merchant scope and source preselection preserved; browser workflow, syntax, localization, and diff checks passed. |
| Friendly operation labels | Payment queues and payment reports now translate canonical operation types such as `WholesaleSale` and `RetailSale` to readable labels while preserving API values; browser regression passed 1/1. |
| Report-picker labels | Operation-bill and payment-receipt search pickers now use readable operation and movement-method labels instead of raw canonical codes; syntax, localization, and browser checks passed. |
| Review and audit labels | Approval inbox, collection-work rows, and payment-audit rows now use the same readable movement-method formatter as payment history and statements. |
| Audit action labels | Payments audit actions and status transitions now render readable bilingual descriptions instead of raw workflow event codes; localization and browser checks passed. |
| Complete audit action coverage | Added readable labels for reassignment, initialization, sub-log submission, cash submission, and financial-adjustment events emitted by the backend; frontend syntax/localization checks passed. |
| Sub-log audit actions | Added explicit bilingual labels for `PaymentSubLogApproved` and `PaymentSubLogRejected`; localization and browser workflow checks passed. |
| Workflow status labels | Payment queues, history, reports, audit transitions, stages, sub-logs, and cash records now render readable status labels instead of codes such as `PendingAdminReview`; syntax, localization, and browser checks passed. |
| Searchable merchant accounts | Merchant account workspace now filters the account selector by business name while preserving the selected account and period; browser workflow passed 1/1. |
| Rebuilt local runtime smoke check | Rebuilt local `lensee_api`/`lensee_web` images from this checkout with the database left untouched. Authenticated GET requests returned 200 for `/api/v1/payments/merchant-accounts`, `/api/v1/payments/other-payments?pageSize=5`, and `/api/v1/payments/collection-work?pageSize=5`; the current merchant statement returned 200 with 7 rows and the current-month default. |
| Current regression rerun | Full application tests 208/208 passed; fresh-container PaymentIntegrityPostgresTests 9/9 passed; frontend syntax, localization (1,280 Arabic translations / 643 UI strings), and the payments workflow browser test 1/1 passed after the migration-designer repair. |
| Live bilingual statement rendering | Rebuilt local API generated populated English (3 pages) and Arabic (4 pages) merchant statements. Rendered first/last pages were inspected: the long merchant reference no longer overlaps the status badge, Arabic signatures are on page one, and the final page contains appendix content rather than a signature-only page. |
| Live bilingual statement spreadsheets | Generated English and Arabic XLSX statements from the same endpoint; both contain 8 populated sheets, including Summary, Confirmed account activity, Operations, Payment logs, Cash records, Refund payouts, Adjustments, and Merchant notes. |
| Legacy-period statement contract | Updated the seeded statement contract to pass its explicit August 2026 period, preserving the current-month default while testing historical period selection; full application tests remain 208/208. |
| Legacy merchant scope fallback | Payment list/history projections now derive merchant scope from the source operation when legacy payment rows have no denormalized merchant ID, preventing registered activity from leaking into Other payments. Host build and full tests passed. |
| Merchant-specific history fallback | Merchant history queries now include legacy logs whose source operation identifies the merchant even when `MainPaymentLog.MerchantId` is blank. Host build passed. |
| Merchant profile context | Merchant-account detail now returns existing CRM business context (contact, phones, email, address, business type, and status); the account-details panel renders it with Arabic translations. Host build, frontend syntax, and localization checks passed. |
| Runtime profile contract | After rebuilding the local API, authenticated merchant-account detail returned the CRM profile fields and the Other payments workspace returned HTTP 200. Health endpoint returned HTTP 200. |
| Live Arabic Payments labels | Rebuilt the local frontend and verified the rendered Arabic Payments page contains translated collection actions, adjustment labels, and the Other payments empty state. |
| Final regression pass | Full application tests 208/208 passed; host build 0 errors; frontend syntax and localization checks passed after scope, profile, direct-operation, and runtime localization changes. |
| Detail scope consistency | Payment detail responses now resolve legacy missing merchant IDs from the source operation before projecting sub-logs and cash records, keeping detail scope aligned with list/history workspaces; full application tests 208/208 passed. |
| Current payment PostgreSQL rerun | `PaymentIntegrityPostgresTests` completed **9/9** after the detail-scope correction. |
| Review-inbox legacy scope isolation | Collection-work now resolves legacy payment-log ownership from source operations, includes registered legacy sub-logs under Merchant account, and excludes their pending cash records from Other payments. Host build passed with 0 errors. |
| Spreadsheet content inspection | Live English and Arabic merchant statements each contain 8 populated sheets and 499 non-empty cells; automated scan found no raw ledger event codes or UUIDs. |
| Full PostgreSQL integration rerun | Fresh run completed **23/23**. The authentication refresh concurrency test now reaches the advisory-lock path with the required trusted-request marker, and all payment, operation, inventory, migration, and auth integration coverage is green. |
| Direct anonymous collection workflow | Added and passed an end-to-end contract test covering completed anonymous RetailSale → DirectOperation collection draft → PendingAdminReview queue item → shared approval → Other payments posting; full application suite now passes 209/209. |
| Collection-work legacy status isolation | Legacy collection-work projections now retain concrete cash/sub-log status independently of parent payment-log status, so pending direct and merchant work remains visible in the correct review queue. |
| Final application regression rerun | Full application contract suite completed **209/209** after the collection-work and PostgreSQL test fixes. |
| Spreadsheet layout inspection | Live English and Arabic merchant-account workbooks each contain all 8 expected sheets and 499 non-empty cells. Automated layout scan found no long-reference cells with insufficient width and no unwrapped overflow candidates. |
| Isolated reset preview/apply | Ran the guarded reset script against a disposable PostgreSQL Compose project. Preview left business rows unchanged; apply reduced business tables to zero while preserving users, permissions, catalog tables, locations, settings, and migration history. The disposable container and volume were removed after verification. |
| Workbook print layout metadata | English and Arabic statement sheets are landscape, fit-to-page enabled, and freeze the header row on all activity and appendix sheets; summary and activity sheets have no print overflow candidates. |
| Persisted collection scope | Added `scope` to payment logs and merchant collection drafts, constrained it to `MerchantAccount`/`DirectOperation`, and added a migration that backfills historical payment-log scope from the source operation merchant link. Host build and migration discovery pass. |
| Scope migration PostgreSQL validation | Fresh PostgreSQL integration run applied the new scope migration successfully; full suite completed 23/23. |
| Scope immutability guard | The scope migration now installs PostgreSQL triggers that reject scope changes after insert for payment logs and merchant collection drafts. Dedicated immutability coverage passed, and the complete PostgreSQL suite now passes 24/24. |
| Final targeted regression rerun | Payments, operations, reports, and financial-capacity contract tests completed **82/82** after the persisted-scope migration changes. |
| Final PostgreSQL rerun | Fresh-container PostgreSQL integration suite completed **24/24**, including collection scope immutability, approval/rejection SQL, migration upgrades, payment integrity, and authentication concurrency. |
| Idempotent scope migration script | Generated `tmp/persist-scope.sql` from the previous Payments migration to `20260913025301_PersistCollectionScope`; verified it includes the source-operation merchant backfill, scope checks, and immutable-scope trigger function. |
| Registered-payment workspace isolation | A completed registered-merchant sale is returned by `merchant-account-payments` and excluded from `other-payments`; the contract test passed 1/1. |
| Fresh frontend gates | `node --check frontend/app.js`, localization validation (1,280 Arabic translations / 643 UI strings), and `payments-workflow-ui.spec.js` completed successfully (1/1 browser test). |
| Statement appendix legacy linkage | Merchant statement PDF/Excel appendix queries now include legacy payment logs linked through the merchant's source operations even when the denormalized payment merchant ID is blank; ReportsEndpointContractTests passed 7/7. |
| Payment report legacy linkage | Payment report rows now resolve merchant ownership through source operations when legacy denormalized IDs are blank, keeping report scope aligned with account statements; ReportsEndpointContractTests passed 7/7. |
| Legacy cash-receipt scope guard | Cash-record adapters now resolve merchant ownership from the source operation before accepting a direct receipt, so registered-merchant cash cannot leak into Other payments; isolation test passed 1/1. |
| Legacy approval scope resolution | Cash-receipt and sub-log approval now resolve merchant ownership from the source operation before posting, preserving account-ledger routing for legacy rows with missing denormalized IDs; Host build passed with 0 warnings and 0 errors. |
| Legacy missing-ID regression | The registered-merchant isolation test now clears the denormalized merchant ID before submitting a cash receipt and still receives the registered-merchant rejection; targeted test passed 1/1. |
| Full application regression after legacy approval fixes | Complete application contract suite passed **209/209** after source-operation merchant resolution was added to cash and sub-log approval paths. |
| Payment-focused PostgreSQL rerun | Fresh-container `PaymentIntegrityPostgresTests` completed **10/10** after source-operation merchant resolution was added to approval paths. |
| Merchant snapshot legacy pending coverage | `MerchantAccountService` now includes pending installment and cash collections from source operations when legacy rows lack merchant IDs; full PostgreSQL integration suite completed **24/24**. |
| Focused balance/report regression after pending-method coverage | `PaymentFinancialCapacityTests` and `ReportsEndpointContractTests` passed **13/13** after legacy pending collections were expanded to all movement methods and source-operation merchant fallback. |
| Full PostgreSQL rerun after pending-method coverage | `Lensee.PostgresIntegrationTests` passed **24/24** with `LENSEE_RUN_POSTGRES_TESTS=true`; the merchant snapshot query and source-operation fallback remain PostgreSQL-compatible. |
| Merchant classification scope consistency | Account health now evaluates every completed sale obligation for the registered merchant, including cash, bank, and wallet settlement methods; direct anonymous retail activity remains excluded. Targeted workflow/report regression passed **9/9**. |
| Other-payments retail-only scope | Direct-operation payment history and list queries now exclude non-retail anonymous operations; registered activity remains account-scoped. Full application contract suite passed **209/209** after the filter change. |
| Browser workspace isolation after retail-only scope | `payments-workflow-ui.spec.js` passed **1/1** after the backend scope filter change; merchant-account and Other payments panels remain separately visible through the landing switch. |
| Collection detail contract completeness | Installment sub-log detail responses now expose their transaction reference alongside scope, operation reference, movement method, status, approval details, and permitted actions; Operations and payment-capacity contracts passed **75/75**. |
| Review-inbox retail-only scope | Direct-operation collection work now excludes anonymous non-retail logs while retaining anonymous retail review items; direct/registered workflow isolation passed **2/2**. |
| Complete browser regression after inbox filtering | Payments, friendly identifiers, bilingual forms, audit localization, and mobile RTL/overflow checks passed **7/7**. |
| Unfiltered review-inbox isolation | The shared inbox now includes registered activity and anonymous retail work only when no scope is supplied, while honoring merchant filters; OperationsEndpointContractTests passed **69/69**. |
| Arabic statement label coverage | Added the missing Arabic translation for `Net collected`; the statement financial-label regression and document-renderer tests passed **9/9**. XLSX-to-HTML rendering now shows Arabic for all inspected summary labels. |
| Arabic merchant XLSX contract | Merchant statement export test now downloads the Arabic XLSX and asserts localized financial labels are present with no raw `Net collected` label; passed **1/1**. |
| Dynamic Arabic value aliases | Added Arabic aliases for Wallet, Wholesale sale, and Added by, with the shared translation regression covering all statement dynamic financial values; focused export/localization tests passed **2/2**. |
| Migration discovery rerun | Payment migration discovery checks completed **2/2** after the latest service and scope changes. |
| Legacy collection approval regression | Anonymous direct and registered-merchant collection workflow tests passed **2/2** after approval scope resolution; registered cash adapters remain blocked from the direct workspace. |
| Seed-script secret hygiene | Removed the previously embedded JWT from `scripts/seed-supply-transfers-api.ps1`; it now requires `<PASTE_API_TOKEN_HERE>`. PowerShell syntax validation passed and no JWT-like token literals remain in tracked source. |
| Local release image build | `docker compose build lensee.host` completed successfully after the payment scope and Arabic statement-label fixes; image `lenseehost:latest` was built locally. |
| Guarded local reset preview | `pwsh -NoProfile -File scripts/clear-db-keep-users-locations.ps1` ran in preview mode against the local PostgreSQL container, listed 44 business tables with before/after counts, preserved users/permissions/catalog/locations/settings, and rolled back without changing data. PowerShell parse validation passed. |
| PostgreSQL migration startup repair | Local startup reproduced `55006 cannot ALTER TABLE main_payment_logs because it has pending trigger events` in `PersistCollectionScope`; the migration now suspends only the deferred aggregate trigger during its metadata backfill. Rebuilt image starts cleanly with `/ready` reporting PostgreSQL healthy and no pending migrations. |
| Authenticated local workspace smoke | Login as the seeded local Admin returned 200; merchant-account list, Other payments, and both scoped collection-work queries returned 200 from the rebuilt container. |
| Live statement document inspection | Authenticated English and Arabic merchant PDFs were generated locally (3 and 4 pages respectively). Arabic page 4 was rendered and visually inspected: section headings, status, event/method values, empty states, and generated notes are localized; no blank signature-only page or clipped page was observed. |
| Arabic translation regression after runtime fix | Added coverage for statement section headings, empty states, status, and generated cash-sale notes; focused document/export tests passed **2/2**. |
| PostgreSQL migration and integrity rerun | With `LENSEE_RUN_POSTGRES_TESTS=true`, `PaymentIntegrityPostgresTests|MigrationDiscoveryTests` passed **10/10** after the trigger-safe migration repair. |
| Live bilingual spreadsheet inspection | Fresh authenticated Arabic/English XLSX exports contain all 8 statement sheets, render at 1440px without horizontal overflow, and preserve populated summary/ledger values. Arabic sheet names are localized and the period uses `إلى`; no English sheet-name leakage remains. |
| Final statement localization regression | Expanded translation coverage for Operations, Payment logs, Refund payouts, period ranges, and generated notes; focused tests passed **2/2**. |
| Full application regression after document changes | `dotnet test backend/Lensee.Tests/Lensee.Tests.csproj --no-restore` passed **210/210**. |
| Live authenticated SPA smoke | Local frontend login as Admin succeeded; `#/payments` loaded against the rebuilt API with merchant-account and Other-payments workspaces, Arabic navigation, seeded payment rows, account controls, and no horizontal overflow at 1440px. |
| Reset apply verification on isolated clone | Stopped the local API, cloned `lensee` to `lensee_reset_verification`, ran `-Apply -ConfirmReset 'RESET BUSINESS DATA' -DatabaseName lensee_reset_verification`, and verified 7 users, 19 permissions, 3 locations, 12 products, 4,698 SKUs, and 54 migrations remained while operations, payments, and stock transactions were zero. The clone was dropped and the local API restarted healthy; source `lensee` remained unchanged. |
| Period closing-position clarity | Screen and PDF now expose positive `Period amount due` and `Period merchant credit` values instead of an unexplained signed closing balance; live PDF contains both labels and no raw negative closing-balance presentation. |

## Current merchant-account contract

Merchant account calculations intentionally expose only these seven financial aspects:

1. Total sales — confirmed merchant sale charges.
2. Net collected — approved collections less paid refunds.
3. Remaining owed — total sales plus approved additional charges, less accepted returns, approved reductions, and net collected.
4. Refunds — approved cash refunds that have actually been paid.
5. Accepted return value — approved returns and lower-value exchange credits.
6. Additional charges — approved adjustments and higher-value exchange surcharges.
7. Amount reductions — approved reductions.

Collections belong to the merchant account, not a compulsory single order. A source operation is optional; if absent, allocation is oldest-first across open merchant mini-invoices. Financial adjustments follow the same account-level rule; an optional order link narrows lineage, while a cash refund requires an order because its physical payout must reconcile to a cash record.

The collection form has one mutation action: **Send collection for approval**. Once its POST has committed, form cleanup and refresh work are non-fatal; the idempotency key can be replayed or resolved without creating a duplicate collection.

Payment audit rows must expose a friendly reference, actor and role, merchant or non-CRM buyer, operation number, scope, method, electronic transaction reference, lifecycle status, date/time, and recorded payload details. System Audit History remains the cross-module evidence source and links to the underlying record.

## Browser

| Area | Evidence |
| --- | --- |
| Split merchant-account/other-payment workspaces, main balances, review action, rejection reason, merchant-source preselection, friendly method labels, mobile width | payments-workflow-ui.spec.js — 1/1 passed |
| Arabic/English direction, localization, canonical values, friendly identifiers | localization and friendly-identifier specs — 5/5 passed |
| Payments workflow UI regression | payments-workflow-ui.spec.js — 1/1 passed after shared inbox changes |
| Split-workspace bilingual/mobile regression | Combined payments, localization, friendly-identifier, and mobile suites — 7/7 passed after direct-operation scope enforcement. |
| Payments landing switch | Merchant, Other payments, Approval inbox, Ledger, Tools, and Audit tabs now show only their selected workspace; the browser regression asserts the initial and switched visibility states and passes 1/1. |

## Unverified environment checks

- Authenticated live browser interaction against the running SPA has not been completed; the rebuilt API smoke check is recorded above.
- Full PostgreSQL suite completed **24/24** after the refresh-concurrency test was aligned with the application CSRF request-marker middleware and scope immutability coverage was added; the payment-focused PostgreSQL suite completed **10/10**.
- Populated spreadsheet page-by-page visual inspection remains unverified; workbook content and layout metadata checks passed, and PDF rendering was inspected above for both languages.
- No production migration, reset, deployment, or remote database action was performed.

