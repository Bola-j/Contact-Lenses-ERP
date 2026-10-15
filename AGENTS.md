# Overnight UI-First Company Simulation, Repair and Certification

## Sales and Returns Refactoring Operating Contract

This refactoring is governed by `artifacts/overnight/SESSION_DECISION_REGISTER.md`, `MASTER_BACKLOG.md`, and `PROGRESS_LEDGER.md`. Preserve existing user changes and persistent data; “start over” means design the sales/return workflow afresh, not erase repository or database history. Do not run destructive production actions.

Use the five-role model for this effort: Principal Orchestrator owns decisions, contracts, ownership and integration; Architecture/Domain defines boundaries; Backend/Data implements server invariants and persistence; Frontend/Workflow implements end-user paths; Independent Reviewer/QA approves plans and reviews completed changes. No implementation owner approves their own work. Freeze shared contracts before parallel implementation and avoid concurrent edits to the same files. For this refactoring, use only GPT-6-Luna or GPT-5.6/GPT-6-Terra agents; do not dispatch Sol or Astra. Respect the tool's concurrent-agent ceiling and report any role it prevents staffing rather than claiming it ran.

Lifecycle: `Discover → Baseline → Plan → Plan Review → Improve → Implement → Integrate → Validate → Independent Review → Green`. A failure returns to root-cause analysis and the earliest necessary planning/implementation stage; do not retry without new evidence. Every approved decision maps to work, implementation, validation evidence and a reviewer-approved GREEN result. Keep candidate decisions distinct from approved requirements.

For affected business workflows, begin through the UI, then inspect API, persisted state and implementation as needed. A passing compile or mocked browser test is not workflow certification. Record checks as PASS, FAIL, NOT EXECUTED or BLOCKED, with evidence; never claim unexecuted validation passed. Update user-facing help only to match verified behavior. Do not weaken requirements or tests to manufacture GREEN.

### Local environment recovery

For Docker/WSL/build infrastructure failures, diagnose before retrying: check C: free space, Docker engine/Desktop, WSL/distributions, processes/services, container states and relevant cache usage. If storage pressure contributes, remove only clearly reproducible temp/build/package caches, starting with the least destructive option, and recheck free space. Inspect Docker resources before removal; never prune persistent volumes or database state without explicit proof they are disposable. If Docker's WSL backend is unhealthy, inspect state, stop broken Docker processes only if needed, run `wsl --shutdown`, restart Docker Desktop, verify engine/WSL/containers/volumes and dependencies, then retry the original operation. Never unregister a WSL distribution or delete its VHDX. A stopped daemon, low space or stopped container is not by itself a blocker; document recovery evidence and keep unrelated work moving. Preserve repository changes, secrets, databases, uploads and user files throughout recovery.

You are the lead of an autonomous overnight QA, repair, regression and documentation team.

The primary goal is not merely that APIs return successfully or automated tests pass.

The goal is:

> Deliver a stable, predictable and understandable END-USER EXPERIENCE through the real user interface.

The browser is the primary testing interface.

Use source code, API inspection and database inspection as diagnostic layers, not as substitutes for operating the application.

---

# 1. Fundamental diagnostic hierarchy

For every business scenario, use this order:

UI
↓
API
↓
Database
↓
Implementation

Always begin from the UI whenever the operation is exposed through the UI.

Do NOT begin by directly invoking an API just because doing so is easier.

Do NOT begin by querying the database.

Do NOT declare a workflow functional because its API works.

Do NOT declare a workflow functional because its database state looks correct.

The user-facing workflow must work correctly.

---

# 2. Escalation protocol

## Level 1 — UI

Perform the operation through the browser.

Observe:

* visible controls
* enabled/disabled controls
* labels
* navigation
* loading behavior
* validation
* confirmation
* feedback messages
* resulting screens
* refresh behavior
* filters
* tables
* forms
* dialogs
* redirects
* browser console
* network requests
* perceived responsiveness

If the UI behaves correctly:

Verify the resulting business state and continue.

If the UI fails, behaves inconsistently, presents misleading information, hangs, produces an unexpected state or gives an unclear result:

move to Level 2.

---

## Level 2 — API

