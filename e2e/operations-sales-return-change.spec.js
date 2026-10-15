const { test, expect } = require("@playwright/test");
const {
  installApiBase,
  login,
  users,
  makeRunData,
  gotoRoute,
  selectOptionByText,
  apiJson,
  ensureCoreData,
  openMerchantDetail,
  createOperationDraft,
  runLatestOperationAction
} = require("./support/helpers");

test.beforeEach(async ({ page }) => {
  await installApiBase(page);
  const e2eApi = process.env.LENSEE_E2E_API_URL || "http://127.0.0.1:55000";
  await page.route(/\/api\//, async (route) => {
    const source = new URL(route.request().url());
    const response = await route.fetch({ url: `${e2eApi}${source.pathname}${source.search}` });
    await route.fulfill({ response });
  });
  await page.route("**/health", async (route) => {
    const response = await route.fetch({ url: `${e2eApi}/health` });
    await route.fulfill({ response });
  });
  await login(page, users.admin);
});

test("operations: sale line kinds allow Paid, Bonus, and fixed-price Offer for one SKU batch", async ({ page }) => {
  const data = makeRunData("LINE-KINDS");
  await seedStock(page, data);
  await gotoRoute(page, "/operations");
  await selectOptionByText(page.locator("#op-source"), /Roxy|Main/i);
  await page.locator("#op-type").selectOption("WholesaleSale");
  await page.waitForTimeout(500);
  const rows = page.locator(".line-editor-row");
  const paid = rows.nth(0);
  await selectOperationLineSkuForE2e(paid, data.product);
  await paid.locator(".op-line-qty").fill("1");
  await paid.locator(".op-line-price").fill("125");
  await paid.locator(".op-line-type").selectOption("Paid");
  const addLine = page.locator("#op-add-line");
  for (const [kind, qty, price] of [["Bonus", "2", "0"], ["Offer", "3", "54.25"]]) {
    await addLine.click();
    const row = rows.nth(await rows.count() - 1);
    await selectOperationLineSkuForE2e(row, data.product);
    await row.locator(".op-line-type").selectOption(kind);
    await row.locator(".op-line-qty").fill(qty);
    if (kind === "Bonus") {
      await expect(row.locator(".op-line-price")).toHaveValue("0");
      await expect(row.locator(".op-line-price")).toBeDisabled();
    } else {
      await row.locator(".op-line-price").fill(price);
    }
  }
  await selectOptionByText(page.locator("#op-merchant"), data.merchant);
  await page.locator("#op-payment").selectOption("MerchantAccount");

  const [response] = await Promise.all([
    page.waitForResponse((value) => value.url().includes("/api/v1/operations") && value.request().method() === "POST" && !/\/(confirm|ship|receive|complete|cancel)(\?|$)/.test(value.url())),
    page.locator("#operation-submit-button").click()
  ]);
  expect(response.ok()).toBeTruthy();
  const created = await response.json();
  const detail = await apiJson(page, "GET", `/api/v1/operations/${created.id}/editor`);
  expect(detail.response.ok()).toBeTruthy();
  expect(detail.data.lines.map((line) => line.lineType).sort()).toEqual(["Bonus", "Offer", "Paid"]);
  expect(detail.data.lines.find((line) => line.lineType === "Offer").unitPrice).toBe(54.25);
  expect(detail.data.lines.find((line) => line.lineType === "Bonus").unitPrice).toBe(0);
  await page.reload();
  await gotoRoute(page, "/operations");
  const row = page.locator(`#operation-rows tr[data-operation-number="${created.operationNumber}"]`);
  await row.getByRole("button", { name: /Details/i }).click();
  const detailRow = page.locator(`#operation-detail-${created.id}`);
  await expect(detailRow).toContainText(/Paid/);
  await expect(detailRow).toContainText(/Bonus/);
  await expect(detailRow).toContainText(/Offer/);
});

