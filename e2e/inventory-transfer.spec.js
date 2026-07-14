const { test, expect } = require("@playwright/test");
const {
  installApiBase,
  login,
  users,
  makeRunData,
  gotoRoute,
  expectNotice,
  selectOptionByText,
  ensureCoreData,
  createOperationDraft,
  runLatestOperationAction,
  fillFirstOperationLine,
  selectOperationLineSku,
  waitForStockOptions,
  resetOperationEditor,
  apiJson,
  apiRequest
} = require("./support/helpers");

test.beforeEach(async ({ page }) => {
  await installApiBase(page);
  await login(page, users.admin);
});

test("inventory: receipt creates balances, batches, transactions, and target state", async ({ page }) => {
  const data = makeRunData("INV");
  await ensureCoreData(page, data);

  await gotoRoute(page, "/operations");
  const receipt = await createOperationDraft(page, {
    type: "InventoryReceipt",
    skuText: data.product,
    quantity: "10",
    lot: data.mainLot,
    expiry: data.expiry,
    supplier: `${data.runId} Supplier`,
    invoice: `${data.runId}-INV`
  });
  const confirmReceipt = await apiRequest(page, "POST", `/api/v1/operations/${receipt.id}/confirm`);
  expect(confirmReceipt.ok()).toBeTruthy();

  await gotoRoute(page, "/inventory");
  await expect(page.locator("#inventory-locations")).toContainText(/Roxy|Main/i);
  const receiptSkuId = receipt.lines?.[0]?.skuId;
  expect(receiptSkuId).toBeTruthy();
  const { response: balanceResponse, data: balanceData } = await apiJson(page, "GET", `/api/v1/inventory/stock-balances?skuId=${receiptSkuId}&pageSize=20`);
  expect(balanceResponse.ok()).toBeTruthy();
  expect((balanceData.items || []).some((balance) => balance.skuId === receiptSkuId && balance.availablePacks >= 10)).toBeTruthy();
  const { response: batchResponse, data: batchData } = await apiJson(page, "GET", `/api/v1/inventory/batches?skuId=${receiptSkuId}&pageSize=20`);
  expect(batchResponse.ok()).toBeTruthy();
  expect((batchData.items || []).some((batch) => batch.lotNumber === data.mainLot && batch.packQuantity >= 10)).toBeTruthy();
  const { response: transactionResponse, data: transactionData } = await apiJson(page, "GET", `/api/v1/inventory/transactions?skuId=${receiptSkuId}&pageSize=20`);
  expect(transactionResponse.ok()).toBeTruthy();
  expect((transactionData.items || []).some((transaction) => /Receipt/i.test(transaction.transactionType || transaction.reason || ""))).toBeTruthy();

  const targetButton = page.locator("[data-target-location]").first();
  await expect(targetButton).toBeVisible();
  await targetButton.click();
  await page.locator(".dialog-input").fill("25");
  await page.locator(".dialog-card").getByRole("button", { name: /Continue/i }).click();
  await expectNotice(page, /Target updated|target/i);
  await expect(page.locator("#inventory-balances")).toContainText(/Low|Below|25|target/i);
});