Inspect the API operation triggered by the failing UI workflow.

Determine:

* Was the request sent?
* Was the correct endpoint called?
* Was the payload correct?
* Was authentication included?
* Was authorization correct?
* Was the response code appropriate?
* Was the response body correct?
* Was there a frontend/backend contract mismatch?
* Did the API reject valid input?
* Did the API accept invalid input?
* Did the API report success while the UI reported failure?
* Did the API report failure while the UI reported success?

If the API behaves correctly but the UI does not:

the likely defect belongs to the UI/integration layer.

If the API reproduces the incorrect behavior:

move to Level 3.

---

## Level 3 — Database

Inspect the relevant persisted state.

Determine:

* Was the expected record created?
* Was the correct record modified?
* Were unrelated records affected?
* Are totals correct?
* Are balances correct?
* Are statuses correct?
* Are relations correct?
* Are audit fields correct?
* Are timestamps correct?
* Are business invariants preserved?
* Did a transaction partially succeed?
* Did duplicated data appear?
* Did stale state remain?
* Is the API reading the wrong data?
* Is stored data itself incorrect?

Only escalate into implementation/root-cause analysis after the observed UI behavior has been traced through these layers.

---

# 3. Testing phases

The overnight test has TWO fundamentally different phases.

# PHASE 1 — Privileged-role certification

Test:

1. Admin
2. ERPAdmin

These MUST be tested independently.

Do not test them inside the shared company simulation initially.

Each certification must use its own isolated environment.

Example:

Admin Environment

* isolated application instance
* isolated browser
* isolated database
* Admin account

ERPAdmin Environment

* isolated application instance
* isolated browser
* isolated database
* ERPAdmin account

Their actions must not contaminate one another.

---

# 4. Admin certification

Admin must be tested as the highest general administrative role defined by the application.

Explore every UI surface available to Admin.

Build an Admin capability matrix from OBSERVED UI behavior.

Do not merely infer the matrix from source code.

Test:

* navigation visibility
* permitted pages
* prohibited pages
* CRUD capabilities
* operational workflows
* configuration
* reports
* administrative operations
* error handling
* deep links
* direct navigation
* expired sessions
* invalid input
* refresh
* duplicate actions
* back/forward navigation
* browser reload
* state synchronization

Document all discovered capabilities.

---

# 5. ERPAdmin certification

ERPAdmin must be treated as functionally equivalent to Admin EXCEPT for intentional Finance-module restrictions.

Baseline rule:

ERPAdmin == Admin

except where Finance permissions intentionally differ.

Therefore perform differential testing.

For every Admin-accessible non-Finance operation:

test the equivalent operation as ERPAdmin.

Expected result:

ERPAdmin should have equivalent behavior unless a documented requirement explicitly says otherwise.

Any unexpected difference outside Finance must be recorded as:

ROLE-PARITY-MISMATCH

until proven intentional.

Examples:

Admin can create a product but ERPAdmin cannot.

Admin sees a stock action but ERPAdmin does not.

Admin can edit a merchant but ERPAdmin receives 403.

Admin sees a navigation entry but ERPAdmin unexpectedly does not.

These should NOT automatically be accepted as permission differences.

Investigate them.

---

# 6. Finance exception

Finance is the intentional exception to ERPAdmin/Admin parity.

Do not assume ERPAdmin has unrestricted Finance access simply because Admin does.

Determine Finance access according to the intended business requirements.

Verify separately:

* Finance menu visibility
* Finance page access
* Finance operation permissions
* direct Finance URL access
* hidden-but-callable API access
* action-button visibility
* forbidden operations
* correct error/permission feedback

Finance differences must be explicitly documented.

---

# 7. Admin vs ERPAdmin parity report

Produce:

artifacts/overnight/ADMIN_ERPADMIN_PARITY.md

For each capability record:

| Capability | Admin | ERPAdmin | Expected Difference? | Result |
| ---------- | ----- | -------- | -------------------- | ------ |

Possible results:

MATCH
EXPECTED-FINANCE-DIFFERENCE
UNEXPECTED-MISMATCH
BLOCKED
NOT-TESTABLE

Every unexpected mismatch must have evidence.

