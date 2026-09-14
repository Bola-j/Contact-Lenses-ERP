# Operations and Payments Complex Certification Scenarios

## Purpose and completion rule

This suite certifies the complete financial and stock consequences of Operations and Payments. It covers the eight operation types, every payment mutation, the unified merchant receivable account, legacy compatibility paths, and connected Catalog, CRM, Inventory, Supply, Stocktake, Reporting, Notifications, Audit, Identity, and frontend behavior.

A scenario passes only when all of its applicable layers agree:

1. HTTP status and response contract.
2. Operation and payment state transitions.
3. PostgreSQL rows, constraints, and transaction boundaries.
4. Inventory balances, batches, reservations, and transaction ledger.
5. Merchant account entries, sequence, obligations, allocations, and refund reservations.
6. Audit events and outbox/notifications.
7. Payment queue, payment history, merchant statement, reports, exports, receipts, and PDFs.
8. Arabic and English UI behavior, permissions, and refreshed totals.

Do not certify from EF InMemory tests alone. Scenarios marked `PG` require PostgreSQL. Scenarios marked `UI` require Playwright or an equivalent real browser. Scenarios marked `DOC` require opening and inspecting the generated document.

## Deterministic test data

Run against a disposable database. Record every generated UUID and business number in the evidence log.

| Symbol | Test record |
|---|---|
| `M-A` | Active merchant Alpha, no opening balance |
| `M-B` | Active merchant Beta, no opening balance |
| `M-X` | Inactive merchant used for rejection tests |
| `REP-A` | Active representative assigned to reserve scenarios |
| `MAIN` | Active main warehouse |
| `RETAIL` | Active retail location |
| `ONLINE` | Active online location |
| `OTHER` | Active non-main subwarehouse |
| `SKU-A` | Active pack SKU, 10 pieces per pack |
| `SKU-B` | Active pack SKU, 20 pieces per pack |
| `SKU-C` | Active SKU used for deactivation races |
| `LOT-A1` | `SKU-A`, expiry +24 months, quantity 1,000 packs at `MAIN` |
| `LOT-A2` | `SKU-A`, expiry +6 months, quantity 500 packs at `MAIN` |
| `LOT-A3` | `SKU-A`, expired, quantity 50 packs at `MAIN` |
| `LOT-B1` | `SKU-B`, expiry +18 months, quantity 1,000 packs at `MAIN` |
| `ADMIN-1` | Primary Admin |
| `ADMIN-2` | Non-primary Admin |
| `ERP` | ERPAdmin |
| `CLEVEL` | C-Level |
| `ACCOUNTANT` | Accountant |
| `CLERK-MAIN` | WarehouseClerk assigned to `MAIN` |
| `CLERK-RETAIL` | WarehouseClerk assigned to `RETAIL` |
| `CLERK-ONLINE` | WarehouseClerk assigned to `ONLINE` |

Use four-decimal database amounts and two-decimal display amounts. Use explicit idempotency keys for every mutation, and retain the request and response for replay tests.

## Non-negotiable invariants

For each merchant account:

```text
NetBalance = sum(Posted DebitAmount) - sum(Posted CreditAmount)
AmountDue = max(NetBalance, 0)
GrossCredit = max(-NetBalance, 0)
ReservedRefunds = sum(approved or partially-paid reservation outstanding amounts)
CreditAvailable = max(GrossCredit - ReservedRefunds, 0)
RunningBalance[n] = RunningBalance[n-1] + Debit[n] - Credit[n]
```

Entry directions:

| Business event | Debit | Credit |
|---|---:|---:|
| Completed merchant-account sale | Sale amount | 0 |
| Approved additional charge | Charge amount | 0 |
| Costlier exchange | Replacement minus returned | 0 |
| Confirmed collection | 0 | Collected amount |
| Confirmed return | 0 | Accepted returned value |
| Approved balance reduction | 0 | Reduction amount |
| Cheaper exchange | 0 | Returned minus replacement |
| Completed refund payout | Paid amount | 0 |

Additional invariants:

- A business event posts at most once even after retry, timeout, double-click, or concurrent requests.
- Merchant account entry sequences are unique, strictly increasing, and gap-free for committed entries.
- Pending or rejected payments and adjustments do not change posted balance.
- A collection allocation never exceeds either its collection or the open net obligation.
- A return/change allocation never exceeds the merchant's eligible sold quantity for the exact SKU, lot, and expiry unless an authorized, reasoned exception is explicitly supported.
- A refund payout requires an approved reservation, never exceeds its outstanding amount, and never exceeds merchant credit.
- Operation, inventory, payment, account, audit, and outbox changes commit or roll back together.
- The same business number and money values appear in UI, API, CSV/XLSX/PDF, notifications, and audit history.

## Golden end-to-end merchant account journey

### `E2E-001` — Multi-operation account with debt, credit, reservation, payout, and future consumption (`API`, `PG`, `UI`, `DOC`)

Perform the following steps for `M-A`. After every step, load the operation, payment log, merchant account, statement, merchant balance report, financial summary, payment history, and relevant document.

