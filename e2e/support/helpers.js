const { expect } = require("@playwright/test");

const apiBaseUrl = process.env.LENSEE_E2E_API_URL || "http://127.0.0.1:55000";
const defaultE2ePassword = "E2E-only-not-production-2026!";

const users = {
  admin: { username: process.env.LENSEE_E2E_ADMIN_USER || "e2e_admin", password: process.env.LENSEE_E2E_ADMIN_PASSWORD || defaultE2ePassword },
  erpAdmin: { username: process.env.LENSEE_E2E_ERP_ADMIN_USER || "e2e_erp_admin", password: process.env.LENSEE_E2E_ERP_ADMIN_PASSWORD || defaultE2ePassword },
  clevel: { username: process.env.LENSEE_E2E_CLEVEL_USER || "e2e_clevel", password: process.env.LENSEE_E2E_CLEVEL_PASSWORD || defaultE2ePassword },
  accountant: { username: process.env.LENSEE_E2E_ACCOUNTANT_USER || "e2e_accountant", password: process.env.LENSEE_E2E_ACCOUNTANT_PASSWORD || defaultE2ePassword },
  clerk: { username: process.env.LENSEE_E2E_CLERK_USER || "e2e_roxy_clerk", password: process.env.LENSEE_E2E_CLERK_PASSWORD || defaultE2ePassword },
  roxyClerk: { username: process.env.LENSEE_E2E_ROXY_CLERK_USER || "e2e_roxy_clerk", password: process.env.LENSEE_E2E_ROXY_CLERK_PASSWORD || defaultE2ePassword },
  retailClerk: { username: process.env.LENSEE_E2E_RETAIL_CLERK_USER || "e2e_retail_clerk", password: process.env.LENSEE_E2E_RETAIL_CLERK_PASSWORD || defaultE2ePassword },
  onlineClerk: { username: process.env.LENSEE_E2E_ONLINE_CLERK_USER || "e2e_online_clerk", password: process.env.LENSEE_E2E_ONLINE_CLERK_PASSWORD || defaultE2ePassword }
};

function makeRunData(prefix = "E2E") {
  const runId = `${prefix}-${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 6)}`;
  return {
    runId,
    category: `${runId} Category`,
    brand: `${runId} Brand`,
    product: `${runId} Lens عميل <script>`,
    solution: `${runId} Solution`,
    skuColor: `${runId} Honey`,
    skuSize: `M${Date.now().toString(36).slice(-4).toUpperCase()}`,
    solutionSize: `${Date.now().toString().slice(-3)}ml`,
    barcode: `BC${Date.now()}${Math.floor(Math.random() * 1000)}`,
    merchant: `${runId} Merchant عميل`,
    merchantContact: "E2E Contact",
    representative: `${runId} Representative`,
    mainLot: `${runId}-MAIN`,
    secondLot: `${runId}-SECOND`,
    badLot: `${runId}-NOT-SOLD`,
    expiry: "2031-06-01",
    secondExpiry: "2032-01-01"
  };
}

async function installApiBase(page) {
  await page.addInitScript((value) => {
    window.localStorage.setItem("lensee.apiBase", value);
    window.localStorage.setItem("lensee.language", "en");
  }, apiBaseUrl);
  page.on("pageerror", (error) => {
    throw error;
  });
  page.on("response", (response) => {
    if (response.url().startsWith(apiBaseUrl) && response.status() >= 500) {
      throw new Error(`Unexpected API ${response.status()} from ${response.request().method()} ${response.url()}`);
    }
  });
}

async function gotoLogin(page) {
  await page.goto("/#/login");
  await expect(page.locator("#login-form")).toBeVisible();
}

