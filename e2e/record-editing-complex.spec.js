const { test, expect } = require("@playwright/test");
const {
  installApiBase,
  login,
  logout,
  users,
  makeRunData,
  gotoRoute,
  expectNotice,
  selectOptionByText,
  openMerchantDetail,
  createOperationDraft,
  runLatestOperationAction,
  waitForStockOptions,
  apiRequest,
  apiJson,
  latestOperationId,
  expectOneTransitionSucceeds,
  expectNoNegativeStock
} = require("./support/helpers");

test.describe.configure({ mode: "serial" });

test.beforeEach(async ({ page }) => {
  await installApiBase(page);
  await login(page, users.admin);
});

async function seedMainStock(page, data, quantity = "20") {
  await ensureCoreDataApi(page, data);
  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "InventoryReceipt",
    skuText: data.product,
    quantity,
    lot: data.mainLot,
    expiry: data.expiry,
    supplier: `${data.runId} Supplier`,
    invoice: `${data.runId}-INV`
  });
  await runLatestOperationAction(page, "InventoryReceipt", /Confirm/i);
}

async function ensureCoreDataApi(page, data) {
  const category = await createApiCategory(page, data.category);
  const brand = await createApiBrand(page, data.brand);
  const product = await createApiProduct(page, data, category.id, brand.id);
  await createApiSku(page, product.id, data);
  await createApiMerchant(page, data);
  await createApiRepresentative(page, data);
  data.categoryId = category.id;
  data.brandId = brand.id;
  data.productId = product.id;
}

async function createApiCategory(page, name) {
  const { response, data } = await apiJson(page, "POST", "/api/v1/catalog/categories", { name, parentId: null });
  expect([200, 201]).toContain(response.status());
  return data;
}

async function createApiBrand(page, name) {
  const { response, data } = await apiJson(page, "POST", "/api/v1/catalog/brands", { name });
  expect([200, 201]).toContain(response.status());
  return data;
}

async function createApiProduct(page, data, categoryId, brandId) {
  const { response, data: product } = await apiJson(page, "POST", "/api/v1/catalog/products", {
    categoryId,
    brandId,
    name: data.product,
    productType: "Lens",
    expiryType: "Batch",
    sealedExpiryDuration: null,
    openedExpiryRate: null,
    openedExpiryDuration: null,
    piecesPerPack: 3,
    sellMode: "Both",
    clinicalParams: JSON.stringify({ duration: "6 months" }),
    extendedAttributes: null
  });
  expect([200, 201]).toContain(response.status());
  return product;
}

async function createApiSku(page, productId, data) {
  const { response, data: sku } = await apiJson(page, "POST", `/api/v1/catalog/products/${productId}/skus`, {
    powerSign: "+",
    powerValue: 1.25,
    colorName: data.skuColor,
    size: data.skuSize,
    barcode: data.barcode
  });
  expect([200, 201]).toContain(response.status());
  data.skuId = sku.id;
  data.skuCode = sku.skuCode;
  return sku;
}

async function createApiMerchant(page, data) {
  const { response, data: merchant } = await apiJson(page, "POST", "/api/v1/crm/merchants", {
    businessName: data.merchant,
    contactPersonName: data.merchantContact,
    phoneNumbers: ["01000000000"],
    address: `${data.runId} address`,
    notes: null
  });
  expect([200, 201]).toContain(response.status());
  data.merchantId = merchant.id;
  return merchant;
}

async function createApiRepresentative(page, data) {
  const { response, data: representative } = await apiJson(page, "POST", "/api/v1/crm/representatives", {
    name: data.representative,
    phoneNumbers: ["01111111111"],
    type: "External",
    notes: null
  });
  expect([200, 201]).toContain(response.status());
  data.representativeId = representative.id;
  return representative;
}