| Step | Action | Debit | Credit | Expected net | Amount due | Refund due | Reserved | Available credit |
|---:|---|---:|---:|---:|---:|---:|---:|---:|
| 1 | Complete sale `S-1` for 1,000 using `MerchantAccount` | 1,000 | 0 | 1,000 | 1,000 | 0 | 0 | 0 |
| 2 | Complete sale `S-2` for 600 using `MerchantAccount` | 600 | 0 | 1,600 | 1,600 | 0 | 0 | 0 |
| 3 | Record collection 600 | 0 | 600 | 1,000 | 1,000 | 0 | 0 | 0 |
| 4 | Approve additional charge 100 linked to `S-1` | 100 | 0 | 1,100 | 1,100 | 0 | 0 | 0 |
| 5 | Confirm return value 500 allocated to `S-1` | 0 | 500 | 600 | 600 | 0 | 0 | 0 |
| 6 | Confirm cheaper exchange: returned 300, replacement 200 | 0 | 100 | 500 | 500 | 0 | 0 | 0 |
| 7 | Confirm costlier exchange: returned 150, replacement 250 | 100 | 0 | 600 | 600 | 0 | 0 | 0 |
| 8 | Approve balance reduction 100 linked to `S-2` | 0 | 100 | 500 | 500 | 0 | 0 | 0 |
| 9 | Record collection 700 | 0 | 700 | -200 | 0 | 200 | 0 | 200 |
| 10 | Request and approve refund 150 | 0 | 0 | -200 | 0 | 200 | 150 | 50 |
| 11 | Pay refund 60 | 60 | 0 | -140 | 0 | 140 | 90 | 50 |
| 12 | Pay remaining reserved refund 90 | 90 | 0 | -50 | 0 | 50 | 0 | 50 |
| 13 | Complete new sale `S-3` for 40 | 40 | 0 | -10 | 0 | 10 | 0 | 10 |
| 14 | Complete new sale `S-4` for 20 | 20 | 0 | 10 | 10 | 0 | 0 | 0 |

Expected allocation assertions:

- The first collection allocates oldest-first to `S-1` unless an authorized override was submitted.
- Return and exchange credits reduce their linked source obligations before creating free merchant credit.
- The 700 collection settles all eligible remaining obligations before leaving 200 merchant credit.
- Refund reservation changes only reserved/available credit; it creates no debit or cash record.
- Each payout creates one debit and reduces the reservation outstanding amount.
- `S-3` consumes existing credit before showing a new amount due.
- Every account statement row has the next sequence and its calculated running balance.

### `E2E-002` — Two merchants and anonymous retail isolation (`API`, `PG`, `UI`)

Interleave operations for `M-A`, `M-B`, and an anonymous retail buyer. Verify that entries, obligations, allocations, classifications, searches, exports, and notifications never cross merchant boundaries. Anonymous retail must remain operation-based and must not create or reuse a normal merchant receivable account merely because the buyer name matches a registered merchant.

## Operation lifecycle scenarios

### Inventory receipt and supply

| ID | Scenario and action | Expected result |
|---|---|---|
| `OP-001` | Create an `InventoryReceipt` with two SKUs, new lot/expiry values, and no payment method; confirm it. | Only `MAIN` increases; one receipt transaction per line; no merchant account/payment log; version and audit created. |
| `OP-002` | Submit missing lot, missing expiry, zero/negative quantity, unknown SKU, inactive SKU/product, or duplicate identical line. | `400` with field errors; no operation/stock/audit/outbox partial rows. |
| `OP-003` | Attempt receipt into `RETAIL`, `ONLINE`, or inactive location. | Rejected because receipt destination must be active `MAIN`. |
| `OP-004` | Create a Supply shipment with product cost, freight, customs, and rounding remainder; confirm. | Supply becomes received; generated inventory receipt posts exact quantities; allocated landed costs sum exactly to landed total. |
| `OP-005` | Leave a supply unit price blank, save draft, then confirm. | Draft succeeds; confirmation fails without any stock effect; succeeds once price is completed. |
| `OP-006` | Confirm a supply shipment twice or replay the same idempotency key after response loss. | One receipt operation, one stock effect per line, one terminal history event. |
| `OP-007` | Deactivate a SKU after supply draft but before confirmation. | Confirmation conflicts; supply remains unreceived; no inventory or cost posting. |
| `OP-008` | Cancel a supply draft, then attempt update and confirmation. | Cancel occurs once; later mutations fail; no inventory receipt. |

### Warehouse transfer

| ID | Scenario and action | Expected result |
|---|---|---|
| `OP-009` | Transfer `LOT-A2` from `MAIN` to `RETAIL`; confirm, ship, receive. | Reserve reduces available at source; ship records outbound once; receive adds exact batch/expiry at destination; final status `Received`. |
| `OP-010` | Transfer two lots of the same SKU and explicitly select the later expiry. | Selected lots are preserved; the server does not silently switch to FEFO. |
| `OP-011` | Use non-main source, main destination, same source/destination, missing destination, or inactive destination. | Validation failure and no reservation. |
| `OP-012` | Request more than available, include expired/blocked batch, or reuse mismatched lot/expiry. | Conflict/validation response; stock and transaction ledger unchanged. |
| `OP-013` | Cancel while `Reserved`; then attempt ship/receive. | Reservation is released exactly once; terminal actions fail. |
| `OP-014` | Cancel after `Shipped` if policy permits reversal; otherwise attempt it. | Observed result matches explicit policy; never leaves quantity missing from both locations or present in both. |
| `OP-015` | Simultaneously confirm two transfers that together exceed a batch. | At most available stock is reserved; loser returns `409`; no negative stock or orphan transaction. |
| `OP-016` | Simultaneously call ship and cancel, then receive twice. | One valid transition path wins; each physical stock movement occurs once. |

### Wholesale sale