async function login(page, credential = users.admin) {
  await gotoLogin(page);
  await page.locator("#username").fill(credential.username);
  await page.locator("#password").fill(credential.password);
  await page.locator("#login-submit").click();
  try {
    await page.waitForURL(/#\/dashboard/, { timeout: 25_000 });
  } catch (error) {
    const loginError = await page.locator("#login-error").innerText().catch(() => "");
    throw new Error(loginError || error.message);
  }
  await expect(page.locator("#page-title")).toContainText("Overview");
}

async function logout(page) {
  await page.locator("#logout-button").click();
  await expect(page.locator("#login-form")).toBeVisible();
}

async function gotoRoute(page, route) {
  await page.goto(`/#${route}`);
  await expect(page.locator("#view")).toBeVisible();
}

async function expectNotice(page, text) {
  await expect(page.locator("#notification-area")).toContainText(text, { timeout: 20_000 });
}

async function selectOptionByText(select, textOrRegex) {
  await expect(select).toBeVisible();
  const source = textOrRegex instanceof RegExp ? textOrRegex.source : escapeRegex(String(textOrRegex));
  const flags = textOrRegex instanceof RegExp ? textOrRegex.flags : "i";
  await expect.poll(async () => {
    return await select.locator("option").evaluateAll((options, args) => {
      const re = new RegExp(args.source, args.flags);
      return Array.from(options).some((option) => re.test(option.textContent || ""));
    }, { source, flags });
  }, { timeout: 20_000 }).toBeTruthy();
  const value = await select.locator("option").evaluateAll((options, args) => {
    const re = new RegExp(args.source, args.flags);
    const match = Array.from(options).find((option) => re.test(option.textContent || ""));
    return match ? match.value : "";
  }, { source, flags });
  await select.selectOption(value);
}

async function createCatalogFixture(page, data) {
  await gotoRoute(page, "/catalog");
  await page.locator("#catalog-search").fill(data.product);
  if (await page.locator("#catalog-products tr", { hasText: data.product }).first().isVisible().catch(() => false)) {
    return;
  }

  await page.locator("#category-name").fill(data.category);
  await page.locator("#category-form button[type='submit']").click();
  await expectNotice(page, /Category saved/i);

  await page.locator("#brand-name").fill(data.brand);
  await page.locator("#brand-form button[type='submit']").click();
  await expectNotice(page, /Brand saved/i);

  await page.locator("#product-name").fill(data.product);
  await page.locator("#product-type").selectOption("Lens");
  await selectOptionByText(page.locator("#product-category"), data.category);
  await selectOptionByText(page.locator("#product-brand"), data.brand);
  await page.locator("#product-sell-mode").selectOption("Both");
  await page.locator("#product-pieces").fill("3");
  await page.locator("#product-duration-value").fill("6");
  await page.locator("#product-form button[type='submit']").click();
  await expectNotice(page, /Product saved/i);

  await page.locator("#catalog-search").fill(data.product);
  await page.locator("#catalog-products tr", { hasText: data.product }).first().click();
  await createSkuForSelectedProduct(page, data);
}

async function createSkuForSelectedProduct(page, data) {
  await page.locator("#sku-power-sign").selectOption("+");
  await page.locator("#sku-power-value").fill("1.25");
  await page.locator("#sku-color").fill(data.skuColor);
  await page.locator("#sku-size").fill(data.skuSize);
  await page.locator("#sku-barcode").fill(data.barcode);
  await page.locator("#sku-form button[type='submit']").click();
  await expectNotice(page, /SKU saved/i);
}

async function createCrmFixture(page, data) {
  await gotoRoute(page, "/crm");
  if (!await page.locator("#merchant-rows tr", { hasText: data.merchant }).first().isVisible().catch(() => false)) {
    await page.locator("#merchant-name").fill(data.merchant);
    await page.locator("#merchant-contact").fill(data.merchantContact);
    await page.locator("#merchant-phone").fill("01000000000");
    await page.locator("#merchant-form button[type='submit']").click();
    await expectNotice(page, /Merchant (created|saved)/i);
  }
  if (!await page.locator("#rep-rows tr", { hasText: data.representative }).first().isVisible().catch(() => false)) {
    await page.locator("#rep-name").fill(data.representative);
    await page.locator("#rep-phone").fill("01111111111");
    await page.locator("#rep-form button[type='submit']").click();
    await expectNotice(page, /Representative (created|saved)/i);
  }
}

async function ensureCoreData(page, data) {
  await createCatalogFixture(page, data);
  await createCrmFixture(page, data);
}

async function openMerchantDetail(page, data) {
  const row = page.locator("#merchant-rows tr", { hasText: data.merchant }).first();
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: /Detail|Batch history/i }).first().click();
}

async function resetOperationEditor(page) {
  await closeBlockingDialogIfPresent(page);
  const reset = page.locator("#operation-editor-reset");
  if (await reset.isVisible().catch(() => false)) {
    await reset.click();
    await page.waitForTimeout(100);
  }
}

