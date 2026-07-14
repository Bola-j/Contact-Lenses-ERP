import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const postmanDir = path.join(root, "postman");
fs.mkdirSync(postmanDir, { recursive: true });
const defaultRunId = `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`.toUpperCase();

const collectionVariables = [
  "runId",
  "adminToken",
  "clevelToken",
  "accountantToken",
  "onlineClerkToken",
  "accountantUserId",
  "mainLocationId",
  "retailLocationId",
  "onlineLocationId",
  "categoryId",
  "brandId",
  "productId",
  "skuId",
  "merchantId",
  "representativeId",
  "receiptOperationId",
  "transferOperationId",
  "wholesaleOperationId",
  "retailOperationId",
  "installmentOperationId",
  "paymentLogId",
  "subLogId",
  "returnOperationId",
  "changeOperationId",
  "writeOffOperationId",
  "stocktakeId",
  "exportLogId"
].map((key) => ({ key, value: "" }));

function script(lines) {
  return {
    type: "text/javascript",
    exec: Array.isArray(lines) ? lines : lines.trim().split("\n")
  };
}

function testStatus(code) {
  return `pm.test("HTTP ${code}", () => pm.response.to.have.status(${code}));`;
}

function requestItem({ name, method = "GET", path, token = "adminToken", body, tests = [] }) {
  const headers = [];
  if (token) {
    headers.push({ key: "Authorization", value: `Bearer {{${token}}}` });
  }
  if (body !== undefined) {
    headers.push({ key: "Content-Type", value: "application/json" });
  }

  return {
    name,
    request: {
      method,
      header: headers,
      url: `{{baseUrl}}${path}`,
      ...(body !== undefined
        ? {
            body: {
              mode: "raw",
              raw: typeof body === "string" ? body : JSON.stringify(body, null, 2),
              options: { raw: { language: "json" } }
            }
          }
        : {})
    },
    event: tests.length > 0
      ? [{ listen: "test", script: script(tests) }]
      : []
  };
}

function loginItem(name, usernameVar, passwordVar, tokenVar, refreshVar) {
  return requestItem({
    name,
    method: "POST",
    path: "/api/v1/auth/login",
    token: null,
    body: {
      username: `{{${usernameVar}}}`,
      password: `{{${passwordVar}}}`
    },
    tests: [
      testStatus(200),
      "const json = pm.response.json();",
      `pm.environment.set("${tokenVar}", json.accessToken);`,
      `pm.environment.set("${refreshVar}", json.refreshToken);`,
      "pm.test('access token returned', () => pm.expect(json.accessToken).to.be.a('string').and.not.empty);",
      "pm.test('user role returned', () => pm.expect(json.user.role).to.be.a('string').and.not.empty);"
    ]
  });
}