test("operations: anonymous retail checkout does not require a CRM merchant", async ({ page }) => {
  const data = makeRunData("ANON-SALES");
  await seedStock(page, data);
  const retailTransfer = await createOperationDraft(page, {
    type: "WarehouseTransfer", sourceText: /Roxy|Main/i, skuText: data.product, quantity: "5",
    stockText: data.mainLot, destinationText: /Retail|Online|Mohamed/i
  });
  await runLatestOperationAction(page, "WarehouseTransfer", /Confirm/i, retailTransfer.operationNumber);
  await runLatestOperationAction(page, "WarehouseTransfer", /Ship/i, retailTransfer.operationNumber);
  await runLatestOperationAction(page, "WarehouseTransfer", /Receive/i, retailTransfer.operationNumber);
  await gotoRoute(page, "/operations");

  const retail = await createOperationDraft(page, {
    type: "RetailSale", skuText: data.product, quantity: "1", price: "33", stockText: data.mainLot,
    sourceText: /Retail|Online|Mohamed/i, paymentMethod: "CashHandToHand", buyerName: `${data.runId} walk-in`
  });
  expect(retail.clientId ?? null).toBeNull();
  await runLatestOperationAction(page, "RetailSale", /Confirm/i, retail.operationNumber);
  const retailRow = page.locator(`#operation-rows tr[data-operation-number="${retail.operationNumber}"]`);
  await expect(retailRow).toContainText(/Completed/i);
  await expect(retailRow.locator("[data-op-action='ship'], [data-op-action='complete']")).toHaveCount(0);
  const retailEditor = await apiJson(page, "GET", `/api/v1/operations/${retail.id}`);
  expect(retailEditor.response.ok()).toBeTruthy();
  expect(retailEditor.data).toMatchObject({ clientId: null, clientName: `${data.runId} walk-in` });
  const retailPayment = await apiJson(page, "GET", `/api/v1/payments?operationId=${retail.id}&page=1&pageSize=10`);
  expect(retailPayment.response.ok()).toBeTruthy();
});

async function selectOperationLineSkuForE2e(row, productName) {
  const search = row.locator(".op-line-search");
  await search.fill(productName);
  const result = row.locator(".op-line-search-results .op-line-search-result:not([disabled])").first();
  await expect(result).toBeVisible({ timeout: 25_000 });
  await result.click();
  await expect(row.locator(".op-line-sku")).not.toHaveValue("");
  await expect.poll(async () => row.locator(".op-line-stock-option option").count(), { timeout: 25_000 }).toBeGreaterThan(1);
  const stock = row.locator(".op-line-stock-option");
  const available = stock.locator("option:not([value=''])").first();
  await stock.selectOption(await available.getAttribute("value"));
}

async function seedStock(page, data, quantity = "20") {
  await ensureCoreData(page, data);
  await gotoRoute(page, "/operations");
  const receipt = await createOperationDraft(page, {
    type: "InventoryReceipt",
    destinationText: /Main/i,
    skuText: data.product,
    quantity,
    lot: data.mainLot,
    expiry: data.expiry,
    supplier: `${data.runId} Supplier`,
    invoice: `${data.runId}-INV`
  });
  await runLatestOperationAction(page, "InventoryReceipt", /Confirm/i, receipt.operationNumber);
}