async function createOperationDraft(page, options) {
  await resetOperationEditor(page);
  await fillOperationDraftForm(page, options);
  if (await page.locator("#op-type").inputValue() !== options.type) {
    await fillOperationDraftForm(page, options);
  }
  const [response] = await Promise.all([
    page.waitForResponse((value) => {
      if (!value.url().includes("/api/v1/operations") ||
        value.request().method() !== "POST" ||
        value.url().includes("/confirm") ||
        value.url().includes("/ship") ||
        value.url().includes("/receive") ||
        value.url().includes("/complete") ||
        value.url().includes("/cancel")) {
        return false;
      }
      const body = value.request().postDataJSON();
      return body?.operationType === options.type;
    }, { timeout: 30_000 }),
    page.locator("#operation-submit-button").click()
  ]);
  if (!response.ok()) {
    throw new Error(`Operation draft create failed with ${response.status()}: ${await response.text()}`);
  }
  const created = await response.json();
  expect(created.operationType).toBe(options.type);
  await expect(page.locator("#operation-rows")).toContainText(created.operationNumber, { timeout: 20_000 });
  return created;
}

async function createSupplyReceipt(page, options) {
  await gotoRoute(page, "/supply");
  await expect(page.locator("#supply-form")).toBeVisible();
  await page.locator("#supply-reset").click();
  await page.locator("#supply-supplier").fill(options.supplier || "E2E Supplier");
  if (options.invoice) await page.locator("#supply-invoice").fill(options.invoice);
  const row = page.locator(".supply-line-row").first();
  await selectSupplyLineSku(row, options.skuText);
  await row.locator(".supply-line-qty").fill(options.quantity || "1");
  if (options.price) await row.locator(".supply-line-price").fill(options.price);
  if (options.lot) await row.locator(".supply-line-lot").fill(options.lot);
  if (options.expiry) await row.locator(".supply-line-expiry").fill(options.expiry);
  const [createResponse] = await Promise.all([
    page.waitForResponse((response) =>
      response.url().includes("/api/v1/supply/shipments") &&
      response.request().method() === "POST" &&
      response.status() === 201),
    page.locator("#supply-form button[type='submit']").click()
  ]);
  const shipment = await createResponse.json();
  await expect(page.locator("#supply-rows")).toContainText(shipment.shipmentNumber, { timeout: 20_000 });
  await page.locator("#supply-rows tr", { hasText: shipment.shipmentNumber }).first().click();
  await expect(page.locator("#supply-detail")).toContainText(shipment.shipmentNumber);
  await page.locator("#supply-confirm").click();
  await expect(page.locator("#notification-area")).toContainText(/received into inventory/i);
  await expect(page.locator("#supply-detail")).toContainText(/Received/i);
  return shipment;
}

async function selectSupplyLineSku(row, textOrRegex) {
  const search = row.locator(".supply-line-search");
  const hiddenSku = row.locator(".supply-line-sku");
  const results = row.locator(".op-line-search-results");
  await expect(search).toBeVisible();
  const query = textOrRegex instanceof RegExp ? textOrRegex.source.replace(/\\/g, "") : String(textOrRegex);
  await search.fill(query);
  await expect(results).toBeVisible();
  const result = results.locator(".op-line-search-result:not([disabled])", { hasText: textOrRegex }).first();
  if (await result.isVisible().catch(() => false)) {
    await result.click();
  } else {
    await results.locator(".op-line-search-result:not([disabled])").first().click();
  }
  await expect(hiddenSku).not.toHaveValue("");
}

async function fillOperationDraftForm(page, options) {
  await page.locator("#op-type").selectOption(options.type);
  await expect(page.locator("#op-type")).toHaveValue(options.type);
  await page.waitForTimeout(250);
  await selectOptionByTextIfEnabled(page.locator("#op-source"), options.sourceText);
  await selectOptionByTextIfEnabled(page.locator("#op-destination"), options.destinationText);
  await selectOptionByTextIfEnabled(page.locator("#op-merchant"), options.merchantText);
  await selectOptionByTextIfEnabled(page.locator("#op-representative"), options.representativeText);
  await selectValueIfEnabled(page.locator("#op-payment"), options.paymentMethod);
  await fillIfEnabled(page.locator("#op-buyer"), options.buyerName);
  if (options.supplier) await page.locator("#op-supplier").fill(options.supplier);
  if (options.invoice) await page.locator("#op-invoice").fill(options.invoice);
  await fillFirstOperationLine(page, options);
}