| ID | Scenario and action | Expected result |
|---|---|---|
| `OP-017` | Complete a multi-line wholesale sale with paid, bonus, and two-lot lines using `MerchantAccount`. | Packs only; selected lots leave stock; bonus value is zero; one sale debit/obligation equals non-bonus total. |
| `OP-018` | Omit merchant or payment method; use pieces; use zero non-bonus price; use bonus on an invalid operation. | Field-level rejection; no stock/payment/account effect. |
| `OP-019` | Complete a `CashHandToHand` sale as Admin and ERPAdmin, then complete another as a location-scoped WarehouseClerk. | Administrative completion posts the sale debit and equal confirmed collection once. Clerk completion creates a pending cash record that posts only when a trusted financial user confirms it. C-Level and Accountant cannot transition operations. |
| `OP-020` | Use `CashTransaction` for a wholesale sale and exercise the required collection evidence. | Operation settlement method remains separate from actual money-movement method/reference; missing required movement reference cannot create a confirmed collection. |
| `OP-021` | Use compatibility aliases `Installment` and misspelled `Installlaugment` through API. | Accepted only as compatibility inputs and persisted/returned canonically as `MerchantAccount`; new UI never submits aliases. |
| `OP-022` | Edit a draft’s quantity, price, lot, merchant, and method, then confirm concurrently. | Confirmation uses one committed version; stale edit fails; stock and financial posting match the winning version. |
| `OP-023` | Cancel a reserved sale. | Exact stock reservation released; no sale ledger debit or payment obligation is posted. |
| `OP-024` | Retry confirm/ship/complete with new and repeated idempotency keys. | Terminal state and inventory/payment/account effects occur once. |

### Retail sale

| ID | Scenario and action | Expected result |
|---|---|---|
| `OP-025` | Complete registered-merchant retail sale from `RETAIL` using `MerchantAccount`. | Pieces/packs convert correctly; merchant sale debit and obligation post once. |
| `OP-026` | Complete anonymous `CashHandToHand` retail sale. | Operation/payment receipt completes; no normal merchant receivable debt remains; buyer identity is traceable without contaminating another merchant account. |
| `OP-027` | Attempt anonymous `MerchantAccount` sale. | Rejected because an account sale requires an active merchant. |
| `OP-028` | Sell from `MAIN`, inactive location, or a clerk’s unassigned location. | Rejected/forbidden; no stock or financial mutation. |
| `OP-029` | Mix packs and pieces at a location with known pieces-per-pack, including boundary quantity. | Conversion, available balance, unit pricing, and document quantity all agree. |
| `OP-030` | Complete exact-stock sale, then attempt another sale from same lot. | First reaches zero without going negative; second conflicts. |

### Reserve and replenishment

| ID | Scenario and action | Expected result |
|---|---|---|
| `OP-031` | Create reserve for `REP-A`, confirm, then complete. | Stock is reserved and later issued once; representative and version lineage remain visible; no merchant payment account. |
| `OP-032` | Cancel a confirmed/reserved reserve. | Reserved stock returns to available once; no financial entries. |
| `OP-033` | Omit representative, use inactive representative, or exceed stock. | Rejected without reservation or notification side effects. |
| `OP-034` | Run replenishment when destination is below target and main has enough stock. | Proposed quantity respects destination target and main target; resulting transfer follows normal lifecycle. |
| `OP-035` | Run replenishment with short-dated, expired, blocked, and insufficient main stock. | Eligible stock is selected according to policy; main never drops below target; low-main alert is idempotent. |
| `OP-036` | Compare visible replenishment action with direct API access for every role. | UI visibility and backend permission agree; no visible action returns `403` for its intended role. |

### Return

| ID | Scenario and action | Expected result |
|---|---|---|
| `OP-037` | Partially return exact `SKU-A`/lot/expiry from one unique sale using `MerchantAccount`. | Return stock posts once; return credit reduces linked sale debt; source operation and line allocation are queryable. |
| `OP-038` | Fully return the remaining quantity of a partially paid sale. | Obligation closes; amount due cannot fall below zero; excess confirmed collection becomes refund due. |
| `OP-039` | Return value is lower/equal/higher than unpaid debt. | Lower leaves debt; equal clears debt; higher creates merchant credit for only the excess. |
| `OP-040` | Same SKU/lot/expiry exists in several sales. | UI/API require explicit source sale-line allocation; no arbitrary automatic allocation. |
| `OP-041` | Exactly one eligible sale-line match exists. | Server proposes/selects it automatically and persists quantity plus financial allocation. |
| `OP-042` | Return wrong merchant, unsold SKU/lot/expiry, excess quantity, or previously fully returned line. | Rejected, or authorized exception workflow is explicit and reasoned; no silent over-allocation. |
| `OP-043` | Two returns concurrently consume the final returnable quantity. | One succeeds; one conflicts; accepted quantity and credit never exceed sold quantity/value. |
| `OP-044` | Return expired goods. | Stock goes to the configured non-sellable/quarantine/write-off path; no sellable stock inflation; financial credit remains correct. |
| `OP-045` | Confirm return, then request refund without paying it. | Return credit posts; inventory posts; refund due appears; no cash payout/debit until approved payout. |
| `OP-046` | Retry return confirmation after timeout. | One stock receipt, one return credit, one allocation, one audit/outbox event. |

### Change/exchange

