# Reporting migration inventory

This tracked inventory is the compatibility baseline for the QuestPDF and ClosedXML migration. All legacy routes remain authorized by `reports.read`; stock data additionally retains the existing accountant denial and clerk location scope enforced by the stock query.

Implementation status: compatibility routes, standardized exports, semantic document models, QuestPDF/ClosedXML/CSV renderers, bundled fonts, catalog metadata, server filenames, and the precision-ledger workspace are implemented and covered by the focused contract/renderer checks below.

## Analytical reports

| Key | Legacy routes | Caller | Fields preserved | Data source | Target formats |
|---|---|---|---|---|---|
| `financial-summary` | `GET /api/v1/reports/financial-summary` | Reports workspace/dashboard | `totalSales`, `actualCollected`, `remainingReceivable` (JSON names and decimal values preserved) | Operations + Payments financial projection | JSON, PDF, XLSX |
| `stock` | `GET /api/v1/reports/stock`, `.csv` | Reports workspace | location id/name/type, SKU id/code, product, available, warehouse/rep reserved, target, updated | Inventory + Catalog projections | JSON, PDF, XLSX, CSV |
| `operations` | `GET /api/v1/reports/operations`, `.csv` | Reports workspace and document picker | id/number/type/status, client, location, quantity, total, created | Operations | JSON, PDF, XLSX, CSV |
| `payments` | `GET /api/v1/reports/payments`, `.csv` | Reports workspace and receipt pickers | id/reference, operation, merchant, method, total, paid, remaining, status, created | Payments + Operations + CRM | JSON, PDF, XLSX, CSV |
| `supply` | `GET /api/v1/reports/supply`, `.csv` | Reports workspace and landed-cost picker | shipment/reference, supplier, invoice, status, quantity, landed total, receipt operation, dates | Operations supply aggregate | JSON, PDF, XLSX, CSV |
| `merchant-balances` | `GET /api/v1/reports/merchant-balances`, `.csv` | Reports workspace and statement picker | merchant id/name/status, sales, returns, change, payments, refunds, charges, reductions, balance | CRM + merchant balance service | JSON, PDF, XLSX, CSV |

Standard route: `GET /api/v1/reports/{key}/export?format=pdf|xlsx|csv&language=ar|en|bi`. Filters are limited to the existing stock location, operations date/type, and supply date/status filters exposed by the catalog.

## Official documents

| Key | Legacy route | Representative legacy sections | Data source | Template | Target formats |
|---|---|---|---|---|---|
| `operation-bill` | `/api/v1/reports/operations/{id}/bill.pdf` | operation/client facts, line items, totals, notes, signatures, reference/footer | Operations, Catalog, CRM, Identity | Transactional | PDF |
| `payment-receipt` | `/api/v1/reports/payments/{id}/receipt.pdf` | payment/operation/merchant facts, entries, totals, signatures | Payments, Operations, CRM, Identity | Transactional | PDF |
| `cash-receipt` | `/api/v1/reports/payments/{id}/cash-receipt.pdf` | cash custody, related movement, custody trail, signatures | Payments, Operations, CRM, Identity | Transactional | PDF |
| `supply-landed-cost` | `/api/v1/reports/supply/{id}/landed-cost.pdf` | shipment, lines, cost breakdown, history, landed totals | Operations, Identity | Analytical | PDF, XLSX |
| `merchant-statement` | `/api/v1/reports/merchants/{id}/statement.pdf` | merchant overview, operations/payments, reconciliation totals, acknowledgment | CRM, Operations, Payments, Identity | Analytical | PDF, XLSX |
| `stocktake-summary` | `/api/v1/reports/stocktakes/{id}/summary.pdf` | stocktake metadata, counted lines/variance, notes, confirmations | Inventory, Catalog, Identity | Operational | PDF, XLSX |

Standard route: `GET /api/v1/documents/{key}/{id}?format=pdf|xlsx&language=ar|en|bi`. Missing records remain `404`. Unsupported formats/languages and invalid filters return Problem Details. Successful compatibility and standard exports continue writing the existing export-log table; no schema migration is introduced.

## Compatibility surfaces

- Frontend: `frontend/app.js` reports workspace, report tables, official-document search pickers, downloads, and export history.
- Backend contracts: `backend/Lensee.Tests/ReportsEndpointContractTests.cs` and report-related integration coverage.
- API collections: repository Postman definitions referencing `/api/v1/reports` and the six document URLs.
- Legacy artifact baseline: the original JSON payload fields, CSV headings/rows, and the six PDF fact/section models in `ReportsEndpoints` are the semantic comparison source during migration.

## Migration gates

1. Pilot equivalence: operation bill, stock report, and stocktake summary preserve every legacy business field.
2. Compatibility routes and standard routes use the same semantic model and export service.
3. Controllers/endpoints contain no renderer construction after legacy helper removal.
4. Contract baseline, renderer tests, frontend checks, Alpine generation, and representative visual review are recorded before release.