async function openLatestOperationForEdit(page, operationType, buttonName = /Edit/i) {
  await gotoRoute(page, "/operations");
  const showCompleted = page.locator("#operations-show-completed");
  if (await showCompleted.isVisible().catch(() => false)) {
    await showCompleted.check({ force: true }).catch(() => undefined);
  }
  const row = page.locator("#operation-rows tr", { hasText: operationType }).first();
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: buttonName }).click();
  await expect(page.locator("#op-type")).toHaveValue(operationType);
  await expect(page.locator("#operation-editor-mode")).toContainText(buttonName.source?.includes("Revise") ? /Revision/i : /Draft edit/i);
}

async function submitOperationEditor(page, expectedNotice) {
  await Promise.all([
    page.waitForResponse((response) =>
      response.url().includes("/api/v1/operations") &&
      ["PUT", "POST"].includes(response.request().method()),
      { timeout: 30_000 }),
    page.locator("#operation-submit-button").click()
  ]);
  await expectNotice(page, expectedNotice);
}

async function expectOperationEditorPrefilled(page, type, data, quantity = "2") {
  await expect(page.locator("#op-type")).toHaveValue(type);
  await expect(page.locator(".line-editor-row")).toHaveCount(1);
  const row = page.locator(".line-editor-row").first();
  await expect(row.locator(".op-line-qty")).toHaveValue(quantity);
  await expect(row.locator(".op-line-lot")).toHaveValue(data.mainLot);
  await expect(row.locator(".op-line-expiry")).toHaveValue(data.expiry);
  await expect(row.locator(".op-line-resolved")).toContainText(/Resolved SKU/i);
}

async function createInstallmentSale(page, data, amount = "100") {
  await seedMainStock(page, data, "10");
  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "WholesaleSale",
    skuText: data.product,
    quantity: "2",
    price: amount,
    stockText: data.mainLot,
    merchantText: data.merchant,
    paymentMethod: "Installment",
    sourceText: /Roxy|Main/i
  });
  await runLatestOperationAction(page, "WholesaleSale", /Confirm/i);
  await runLatestOperationAction(page, "WholesaleSale", /Ship/i);
  await runLatestOperationAction(page, "WholesaleSale", /Complete/i);
  return await latestOperationId(page, "WholesaleSale", "Completed");
}

async function latestPaymentForOperation(page, operationId) {
  const { response, data } = await apiJson(page, "GET", "/api/v1/payments?page=1&pageSize=100");
  expect(response.ok()).toBeTruthy();
  const items = Array.isArray(data) ? data : data?.items || data?.data || [];
  const match = items.find((item) => String(item.operationId) === String(operationId));
  expect(match, `Expected payment log for operation ${operationId}`).toBeTruthy();
  return match;
}

async function firstAccountantId(page) {
  const { response, data } = await apiJson(page, "GET", "/api/v1/users?page=1&pageSize=100");
  expect(response.ok()).toBeTruthy();
  const items = Array.isArray(data) ? data : data?.items || data?.data || [];
  const accountant = items.find((user) => user.username === users.accountant.username || user.role === "Accountant");
  expect(accountant).toBeTruthy();
  return accountant.id;
}