async function selectValueIfEnabled(select, value) {
  if (!value) {
    return;
  }

  await expect(select).toBeVisible();
  if (await select.isDisabled()) {
    return;
  }

  await select.selectOption(value);
}

async function fillIfEnabled(input, value) {
  if (!value) {
    return;
  }

  await expect(input).toBeVisible();
  if (await input.isDisabled()) {
    return;
  }

  await input.fill(value);
}

async function selectOptionByTextIfEnabled(select, textOrRegex) {
  if (!textOrRegex) {
    return;
  }

  await expect(select).toBeVisible();
  if (await select.isDisabled()) {
    return;
  }

  await selectOptionByText(select, textOrRegex);
}

async function fillFirstOperationLine(page, options) {
  const row = page.locator(".line-editor-row").first();
  await selectOperationLineSku(row, options.skuText);
  if (options.quantity) await row.locator(".op-line-qty").fill(options.quantity);
  if (options.price) await row.locator(".op-line-price").fill(options.price);
  if (options.stockText) {
    await waitForStockOptions(row, options.stockText);
    await selectOptionByText(row.locator(".op-line-stock-option"), options.stockText);
  }
  if (options.lot) await row.locator(".op-line-lot").fill(options.lot);
  if (options.expiry) await row.locator(".op-line-expiry").fill(options.expiry);
}

async function selectOperationLineSku(row, textOrRegex) {
  const hiddenSku = row.locator(".op-line-sku");
  const legacySelect = hiddenSku.locator("xpath=.");
  if (await legacySelect.evaluate((element) => element.tagName === "SELECT").catch(() => false)) {
    await selectOptionByText(legacySelect, textOrRegex);
    return;
  }

  const search = row.locator(".op-line-search");
  const results = row.locator(".op-line-search-results");
  await expect(search).toBeVisible();
  const query = textOrRegex instanceof RegExp ? textOrRegex.source.replace(/\\/g, "") : String(textOrRegex);
  await search.fill(query);
  await expect(results).toBeVisible();
  const result = results.locator(".op-line-search-result:not([disabled])", { hasText: textOrRegex }).first();
  if (await result.isVisible().catch(() => false)) {
    await result.click();
  } else {
    await results.locator(".op-line-search-result:not([disabled])").first().click();
  }
  await expect(hiddenSku).not.toHaveValue("");
  await expect(row.locator(".op-line-resolved")).toContainText(/Resolved SKU/i);
}

async function waitForStockOptions(row, textOrRegex) {
  const source = textOrRegex instanceof RegExp ? textOrRegex.source : escapeRegex(String(textOrRegex));
  const flags = textOrRegex instanceof RegExp ? textOrRegex.flags : "i";
  await expect.poll(async () => {
    return await row.locator(".op-line-stock-option option").evaluateAll((options, args) => {
      const re = new RegExp(args.source, args.flags);
      return Array.from(options).some((option) => re.test(option.textContent || ""));
    }, { source, flags });
  }, { timeout: 25_000 }).toBeTruthy();
}

async function runLatestOperationAction(page, operationType, labelRegex) {
  const showCompleted = page.locator("#operations-show-completed");
  if (await showCompleted.isVisible().catch(() => false)) {
    await showCompleted.check({ force: true }).catch(() => undefined);
  }
  const row = page.locator("#operation-rows tr", { hasText: operationType }).first();
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: labelRegex }).click();
  await page.waitForTimeout(250).catch(() => undefined);
}

async function closeBlockingDialogIfPresent(page) {
  const dialog = page.locator(".dialog-overlay").last();
  if (!await dialog.isVisible().catch(() => false)) return;
  const confirm = dialog.locator("[data-dialog-confirm]");
  const cancel = dialog.locator("[data-dialog-cancel]");
  if (await confirm.isVisible().catch(() => false)) await confirm.click();
  else if (await cancel.isVisible().catch(() => false)) await cancel.click();
  else await page.keyboard.press("Escape").catch(() => undefined);
  await expect(dialog).toBeHidden({ timeout: 10_000 }).catch(() => undefined);
}