| ID | Scenario and action | Expected result |
|---|---|---|
| `OP-047` | Exchange equal-value goods. | Return and replacement inventory legs post; no net merchant ledger entry or exactly offset legs according to canonical design; balance unchanged. |
| `OP-048` | Cheaper replacement: return 300, issue 200. | Net credit 100 reduces linked debt first and only then creates merchant credit. |
| `OP-049` | Costlier replacement: return 150, issue 250. | Net debit 100 increases amount due. |
| `OP-050` | Multi-line exchange with two returned lots, paid/bonus history, and two replacement SKUs. | Each source allocation and inventory leg is exact; net financial effect equals replacement total minus accepted return total. |
| `OP-051` | Missing `ChangeOut` or `ChangeIn`, wrong section, zero quantity, or non-pack mode. | Draft rejected without stock or ledger effects. |
| `OP-052` | Replacement stock becomes unavailable between draft and confirmation. | Whole exchange rolls back: no returned stock, no issued stock, no ledger entry. |
| `OP-053` | Concurrent exchange and return consume the same source-sale quantity. | One allocation wins; other conflicts; no over-return or double credit. |
| `OP-054` | Retry exchange confirmation and reverse/correct it. | Original effects post once; correction uses reversal/replacement lineage and restores both inventory and finance atomically. |

### Write-off and stocktake

| ID | Scenario and action | Expected result |
|---|---|---|
| `OP-055` | Admin writes off available stock with lot/expiry and reason. | Stock decreases once; `WriteOff` transaction and audit exist; no merchant/payment entry. |
| `OP-056` | ERPAdmin, C-Level, Accountant, or clerk attempts write-off directly. | Forbidden or validation response according to policy; no mutation and UI hides action. |
| `OP-057` | Write off reserved, unavailable, or excessive quantity. | Conflict; no negative stock or partial ledger. |
| `OP-058` | Confirm stocktake with positive and negative variances over several SKUs/lots. | One stocktake adjustment per difference; resulting physical balances equal counts; no merchant finance. |
| `OP-059` | Edit/confirm stocktake concurrently or confirm twice. | One terminal version and one set of inventory effects. |

### Operation corrections

| ID | Scenario and action | Expected result |
|---|---|---|
| `OP-060` | Correct finalized sale quantity/price with no cash settlement. | Immutable original remains; reversal and replacement operations/entries reconcile stock and debt exactly. |
| `OP-061` | Correction requires additional collection. | Settlement method and amount are explicit; confirmed collection posts once with reference rules. |
| `OP-062` | Correction creates refund due. | Credit is recorded first; cash payout follows approval/reservation workflow and is not invented automatically. |
| `OP-063` | Requester approves own correction as Admin/ERPAdmin where policy allows. | Permission follows configured administrative policy; actor and timestamps are audited. |
| `OP-064` | Accountant, C-Level, clerk, and unauthorized user exercise create/approve/reject matrix. | Exact policy enforced in API and UI; forbidden calls leave all contexts unchanged. |
| `OP-065` | Two active corrections for same finalized source race. | One proposal wins; the other conflicts; only one reversal lineage exists. |
| `OP-066` | Reject correction, retry approval, or create correction after source reversed. | Invalid transitions fail and do not mutate inventory, payments, or audit beyond the rejection event. |

## Payment workflow scenarios

### Payment log initialization and legacy operation-specific collections

| ID | Scenario and action | Expected result |
|---|---|---|
| `PAY-001` | Complete eligible sale and inspect automatic payment artifacts. | Exactly one active main payment log with correct operation number, merchant, total, method, paid, pending, remaining, and refund due. |
| `PAY-002` | Initialize same operation twice or initialize ineligible/unfinalized operation. | Existing log returned or conflict according to contract; never two active logs. |
| `PAY-003` | Assign payment to active Accountant, reassign, assign inactive/non-accountant, and assign completed log. | Valid queue transitions only; invalid assignments do not mutate state. |
| `PAY-004` | Accountant records partial operation-specific collection. | Draft is assigned and enters `PendingAdminReview`; no posted credit or amount-paid change occurs until Admin/ERPAdmin approval. |
| `PAY-005` | Admin, primary Admin, ERPAdmin, and C-Level each attempt to record/review a collection. | Accountant/Admin/ERPAdmin may prepare; Admin/ERPAdmin may approve or reject; C-Level retains read access but cannot review collections. |
| `PAY-006` | A permitted non-reviewer submits collection work. | Remains pending; reserves capacity; no posted merchant account credit until authorized confirmation. |
| `PAY-007` | Submit zero, negative, above-cap, duplicate, missing method, `MerchantAccount` as movement method, translated method, or unsupported method. | Field-level `400/409`; no partial sub-log, aggregate, ledger, audit, or outbox mutation. |
| `PAY-008` | Use `CashHandToHand`, `CashTransaction`, `BankTransfer`, and `Wallet`. | All valid movement methods work; electronic methods require reference; cash receives an internal receipt reference where specified. |
| `PAY-009` | Reject pending sub-log, then submit valid replacement. | Rejected amount releases reserved capacity; replacement can use it; rejected entry never affects posted balance. |
| `PAY-010` | Approve same pending sub-log twice or approve after rejection. | One approval effect at most; subsequent request conflicts; amount paid and ledger remain unchanged. |
| `PAY-011` | Operation adjustment changes payable amount while a collection is pending. | Approval rechecks capacity under lock; no over-collection or stale approval. |

### Cash receipt adapter

| ID | Scenario and action | Expected result |
|---|---|---|
| `PAY-012` | Record cash/electronic receipt against exact operation number and full UUID. | Both references resolve identically; one cash record and one collection posting; business number shown in UI/documents. |
| `PAY-013` | Record receipt for missing, draft, cancelled, return, change, reserve, or write-off operation. | Rejected as ineligible; no cash or ledger rows. |
| `PAY-014` | Combine confirmed installments, cash receipts, pending entries, refunds, and new receipt at exact cap. | Exact cap succeeds; one currency unit above cap fails; all components are counted once. |
| `PAY-015` | Retry cash receipt with same idempotency key/body and then with changed body. | Same body returns original result; changed body conflicts; no duplicate receipt or collection. |
| `PAY-016` | Submit the same external transaction reference through cash receipt, sub-log, and merchant collection paths. | Cross-path duplicate is blocked or explicitly reported; failure exposes a duplicate-payment defect. |

