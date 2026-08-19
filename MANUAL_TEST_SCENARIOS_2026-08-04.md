# Manual Business-Workflow Test Scenarios - 2026-08-04

## Test data and roles

Use the six disposable accounts recorded in `MANUAL_TEST_BUG_LOG_2026-08-04.md`. Keep the three clerks assigned respectively to Roxy (Main), Mohamed Naguib (Retail), and Online. Create a small, reversible QA data set only after approval: one product with a traceable SKU/batch, sufficient stock at Roxy, a merchant/representative, and a controlled payment confirmation.

## Executed scenarios

| ID | Role and context | One task | Success criteria | Outcome |
| --- | --- | --- | --- | --- |
| S01 | Administrator; no account data required | Create one employee for each role and assign one clerk per location | Each account appears active with the requested role; clerks require and retain their assigned location | Passed: six accounts created, including clerks for Roxy, Mohamed Naguib, and Online. |
| S02 | Roxy warehouse clerk | Sign in and review available modules | Clerk sees only permitted operational modules and assigned-location notice | Passed. |
| S03 | Online warehouse clerk | Open Inventory | Inventory is read-only; location control is disabled; only Online is available | Passed. |
| S04 | Mohamed Naguib warehouse clerk | Open Inventory | Inventory is read-only; location control is disabled; only Mohamed Naguib (Retail) is available | Passed. |
| S05 | ERP administrator | Sign in and review landing navigation | Role-specific operational/admin entry points render and session identifies ERP Admin | Passed for UI access. Authorization of a persistent employee-create action is not exercised. |
| S06 | C-Level | Sign in and review landing navigation | Oversight/operational modules render; Admin is unavailable | Passed for UI access. |
| S07 | Accountant | Sign in and review landing navigation | Payments, reports, CRM, operations, and notifications render; inventory/catalog/admin modules are unavailable | Passed for UI access. |
| S08 | Any tested role in Arabic | Review dashboard guidance and metrics | Arabic text is complete and bidirectionally correct | Failed: BUG-01. |

## Data-provisioned end-to-end scenarios

| ID | Role and context | One task | Success criteria | Observation guide |
| --- | --- | --- | --- | --- |
| S09 | Administrator; empty catalog | Create one QA product and one traceable SKU | Product/SKU appears in catalog with valid attributes and is selectable in operations | Check validation messages, duplicate prevention, Arabic labels, and post-save list/detail state. |
| S10 | Roxy clerk; product/SKU and stock receipt prepared | Receive a controlled batch into Roxy | Receipt confirms once, stock and batch balances update, and ledger/audit view records it | Record before/after quantity, batch expiry, source reference, and duplicate-submit behavior. |
| S11 | Roxy clerk and Retail clerk; controlled Roxy stock | Transfer a batch from Roxy to Mohamed Naguib | Source decreases, destination increases only after the expected confirmation state, and each clerk sees only own location | Attempt destination/source switching and an over-quantity request; confirm denial. |
| S12 | Roxy clerk; sale-ready stock and merchant | Record a sale | Sale consumes allowed stock, creates the correct operational record, and preserves payment status | Check no-stock/over-quantity validation, repeat-submit safety, and Arabic receipt content. |
| S13 | Roxy clerk; completed sale | Process a return | Return eligibility is enforced and stock/payment balances reconcile | Attempt invalid original reference, excessive quantity, and duplicate return. |
| S14 | Roxy clerk; exchange-eligible sale | Process an exchange | Old and new SKU movements and any price difference are correctly recorded | Check unavailable replacement SKU, different location, and cancellation/retry behavior. |
| S15 | Roxy clerk; controlled stock | Post an adjustment/write-off | Reason, quantity, audit trail, and resulting stock are correct; unauthorized clerks cannot bypass rules | Try blank reason, negative/too-large quantity, and double-submit. |
| S16 | Accountant; payment confirmation ready | Assign and approve a payment | Confirmation moves through expected state once and ledger/remaining amount reconcile | Test partial/full assignment, invalid amount, repeat approval, and accounting visibility. |
| S17 | Accountant; completed payment | Record refund or cash movement | Ledger direction, balance, reference, and audit trail are accurate | Try amount above balance, missing reference, and repeated action. |
| S18 | C-Level; transactions exist | Review/export reports | Totals match underlying records and exported output opens with expected rows/locale | Compare dashboard, payments, inventory, and export totals for same time window. |
| S19 | Administrator; stock exists | Perform stocktake workflow | Count, variance, approval, and resulting adjustment are traceable | Check zero/negative count, stale session, conflicting concurrent updates, and re-open rules. |
| S20 | All roles; complete QA data exists | Attempt out-of-role navigation and direct UI actions | Unauthorized modules/actions are hidden or refused without data mutation | Capture each role’s visible navigation and attempted action result; verify data unchanged. |

## Completion rule

Mark a data-provisioned scenario complete only after recording the input identifiers, before/after balances, user role/location, expected result, observed result, and cleanup of the controlled QA records. Report any failure in the bug log with reproducible steps and severity.
