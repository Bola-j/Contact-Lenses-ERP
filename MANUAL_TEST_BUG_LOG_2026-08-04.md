# Manual Test Bug Log - 2026-08-04

## Test target and method

- Frontend: `http://localhost:3001/`
- Method: visible, manual browser testing using the requested Computer Use workflow.
- Test data safety: no catalog, stock, operation, payment, or CRM records were written. Employee accounts were created because the user explicitly authorized disposable role-account provisioning.

## Provisioned disposable accounts

| Username | Role | Location scope | Status |
| --- | --- | --- | --- |
| `qa_erpadmin_20260804` | ERP administrator | All locations | Active |
| `qa_clevel_20260804` | C-Level | All locations | Active |
| `qa_accountant_20260804` | Accountant | All locations | Active |
| `qa_warehouse_20260804` | Warehouse clerk | Roxy (Main) | Active |
| `qa_retail_20260804` | Warehouse clerk | Mohamed Naguib (Retail) | Active |
| `qa_online_20260804` | Warehouse clerk | Online | Active |

The administrator user list increased from one account to seven. Passwords are intentionally not recorded in this report.

## Executed checks

| ID | Scenario | Result | Evidence |
| --- | --- | --- | --- |
| MT-01 | Open the ERP sign-in page | Passed | Lensee sign-in form rendered and displayed `API healthy`. |
| MT-02 | Sign in with the supplied administrator account | Passed | Administrator overview opened with all primary modules, including Admin. |
| MT-03 | Empty product validation | Passed | Catalog loaded with zero products/categories/brands; native required validation prevented an empty product submission. |
| MT-04 | Empty employee-account validation | Passed | The page showed `Full name and username are required.` and created no account. |
| MT-05 | Account roles and location selection | Passed | All five roles were available. Selecting Warehouse clerk enabled a selector with Mohamed Naguib (Retail), Online, and Roxy (Main). |
| MT-06 | Provision test accounts | Passed | Six active role/location accounts above were created from the Admin UI. |
| MT-07 | Roxy warehouse-clerk login and module boundary | Passed | Login succeeded. Dashboard stated that the clerk is restricted to the assigned location; Admin, Payments, Reports, Supply, and Stocktake were absent from navigation. |
| MT-08 | Online warehouse-clerk location enforcement | Passed | Inventory was read-only, the location selector was disabled, and Online was the only active location shown. |
| MT-09 | Mohamed Naguib warehouse-clerk location enforcement | Passed | Inventory was read-only, the location selector was disabled, and Mohamed Naguib (Retail) was the only active location shown. |
| MT-10 | ERP administrator sign-in and UI access | Passed | Login succeeded as ERP Admin; the operational, finance, reporting, inventory, catalog, stocktake, CRM, online-intake, and Admin UI entry points were visible. |
| MT-11 | C-Level sign-in and UI access | Passed | Login succeeded as C-Level. Oversight, payments, reports, inventory, supply, catalog, stocktake, CRM, operations, and notifications were visible; Admin was absent. |
| MT-12 | Accountant sign-in and UI access | Passed | Login succeeded as Accountant. Payments, reports, CRM, operations, and notifications were visible; catalog, inventory, stocktake, supply, online intake, and Admin were absent. |
| MT-13 | Arabic/RTL rendering | Failed | Core dashboard copy and counters remained English and rendered with broken bidirectional punctuation; see BUG-01. |
| MT-14 | Empty operational draft validation | Passed | Operations loaded a blank queue; required selection validation prevented an incomplete warehouse-transfer draft. |
| MT-15 | Inventory and payment entry points | Passed, no data | Inventory exposed the three locations with zero stock/batches/transactions. Payments loaded with zero confirmations and ledger records. |

## Application bugs

### BUG-01 - Arabic workspace contains untranslated English strings and malformed RTL/LTR composition

- **Severity:** Medium
- **Area:** Dashboard and navigation localization
- **Steps to reproduce:**
  1. Sign in as Administrator, C-Level, Accountant, or Warehouse clerk.
  2. Leave the workspace in Arabic (the default tested locale) and open Dashboard.
  3. Review the dashboard summary, tile labels, and supporting copy.
- **Expected result:** Arabic mode should present fully localized Arabic copy and correctly ordered bidirectional text.
- **Actual result:** Role dashboards show English strings such as `Inventory and operational execution`, `Executive oversight`, `Payments and remaining control`, `Total sales`, `Unavailable`, `categories`, `balances`, `batches`, `transactions`, and `Online intake`. The English paragraph is inserted in an RTL layout with misplaced leading/trailing punctuation, for example `.stock, and reports`.
- **Impact:** Arabic-first operators see mixed-language operational guidance and visually broken sentence order across multiple roles.

## Test infrastructure note

### TST-01 - Computer Use URL-confidence interruption

The first attempt to submit the local sign-in form was interrupted by the Computer Use runtime with `could not determine the current browser URL on Windows with enough confidence to enforce policy`. A resumed visible-browser run completed login. This is a test-tool limitation, not an ERP defect.

## Remaining coverage boundary

The environment contains zero products, categories, brands, stock, batches, transactions, operations, payment confirmations, ledger records, merchants, and representatives. Therefore the data-dependent happy paths in the companion scenario plan were not executed: product/SKU creation, receiving, transfer, sale, return, exchange, adjustment, stocktake confirmation, payment assignment/approval, refund, cash movement, merchant credit, and report export. No business records were created solely to force these paths.

## Coverage status

Role provisioning, all three warehouse-location clerk logins, UI module boundaries, location-scope enforcement, empty-form validation, and Arabic RTL review are complete. The companion plan identifies the remaining data-provisioned end-to-end scenarios. One application defect was reproduced: BUG-01.