### Unified merchant collections and allocations

| ID | Scenario and action | Expected result |
|---|---|---|
| `PAY-017` | Preview collection across three open obligations. | Oldest-first proposal sums to at most collection amount and each obligation remaining amount; preview writes nothing. |
| `PAY-018` | Post collection with no override. | Posted credit uses the preview ordering under the locked current state. |
| `PAY-019` | Post authorized allocation override split across selected obligations. | Exact requested split persists; unallocated remainder stays account-level credit. |
| `PAY-020` | Override with another merchant’s obligation, closed obligation, negative amount, duplicate obligation, over-allocation, or sum above collection. | Entire request rejected; no account entry or allocations. |
| `PAY-021` | Collect more than total open debt. | All obligations settle and excess becomes merchant credit/refund due; no zero clamp hides it. |
| `PAY-022` | Two collections concurrently target the final obligation capacity. | Account lock serializes entries and allocations; no allocation exceeds obligation; sequences remain unique. |
| `PAY-023` | Merchant exists but has no account or postings; load statement and record first valid collection behavior. | API/UI provide an intentional empty-account response or a clear validation message, never generic “workspace request failed.” |
| `PAY-024` | Search/list accounts after activity in several merchants. | Correct ordering, amount due, credit, reserved refund, confirmed collections, grade, flags, and last/opened activity. |

### Adjustments

| ID | Scenario and action | Expected result |
|---|---|---|
| `PAY-025` | Accountant/Admin/ERPAdmin creates `AdditionalCharge` with source operation and reason. | Pending request; no debit until approved. |
| `PAY-026` | Create additional charge without reason, nonpositive amount, missing operation, wrong merchant, inactive merchant, or missing payment log. | Field-level rejection; no request or ledger effect. |
| `PAY-027` | Admin, primary Admin, ERPAdmin, and C-Level approve own or another user’s charge. | One debit; creator/reviewer/time preserved; self-approval allowed for administrative roles. |
| `PAY-028` | Accountant or clerk attempts approval. | Forbidden; pending request and balance unchanged. |
| `PAY-029` | Approve `BalanceReduction` within remaining source capacity. | One credit; operation and account views reduce consistently. |
| `PAY-030` | Create overlapping reductions/returns that separately fit but together exceed capacity. | Approval locks and rechecks; only valid total is accepted. |
| `PAY-031` | Reject adjustment, approve twice, approve after rejection, or reject after approval. | Only legal transition occurs; ledger entry is never duplicated. |
| `PAY-032` | Read historical `MerchantCredit`. | Presented as `AdditionalCharge`; original classification remains in migration/audit metadata; pending/rejected semantics preserved. |

### Refund reservation and payout

| ID | Scenario and action | Expected result |
|---|---|---|
| `PAY-033` | Request refund equal to available credit. | Pending reservation; posted balance unchanged; available credit is reserved only after approval according to contract. |
| `PAY-034` | Request zero, negative, or more than available credit. | Rejected without reservation. |
| `PAY-035` | Approve reservation, then partially pay it twice with different valid movement methods. | Status progresses `Approved` → `PartiallyPaid` → `Paid`; each payout debit and reference is separate. |
| `PAY-036` | Payout without approval, above remaining reservation, or after paid/rejected/cancelled. | Rejected; no cash record, debit, or reservation mutation. |
| `PAY-037` | Missing electronic reference or unsupported payout method. | Field-level rejection; reservation remains available for valid payout. |
| `PAY-038` | Two payouts concurrently consume the final reservation/credit. | Total paid never exceeds reservation or credit; only valid request commits. |
| `PAY-039` | New sale arrives while refund is reserved. | Balance and reservation rules remain explicit; system cannot refund credit already consumed by sale. |
| `PAY-040` | Compare legacy `CashRefund` adjustment payout with merchant refund-reservation API. | One canonical workflow owns new refunds; no double reservation or double debit across both paths. |

### Idempotency, reversal, and reconciliation

| ID | Scenario and action | Expected result |
|---|---|---|
| `PAY-041` | Replay every payment mutation after simulated response loss. | Original result returned and every business effect count remains one. |
| `PAY-042` | Reuse idempotency key on another route, merchant, payment, or different payload. | Conflict; original completed result is unchanged. |
| `PAY-043` | Reverse/correct collection, charge, reduction, return, exchange, and payout. | Immutable original retained; linked opposite entry posted once; statement running balance reconciles. |
| `PAY-044` | Run reconciliation preview twice. | Same deterministic counts/discrepancies; no data writes. |
| `PAY-045` | Reconcile confirmed legacy sales/payments/refunds/adjustments with unambiguous data. | Apply creates exactly-once account entries and allocations; second apply is a no-op. |
| `PAY-046` | Reconcile missing/ambiguous movement methods or return sources. | Preview lists exact unresolved IDs; apply is blocked; no methods or cash payouts invented. |
| `PAY-047` | Compare legacy projection and new account beyond/within currency tolerance. | Beyond tolerance blocks apply with discrepancy detail; rounding-only difference follows documented tolerance. |

### Classification

