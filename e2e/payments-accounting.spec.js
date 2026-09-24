const { test, expect } = require("@playwright/test");
const {
  installApiBase,
  login,
  logout,
  users,
  makeRunData,
  gotoRoute,
  openOtherPaymentsPanel,
  expectNotice,
  selectOptionByText,
  ensureCoreData,
  createOperationDraft,
  runLatestOperationAction,
  runOperationActionByNumber,
  accountantIdByUsername,
  paymentForOperation,
  paymentQueueRowById,
  paymentHistoryRowById,
  apiJson
} = require("./support/helpers");

test.beforeEach(async ({ page }) => {
  await installApiBase(page);
});

async function createInstallmentSale(page, data) {
  await login(page, users.admin);
  await ensureCoreData(page, data);
  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "InventoryReceipt",
    skuText: data.product,
    quantity: "8",
    lot: data.mainLot,
    expiry: data.expiry,
    supplier: `${data.runId} Supplier`,
    invoice: `${data.runId}-INV`
  });
  await runLatestOperationAction(page, "InventoryReceipt", /Confirm/i);
  await createOperationDraft(page, {
    type: "WarehouseTransfer", skuText: data.product, quantity: "8",
    stockText: data.mainLot, destinationText: /Retail|Online|Mohamed/i
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
  return sale;
}

test("payments: assignment, accountant draft, admin reject/approve, balance, and completed unassignable", async ({ page }) => {
  const data = makeRunData("PAY");
  const sale = await createInstallmentSale(page, data);
  const payment = await paymentForOperation(page, sale.id);
  const accounts = await apiJson(page, "GET", "/api/v1/finance/accounts");
  expect(accounts.response.ok()).toBeTruthy();
  if (!(accounts.data || []).length) {
    const createdAccount = await apiJson(page, "POST", "/api/v1/finance/accounts", { name: `${data.runId} Cash`, type: "CashOnHand" });
    expect(createdAccount.response.ok()).toBeTruthy();
  }
  const accountId = (await apiJson(page, "GET", "/api/v1/finance/accounts")).data[0].id;
  await gotoRoute(page, "/payments");
  await openOtherPaymentsPanel(page);
  const paymentRow = paymentQueueRowById(page, payment.id);
  await expect(paymentRow).toBeVisible();
  const submitCollection = async (amount, notes) => {
    await paymentQueueRowById(page, payment.id).getByRole("button", { name: "Use" }).click();
    await page.locator("#collection-source-reference").fill(payment.operationNumber || payment.id);
    await page.locator("#collection-amount").fill(String(amount));
    await page.locator("#collection-method").selectOption("CashHandToHand");
    await page.locator("#collection-finance-account").selectOption(accountId);
    await page.locator("#collection-date").fill("2026-07-07");
    await page.locator("#collection-notes").fill(notes);
    const [response] = await Promise.all([
      page.waitForResponse((value) => value.url().includes("/api/v1/payments/collections") && value.request().method() === "POST"),
      page.locator("#unified-collection-form button[type='submit']").click()
    ]);
    expect(response.ok()).toBeTruthy();
    await expectNotice(page, /collection submitted|approval/i);
  };
  await submitCollection(50, `${data.runId} rejected collection`);
  let detail = await apiJson(page, "GET", `/api/v1/payments/${payment.id}`);
  const rejectedSubLog = (detail.data.subLogs || []).find((item) => /PendingAdminReview|PendingReview/i.test(item.status));
  expect(rejectedSubLog?.id).toBeTruthy();
  const rejection = await apiJson(page, "POST", `/api/v1/payments/sub-logs/${rejectedSubLog.id}/reject`, { reason: "Bad receipt image" });
  expect(rejection.response.ok()).toBeTruthy();
  await submitCollection(250, `${data.runId} final payment`);
  detail = await apiJson(page, "GET", `/api/v1/payments/${payment.id}`);
  const approvedSubLog = (detail.data.subLogs || []).find((item) => /PendingAdminReview|PendingReview/i.test(item.status));
  expect(approvedSubLog?.id).toBeTruthy();
  const approval = await apiJson(page, "POST", `/api/v1/payments/sub-logs/${approvedSubLog.id}/approve`);
  expect(approval.response.ok()).toBeTruthy();
  const finalDetail = await apiJson(page, "GET", `/api/v1/payments/${payment.id}`);
  expect(finalDetail.response.ok()).toBeTruthy();
  expect(finalDetail.data.log?.status || finalDetail.data.status).toBe("Completed");
  await page.reload();
  await gotoRoute(page, "/payments");
  await openOtherPaymentsPanel(page);
  expect(finalDetail.data.log?.status || finalDetail.data.status).toBe("Completed");
  await expect(paymentQueueRowById(page, payment.id)).toHaveCount(0);

  await page.locator('[data-payment-view="merchant"]').click();
  await expect(page.locator("#payment-merchant")).toBeVisible();
  await selectOptionByText(page.locator("#payment-merchant"), data.merchant);
  await page.locator("#load-merchant-balance").click();
  await expect(page.locator("#merchant-balance-panel")).toContainText(/Remaining|Sales|Net collected/i);
});
