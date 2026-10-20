const fs = require("node:fs/promises");
const path = require("node:path");
const { test, expect } = require("@playwright/test");
const {
  users, installApiBase, login, makeRunData, createCatalogFixture, createSupplyReceipt, gotoRoute
} = require("./support/helpers");

test.use({ channel: "chrome" });

const enabled = process.env.LENSEE_E2E_SHOPIFY_COMPANY === "1";
const stage = process.env.LENSEE_E2E_SHOPIFY_COMPANY_STAGE || "stock";
const order = process.env.LENSEE_E2E_SHOPIFY_COMPANY_ORDER || "";
const stockArtifact = path.resolve("artifacts/overnight/shopify-ui/company-handoff-stock.json");
const outputDirectory = path.resolve("artifacts/overnight/shopify-ui");
const stockLocation = process.env.LENSEE_E2E_SHOPIFY_COMPANY_STOCK_LOCATION || "TEST SubWarehouse";

async function findOrderRow(page) {
  const hash = new URL(page.url()).hash;
  if (!hash.startsWith("#/operations")) await gotoRoute(page, "/operations");
  await page.locator("#operations-search").fill(order);
  const row = page.locator("#operation-rows tr[data-operation-id]").filter({ hasText: order }).first();
  await expect(row).toBeVisible({ timeout: 30_000 });
  return row;
}