test("transfer: source/destination stay fixed while adding lines and lifecycle reaches received", async ({ page }) => {
  const data = makeRunData("TRN");
  await ensureCoreData(page, data);

  await gotoRoute(page, "/operations");
  const receipt = await createOperationDraft(page, {
    type: "InventoryReceipt",
    skuText: data.product,
    quantity: "12",
    lot: data.mainLot,
    expiry: data.expiry,
    supplier: `${data.runId} Supplier`,
    invoice: `${data.runId}-INV`
  });
  const confirmReceipt = await apiRequest(page, "POST", `/api/v1/operations/${receipt.id}/confirm`);
  expect(confirmReceipt.ok()).toBeTruthy();

  await resetOperationEditor(page);
  await page.locator("#op-type").selectOption("WarehouseTransfer");
  await selectOptionByText(page.locator("#op-destination"), /Retail|Online|Mohamed/i);
  const sourceBefore = await page.locator("#op-source").inputValue();
  const destinationBefore = await page.locator("#op-destination").inputValue();
  await fillFirstOperationLine(page, { skuText: data.product, quantity: "1", stockText: data.mainLot });
  await page.locator("#op-add-line").click();
  await expect(page.locator("#op-source")).toHaveValue(sourceBefore);
  await expect(page.locator("#op-destination")).toHaveValue(destinationBefore);
  await selectOperationLineSku(page.locator(".line-editor-row").nth(1), data.product);
  await expect.poll(async () => await page.locator(".line-editor-row").nth(1).locator(".op-line-stock-option option").count()).toBeGreaterThan(1);

  await createOperationDraft(page, {
    type: "WarehouseTransfer",
    skuText: data.product,
    quantity: "4",
    stockText: data.mainLot,
    destinationText: /Retail|Online|Mohamed/i
  });
  await runLatestOperationAction(page, "WarehouseTransfer", /Confirm/i);
  await runLatestOperationAction(page, "WarehouseTransfer", /Ship/i);
  await runLatestOperationAction(page, "WarehouseTransfer", /Receive/i);
  await expect(page.locator("#operation-rows tr", { hasText: "WarehouseTransfer" }).first()).toContainText(/Received|Completed/);
});

test("operations editor: edit and revise prefill route, party, lines, and batch data", async ({ page }) => {
  const data = makeRunData("PREFILL");
  await ensureCoreData(page, data);

  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "InventoryReceipt",
    skuText: data.product,
    quantity: "6",
    lot: data.mainLot,
    expiry: data.expiry,
    supplier: `${data.runId} Supplier`,
    invoice: `${data.runId}-INV`
  });
  await runLatestOperationAction(page, "InventoryReceipt", /Confirm/i);

  await createOperationDraft(page, {
    type: "WarehouseTransfer",
    skuText: data.product,
    quantity: "2",
    stockText: data.mainLot,
    destinationText: /Retail|Online|Mohamed/i
  });

  const draftRow = page.locator("#operation-rows tr", { hasText: "WarehouseTransfer" }).first();
  await draftRow.getByRole("button", { name: /Edit/i }).click();
  await expectNotice(page, /Draft loaded/i);
  await expect(page.locator("#op-type")).toHaveValue("WarehouseTransfer");
  await expect(page.locator("#op-destination")).not.toHaveValue("");
  await expect(page.locator(".line-editor-row")).toHaveCount(1);
  await expect(page.locator(".line-editor-row").first().locator(".op-line-qty")).toHaveValue("2");
  await expect(page.locator(".line-editor-row").first().locator(".op-line-lot")).toHaveValue(data.mainLot);
  await expect(page.locator(".line-editor-row").first().locator(".op-line-expiry")).toHaveValue(data.expiry);
  await expect(page.locator(".line-editor-row").first().locator(".op-line-resolved")).toContainText(/Resolved SKU/i);
  await waitForStockOptions(page.locator(".line-editor-row").first(), data.mainLot);
  await expect(page.locator(".line-editor-row").first().locator(".op-line-stock-option")).toContainText(data.mainLot);

  await runLatestOperationAction(page, "WarehouseTransfer", /Confirm/i);
  const reservedRow = page.locator("#operation-rows tr", { hasText: "WarehouseTransfer" }).first();
  await reservedRow.getByRole("button", { name: /Revise/i }).click();
  await expectNotice(page, /loaded for revision/i);
  await expect(page.locator("#op-type")).toHaveValue("WarehouseTransfer");
  await expect(page.locator("#op-destination")).not.toHaveValue("");
  await expect(page.locator(".line-editor-row").first().locator(".op-line-qty")).toHaveValue("2");
  await expect(page.locator(".line-editor-row").first().locator(".op-line-lot")).toHaveValue(data.mainLot);
  await expect(page.locator(".line-editor-row").first().locator(".op-line-expiry")).toHaveValue(data.expiry);
  await expect(page.locator("#op-revision-reason-field")).toBeVisible();
});