const items = [
  {
    name: "00 - Auth And Platform",
    item: [
      requestItem({
        name: "Health",
        path: "/health",
        token: null,
        tests: [
          testStatus(200),
          "const json = pm.response.json();",
          "pm.test('health is reported', () => pm.expect(json.status).to.be.oneOf(['Healthy', 'Degraded']));"
        ]
      }),
      loginItem("Login Admin", "adminUsername", "adminPassword", "adminToken", "adminRefreshToken"),
      loginItem("Login C-Level", "clevelUsername", "clevelPassword", "clevelToken", "clevelRefreshToken"),
      loginItem("Login Accountant", "accountantUsername", "accountantPassword", "accountantToken", "accountantRefreshToken"),
      loginItem("Login Online Clerk", "onlineClerkUsername", "onlineClerkPassword", "onlineClerkToken", "onlineClerkRefreshToken"),
      requestItem({
        name: "Admin /me",
        path: "/api/v1/auth/me",
        tests: [
          testStatus(200),
          "const json = pm.response.json();",
          "pm.test('admin role', () => pm.expect(json.role).to.eql('Admin'));"
        ]
      }),
      requestItem({
        name: "List Users And Capture Accountant",
        path: "/api/v1/users",
        tests: [
          testStatus(200),
          "const rows = pm.response.json();",
          "const accountant = rows.find(row => row.role === 'Accountant');",
          "const clevel = rows.find(row => row.role === 'CLevel');",
          "pm.test('accountant user exists', () => pm.expect(accountant).to.exist);",
          "if (accountant) pm.environment.set('accountantUserId', accountant.id);",
          "if (clevel) pm.environment.set('clevelUserId', clevel.id);",
          "pm.test('admin user exists', () => pm.expect(rows.some(row => row.username === 'admin')).to.eql(true));"
        ]
      }),
      requestItem({
        name: "Admin Can Change C-Level Password To Same Known Value",
        method: "PATCH",
        path: "/api/v1/users/{{clevelUserId}}/password",
        body: { newPassword: "{{clevelPassword}}" },
        tests: [
          "pm.test('optional password-change smoke skipped unless clevelUserId is set', () => true);",
          "if (pm.environment.get('clevelUserId')) pm.response.to.have.status(204);"
        ]
      })
    ]
  },
  {
    name: "01 - Locations And Catalog",
    item: [
      requestItem({
        name: "List Locations",
        path: "/api/v1/inventory/locations",
        tests: [
          testStatus(200),
          "const rows = pm.response.json();",
          "const main = rows.find(row => row.locationType === 'MainWarehouse');",
          "const online = rows.find(row => row.locationType === 'Online');",
          "const retail = rows.find(row => row.locationType === 'SubWarehouse');",
          "pm.test('main, online, and retail locations exist', () => { pm.expect(main).to.exist; pm.expect(online).to.exist; pm.expect(retail).to.exist; });",
          "pm.environment.set('mainLocationId', main.id);",
          "pm.environment.set('onlineLocationId', online.id);",
          "pm.environment.set('retailLocationId', retail.id);"
        ]
      }),
      requestItem({
        name: "Create Category",
        method: "POST",
        path: "/api/v1/catalog/categories",
        body: { parentId: null, name: "Postman Lenses {{runId}}" },
        tests: [
          testStatus(201),
          "const json = pm.response.json(); pm.environment.set('categoryId', json.id);",
          "pm.test('category id saved', () => pm.expect(pm.environment.get('categoryId')).to.be.ok);"
        ]
      }),
      requestItem({
        name: "Create Brand",
        method: "POST",
        path: "/api/v1/catalog/brands",
        body: { name: "PM Brand {{runId}}" },
        tests: [
          testStatus(201),
          "const json = pm.response.json(); pm.environment.set('brandId', json.id);"
        ]
      }),
      requestItem({
        name: "Create Lens Product",
        method: "POST",
        path: "/api/v1/catalog/products",
        body: {
          categoryId: "{{categoryId}}",
          brandId: "{{brandId}}",
          name: "PM Lens {{runId}}",
          productType: "Lens",
          expiryType: "Batch",
          sealedExpiryDuration: null,
          sealedExpiryRate: null,
          openedExpiryDuration: null,
          piecesPerPack: 3,
          sellMode: "Both",
          clinicalParams: "{\"baseCurve\":\"8.6\",\"diameter\":\"14.2\"}",
          extendedAttributes: "{\"source\":\"postman\"}"
        },
        tests: [
          testStatus(201),
          "const json = pm.response.json(); pm.environment.set('productId', json.id);",
          "pm.test('product is active', () => pm.expect(pm.response.json().isActive).to.eql(true));"
        ]
      }),
      requestItem({
        name: "Create SKU",
        method: "POST",
        path: "/api/v1/catalog/products/{{productId}}/skus",
        body: {
          powerSign: "-",
          powerValue: 2.5,
          colorName: "{{runId}} Honey",
          size: null,
          barcode: "PM-{{runId}}"
        },
        tests: [
          testStatus(201),
          "const json = pm.response.json(); pm.environment.set('skuId', json.id); pm.environment.set('skuCode', json.skuCode);",
          "pm.test('sku code generated', () => pm.expect(json.skuCode).to.be.a('string').and.not.empty);"
        ]
      }),
      requestItem({
        name: "Product Detail Includes SKU",
        path: "/api/v1/catalog/products/{{productId}}",
        tests: [
          testStatus(200),
          "const json = pm.response.json();",
          "pm.test('detail contains created sku', () => pm.expect(json.skus.some(sku => sku.id === pm.environment.get('skuId'))).to.eql(true));"
        ]
      }),
      requestItem({ name: "Deactivate SKU", method: "PATCH", path: "/api/v1/catalog/skus/{{skuId}}/deactivate", tests: [testStatus(200)] }),
      requestItem({ name: "Reactivate SKU", method: "PATCH", path: "/api/v1/catalog/skus/{{skuId}}/reactivate", tests: [testStatus(200)] }),
      requestItem({
        name: "C-Level Can Read Catalog",
        path: "/api/v1/catalog/products?pageSize=5",
        token: "clevelToken",
        tests: [testStatus(200)]
      }),
      requestItem({
        name: "Accountant Cannot Read Catalog",
        path: "/api/v1/catalog/products?pageSize=5",
        token: "accountantToken",
        tests: [testStatus(403)]
      })
    ]
  },
  {
    name: "02 - CRM",
    item: [
      requestItem({
        name: "Create Merchant",
        method: "POST",
        path: "/api/v1/crm/merchants",
        body: {
          businessName: "PM Merchant {{runId}}",
          contactPersonName: "Nadia {{runId}}",
          phoneNumbers: ["01000000000"],
          email: "merchant{{runId}}@example.com",
          address: "Cairo",
          businessType: "Merchant",
          notes: "Created by Postman MVP flow"
        },
        tests: [
          testStatus(201),
          "const json = pm.response.json(); pm.environment.set('merchantId', json.id);"
        ]
      }),
      requestItem({
        name: "Add Merchant Note",
        method: "POST",
        path: "/api/v1/crm/merchants/{{merchantId}}/notes",
        body: { note: "Postman note {{runId}}" },
        tests: [testStatus(201)]
      }),
      requestItem({
        name: "Create Representative",
        method: "POST",
        path: "/api/v1/crm/representatives",
        body: {
          name: "PM Representative {{runId}}",
          phoneNumbers: ["01100000000"],
          email: "rep{{runId}}@example.com",
          type: "External",
          assignedLocationId: "{{mainLocationId}}",
          notes: "Created by Postman MVP flow"
        },
        tests: [
          testStatus(201),
          "const json = pm.response.json(); pm.environment.set('representativeId', json.id);"
        ]
      }),
      requestItem({
        name: "Merchant Detail",
        path: "/api/v1/crm/merchants/{{merchantId}}",
        tests: [
          testStatus(200),
          "const json = pm.response.json();",
          "pm.test('merchant note visible', () => pm.expect(json.notes.length).to.be.greaterThan(0));"
        ]
      })
    ]
  },
  {
    name: "03 - Inventory And Operations",
    item: [
      requestItem({
        name: "Create Inventory Receipt Operation",
        method: "POST",
        path: "/api/v1/operations",
        body: {
          operationType: "InventoryReceipt",
          destinationLocationId: "{{mainLocationId}}",
          receipt: { supplierName: "PM Supplier", invoiceNumber: "PM-INV-{{runId}}" },
          lines: [{ skuId: "{{skuId}}", packQuantity: 24, lotNumber: "PM-LOT-{{runId}}", expiryDate: "2028-06-01" }]
        },
        tests: [
          testStatus(201),
          "const json = pm.response.json(); pm.environment.set('receiptOperationId', json.id);"
        ]
      }),
      requestItem({ name: "Confirm Receipt", method: "POST", path: "/api/v1/operations/{{receiptOperationId}}/confirm", tests: [testStatus(204)] }),
      requestItem({ name: "Set Main Target", method: "PUT", path: "/api/v1/inventory/stock-balances/{{mainLocationId}}/{{skuId}}/target", body: { targetPacks: 8 }, tests: [testStatus(204)] }),
      requestItem({
        name: "Create Warehouse Transfer",
        method: "POST",
        path: "/api/v1/operations",
        body: {
          operationType: "WarehouseTransfer",
          sourceLocationId: "{{mainLocationId}}",
          destinationLocationId: "{{onlineLocationId}}",
          lines: [{ skuId: "{{skuId}}", packQuantity: 6 }]
        },
        tests: [
          testStatus(201),
          "const json = pm.response.json(); pm.environment.set('transferOperationId', json.id);"
        ]
      }),
      requestItem({ name: "Confirm Transfer Reserve", method: "POST", path: "/api/v1/operations/{{transferOperationId}}/confirm", tests: [testStatus(204)] }),
      requestItem({ name: "Ship Transfer", method: "POST", path: "/api/v1/operations/{{transferOperationId}}/ship", tests: [testStatus(204)] }),
      requestItem({ name: "Receive Transfer", method: "POST", path: "/api/v1/operations/{{transferOperationId}}/receive", tests: [testStatus(204)] }),
      requestItem({
        name: "Stock Options Main",
        path: "/api/v1/inventory/stock-options?locationId={{mainLocationId}}&skuId={{skuId}}&entryMode=Packs",
        tests: [
          testStatus(200),
          "const rows = pm.response.json();",
          "pm.test('main selected lot is available', () => pm.expect(rows.some(row => row.lotNumber === `PM-LOT-${pm.environment.get('runId')}`)).to.eql(true));"
        ]
      }),
      requestItem({
        name: "Stock Options Online Pieces",
        path: "/api/v1/inventory/stock-options?locationId={{onlineLocationId}}&skuId={{skuId}}&entryMode=Pieces",
        tests: [
          testStatus(200),
          "const rows = pm.response.json();",
          "pm.test('online pieces available', () => pm.expect(rows.reduce((sum, row) => sum + (row.pieceQuantity || 0), 0)).to.be.greaterThan(0));"
        ]
      }),
      requestItem({
        name: "Create Wholesale Cash Sale",
        method: "POST",
        path: "/api/v1/operations",
        body: {
          operationType: "WholesaleSale",
          sourceLocationId: "{{mainLocationId}}",
          merchantId: "{{merchantId}}",
          paymentMethod: "CashHandToHand",
          lines: [{ skuId: "{{skuId}}", packQuantity: 3, entryMode: "Packs", unitPrice: 125, lotNumber: "PM-LOT-{{runId}}", expiryDate: "2028-06-01" }]
        },
        tests: [
          testStatus(201),
          "const json = pm.response.json(); pm.environment.set('wholesaleOperationId', json.id);"
        ]
      }),
      requestItem({ name: "Reserve Wholesale Sale", method: "POST", path: "/api/v1/operations/{{wholesaleOperationId}}/confirm", tests: [testStatus(204)] }),
      requestItem({ name: "Ship Wholesale Sale", method: "POST", path: "/api/v1/operations/{{wholesaleOperationId}}/ship", tests: [testStatus(204)] }),
      requestItem({ name: "Complete Wholesale Sale", method: "POST", path: "/api/v1/operations/{{wholesaleOperationId}}/complete", tests: [testStatus(204)] }),
      requestItem({
        name: "Create Retail Piece Sale",
        method: "POST",
        path: "/api/v1/operations",
        body: {
          operationType: "RetailSale",
          sourceLocationId: "{{onlineLocationId}}",
          merchantId: "{{merchantId}}",
          buyerName: "Walk-in buyer {{runId}}",
          buyerPhone: "01200000000",
          paymentMethod: "CashHandToHand",
          lines: [{ skuId: "{{skuId}}", packQuantity: 0, pieceQuantity: 2, entryMode: "Pieces", unitPrice: 50, lotNumber: "PM-LOT-{{runId}}", expiryDate: "2028-06-01" }]
        },
        tests: [
          testStatus(201),
          "const json = pm.response.json(); pm.environment.set('retailOperationId', json.id);"
        ]
      }),
      requestItem({ name: "Reserve Retail Sale", method: "POST", path: "/api/v1/operations/{{retailOperationId}}/confirm", tests: [testStatus(204)] }),
      requestItem({ name: "Ship Retail Sale", method: "POST", path: "/api/v1/operations/{{retailOperationId}}/ship", tests: [testStatus(204)] }),
      requestItem({ name: "Complete Retail Sale", method: "POST", path: "/api/v1/operations/{{retailOperationId}}/complete", tests: [testStatus(204)] }),
      requestItem({
        name: "Inventory Balances Include SKU",
        path: "/api/v1/inventory/stock-balances?skuId={{skuId}}&includeZeroStock=true&pageSize=50",
        tests: [
          testStatus(200),
          "const json = pm.response.json();",
          "pm.test('stock balance rows exist', () => pm.expect(json.items.length).to.be.greaterThan(0));"
        ]
      })
    ]
  },
  {
    name: "04 - Payments",
    item: [
      requestItem({
        name: "Create Installment Sale",
        method: "POST",
        path: "/api/v1/operations",
        body: {
          operationType: "WholesaleSale",
          sourceLocationId: "{{mainLocationId}}",
          merchantId: "{{merchantId}}",
          paymentMethod: "Installment",
          lines: [{ skuId: "{{skuId}}", packQuantity: 2, entryMode: "Packs", unitPrice: 100, lotNumber: "PM-LOT-{{runId}}", expiryDate: "2028-06-01" }]
        },
        tests: [
          testStatus(201),
          "const json = pm.response.json(); pm.environment.set('installmentOperationId', json.id);"
        ]
      }),
      requestItem({ name: "Reserve Installment Sale", method: "POST", path: "/api/v1/operations/{{installmentOperationId}}/confirm", tests: [testStatus(204)] }),
      requestItem({ name: "Ship Installment Sale", method: "POST", path: "/api/v1/operations/{{installmentOperationId}}/ship", tests: [testStatus(204)] }),
      requestItem({ name: "Complete Installment Sale", method: "POST", path: "/api/v1/operations/{{installmentOperationId}}/complete", tests: [testStatus(204)] }),
      requestItem({
        name: "Find Payment Log",
        path: "/api/v1/payments?operationId={{installmentOperationId}}",
        tests: [
          testStatus(200),
          "const json = pm.response.json();",
          "pm.test('payment log created', () => pm.expect(json.items.length).to.eql(1));",
          "pm.environment.set('paymentLogId', json.items[0].id);"
        ]
      }),
      requestItem({
        name: "Assign Payment To Accountant",
        method: "POST",
        path: "/api/v1/payments/{{paymentLogId}}/assign",
        body: { accountantUserId: "{{accountantUserId}}" },
        tests: [testStatus(200)]
      }),
      requestItem({
        name: "Accountant Drafts Sub Log",
        method: "POST",
        path: "/api/v1/payments/{{paymentLogId}}/sub-logs",
        token: "accountantToken",
        body: { amount: 120, paymentMethod: "CashTransaction", dateReceived: "2026-07-05", notes: "Postman draft payment" },
        tests: [
          testStatus(201),
          "const json = pm.response.json();",
          "pm.environment.set('subLogId', json.subLogs[0].id);"
        ]
      }),
      requestItem({ name: "Admin Approves Sub Log", method: "POST", path: "/api/v1/payments/sub-logs/{{subLogId}}/approve", tests: [testStatus(200)] }),
      requestItem({
        name: "Merchant Balance",
        path: "/api/v1/payments/merchants/{{merchantId}}/balance",
        tests: [
          testStatus(200),
          "const json = pm.response.json();",
          "pm.test('balance response has totals', () => pm.expect(json.saleTotal).to.be.a('number'));"
        ]
      }),
      requestItem({
        name: "Create Merchant Credit Adjustment",
        method: "POST",
        path: "/api/v1/payments/adjustments",
        body: { merchantId: "{{merchantId}}", operationId: "{{installmentOperationId}}", adjustmentType: "MerchantCredit", amount: 10, notes: "Postman credit test" },
        tests: [testStatus(201)]
      })
    ]
  },
  {
    name: "05 - Return Change Write-Off Stocktake",
    item: [
      requestItem({
        name: "Create Return",
        method: "POST",
        path: "/api/v1/operations",
        body: {
          operationType: "Return",
          sourceLocationId: "{{mainLocationId}}",
          merchantId: "{{merchantId}}",
          paymentMethod: "CashHandToHand",
          lines: [{ skuId: "{{skuId}}", packQuantity: 1, entryMode: "Packs", unitPrice: 100, lotNumber: "PM-LOT-{{runId}}", expiryDate: "2028-06-01" }]
        },
        tests: [
          testStatus(201),
          "const json = pm.response.json(); pm.environment.set('returnOperationId', json.id);"
        ]
      }),
      requestItem({ name: "Confirm Return", method: "POST", path: "/api/v1/operations/{{returnOperationId}}/confirm", tests: [testStatus(204)] }),
      requestItem({
        name: "Create Change",
        method: "POST",
        path: "/api/v1/operations",
        body: {
          operationType: "Change",
          sourceLocationId: "{{mainLocationId}}",
          merchantId: "{{merchantId}}",
          paymentMethod: "CashHandToHand",
          lines: [
            { skuId: "{{skuId}}", section: "ChangeOut", packQuantity: 1, entryMode: "Packs", unitPrice: 100, lotNumber: "PM-LOT-{{runId}}", expiryDate: "2028-06-01" },
            { skuId: "{{skuId}}", section: "ChangeIn", packQuantity: 1, entryMode: "Packs", unitPrice: 100, lotNumber: "PM-LOT-{{runId}}", expiryDate: "2028-06-01" }
          ]
        },
        tests: [
          testStatus(201),
          "const json = pm.response.json(); pm.environment.set('changeOperationId', json.id);"
        ]
      }),
      requestItem({ name: "Confirm Change", method: "POST", path: "/api/v1/operations/{{changeOperationId}}/confirm", tests: [testStatus(204)] }),
      requestItem({
        name: "Create Write-Off",
        method: "POST",
        path: "/api/v1/operations",
        body: {
          operationType: "WriteOff",
          sourceLocationId: "{{mainLocationId}}",
          notes: "Damaged during Postman test",
          lines: [{ skuId: "{{skuId}}", packQuantity: 1, entryMode: "Packs", notes: "Damaged" }]
        },
        tests: [
          testStatus(201),
          "const json = pm.response.json(); pm.environment.set('writeOffOperationId', json.id);"
        ]
      }),
      requestItem({ name: "Confirm Write-Off", method: "POST", path: "/api/v1/operations/{{writeOffOperationId}}/confirm", tests: [testStatus(204)] }),
      requestItem({
        name: "Merchant Eligibility Ledger",
        path: "/api/v1/crm/merchants/{{merchantId}}/eligibility",
        tests: [
          testStatus(200),
          "const rows = pm.response.json();",
          "pm.test('eligibility rows returned', () => pm.expect(rows.length).to.be.greaterThan(0));"
        ]
      }),
      requestItem({
        name: "Create Stocktake",
        method: "POST",
        path: "/api/v1/stocktakes",
        body: { locationId: "{{mainLocationId}}", sessionDate: "2026-07-05T10:00:00", notes: "Postman stocktake {{runId}}" },
        tests: [
          testStatus(201),
          "const json = pm.response.json(); pm.environment.set('stocktakeId', json.id);"
        ]
      }),
      requestItem({
        name: "Save Stocktake Lines",
        method: "PUT",
        path: "/api/v1/stocktakes/{{stocktakeId}}/lines",
        body: {
          lines: [{ skuId: "{{skuId}}", lotNumber: "PM-LOT-{{runId}}", expiryDate: "2028-06-01", physicalCount: 0, lineNote: "Postman reconciliation test" }]
        },
        tests: [testStatus(200)]
      }),
      requestItem({ name: "Confirm Stocktake", method: "POST", path: "/api/v1/stocktakes/{{stocktakeId}}/confirm", tests: [testStatus(200)] })
    ]
  },
  {
    name: "06 - Reports Notifications Authorization",
    item: [
      requestItem({ name: "Stock Report", path: "/api/v1/reports/stock", tests: [testStatus(200)] }),
      requestItem({ name: "Stock CSV", path: "/api/v1/reports/stock.csv", tests: [testStatus(200), "pm.test('csv response', () => pm.expect(pm.response.headers.get('Content-Type')).to.include('text/csv'));" ] }),
      requestItem({ name: "Operations Report", path: "/api/v1/reports/operations", tests: [testStatus(200)] }),
      requestItem({ name: "Operations CSV", path: "/api/v1/reports/operations.csv", tests: [testStatus(200)] }),
      requestItem({ name: "Operation Bill PDF", path: "/api/v1/reports/operations/{{wholesaleOperationId}}/bill.pdf", tests: [testStatus(200), "pm.test('pdf response', () => pm.expect(pm.response.headers.get('Content-Type')).to.include('application/pdf'));" ] }),
      requestItem({ name: "Payments Report", path: "/api/v1/reports/payments", tests: [testStatus(200)] }),
      requestItem({ name: "Payments CSV", path: "/api/v1/reports/payments.csv", tests: [testStatus(200)] }),
      requestItem({ name: "Payment Receipt PDF", path: "/api/v1/reports/payments/{{paymentLogId}}/receipt.pdf", tests: [testStatus(200)] }),
      requestItem({ name: "Merchant Balances CSV", path: "/api/v1/reports/merchant-balances.csv", tests: [testStatus(200)] }),
      requestItem({ name: "Merchant Statement PDF", path: "/api/v1/reports/merchants/{{merchantId}}/statement.pdf", tests: [testStatus(200)] }),
      requestItem({ name: "Stocktake Summary PDF", path: "/api/v1/reports/stocktakes/{{stocktakeId}}/summary.pdf", tests: [testStatus(200)] }),
      requestItem({
        name: "Create Export Log",
        method: "POST",
        path: "/api/v1/reports/exports",
        body: { reportType: "PostmanFullMvp", generatedUrl: "/postman/generated/{{runId}}" },
        tests: [
          testStatus(201),
          "const json = pm.response.json(); pm.environment.set('exportLogId', json.id);"
        ]
      }),
      requestItem({ name: "List Export Logs", path: "/api/v1/reports/exports", tests: [testStatus(200)] }),
      requestItem({ name: "Run Low Stock Alerts", method: "POST", path: "/api/v1/alerts/run/low-stock", tests: [testStatus(200)] }),
      requestItem({ name: "List Notifications", path: "/api/v1/notifications?pageSize=50", tests: [testStatus(200)] }),
      requestItem({ name: "Unread Count", path: "/api/v1/notifications/unread-count", tests: [testStatus(200)] }),
      requestItem({ name: "Read All Notifications", method: "PATCH", path: "/api/v1/notifications/read-all", tests: [testStatus(200)] }),
      requestItem({
        name: "C-Level Cannot Mutate Operations",
        method: "POST",
        path: "/api/v1/operations",
        token: "clevelToken",
        body: { operationType: "InventoryReceipt", destinationLocationId: "{{mainLocationId}}", lines: [{ skuId: "{{skuId}}", packQuantity: 1 }] },
        tests: [testStatus(403)]
      }),
      requestItem({
        name: "Online Clerk Sees Own Location Only",
        path: "/api/v1/inventory/locations",
        token: "onlineClerkToken",
        tests: [
          testStatus(200),
          "const rows = pm.response.json();",
          "pm.test('online clerk has one location', () => pm.expect(rows.length).to.eql(1));"
        ]
      }),
      requestItem({
        name: "Accountant Cannot Read Stock Report",
        path: "/api/v1/reports/stock",
        token: "accountantToken",
        tests: [testStatus(403)]
      })
    ]
  }
];