| ID | Scenario and action | Expected result |
|---|---|---|
| `PAY-048` | Account with fewer than three finalized installment sales and under 90 days. | Grade computed but flagged `Provisional`. |
| `PAY-049` | Verify paid percentage per sale, overall coverage, exposure score, and 50/30/20 weighted grade boundaries. | Scores and A/B/C/D/E boundaries match exact formulas; fully cancelled/returned obligations excluded. |
| `PAY-050` | Amount due exactly below, at, and above 1,000,000 configured threshold. | `HighExposure` begins exactly at threshold. |
| `PAY-051` | Create available credit, reserved refund, low coverage, weak discipline, rejected collections, and insufficient history independently. | Each explainable flag appears independently and clears only when its condition clears. |
| `PAY-052` | Change valid/invalid classification settings. | Valid version affects later calculations; invalid weights/bands/threshold rejected; historical snapshots immutable. |
| `PAY-053` | Account younger/older than 12 months and year-end snapshot in partial first year. | Correct all-history/rolling window; immutable year-end component values and settings version retained. |

## Cross-module consistency scenarios

| ID | Connected area | Scenario and required assertion |
|---|---|---|
| `X-001` | Catalog | Deactivating a product/SKU blocks new confirmation while preserving historical operation, payment, statement, and document rendering. |
| `X-002` | Catalog categories | Product selection remains correct through the category tree; moving a category does not change historical SKU references. |
| `X-003` | CRM merchant status | Inactive/deleted merchant cannot receive new financial operations or adjustments; historical account and statement stay readable. |
| `X-004` | CRM batch history | Sale, partial return, change, recall, and repeat sale update sold/returned quantities per exact lot/expiry without double counting. |
| `X-005` | Inventory | For every operation line, reconcile opening + receipts + returns + inbound transfers − sales − outbound transfers − write-offs ± stocktake = closing. |
| `X-006` | Inventory batches | Lot/expiry selected in UI equals operation snapshot, inventory transaction, merchant history, return allocation, report, and document. |
| `X-007` | Supply | Landed-cost receipt changes stock/cost but never merchant receivable balance. |
| `X-008` | Stocktake | Stocktake variance affects stock/report/audit without creating a merchant payment obligation. |
| `X-009` | Expiry recall | Scan is idempotent; partial returned quantity updates recall; expired return is quarantined/written off; return credit remains correct. |
| `X-010` | Shopify | Imported retail order maps canonical settlement method, selected location/SKU, inventory effect, payment artifact, and account behavior exactly once. |
| `X-011` | Notifications | Pending review, refund approval, open-payment summary, low stock, and recall notifications target correct roles, merchant, operation number, and state. |
| `X-012` | Outbox | Inject failure before/after outbox save; domain, audit, and outbox commit together or all roll back; retry publishes once. |
| `X-013` | Audit | Each create/edit/confirm/ship/receive/cancel/correct/collect/approve/reject/refund/reconcile action records actor, time, business reference, and safe payload. |
| `X-014` | Reports | Financial summary, payments, operations, merchant balances, and account statement use the same posted events and date boundaries. |
| `X-015` | Exports | JSON, CSV, XLSX, and PDF totals/rounding/signs match; Arabic text and business references render without UUID leakage. |
| `X-016` | Documents | Operation bill, payment receipt, cash receipt, merchant statement, supply landed cost, and stocktake summary open and show correct legal/brand data. |
| `X-017` | Dashboard | Dashboard totals refresh after confirmed sale, collection, return, adjustment approval, refund payout, and reversal. |
| `X-018` | Search/navigation | Search by exact operation/payment business number resolves the correct record and audit navigation; similar numbers do not collide. |

## Authorization and location matrix

Execute both UI visibility and direct API calls. A hidden control does not substitute for backend authorization, and a permitted API with a hidden control is also a defect.

| Action | Primary Admin | Admin | ERPAdmin | C-Level | Accountant | WarehouseClerk |
|---|---:|---:|---:|---:|---:|---:|
| Read operations | Allow | Allow | Allow | Allow | Allow | Allow, location scoped |
| Create/update/transition normal operations | Allow | Allow | Allow | Deny | Deny | Allow, location scoped |
| Write-off | Allow | Allow | Deny | Deny | Deny | Deny |
| Read payments/accounts/reports | Allow | Allow | Allow | Allow | Allow | Deny |
| Record collection/cash receipt | Allow | Allow | Allow | Allow | Allow | Deny |
| Create adjustment | Allow | Allow | Allow | Allow | Allow | Deny |
| Approve adjustment/refund | Allow | Allow | Allow | Allow | Deny | Deny |
| Change classification settings | Allow if `settings.write` | Allow if `settings.write` | Allow if `settings.write` | Deny | Deny | Deny |
| Reconcile/apply accounts | Allow | Allow | Allow | Allow | Deny | Deny |

Additional role scenarios:

| ID | Scenario | Expected result |
|---|---|---|
| `AUTH-001` | Remove one permission claim from an otherwise authorized role and call each endpoint directly. | Policy checks claim; role name alone does not bypass endpoint authorization. |
| `AUTH-002` | Clerk reads/acts on own versus another location’s draft, batch, stock, transfer, sale, reserve, return, and change. | Only permitted location data/actions succeed; no IDOR through known UUID. |
| `AUTH-003` | Expired access token, revoked refresh session, and concurrent refresh during mutation. | No anonymous mutation; retry with refreshed token does not duplicate business event. |
| `AUTH-004` | Primary and non-primary Admin approve their own adjustment. | Both behave according to the same administrative approval rule. |
| `AUTH-005` | Manipulate merchant, operation, account, obligation, reservation, and payment IDs in requests. | Server validates ownership relationships and returns safe `404/403/400`; no cross-merchant mutation. |