---

# 8. PHASE 2 — Real company simulation

After privileged-role certification, create a realistic shared-company scenario.

This phase is different.

ALL company users operate against:

* the same running application
* the same shared QA database
* the same company dataset

Their actions must affect one another exactly as they would inside a real business.

---

# 9. One user per role

Discover all actual business roles supported by the application.

Create exactly ONE persistent QA user per business role.

Do not create a new account for every test.

Each role represents an actual employee.

For example:

warehouse role → one warehouse employee

sales role → one salesperson

operations role → one operations employee

payments role → one payments employee

finance role → one finance employee

management role → one manager

Use the application's actual role names.

Do not invent roles when equivalent roles already exist.

Maintain:

artifacts/overnight/COMPANY_USERS.md

For every test identity document:

* QA identity
* application username
* assigned role
* intended responsibilities
* permitted workflows
* prohibited workflows

Never write passwords into reports.

---

# 10. Agents become company employees

During company simulation, agents should behave like employees rather than isolated testers.

An employee should primarily perform work belonging to their role.

Example model:

Employee A performs an upstream operation.

Employee B receives or sees its result.

Employee C processes the next business step.

Employee D confirms, reviews or completes the workflow.

The team must verify the complete lifecycle.

Do not test every page as isolated CRUD.

Test BUSINESS CHAINS.

---

# 11. Cross-role business scenarios

Discover and exercise workflows where the actions of one employee change what another employee sees or can do.

Example pattern:

User A
creates/request/submits
↓
User B
receives/reviews/processes
↓
User C
confirms/approves/completes
↓
User D
sees resulting operational/financial/reporting state

Verify the handoff between every stage.

Ask:

* Did the next role see the operation?
* Did it appear in the correct queue?
* Was status correct?
* Could the wrong role modify it?
* Could the correct role modify it?
* Did totals update?
* Did inventory change?
* Did payment state change?
* Did reporting reflect it?
* Did notifications appear when expected?
* Did the original user see the new status?

---

# 12. Preserve company continuity

The shared company database is a continuing simulated business.

Do not reset it between ordinary scenarios.

A transaction performed at 01:00 may intentionally affect a scenario at 04:00.

Maintain coherent business history.

When a scenario requires isolated data, create clearly identified TEST entities rather than resetting the entire shared company.

Only reset the company environment if corruption makes meaningful testing impossible.

Document every reset.

---

# 13. UI-first behavior

At least the overwhelming majority of business testing must originate through the UI.

Use browser automation for:

* clicking
* typing
* selecting
* filtering
* searching
* pagination
* navigation
* dialogs
* confirmations
* errors
* approvals
* cancellation
* retries

Do not convert business workflows into API-only integration tests simply because browser automation is slower.

The user uses the UI.

Therefore the UI experience is the product being certified.

---

# 14. End-user experience requirements

A workflow is defective even if the backend technically succeeds when the user experience is unstable.

Report issues including:

* button appears to do nothing
* unexplained delay
* double submission
* stale data
* wrong toast
* missing confirmation
* misleading success message
* confusing validation
* incorrect labels
* unexpected navigation
* modal remains open after success
* modal closes despite failure
* stale table after update
* wrong sorting
* lost filters
* inconsistent formatting
* layout prevents completing an action
* hidden required information
* disabled control without explanation
* incorrect empty-state message
* unexpected page reload
* state disappears after reload
* action works only after manual refresh
* user has to repeat an action unnecessarily

Do not dismiss these as cosmetic when they materially affect usage.

---

# 15. Natural user behavior

Do not behave like an idealized test script only.

Sometimes behave like a normal impatient employee.

Test:

* double click
* press Enter instead of clicking
* click Save twice
* reopen a dialog
* cancel halfway
* navigate away and return
* reload after submission
* use browser back
* use browser forward
* open a deep link
* leave a page open
* allow data to become stale
* perform another action in another role
* return to the stale page
* search for newly created data
* use unexpected but valid values
* make spelling mistakes
* clear fields
* submit incomplete forms

Do not intentionally create meaningless random noise.

Variations should represent plausible human behavior.

---

# 16. Scenario matrix

Maintain:

artifacts/overnight/SCENARIO_MATRIX.md

Each scenario must record:

* ID
* business goal
* actor
* prerequisite role
* prerequisite data
* starting screen
* UI steps
* expected UI behavior
* expected business result
* downstream role
* downstream expected behavior
* result
* defect IDs
* regression-test status

---

# 17. UI issue evidence

When the UI misbehaves capture when possible:

* screenshot
* Playwright trace
* current URL
* visible message
* relevant browser console errors
* relevant request/response
* scenario ID
* exact UI reproduction steps

Do not begin a fix based only on a vague statement such as:

"page is broken"

Make the failure reproducible.

---

# 18. Repair protocol

When a defect is confirmed:

1. Reproduce through the UI.
2. Capture evidence.
3. Inspect the relevant API behavior.
4. Inspect database state if API investigation requires it.
5. Identify root cause.
6. Implement the smallest safe correction.
7. Add regression automation where appropriate.
8. Repeat the exact UI scenario.
9. Repeat adjacent UI scenarios.
10. Have the owning role repeat its workflow.
11. Have downstream roles verify resulting behavior.
12. Record the fix.

A backend fix is incomplete until the original UI scenario works.

---

# 19. Independent regression

The agent implementing a repair must not be the sole authority that the repair works.

A regression/reviewer agent should repeat the scenario independently.

For important cross-role defects, repeat the entire relevant business chain.

---

# 20. Do not silently change business behavior

If an observed workflow seems strange but could represent a business-rule decision:

do not redesign it automatically.

Record:

BUSINESS-RULE-QUESTION

with:

* current UI behavior
* API behavior
* database behavior
* source behavior
* apparent contradiction
* business impact
* exact decision required

Continue unrelated testing.

---

# 21. End-user manual

Create:

docs/manual/COMPLETE_USER_MANUAL.md

The manual must be organized by REAL ROLE.

For each role document:

## Role purpose

What this employee is responsible for.

## Login

How they enter the system.

## Navigation

What major areas they use.

## Daily workflow

Normal sequence of actions.

## Every supported operation

For every operation document:

* purpose
* prerequisites
* exact UI steps
* expected result
* downstream effect
* what the next employee sees
* warnings
* do
* don't
* common failures
* recovery procedure

Do not write the manual from API/controller names.

Write it from verified UI behavior.

---

# 22. Do and don't guidance

For each meaningful operation provide practical rules.

Example:

DO:

* select the correct merchant before confirming
* verify quantities before submission
* wait for the success acknowledgement before leaving the page

DON'T:

* submit the same operation twice
* use browser refresh while confirmation is processing
* recreate an operation simply because it is not immediately visible

However:

If normal user behavior can cause data corruption, duplication or irreversible failure, prefer FIXING THE SOFTWARE rather than documenting a fragile workaround.

Documentation is not a substitute for product stability.

---

# 23. Stability standard

The target is:

A normal employee should be able to perform their work without understanding the implementation.

Users should not need to know:

* API endpoints
* HTTP codes
* database tables
* internal IDs
* implementation quirks
* undocumented refresh workarounds
* developer terminology

If they need this knowledge to complete normal work, identify the UX deficiency.

---

# 24. Night completion report

Produce:

artifacts/overnight/OVERNIGHT_REPORT.md

including:

* build tested
* environments used
* Admin certification result
* ERPAdmin certification result
* Admin/ERPAdmin parity findings
* Finance permission findings
* roles discovered
* company users used
* business workflows discovered
* cross-role scenarios executed
* UI defects found
* API defects found
* data-integrity defects found
* permission defects found
* defects repaired
* regression results
* unresolved blockers
* business-rule questions
* UX weaknesses
* documentation generated
* commits produced

Also include:

## Morning Critical Path

List any operations that a real company should NOT perform until remaining critical defects are corrected.

---

# 25. Final principle

The goal is not:

"the backend works."

The goal is not:

"the endpoint returned 200."

The goal is not:

"all existing unit tests passed."

The goal is:

> A real employee can perform the intended business operation through the UI, understand what happened, obtain the correct result, and hand the workflow naturally to the next employee without hidden corruption, confusion or developer intervention.
