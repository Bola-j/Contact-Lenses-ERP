const fs = require("node:fs/promises");
const path = require("node:path");
const { test, expect } = require("@playwright/test");
const { users, installApiBase, login, gotoRoute } = require("./support/auth");

test.use({ channel: "chrome" });
const workflowNetworkStatuses = new WeakMap();
test.beforeEach(async ({ page }) => {
  await installApiBase(page);
  const statuses = [];
  workflowNetworkStatuses.set(page, statuses);
  page.on("response", (response) => {
    const url = new URL(response.url());
    if (!url.pathname.startsWith("/api/v1/")) return;
    statuses.push({ at: new Date().toISOString(), method: response.request().method(), path: url.pathname.replace(/\/[0-9a-f]{8}-[0-9a-f-]{27,}/gi, "/{id}"), status: response.status() });
  });
});
test.afterEach(async ({ page }, testInfo) => {
  if (testInfo.status === testInfo.expectedStatus) return;
  const evidencePath = path.resolve(`artifacts/overnight/shopify-ui/${roleName}-workflow-http-statuses.json`);
  await fs.mkdir(path.dirname(evidencePath), { recursive: true });
  const uiState = await page.evaluate(() => ({ path: `${location.pathname}${location.hash}`, language: document.documentElement.lang, direction: document.documentElement.dir })).catch(() => null);
  await fs.writeFile(evidencePath, `${JSON.stringify({ role: roleName, uiState, responses: workflowNetworkStatuses.get(page) || [] }, null, 2)}\n`, "utf8");
});

const enabled = process.env.LENSEE_E2E_SHOPIFY_WORKFLOW === "1";
const roleName = process.env.LENSEE_E2E_SHOPIFY_ROLE === "ERPAdmin" ? "ERPAdmin" : "Admin";
const roleUser = roleName === "ERPAdmin" ? users.erpAdmin : users.admin;
const eventOrder = process.env.LENSEE_E2E_SHOPIFY_ATTENTION_ORDER;
const shopifySku = process.env.LENSEE_E2E_SHOPIFY_VARIANT_SKU;
const targetSku = process.env.LENSEE_E2E_SHOPIFY_TARGET_SKU;
const stockLocation = process.env.LENSEE_E2E_SHOPIFY_STOCK_LOCATION || "TEST SubWarehouse";
const stockLot = process.env.LENSEE_E2E_SHOPIFY_STOCK_LOT || "";
const paidOrder = process.env.LENSEE_E2E_SHOPIFY_PAID_ORDER;
const pendingOrder = process.env.LENSEE_E2E_SHOPIFY_PENDING_ORDER;