test("editing: catalog and CRM edits preserve historical operation snapshots while current profiles update", async ({ page }) => {
  const data = makeRunData("EDITCAT");
  await seedMainStock(page, data, "6");

  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "WholesaleSale",
    skuText: data.product,
    quantity: "1",
    price: "120",
    stockText: data.mainLot,
    merchantText: data.merchant,
    paymentMethod: "CashHandToHand",
    sourceText: /Roxy|Main/i
  });
  await runLatestOperationAction(page, "WholesaleSale", /Confirm/i);
  await runLatestOperationAction(page, "WholesaleSale", /Ship/i);
  await runLatestOperationAction(page, "WholesaleSale", /Complete/i);

  await gotoRoute(page, "/catalog");
  await page.locator("#category-list [data-category-id]", { hasText: data.category }).first().click();
  await page.locator("#category-name").fill(`${data.category} Updated`);
  await page.locator("#category-form button[type='submit']").click();
  await expectNotice(page, /Category saved/i);
  await expect(page.locator("#category-list")).toContainText(`${data.category} Updated`);

  await page.locator("#brand-list [data-brand-id]", { hasText: data.brand }).first().click();
  await page.locator("#brand-name").fill(`${data.brand} Updated`);
  await page.locator("#brand-form button[type='submit']").click();
  await expectNotice(page, /Brand saved/i);
  await expect(page.locator("#brand-list")).toContainText(`${data.brand} Updated`);

  await page.locator("#catalog-search").fill(data.product);
  await page.locator("#catalog-products tr", { hasText: data.product }).first().click();
  await page.locator("[data-toggle-sku]").first().click();
  await expect(page.locator("#catalog-detail")).toContainText(/Inactive|Reactivate/i);

  await gotoRoute(page, "/operations");
  await page.locator("#operations-show-completed").check({ force: true }).catch(() => undefined);
  await page.locator("#operation-rows tr", { hasText: "WholesaleSale" }).first().getByRole("button", { name: /Show|Details/i }).click();
  await expect(page.locator(".operation-detail").first()).toContainText(data.product);
  await expect(page.locator(".operation-detail").first()).toContainText(data.merchant);

  await gotoRoute(page, "/crm");
  await page.locator("#merchant-rows tr", { hasText: data.merchant }).first().getByRole("button", { name: /Edit/i }).click();
  const updatedMerchant = `${data.merchant} Updated`;
  await page.locator("#merchant-name").fill(updatedMerchant);
  await page.locator("#merchant-form button[type='submit']").click();
  await expect(page.locator("#merchant-rows")).toContainText(updatedMerchant);
  await openMerchantDetail(page, { ...data, merchant: updatedMerchant });
  await expect(page.locator("#merchant-detail-panel")).toContainText(/Recent operations|Balance|Eligibility/i);
  await page.locator("#merchant-rows tr", { hasText: updatedMerchant }).first().getByRole("button", { name: /Add note/i }).click();
  await page.locator(".dialog-input").fill(`${data.runId} post-edit note`);
  await page.locator(".dialog-card").getByRole("button", { name: /Continue/i }).click();
  await expectNotice(page, /Note added/i);
});

test("editing: operation drafts and shipped revisions prefill lines, preserve route, and require revision reasons", async ({ page }) => {
  const data = makeRunData("EDITOPS");
  await seedMainStock(page, data, "20");

  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "WholesaleSale",
    skuText: data.product,
    quantity: "2",
    price: "110",
    stockText: data.mainLot,
    merchantText: data.merchant,
    paymentMethod: "Installment",
    sourceText: /Roxy|Main/i
  });

  await openLatestOperationForEdit(page, "WholesaleSale", /Edit/i);
  await expectOperationEditorPrefilled(page, "WholesaleSale", data, "2");
  await expect(page.locator("#op-merchant option:checked")).toContainText(data.merchant);
  await expect(page.locator("#op-payment")).toHaveValue("Installment");
  await page.locator(".line-editor-row").first().locator(".op-line-qty").fill("3");
  await submitOperationEditor(page, /Draft updated/i);

  await openLatestOperationForEdit(page, "WholesaleSale", /Edit/i);
  await expectOperationEditorPrefilled(page, "WholesaleSale", data, "3");

  await runLatestOperationAction(page, "WholesaleSale", /Confirm/i);
  await openLatestOperationForEdit(page, "WholesaleSale", /Revise/i);
  await expectOperationEditorPrefilled(page, "WholesaleSale", data, "3");
  await expect(page.locator("#op-revision-reason-field")).toBeVisible();
  await page.locator("#operation-submit-button").click();
  await expectNotice(page, /Revision reason is required/i);
  await page.locator("#op-revision-reason").fill(`${data.runId} quantity audit revision`);
  await page.locator(".line-editor-row").first().locator(".op-line-qty").fill("2");
  await submitOperationEditor(page, /Operation revised/i);

  await runLatestOperationAction(page, "WholesaleSale", /Ship/i);
  await openLatestOperationForEdit(page, "WholesaleSale", /Revise/i);
  await expectOperationEditorPrefilled(page, "WholesaleSale", data, "2");
  await page.locator("#op-revision-reason").fill(`${data.runId} shipped sale correction`);
  await page.locator(".line-editor-row").first().locator(".op-line-qty").fill("1");
  await submitOperationEditor(page, /Operation revised/i);

  await page.locator("#operations-show-completed").check({ force: true }).catch(() => undefined);
  const row = page.locator("#operation-rows tr", { hasText: "WholesaleSale" }).first();
  await row.getByRole("button", { name: /Show|Details/i }).click();
  await expect(page.locator(".operation-detail").first()).toContainText(/Current version|v\d+|Created by|Confirmed by/i);

  await createOperationDraft(page, {
    type: "WarehouseTransfer",
    skuText: data.product,
    quantity: "1",
    stockText: data.mainLot,
    destinationText: /Retail|Online|Mohamed/i
  });
  await openLatestOperationForEdit(page, "WarehouseTransfer", /Edit/i);
  const destination = await page.locator("#op-destination").inputValue();
  await page.locator("#op-add-line").click();
  await expect(page.locator("#op-destination")).toHaveValue(destination);
  await expect(page.locator(".line-editor-row")).toHaveCount(2);
});