test.describe("Shopify company handoff through employee UI", () => {
  test.skip(!enabled, "Runs only in the shared isolated company QA environment.");

  test("Admin creates catalog stock through the UI for the next Shopify handoff", async ({ page }) => {
    test.skip(stage !== "stock", "This invocation is for the fulfillment or read-only review stage.");
    await installApiBase(page);
    await login(page, users.admin);
    const data = makeRunData("SHOPIFY-COMPANY");
    const sku = await createCatalogFixture(page, data, { captureSkuResponse: true });
    expect(sku?.skuCode).toBeTruthy();
    await expect(page.locator("#catalog-detail")).toContainText(sku.skuCode);

    const receipt = await createSupplyReceipt(page, {
      supplier: `${data.runId} supplier`,
      invoice: `${data.runId}-invoice`,
      skuText: sku.skuCode,
      quantity: "20",
      lot: `${data.runId}-LOT`,
      expiry: data.expiry,
      destinationText: stockLocation
    });
    await fs.mkdir(outputDirectory, { recursive: true });
    await fs.writeFile(stockArtifact, `${JSON.stringify({
      role: "Admin",
      skuCode: sku.skuCode,
      stockLocation,
      shipmentNumber: receipt.shipmentNumber,
      lot: `${data.runId}-LOT`,
      expiry: data.expiry,
      receivedPacks: 20,
      createdFrom: "Admin catalog and Supply UI"
    }, null, 2)}\n`, "utf8");
    await page.screenshot({ path: path.join(outputDirectory, "company-admin-catalog-stock.png"), fullPage: true });
  });

  test("Online Warehouse Clerk allocates, reserves, and dispatches the paid Shopify order", async ({ page }) => {
    test.skip(stage !== "fulfill", "This invocation is for the stock or read-only review stage.");
    test.skip(!order, "Requires the Shopify order created by the isolated signed receiver after Admin stock setup.");
    const fixture = JSON.parse(await fs.readFile(stockArtifact, "utf8"));
    const writes = [];
    page.on("request", (request) => {
      const pathname = new URL(request.url()).pathname;
      if (pathname.startsWith("/api/v1/") && !["GET", "HEAD", "OPTIONS"].includes(request.method())) {
        writes.push({ method: request.method(), path: pathname.replace(/\/[0-9a-f]{8}-[0-9a-f-]{27,}/gi, "/{id}") });
      }
    });
    await installApiBase(page);
    await login(page, users.onlineClerk);
    await page.goto("/en/#/operations");
    let row = await findOrderRow(page);
    await expect(row).toHaveAttribute("data-operation-status", "Draft");
    const operationId = await row.getAttribute("data-operation-id");
    const editorResponse = page.waitForResponse((response) =>
      response.request().method() === "GET" && new URL(response.url()).pathname === `/api/v1/operations/${operationId}/editor`);
    await row.locator("[data-op-edit]").click();
    const editor = await editorResponse;
    expect(editor.status(), "Warehouse Clerk must load the Shopify draft editor").toBe(200);
    const editorData = await editor.json();
    expect(editorData.operationType).toBe("RetailSale");
    expect(editorData.salesChannel).toBe("Shopify");
    expect(editorData.lines?.[0]?.skuCode).toBe(fixture.skuCode);

    const form = page.locator("#operation-form");
    await expect(form).toHaveAttribute("data-shopify-draft", "true");
    const line = page.locator("#op-lines .line-editor-row").first();
    await expect(line).toContainText(fixture.skuCode);
    await page.locator("#operation-editor-reset").click();
    await expect(form).not.toHaveAttribute("data-shopify-draft", "true");
    await expect(page.locator('#op-type option[value="RetailSale"]')).toHaveCount(0);
    row = await findOrderRow(page);
    const editorReload = page.waitForResponse((response) =>
      response.request().method() === "GET" && new URL(response.url()).pathname === `/api/v1/operations/${operationId}/editor`);
    await row.locator("[data-op-edit]").click();
    await editorReload;
    await expect(form).toHaveAttribute("data-shopify-draft", "true");
    await expect(page.locator("#op-lines .line-editor-row").first()).toContainText(fixture.skuCode);
    const source = page.locator("#op-source");
    const sourceId = await source.locator("option").evaluateAll((options, location) =>
      options.find((option) => option.textContent.trim() === location)?.value || "", stockLocation);
    expect(sourceId).toBeTruthy();
    await source.selectOption(sourceId);
    const batch = line.locator(".op-line-stock-option");
    await batch.click();
    await batch.focus();
    await expect.poll(async () => batch.locator("option").evaluateAll((options) =>
      options.filter((option) => option.value && option.value !== "__new_batch__").length), { timeout: 20_000 }).toBeGreaterThan(0);
    const selectedBatch = await batch.locator("option").evaluateAll((options) => options.find((option) => option.value && option.value !== "__new_batch__")?.value || "");
    expect(selectedBatch).toBeTruthy();
    await batch.selectOption(selectedBatch);
    const allocation = page.waitForResponse((response) => response.request().method() === "PUT" &&
      new URL(response.url()).pathname.endsWith("/shopify-allocation"));
    await page.locator("#operation-submit-button").click();
    const allocationResponse = await allocation;
    expect(allocationResponse.ok()).toBeTruthy();
    await expect(page.locator("#notification-area")).toContainText("Shopify batch allocation saved");

    row = await findOrderRow(page);
    const confirmation = page.waitForResponse((response) => response.request().method() === "POST" &&
      new URL(response.url()).pathname === `/api/v1/operations/${operationId}/confirm`);
    await row.locator('[data-op-action="confirm"]').click();
    await page.locator('[role="alertdialog"] [data-dialog-confirm]').click();
    const confirmationResponse = await confirmation;
    expect(confirmationResponse.ok()).toBeTruthy();
    await expect.poll(async () => (await findOrderRow(page)).getAttribute("data-operation-status"), { timeout: 30_000 }).toBe("Reserved");

    row = await findOrderRow(page);
    const dispatch = page.waitForResponse((response) => response.request().method() === "POST" &&
      new URL(response.url()).pathname === `/api/v1/operations/${operationId}/ship`);
    await row.locator('[data-op-action="ship"]').click();
    await page.locator('[role="alertdialog"] [data-dialog-confirm]').click();
    const dispatchResponse = await dispatch;
    expect(dispatchResponse.ok()).toBeTruthy();
    await expect.poll(async () => (await findOrderRow(page)).getAttribute("data-operation-status"), { timeout: 30_000 }).toBe("Dispatched");
    expect(writes.some((entry) => entry.method === "PUT" && entry.path.endsWith("/shopify-allocation"))).toBeTruthy();
    expect(writes.some((entry) => entry.method === "POST" && entry.path.endsWith("/confirm"))).toBeTruthy();
    expect(writes.some((entry) => entry.method === "POST" && entry.path.endsWith("/ship"))).toBeTruthy();
    await page.screenshot({ path: path.join(outputDirectory, "company-online-clerk-dispatched.png"), fullPage: true });
  });

  test("Online Warehouse Clerk sees why a pending Shopify order cannot be dispatched", async ({ page }) => {
    test.skip(stage !== "gate", "This invocation is for the unpaid dispatch gate.");
    test.skip(!order, "Requires a reserved pending Shopify order in isolated company QA.");
    const writes = [];
    page.on("request", (request) => {
      const pathname = new URL(request.url()).pathname;
      if (pathname.startsWith("/api/v1/") && !["GET", "HEAD", "OPTIONS"].includes(request.method())) {
        writes.push(`${request.method()} ${pathname}`);
      }
    });
    await installApiBase(page);
    await login(page, users.onlineClerk);
    await page.goto("/en/#/operations");
    const row = await findOrderRow(page);
    await expect(row).toHaveAttribute("data-operation-status", "Reserved");
    const shipButton = row.locator('[data-op-action="ship"]');
    await expect(shipButton).toBeDisabled();
    await expect(row).toContainText(/Dispatch is blocked until Shopify reports the order as paid and no order review is open/i);
    expect(writes.some((entry) => entry.endsWith("/ship"))).toBeFalsy();
    await page.screenshot({ path: path.join(outputDirectory, "company-unpaid-dispatch-blocked-chrome.png"), fullPage: true });
  });

  test("Accountant reads the dispatched order and independent payment states without writing finance data", async ({ page }) => {
    test.skip(stage !== "review", "This invocation is for the stock or fulfillment stage.");
    test.skip(!order, "Requires a dispatched Shopify order from the Online Warehouse Clerk stage.");
    const mutations = [];
    page.on("request", (request) => {
      const pathname = new URL(request.url()).pathname;
      if (pathname.startsWith("/api/v1/") && !["GET", "HEAD", "OPTIONS"].includes(request.method()) && !pathname.startsWith("/api/v1/auth/")) {
        mutations.push({ method: request.method(), path: pathname.replace(/\/[0-9a-f]{8}-[0-9a-f-]{27,}/gi, "/{id}") });
      }
    });
    await installApiBase(page);
    await login(page, users.accountant);
    await page.goto("/en/#/operations");
    const row = await findOrderRow(page);
    await expect(row).toHaveAttribute("data-operation-status", "Dispatched");
    await row.locator("[data-op-toggle]").click();
    const detail = page.locator(`#operation-detail-${await row.getAttribute("data-operation-id")} .operation-detail`);
    await expect(detail).toContainText("Shopify-reported payment");
    await expect(detail).toContainText("Reported paid");
    await expect(detail).toContainText("Payment provider verification");
    await expect(detail).toContainText("Not independently verified");
    await expect(detail).toContainText("Finance reconciliation");
    await expect(detail).toContainText("Pending Finance reconciliation");
    expect(mutations).toEqual([]);
    await page.screenshot({ path: path.join(outputDirectory, "company-accountant-payment-review.png"), fullPage: true });
  });
});