const collection = {
  info: {
    name: "Lensee PRD-MVP Full API Smoke",
    description: "End-to-end Postman/Newman flow for auth, catalog, inventory, CRM, operations, payments, stocktake, reports, notifications, and role checks. Generated by scripts/generate-postman-collection.mjs.",
    schema: "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
  },
  variable: collectionVariables,
  event: [
    {
      listen: "prerequest",
      script: script([
        "if (!pm.environment.get('runId')) {",
        "  pm.environment.set('runId', Date.now().toString());",
        "}"
      ])
    }
  ],
  item: items
};

const environment = {
  name: "Lensee Local",
  values: [
    { key: "baseUrl", value: "http://localhost:5000", enabled: true },
    { key: "runId", value: defaultRunId, enabled: true },
    { key: "adminUsername", value: "admin", enabled: true },
    { key: "adminPassword", value: "Admin123!", enabled: true },
    { key: "clevelUsername", value: "clevel", enabled: true },
    { key: "clevelPassword", value: "CLevel123!", enabled: true },
    { key: "accountantUsername", value: "accountant", enabled: true },
    { key: "accountantPassword", value: "Accountant123!", enabled: true },
    { key: "onlineClerkUsername", value: "online_clerk", enabled: true },
    { key: "onlineClerkPassword", value: "Clerk123!", enabled: true },
    { key: "clevelUserId", value: "", enabled: true }
  ]
};

fs.writeFileSync(
  path.join(postmanDir, "Lensee_PRD_MVP_Full.postman_collection.json"),
  JSON.stringify(collection, null, 2)
);
fs.writeFileSync(
  path.join(postmanDir, "Lensee_Local.postman_environment.json"),
  JSON.stringify(environment, null, 2)
);

console.log("Generated Postman collection and environment in postman/.");