async function createChangeDraft(page, data) {
  await page.locator("#op-type").selectOption("Change");
  await selectOptionByText(page.locator("#op-source"), /Roxy|Main/i);
  await selectOptionByText(page.locator("#op-merchant"), data.merchant);
  await page.locator("#op-payment").selectOption("CashHandToHand");
  await fillFirstOperationLine(page, {
    skuText: data.product,
    quantity: "1",
    lot: data.mainLot,
    expiry: data.expiry,
    price: "100"
  });
  await page.locator("#op-add-line").click();
  const second = page.locator(".line-editor-row").nth(1);
  await second.locator(".op-line-section").selectOption("ChangeIn");
  await selectOperationLineSku(second, data.product);
  await second.locator(".op-line-qty").fill("1");
  await second.locator(".op-line-price").fill("100");
  await page.locator("#operation-submit-button").click();
  await expect(page.locator("#operation-rows")).toContainText("Change");
}

async function getAuth(page) {
  return await page.evaluate(() => window.__lenseeGetAuth?.() || null);
}

async function apiRequest(page, method, path, body) {
  const auth = await getAuth(page);
  const response = await page.request.fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      "Content-Type": "application/json",
      ...(auth?.accessToken ? { Authorization: `Bearer ${auth.accessToken}` } : {})
    },
    data: body
  });
  return response;
}

async function expectApiForbidden(page, method, path, body) {
  const response = await apiRequest(page, method, path, body);
  expect([401, 403]).toContain(response.status());
}

async function apiJson(page, method, path, body) {
  const response = await apiRequest(page, method, path, body);
  const text = await response.text();
  let data = null;
  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      data = text;
    }
  }
  return { response, data };
}

async function latestOperationId(page, operationType, status) {
  const { response, data } = await apiJson(page, "GET", "/api/v1/operations?page=1&pageSize=50&includeCompleted=true");
  expect(response.ok()).toBeTruthy();
  const items = Array.isArray(data) ? data : data?.items || data?.data || [];
  const match = items.find((operation) =>
    operation.operationType === operationType && (!status || operation.status === status));
  expect(match, `Expected latest operation ${operationType}${status ? ` in ${status}` : ""}`).toBeTruthy();
  return match.id;
}

async function expectOneTransitionSucceeds(page, requests) {
  const responses = await Promise.all(requests);
  const successCount = responses.filter((response) => response.ok()).length;
  const rejectedCount = responses.filter((response) => [400, 401, 403, 404, 409, 422].includes(response.status())).length;
  expect(successCount).toBe(1);
  expect(rejectedCount).toBe(requests.length - 1);
  return responses;
}

async function getStockBalances(page) {
  const { response, data } = await apiJson(page, "GET", "/api/v1/inventory/stock-balances?page=1&pageSize=500");
  expect(response.ok()).toBeTruthy();
  return Array.isArray(data) ? data : data?.items || data?.data || [];
}

async function expectNoNegativeStock(page) {
  const balances = await getStockBalances(page);
  for (const balance of balances) {
    for (const key of ["availableQty", "reservedInWarehouseQty", "reservedWithRepQty", "totalPhysicalQty", "loosePiecesQty"]) {
      if (typeof balance[key] === "number") {
        expect(balance[key], `${key} should not be negative for ${balance.skuCode || balance.skuId}`).toBeGreaterThanOrEqual(0);
      }
    }
  }
}

async function expectDownload(page, action) {
  const downloadPromise = page.waitForEvent("download");
  await action();
  const download = await downloadPromise;
  expect(download.suggestedFilename()).toBeTruthy();
}

function escapeRegex(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

module.exports = {
  apiBaseUrl,
  users,
  makeRunData,
  installApiBase,
  login,
  logout,
  gotoLogin,
  gotoRoute,
  expectNotice,
  selectOptionByText,
  createCatalogFixture,
  createSkuForSelectedProduct,
  createCrmFixture,
  ensureCoreData,
  openMerchantDetail,
  createOperationDraft,
  createSupplyReceipt,
  runLatestOperationAction,
  selectOperationLineSku,
  waitForStockOptions,
  createChangeDraft,
  resetOperationEditor,
  fillFirstOperationLine,
  expectApiForbidden,
  apiRequest,
  apiJson,
  latestOperationId,
  expectOneTransitionSucceeds,
  getStockBalances,
  expectNoNegativeStock,
  expectDownload
};