test("editing: payment reassignment, rejection loop, approval, completed lock, and balance checks", async ({ page }) => {
  const data = makeRunData("EDITPAY");
  const operationId = await createInstallmentSale(page, data, "150");
  const payment = await latestPaymentForOperation(page, operationId);
  const accountantId = await firstAccountantId(page);

  await gotoRoute(page, "/payments");
  await selectOptionByText(page.locator("#payment-accountant"), /\(accountant\)/i);
  await page.locator("#payment-rows tr", { hasText: "Installment" }).first().getByRole("button", { name: "Assign" }).click();
  await expectNotice(page, /accountant queue|assigned/i);
  const reassign = await apiRequest(page, "POST", `/api/v1/payments/${payment.id}/assign`, { accountantUserId: accountantId });
  expect([200, 204]).toContain(reassign.status());

  await logout(page);
  await login(page, users.accountant);
  await gotoRoute(page, "/payments");
  await page.locator("#payment-rows tr", { hasText: "Installment" }).first().getByRole("button", { name: "Use" }).click();
  await page.locator("#payment-amount").fill("50");
  await page.locator("#payment-method").selectOption("CashTransaction");
  await page.locator("#payment-date").fill("2026-07-09");
  await page.locator("#payment-notes").fill(`${data.runId} draft to reject`);
  await page.locator("#payment-sublog-form button[type='submit']").click();
  await expectNotice(page, /Payment sub-log drafted/i);

  await logout(page);
  await login(page, users.admin);
  await gotoRoute(page, "/payments");
  await page.locator("#payment-rows tr", { hasText: "Installment" }).first().getByRole("button", { name: "Details" }).click();
  await page.locator("[data-sublog-reject]").first().click();
  await page.locator(".dialog-input").fill(`${data.runId} rejected for test`);
  await page.locator(".dialog-card").getByRole("button", { name: /Continue/i }).click();
  await expectNotice(page, /Payment rejected/i);
  await expect(page.locator("#payment-detail-panel, #payment-rows")).toContainText(/Rejected/i);

  await logout(page);
  await login(page, users.accountant);
  await gotoRoute(page, "/payments");
  await page.locator("#payment-rows tr", { hasText: "Installment" }).first().getByRole("button", { name: "Use" }).click();
  await page.locator("#payment-amount").fill("300");
  await page.locator("#payment-method").selectOption("CashTransaction");
  await page.locator("#payment-date").fill("2026-07-09");
  await page.locator("#payment-notes").fill(`${data.runId} final approved`);
  await page.locator("#payment-sublog-form button[type='submit']").click();
  await expectNotice(page, /Payment sub-log drafted/i);

  await logout(page);
  await login(page, users.admin);
  await gotoRoute(page, "/payments");
  await page.locator("#payment-rows tr", { hasText: "Installment" }).first().getByRole("button", { name: "Details" }).click();
  await page.locator("[data-sublog-approve]").first().click();
  await expectNotice(page, /Payment approved/i);
  await expect(page.locator("#payment-rows tr", { hasText: "Installment" }).first()).toContainText("Completed");
  await expect(page.locator("#payment-rows tr", { hasText: "Completed" }).first().getByRole("button", { name: "Assign" })).toHaveCount(0);
  const assignCompleted = await apiRequest(page, "POST", `/api/v1/payments/${payment.id}/assign`, { accountantUserId: accountantId });
  expect([400, 409, 422]).toContain(assignCompleted.status());

  await selectOptionByText(page.locator("#payment-merchant"), data.merchant);
  await page.locator("#load-merchant-balance").click();
  await expect(page.locator("#merchant-balance-panel")).toContainText(/Balance|Payments|Completed/i);
});