test.describe(`Shopify real workflow (${roleName})`, () => {
  test.skip(!enabled, "Requires the isolated disposable Shopify workflow environment; this test performs mapping and retry writes there.");
  test.skip(!eventOrder || !shopifySku || !targetSku || !paidOrder || !pendingOrder,
    "Requires isolated seeded Shopify attention, linked paid, and linked pending order fixtures.");

  test("map and replay when needed, allocate Shopify drafts, and inspect separate payment states", async ({ page }) => {
    const writes = [];
    const stockLookups = [];
    page.on("request", (request) => {
      const apiPath = new URL(request.url()).pathname;
      if (apiPath.startsWith("/api/v1/") && !["GET", "HEAD", "OPTIONS"].includes(request.method())) {
        writes.push(`${request.method()} ${apiPath}`);
      }
    });
    page.on("response", (response) => {
      const pathname = new URL(response.url()).pathname;
      if (["/api/v1/inventory/available-sku-ids", "/api/v1/inventory/stock-options"].includes(pathname)) {
        stockLookups.push({ method: response.request().method(), path: pathname, status: response.status() });
      }
    });

    await login(page, roleUser);
    await page.goto("/en/#/integrations");
    await expect(page.getByRole("heading", { name: "Shopify variant links" })).toBeVisible();
    await page.locator("#shopify-mapping-filter").fill(shopifySku);
    await page.locator("#shopify-mapping-filter").press("Enter");
    const linkRow = page.locator("#shopify-variant-links tbody tr").filter({ hasText: shopifySku }).first();
    await expect(linkRow).toBeVisible();
    let mappingPerformed = false;
    if (!(await linkRow.locator("td").nth(2).innerText()).includes(targetSku)) {
      await linkRow.locator("[data-shopify-map]").click();
      await page.locator("#shopify-mapping-sku-search").fill(targetSku);
      await page.locator("#shopify-mapping-sku-search").press("Enter");
      const skuChoice = page.locator("#shopify-mapping-sku-choice");
      const targetOption = skuChoice.locator("option").filter({ hasText: targetSku }).first();
      await expect(targetOption).toBeAttached();
      const targetSkuId = await targetOption.getAttribute("value");
      expect(targetSkuId).toBeTruthy();
      await skuChoice.selectOption(targetSkuId);
      await expect(skuChoice).toHaveValue(targetSkuId);
      await page.locator("#shopify-mapping-reason").fill(`Isolated UI certification ${roleName}: verified Shopify variant to ERP SKU mapping`);
      await page.locator("#shopify-mapping-save").click();
      await expect(page.locator("#notification-area")).toContainText("Shopify variant mapping saved", { timeout: 20_000 });
      await expect(page.locator("#shopify-variant-links")).toContainText(targetSku);
      mappingPerformed = true;
    }

    await page.locator("#shopify-event-status").selectOption("");
    let event = page.locator("#shopify-event-list article").filter({ hasText: eventOrder }).first();
    await expect(event).toBeVisible();
    let retryResult = await event.innerText();
    let retryPerformed = false;
    if (!/Imported|Succeeded/i.test(retryResult)) {
      await expect(event.locator("[data-shopify-retry]")).toBeEnabled();
      await event.locator("[data-shopify-retry]").click();
      await page.locator("[data-dialog-confirm]").click();
      await expect(page.locator("#notification-area")).toContainText(/queued for retry/i, { timeout: 20_000 });
      retryPerformed = true;
      const retryDeadline = Date.now() + 60_000;
      while (Date.now() < retryDeadline) {
        await page.reload();
        await page.goto("/en/#/integrations");
        await page.locator("#shopify-event-status").selectOption("");
        event = page.locator("#shopify-event-list article").filter({ hasText: eventOrder }).first();
        retryResult = await event.innerText().catch(() => "");
        if (/Imported|Succeeded/i.test(retryResult)) break;
        await page.waitForTimeout(1_500);
      }
    }
    expect(retryResult).toMatch(/Imported|Succeeded/i);

    await fs.mkdir(path.resolve("artifacts/overnight/shopify-ui"), { recursive: true });
    await page.screenshot({ path: path.resolve(`artifacts/overnight/shopify-ui/${roleName}-workflow.png`), fullPage: true });

    await inspectShopifyOperation(page, paidOrder, "paid");
    const paidAllocated = await allocateShopifyDraft(page, paidOrder, writes, stockLookups);
    const paidConfirmed = await confirmShopifyReservation(page, paidOrder);
    const paidDispatched = await dispatchShopifyOperation(page, paidOrder, true, writes);
    await page.screenshot({ path: path.resolve(`artifacts/overnight/shopify-ui/${roleName}-paid-dispatched.png`), fullPage: true });

    await inspectShopifyOperation(page, pendingOrder, "pending");
    const pendingAllocated = await allocateShopifyDraft(page, pendingOrder, writes, stockLookups);
    const pendingConfirmed = await confirmShopifyReservation(page, pendingOrder);
    await page.locator("#language-toggle").click();
    await expect(page).toHaveURL(/\/ar\//);
    await expect(page.locator("#operation-rows")).toBeVisible();
    const pendingDispatchBlocked = await dispatchShopifyOperation(page, pendingOrder, false, writes, "ar");
    await page.screenshot({ path: path.resolve(`artifacts/overnight/shopify-ui/${roleName}-pending-blocked.png`), fullPage: true });

    await page.reload();
    await expect(page.locator("#operation-rows")).toBeVisible();
    await expect(await findShopifyOperationRow(page, paidOrder)).toHaveAttribute("data-operation-status", "Dispatched");
    await expect(await findShopifyOperationRow(page, pendingOrder)).toHaveAttribute("data-operation-status", "Reserved");
    for (const [order, expectedReportedPayment] of [[paidOrder, "أبلغ Shopify عن الدفع"], [pendingOrder, "معلق"]]) {
      const row = await findShopifyOperationRow(page, order);
      await row.locator("[data-op-toggle]").click();
      const detail = page.locator(`#operation-detail-${await row.getAttribute("data-operation-id")} .operation-detail`);
      await expect(detail).toContainText(expectedReportedPayment);
      await expect(detail).toContainText("لم يتم التحقق منه بشكل مستقل");
      await expect(detail).toContainText("بانتظار التسوية المالية");
    }
    await page.screenshot({ path: path.resolve(`artifacts/overnight/shopify-ui/${roleName}-workflow-after-reload-ar.png`), fullPage: true });

    expect(writes.some((entry) => entry.includes("/variant-links/") && entry.startsWith("PUT "))).toBe(mappingPerformed);
    expect(writes.some((entry) => entry.includes("/events/") && entry.endsWith("/retry") && entry.startsWith("POST "))).toBe(retryPerformed);
    expect(writes.some((entry) => entry.includes("/shopify-allocation") && entry.startsWith("PUT "))).toBe(paidAllocated || pendingAllocated);
    expect(writes.some((entry) => entry.includes("/operations/") && entry.endsWith("/confirm") && entry.startsWith("POST "))).toBe(paidConfirmed || pendingConfirmed);
    expect(writes.some((entry) => entry.includes("/operations/") && entry.endsWith("/ship") && entry.startsWith("POST "))).toBe(paidDispatched);
    expect(pendingDispatchBlocked).toBeTruthy();
  });
});

async function findShopifyOperationRow(page, order) {
  const currentRoute = new URL(page.url()).hash;
  if (!currentRoute.startsWith("#/operations") || !(await page.locator("#operation-rows").count())) {
    await gotoRoute(page, "/operations");
  }
  await page.locator("#operations-search").fill(order);
  const row = page.locator("#operation-rows tr[data-operation-id]").filter({ hasText: order }).first();
  await expect(row).toBeVisible({ timeout: 30_000 });
  return row;
}

async function inspectShopifyOperation(page, order, expectedPayment) {
  const row = await findShopifyOperationRow(page, order);
  await row.locator("[data-op-toggle]").click();
  const detail = page.locator(`#operation-detail-${await row.getAttribute("data-operation-id")} .operation-detail`);
  await expect(detail).toContainText("Shopify-reported payment");
  await expect(detail).toContainText("Payment provider verification");
  await expect(detail).toContainText("Finance reconciliation");
  await expect(detail).toContainText(expectedPayment === "paid" ? "Reported paid" : "Pending");
  await expect(detail).toContainText("Not independently verified");
  await expect(detail).toContainText("Pending Finance reconciliation");
  const screenshotName = expectedPayment === "paid" ? "paid-payment" : "pending-payment";
  await page.screenshot({ path: path.resolve(`artifacts/overnight/shopify-ui/${roleName}-${screenshotName}.png`), fullPage: true });
}

async function allocateShopifyDraft(page, order, writes, stockLookups) {
  const row = await findShopifyOperationRow(page, order);
  const status = await row.getAttribute("data-operation-status");
  if (status !== "Draft") return false;
  const operationId = await row.getAttribute("data-operation-id");
  await row.locator("[data-op-edit]").click();

  const form = page.locator("#operation-form");
  await expect(form).toHaveAttribute("data-shopify-draft", "true");
  const line = page.locator("#op-lines .line-editor-row").first();
  await expect(line).toContainText(targetSku);
  await expect(line.locator(".op-line-qty")).toHaveValue("2");

  const source = page.locator("#op-source");
  const sourceLocationId = await source.locator("option").evaluateAll((options, desiredName) =>
    options.find((option) => option.textContent.trim() === desiredName)?.value || "", stockLocation);
  expect(sourceLocationId, `Missing expected stock location ${stockLocation}`).not.toBe("");
  await source.selectOption(sourceLocationId);

  const batch = line.locator(".op-line-stock-option");
  await batch.click();
  await batch.focus();
  try {
    await expect.poll(async () => batch.locator("option").evaluateAll((options) =>
      options.filter((option) => option.value && option.value !== "__new_batch__").length), { timeout: 20_000 }).toBeGreaterThan(0);
  } catch (error) {
    const evidencePath = path.resolve(`artifacts/overnight/shopify-ui/${roleName}-allocation-lookups.json`);
    await fs.mkdir(path.dirname(evidencePath), { recursive: true });
    await fs.writeFile(evidencePath, `${JSON.stringify({ role: roleName, lookups: stockLookups }, null, 2)}\n`, "utf8");
    throw error;
  }
  const stockOptions = await batch.locator("option").evaluateAll((options) => options
    .filter((option) => option.value && option.value !== "__new_batch__")
    .map((option) => ({ value: option.value, label: option.textContent.trim() })));
  const chosenBatch = stockLot
    ? stockOptions.find((option) => option.label.includes(stockLot))
    : stockOptions[0];
  expect(chosenBatch, stockLot ? `No available stock batch matched ${stockLot}` : "No available stock batch was listed").toBeTruthy();
  await batch.selectOption(chosenBatch.value);
  await expect(batch).toHaveValue(chosenBatch.value);

  const allocationResponse = page.waitForResponse((response) =>
    response.request().method() === "PUT" && new URL(response.url()).pathname === `/api/v1/operations/${operationId}/shopify-allocation`);
  await page.locator("#operation-submit-button").click();
  const response = await allocationResponse;
  expect(response.ok(), `Shopify allocation returned HTTP ${response.status()}`).toBeTruthy();
  await expect(page.locator("#notification-area")).toContainText("Shopify batch allocation saved", { timeout: 20_000 });
  expect(writes.some((entry) => entry === `PUT /api/v1/operations/${operationId}/shopify-allocation`)).toBeTruthy();
  return true;
}

async function confirmShopifyReservation(page, order) {
  const row = await findShopifyOperationRow(page, order);
  const status = await row.getAttribute("data-operation-status");
  if (status === "Reserved" || status === "Dispatched") return false;
  expect(status).toBe("Draft");
  const operationId = await row.getAttribute("data-operation-id");
  const responsePromise = page.waitForResponse((response) =>
    response.request().method() === "POST" && new URL(response.url()).pathname === `/api/v1/operations/${operationId}/confirm`);
  await row.locator('[data-op-action="confirm"]').click();
  await page.locator('[role="alertdialog"] [data-dialog-confirm]').click();
  const response = await responsePromise;
  expect(response.ok(), `Reservation confirmation returned HTTP ${response.status()}`).toBeTruthy();
  await expect.poll(async () => (await findShopifyOperationRow(page, order)).getAttribute("data-operation-status"), { timeout: 30_000 }).toBe("Reserved");
  return true;
}

async function dispatchShopifyOperation(page, order, expectAllowed, writes, language = "en") {
  const row = await findShopifyOperationRow(page, order);
  const status = await row.getAttribute("data-operation-status");
  if (expectAllowed && status === "Dispatched") return false;
  await expect(row).toHaveAttribute("data-operation-status", "Reserved");
  const operationId = await row.getAttribute("data-operation-id");
  const shipButton = row.locator('[data-op-action="ship"]');
  if (!expectAllowed) {
    const shipRequest = `POST /api/v1/operations/${operationId}/ship`;
    await expect(shipButton).toBeDisabled();
    if (language === "ar") {
      await expect(row).toContainText(/لا يمكن شحن الطلب حتى تُبلغ Shopify عن سداده/);
    } else {
      await expect(row).toContainText(/Dispatch is blocked until Shopify reports the order as paid/i);
    }
    await expect(row).toHaveAttribute("data-operation-status", "Reserved");
    expect(writes).not.toContain(shipRequest);
    return true;
  }
  const responsePromise = page.waitForResponse((response) =>
    response.request().method() === "POST" && new URL(response.url()).pathname === `/api/v1/operations/${operationId}/ship`);
  await row.locator('[data-op-action="ship"]').click();
  await page.locator('[role="alertdialog"] [data-dialog-confirm]').click();
  const response = await responsePromise;
  expect(response.ok(), `Dispatch returned HTTP ${response.status()}`).toBeTruthy();
  await expect.poll(async () => (await findShopifyOperationRow(page, order)).getAttribute("data-operation-status"), { timeout: 30_000 }).toBe("Dispatched");
  return true;
}