## PostgreSQL transaction and concurrency matrix

Each race uses two independent HTTP clients and two database connections, synchronized immediately before the contested write. Repeat at least 20 times.

| ID | Competing actions | Required result |
|---|---|---|
| `PG-001` | Confirm operation vs edit draft | One version wins; effects match committed content. |
| `PG-002` | Confirm vs confirm | One terminal transition and one set of stock/payment effects. |
| `PG-003` | Ship vs cancel | One legal path; reservation and transactions reconcile. |
| `PG-004` | Receive vs receive | Destination receives once. |
| `PG-005` | Sale vs transfer on final stock | Total committed consumption never exceeds stock. |
| `PG-006` | Return vs return on final source quantity | Returned quantity/value never exceeds eligibility. |
| `PG-007` | Return vs change on same source line | One consumes the available return allocation. |
| `PG-008` | Collection vs collection on same account | Unique sequences, valid allocations, exact net balance. |
| `PG-009` | Collection approval vs balance reduction approval | Capacity recalculated under lock; no hidden credit/double settlement. |
| `PG-010` | Additional charge approval vs collection | Serialized account entries and correct final net independent of order. |
| `PG-011` | Refund approval/payout vs new sale | Cannot pay credit consumed by the sale; reservation and balance remain valid. |
| `PG-012` | Payout vs payout | Paid total bounded by reservation and credit. |
| `PG-013` | Reconciliation apply vs live posting | Unique source-event keys prevent duplicates. |
| `PG-014` | Correction creation vs correction creation | One active correction/reversal lineage. |
| `PG-015` | Inject failure after inventory write but before finance/audit/outbox save. | Entire cross-context transaction rolls back. |
| `PG-016` | Inject failure after ledger append but before allocation/save completion. | No orphan entry, partial allocation, or consumed sequence. |

Direct database constraints must reject:

- A ledger row with both debit and credit positive, or both zero.
- Duplicate `(account_id, sequence)`.
- Duplicate `(source_type, source_id, entry_type)`.
- Invalid operation/payment/movement method or status.
- Negative aggregate amounts, invalid refund paid amount, or nonpositive allocation.
- More than one merchant account per merchant or obligation per operation.
- Broken account-entry-obligation foreign keys.
- Electronic movement without a reference when the final database constraint is enabled.

## Migration and historical reconciliation scenarios

| ID | Scenario | Expected result |
|---|---|---|
| `MIG-001` | Apply every module migration to an empty PostgreSQL database. | All contexts reach current heads; required tables/indexes/checks exist. |
| `MIG-002` | Upgrade from the last released migration heads with realistic legacy rows. | No data loss; aliases remain readable; new code starts successfully. |
| `MIG-003` | Verify migration discovery metadata for every new migration. | Runtime and tests discover each migration; no hand-written undiscoverable migration. |
| `MIG-004` | Legacy installment operation/log with proven method. | Backfilled to canonical merchant account semantics without changing historical receipt. |
| `MIG-005` | Legacy cash/electronic payment with known reference. | Exact method/reference retained and one collection posted. |
| `MIG-006` | Missing or contradictory historical method. | Listed unresolved; no guessed value; `NOT NULL` phase blocked. |
| `MIG-007` | Historical `MerchantCredit` in pending, rejected, and completed states. | Reclassified in audit metadata; only completed/approved row posts as additional-charge debit. |
| `MIG-008` | Ambiguous historical return/change source. | Listed for manual allocation; apply does not invent a source. |
| `MIG-009` | Preview then apply then preview/apply again. | First preview is read-only; first apply exact; repeat has zero new postings. |
| `MIG-010` | Restore production-like backup to isolation, migrate, reconcile, and compare. | Legacy/new totals agree within rounding; discrepancy report grouped by payment and merchant is retained. |

## API contract and error scenarios

| ID | Scenario | Expected result |
|---|---|---|
| `API-001` | Missing/invalid JSON, wrong content type, oversized notes/reference, empty GUID, malformed date, overflow amount. | Stable ProblemDetails without stack trace or HTML; field errors are actionable. |
| `API-002` | Unauthorized, forbidden, missing resource, validation, capacity conflict, concurrency conflict, and idempotency conflict. | Consistent `401`, `403`, `404`, `400`, and `409` semantics across both modules. |
| `API-003` | Pagination at 0, 1, maximum, and beyond maximum for operations, payments, statements, history, and accounts. | Deterministic order, bounded response, no missing/duplicate records between pages. |
| `API-004` | Filter by merchant, status, type, date boundaries, operation number, payment number, location, and search text. | Exact scope and timezone behavior; no cross-tenant/location leakage. |
| `API-005` | Send canonical English values while UI is Arabic; send translated Arabic enum values directly. | Canonical values succeed; translated system values are rejected rather than silently misclassified. |
| `API-006` | Request statement for nonexistent merchant, active merchant without account, and merchant with empty account. | Distinct intentional responses; frontend shows useful empty/not-found state instead of generic failure. |

## Frontend and document scenarios