test("editing: stocktake draft lines can be revised before confirmation and lock after confirmation", async ({ page }) => {
  const data = makeRunData("EDITSTK");
  await seedMainStock(page, data, "5");

  await gotoRoute(page, "/stocktakes");
  await selectOptionByText(page.locator("#stocktake-location"), /Roxy \(Main\)/i);
  await page.locator("#stocktake-notes").fill(`${data.runId} editable stocktake`);
  await page.locator("#stocktake-create-form button[type='submit']").click();
  await expectNotice(page, /Stocktake session opened/i);

  await selectOptionByText(page.locator(".stocktake-line-sku").first(), data.product);
  await page.locator(".stocktake-line-lot").first().fill(data.mainLot);
  await page.locator(".stocktake-line-expiry").first().fill(data.expiry);
  await page.locator(".stocktake-line-count").first().fill("4");
  await page.locator(".stocktake-line-note").first().fill("Initial count");
  await page.locator("#stocktake-lines-form button[type='submit']").click();
  await expect(page.locator("#stocktake-detail")).toContainText(data.mainLot);

  await page.locator(".stocktake-line-count").first().fill("3");
  await page.locator(".stocktake-line-note").first().fill("Corrected count before confirm");
  await page.locator("#stocktake-lines-form button[type='submit']").click();
  await expect(page.locator("#stocktake-detail")).toContainText("Corrected count before confirm");

  await page.locator("#stocktake-confirm").click();
  await expect(page.locator("#stocktake-detail")).toContainText(/Confirmed|confirmed/i);
  await expect(page.locator("#stocktake-lines-form")).toHaveCount(0);
  await expectNoNegativeStock(page);
});

test("editing: stale operations and double transitions fail safely without negative stock", async ({ page }) => {
  const data = makeRunData("EDITRACE");
  await seedMainStock(page, data, "3");

  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "WarehouseTransfer",
    skuText: data.product,
    quantity: "2",
    stockText: data.mainLot,
    destinationText: /Retail|Online|Mohamed/i
  });
  const transferId = await latestOperationId(page, "WarehouseTransfer", "Draft");
  await expectOneTransitionSucceeds(page, [
    apiRequest(page, "POST", `/api/v1/operations/${transferId}/confirm`),
    apiRequest(page, "POST", `/api/v1/operations/${transferId}/confirm`)
  ]);

  const invalidDraftEdit = await apiRequest(page, "PUT", `/api/v1/operations/${transferId}`, {
    operationType: "WarehouseTransfer",
    destinationLocationId: null,
    lines: []
  });
  expect([400, 409, 422]).toContain(invalidDraftEdit.status());

  await expect((await apiRequest(page, "POST", `/api/v1/operations/${transferId}/ship`)).ok()).toBeTruthy();
  await expectOneTransitionSucceeds(page, [
    apiRequest(page, "POST", `/api/v1/operations/${transferId}/receive`),
    apiRequest(page, "POST", `/api/v1/operations/${transferId}/receive`)
  ]);
  await expectNoNegativeStock(page);
});