test("operations: wholesale requires Confirm → Ship → Receive → Complete and retail is an immediate checkout", async ({ page }) => {
  const data = makeRunData("SALE");
  await seedStock(page, data);

  await gotoRoute(page, "/operations");
  const transfer = await createOperationDraft(page, {
    type: "WarehouseTransfer",
    sourceText: /Roxy|Main/i,
    skuText: data.product,
    quantity: "5",
    stockText: data.mainLot,
    destinationText: /Retail|Online|Mohamed/i
  });
  await runLatestOperationAction(page, "WarehouseTransfer", /Confirm/i, transfer.operationNumber);
  await runLatestOperationAction(page, "WarehouseTransfer", /Ship/i, transfer.operationNumber);
  await runLatestOperationAction(page, "WarehouseTransfer", /Receive/i, transfer.operationNumber);

  const wholesale = await createOperationDraft(page, {
    type: "WholesaleSale",
    skuText: data.product,
    quantity: "2",
    price: "125",
    stockText: data.mainLot,
    merchantText: data.merchant,
    paymentMethod: "MerchantAccount",
    sourceText: /Roxy|Main/i
  });
  await runLatestOperationAction(page, "WholesaleSale", /Confirm/i, wholesale.operationNumber);
  await runLatestOperationAction(page, "WholesaleSale", /Ship/i, wholesale.operationNumber);
  await runLatestOperationAction(page, "WholesaleSale", /Receive/i, wholesale.operationNumber);
  await page.locator("#operations-show-completed").uncheck();
  await expect(page.locator(`#operation-rows tr[data-operation-number="${transfer.operationNumber}"]`)).toHaveCount(0);
  const receivedWholesaleRow = page.locator(`#operation-rows tr[data-operation-number="${wholesale.operationNumber}"]`);
  await expect(receivedWholesaleRow).toContainText(/Received/i);
  await expect(receivedWholesaleRow.locator("[data-op-action='complete']")).toBeVisible();
  await runLatestOperationAction(page, "WholesaleSale", /Complete/i, wholesale.operationNumber);

  const retail = await createOperationDraft(page, {
    type: "RetailSale",
    skuText: data.product,
    quantity: "1",
    price: "150",
    stockText: data.mainLot,
    sourceText: /Retail|Online|Mohamed/i,
    paymentMethod: "CashHandToHand",
    buyerName: `${data.runId} Walk-in buyer`
  });
  await runLatestOperationAction(page, "RetailSale", /Confirm/i, retail.operationNumber);
  const retailRow = page.locator(`#operation-rows tr[data-operation-number="${retail.operationNumber}"]`);
  await expect(retailRow.locator("[data-op-action='ship'], [data-op-action='complete']")).toHaveCount(0);
  await expect(retailRow).toContainText(/Completed/i);

  await gotoRoute(page, "/crm");
  await openMerchantDetail(page, data);
  await expect(page.locator("#merchant-detail-panel")).toContainText(/WholesaleSale|Merchant Batch History|Balance/i);
});

test("operations: write-off is confirmed without return quantity-cap bypass", async ({ page }) => {
  const data = makeRunData("RET");
  await seedStock(page, data);

  await gotoRoute(page, "/operations");
  const writeOff = await createOperationDraft(page, {
    type: "WriteOff",
    skuText: data.product,
    quantity: "1",
    stockText: data.mainLot,
    sourceText: /Roxy|Main/i
  });
  await runLatestOperationAction(page, "WriteOff", /Confirm/i, writeOff.operationNumber);
  await expect(page.locator(`#operation-rows tr[data-operation-number="${writeOff.operationNumber}"]`)).toContainText(/Confirmed/i);
});

test("operations: transfer detail expansion, actor labels, and version timeline are visible", async ({ page }) => {
  const data = makeRunData("DETAIL");
  await seedStock(page, data);

  await gotoRoute(page, "/operations");
  const transfer = await createOperationDraft(page, {
    type: "WarehouseTransfer",
    skuText: data.product,
    quantity: "1",
    stockText: data.mainLot,
    sourceText: /Roxy|Main/i,
    destinationText: /Retail|Online|Mohamed/i
  });
  await runLatestOperationAction(page, "WarehouseTransfer", /Confirm/i, transfer.operationNumber);

  const transferRow = page.locator(`#operation-rows tr[data-operation-number="${transfer.operationNumber}"]`);
  await transferRow.getByRole("button", { name: /Show|Details/i }).first().click();
  await expect(page.locator(".operation-detail").first()).toContainText(/Operation code|Created by|Confirmed by|Current version|Batch expiry/i);
});