| ID | Scenario | Expected result |
|---|---|---|
| `UI-001` | Create each operation type in Arabic and English. | Labels translate, payload values remain canonical English, direction is correct, and selected values persist after validation errors. |
| `UI-002` | Exercise required payment-method control for every financial operation and money movement. | Submission disabled/blocked until explicit selection; no hidden default. |
| `UI-003` | Switch between cash and electronic methods. | Reference field appears and becomes required only for `CashTransaction`, `BankTransfer`, and `Wallet`; stale reference is cleared appropriately. |
| `UI-004` | Use batch/expiry picker with multiple lots and “Create new batch” for inbound flow. | Outbound offers eligible stock; return uses merchant history; inbound permits controlled new batch; chosen values reach API unchanged. |
| `UI-005` | Load merchant with no account, debt, credit, reservations, and more than 200 entries. | Clear state for each case; signs/labels are unambiguous; pagination/limit is communicated. |
| `UI-006` | Record collection and inspect preview, allocation override, pending-review notice, and refreshed views. | Preview matches the eventual posted result; pending work stays outside balances, and approval refreshes statement/payment/report panels without reload. |
| `UI-007` | Run operation/payment forms at 320, 768, 1024, and 1440 px in Arabic RTL and English LTR. | No clipped controls/table actions; keyboard focus and logical order remain usable. |
| `UI-008` | Double-click every mutation button and navigate away during request. | Button prevents duplicate submit or idempotency guarantees one effect; return state is understandable. |
| `UI-009` | Simulate API offline, timeout, `401`, `403`, `404`, validation, and `409`. | Specific recoverable message shown; user input retained when safe; generic workspace error only for truly unknown failure. |
| `UI-010` | Inspect statement event labels, methods, actors, operation/payment references, notes, debit/credit, and running balance. | Friendly business numbers and translated labels shown; UUIDs remain internal. |
| `DOC-001` | Generate every operation bill across all types/statuses. | Correct operation number, parties, locations, lines, batch/expiry, method, totals, and actor timeline. |
| `DOC-002` | Generate partial/full collection and cash receipts for each movement method. | Gross collection remains distinct from discounts, reductions, and refunds; references and confirmation actor/time correct. |
| `DOC-003` | Generate merchant statement after `E2E-001`. | All 14 steps reconcile, signs and running balance agree, Arabic shaping is valid, and no row is silently clamped. |
| `DOC-004` | Compare CSV/XLSX/PDF under Arabic/English and Cairo date boundaries. | Same record set and monetary totals; encoding, decimal format, and RTL/LTR correct. |

## Existing automated coverage that must be updated or extended

Current useful suites include:

- `backend/Lensee.Tests/OperationsEndpointContractTests.cs`
- `backend/Lensee.Tests/FinancialProjectionTests.cs`
- `backend/Lensee.Tests/PermissionTests.cs`
- `backend/Lensee.PostgresIntegrationTests/OperationTransitionPostgresTests.cs`
- `backend/Lensee.PostgresIntegrationTests/PaymentIntegrityPostgresTests.cs`
- `e2e/operations-sales-return-change.spec.js`
- `e2e/payments-accounting.spec.js`
- `e2e/inventory-transfer.spec.js`
- `e2e/notifications-reports.spec.js`
- `e2e/auth-permissions.spec.js`

Required extensions:

1. Replace canonical browser submissions of `Installment` with `MerchantAccount`; keep separate API compatibility tests for both legacy aliases.
2. Update return/change browser cases to submit `MerchantAccount`, because cash refund is a separate payout workflow.
3. Add direct coverage for merchant account list/detail/statement, collection preview/posting, allocation overrides, refund reservations/payouts, classification settings/history, and reconciliation.
4. Add PostgreSQL races `PG-006` through `PG-016`; EF InMemory cannot prove their locking, constraints, or rollback behavior.
5. Add browser assertions using exact operation/payment IDs and business numbers rather than first-row selectors.
6. Assert persisted downstream effects, not only notices and displayed statuses.

## Execution order and gates

1. **Static gate:** build with warnings as errors, frontend syntax/localization checks, migration discovery, and scoped diff check.
2. **Unit gate:** financial equations, allocation math, classification boundaries, normalization, and state-machine tests.
3. **HTTP contract gate:** all validation, permissions, and deterministic endpoint scenarios with isolated data.
4. **PostgreSQL gate:** migrations, constraints, cross-context rollback, idempotency, and concurrency matrix.
5. **Browser gate:** role-specific English/Arabic desktop/mobile workflows using exact IDs.
6. **Document gate:** open representative PDF/XLSX/CSV artifacts and reconcile them to API/database evidence.
7. **Recovery gate:** backup, isolated restore, migration, reconciliation preview/apply, and repeatability.

No release passes while any of these conditions exists:

- Negative stock or over-returned quantity.
- Duplicate inventory or account effect from one event.
- Posted financial entry without source lineage.
- Payment/operation/account/report/document disagreement beyond currency rounding.
- Refund payout without approved available credit.
- Pending/rejected item affecting finalized balance.
- UI action visible to a role that receives `403`, or hidden from a role that is expected to perform it.
- Ambiguous historical method or return allocation silently inferred.
- Required PostgreSQL or browser evidence skipped.

## Evidence and defect register

Record one row per scenario:

| Field | Required value |
|---|---|
| Scenario | Exact ID from this document |
| Candidate | Commit SHA and migration heads |
| Actor | User ID, role, and location |
| References | Merchant ID, operation ID/number, payment ID/number, account ID, entry IDs, batch IDs |
| Before | Stock by location/batch, obligations/allocations, debit/credit totals, balance, pending/reserved amounts |
| Request | Method, route, sanitized body, idempotency key |
| Response | Status, sanitized body, correlation/request ID |
| After | Same measurements as Before plus audit/outbox/notification rows |
| Documents | File names and reconciled totals where applicable |
| Result | Pass, fail, blocked, or not run |
| Defect | Severity, reproducible steps, expected, observed, owner, and fix validation scenario |

Never mark a scenario passed from expected behavior alone. Store the actual observed values and queries with the candidate evidence.
