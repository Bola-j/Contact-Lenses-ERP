const { test, expect } = require("@playwright/test");
const {
  installApiBase,
  login,
  logout,
  users,
  makeRunData,
  gotoRoute,
  openOtherPaymentsPanel,
  selectOptionByText,
  ensureCoreData,
  openMerchantDetail,
  createOperationDraft,
  createSupplyReceipt,
  runLatestOperationAction,
  runOperationActionByNumber,
  createChangeDraft,
  expectDownload,
  accountantIdByUsername,
  paymentForOperation, apiJson,
  paymentQueueRowById
} = require("./support/helpers");

test.beforeEach(async ({ page }) => {
  await installApiBase(page);
});

test("full business day: catalog, CRM, inventory, operations, payments, reports, notifications, stocktake", async ({ page }) => {
  test.setTimeout(300_000);
  const data = makeRunData("DAY");

  await login(page, users.admin);
  await ensureCoreData(page, data);

  await gotoRoute(page, "/notifications");
  await expect(page.locator("#notification-list")).toBeVisible();

  await createSupplyReceipt(page, {
    skuText: data.product,
    quantity: "20",
    price: "75",
    lot: data.mainLot,
    expiry: data.expiry,
    supplier: `${data.runId} Supplier`,
    invoice: `${data.runId}-INV`
  });

  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "WarehouseTransfer",
    skuText: data.product,
    quantity: "5",
    stockText: data.mainLot,
    destinationText: /Retail|Online|Mohamed/i
  });
  await runLatestOperationAction(page, "WarehouseTransfer", /Confirm/i);
  await runLatestOperationAction(page, "WarehouseTransfer", /Ship/i);
  await runLatestOperationAction(page, "WarehouseTransfer", /Receive/i);

  const sale = await createOperationDraft(page, {
    type: "RetailSale",
    skuText: data.product,
    quantity: "2",
    price: "125",
    stockText: data.mainLot,
    paymentMethod: "CashHandToHand",
    buyerName: `${data.runId} payment buyer`,
    sourceText: /Roxy|Main/i
  });
  await runOperationActionByNumber(page, sale.operationNumber, /Confirm/i);
  await runOperationActionByNumber(page, sale.operationNumber, /Ship/i);
  await runOperationActionByNumber(page, sale.operationNumber, /Complete/i);
  const payment = await paymentForOperation(page, sale.id);
  const accountList = await apiJson(page, "GET", "/api/v1/finance/accounts");
  expect(accountList.response.ok()).toBeTruthy();
  let cashAccount = (accountList.data || []).find((account) => account.type === "CashOnHand");
  if (!cashAccount) {
    const accountCreate = await apiJson(page, "POST", "/api/v1/finance/accounts", { name: `${data.runId} Main Cash`, type: "CashOnHand", reference: data.runId });
    expect(accountCreate.response.ok()).toBeTruthy();
    cashAccount = accountCreate.data;
  }
  expect(cashAccount?.id).toBeTruthy();

  await createOperationDraft(page, {
    type: "RetailSale",
    skuText: data.product,
    quantity: "1",
    price: "150",
    stockText: data.mainLot,
    sourceText: /Retail|Online|Mohamed/i,
    paymentMethod: "CashHandToHand",
    buyerName: `${data.runId} Walk-in buyer`
  });
  await runLatestOperationAction(page, "RetailSale", /Confirm/i);
  await runLatestOperationAction(page, "RetailSale", /Ship/i);
  await runLatestOperationAction(page, "RetailSale", /Complete/i);

  await gotoRoute(page, "/payments");
  await openOtherPaymentsPanel(page);
  await expect(paymentQueueRowById(page, payment.id)).toBeVisible();
  await paymentQueueRowById(page, payment.id).getByRole("button", { name: "Use" }).click();
  await expect(page.locator("#collection-merchant-field")).toBeHidden();
  await page.locator("#collection-source-reference").fill(payment.operationNumber || payment.id);
  await page.locator("#collection-amount").fill("250");
  await page.locator("#collection-method").selectOption("CashHandToHand");
  await page.locator("#collection-finance-account").selectOption(cashAccount.id);
  await page.locator("#collection-date").fill("2026-07-07");
  await page.locator("#collection-notes").fill(`${data.runId} full payment`);
  const [collectionResponse] = await Promise.all([
    page.waitForResponse((response) => response.url().includes("/api/v1/payments/collections") && response.request().method() === "POST"),
    page.locator("#unified-collection-form button[type='submit']").click()
  ]);
  expect(collectionResponse.ok()).toBeTruthy();
  const collection = await collectionResponse.json();
  await expect(page.locator("#notification-area")).toContainText(/collection submitted|approval/i);
  const paymentDetailResult = await apiJson(page, "GET", `/api/v1/payments/${payment.id}`);
  expect(paymentDetailResult.response.ok()).toBeTruthy();
  const submittedSubLog = (paymentDetailResult.data?.subLogs || paymentDetailResult.data?.SubLogs || []).find((item) => /PendingAdminReview|PendingReview/i.test(item.status));
  expect(submittedSubLog?.id).toBeTruthy();
  const approval = await apiJson(page, "POST", `/api/v1/payments/sub-logs/${submittedSubLog.id}/approve`);
  if (!approval.response.ok()) throw new Error(`Collection approval failed with ${approval.response.status()}: ${JSON.stringify(approval.data)}`);

  await gotoRoute(page, "/operations");
  const correctionSale = await createOperationDraft(page, {
    type: "WholesaleSale", skuText: data.product, quantity: "2", price: "125",
    stockText: data.mainLot, merchantText: data.merchant, sourceText: /Roxy|Main/i,
    paymentMethod: "CashHandToHand", financeAccountId: cashAccount.id
  });
  await runOperationActionByNumber(page, correctionSale.operationNumber, /Confirm/i);
  await runOperationActionByNumber(page, correctionSale.operationNumber, /Ship/i);
  await runOperationActionByNumber(page, correctionSale.operationNumber, /Complete/i);
  const correctionAllocations = await apiJson(page, "GET", `/api/v1/operations/${correctionSale.id}/allocations?page=1&pageSize=10`);
  expect(correctionAllocations.response.ok()).toBeTruthy();
  const correctionBatchId = (correctionAllocations.data?.items || correctionAllocations.data?.data || [])[0]?.batchId;
  expect(correctionBatchId).toBeTruthy();
  await createOperationDraft(page, {
    type: "Return",
    skuText: data.product,
    quantity: "1",
    lot: data.mainLot,
    expiry: data.expiry,
    merchantText: data.merchant,
    sourceText: /Roxy|Main/i,
    paymentMethod: "MerchantAccount",
    sourceOperationId: correctionSale.id,
    sourceOperationLineId: correctionSale.lines?.[0]?.id,
    sourceBatchId: correctionBatchId,
    sourceMerchantId: correctionSale.clientId
  });
  await runLatestOperationAction(page, "Return", /Confirm/i);
  await createChangeDraft(page, data, { id: correctionSale.id, lineId: correctionSale.lines?.[0]?.id, batchId: correctionBatchId, merchantId: correctionSale.clientId });
  await runLatestOperationAction(page, "Change", /Confirm/i);

  await gotoRoute(page, "/stocktakes");
  await expect(page.locator("#stocktake-create-form")).toBeVisible();
  await expect(page.locator("#stocktake-count")).not.toContainText(/Loading/i);
  await selectOptionByText(page.locator("#stocktake-location"), /Roxy|Main/i);
  await expect(page.locator("#stocktake-location option:checked")).toContainText(/Roxy|Main/i);
  await page.locator("#stocktake-notes").fill(`${data.runId} stocktake`);
  await Promise.all([
    page.waitForResponse((response) =>
      response.url().includes("/api/v1/stocktakes") &&
      response.request().method() === "POST" &&
      response.status() === 201),
    page.locator("#stocktake-create-form button[type='submit']").click()
  ]);
  await expect(page.locator("#stocktake-detail")).toContainText(/Draft|stocktake/i);
  const stocktakeLine = page.locator(".stocktake-line-row").first();
  await stocktakeLine.locator(".stocktake-line-search").fill(data.product);
  const stocktakeResults = stocktakeLine.locator(".op-line-search-results");
  await expect(stocktakeResults).toBeVisible();
  const stocktakeResult = stocktakeResults.locator(".op-line-search-result:not([disabled])").filter({ hasText: data.product }).first();
  await expect(stocktakeResult).toBeVisible({ timeout: 25_000 });
  await stocktakeResult.click();
  await expect(stocktakeLine.locator(".stocktake-line-sku")).not.toHaveValue("");
  await page.locator(".stocktake-line-lot").first().fill(data.mainLot);
  await page.locator(".stocktake-line-expiry").first().fill(data.expiry);
  await page.locator(".stocktake-line-pack-count").first().fill("1");
  await page.locator(".stocktake-line-piece-count").first().fill("0");
  await Promise.all([
    page.waitForResponse((response) =>
      response.url().includes("/api/v1/stocktakes/") &&
      response.url().includes("/lines") &&
      response.request().method() === "PUT" &&
      response.ok()),
    page.locator("#stocktake-lines-form button[type='submit']").click()
  ]);
  await expect(page.locator("#stocktake-detail")).toContainText(data.mainLot);
  await page.locator("#stocktake-confirm").click();
  await expect(page.locator("#notification-area")).toContainText(/Stocktake confirmed/i);
  await expect(page.locator("#stocktake-detail")).toContainText(/Confirmed|confirmed/i);

  await gotoRoute(page, "/crm");
  await openMerchantDetail(page, data);
  await expect(page.locator("#merchant-detail-panel")).toContainText(/Balance|Merchant Batch History|WholesaleSale/i);

  await gotoRoute(page, "/reports");
  await expectDownload(page, () => page.getByRole("button", { name: "CSV" }).first().click());
  await expect(page.locator("#report-exports")).toBeVisible();
});
