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
  ensureCoreData,
  createOperationDraft,
  runLatestOperationAction,
  runOperationActionByNumber,
  accountantIdByUsername,
  paymentForOperation,
  paymentQueueRowById,
  paymentHistoryRowById
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

  const sale = await createOperationDraft(page, {
    type: "WholesaleSale",
    skuText: data.product,
    quantity: "2",
    price: "125",
    stockText: data.mainLot,
    merchantText: data.merchant,
    paymentMethod: "Installment",
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
  const accountantId = await accountantIdByUsername(page);

  await gotoRoute(page, "/payments");
  const paymentRow = paymentQueueRowById(page, payment.id);
  await expect(paymentRow).toBeVisible();
  await page.locator("#payment-accountant").selectOption(accountantId);
  await paymentRow.getByRole("button", { name: /Assign/i }).click();
  await expectNotice(page, /assigned|Payment log/i);
  await logout(page);

  await login(page, users.accountant);
  await gotoRoute(page, "/payments");
  await paymentQueueRowById(page, payment.id).getByRole("button", { name: "Use" }).click();
  await page.locator("#payment-amount").fill("50");
  await page.locator("#payment-method").selectOption("CashTransaction");
  await page.locator("#payment-date").fill("2026-07-07");
  await page.locator("#payment-notes").fill(`${data.runId} rejected draft`);
  await page.locator("#payment-sublog-form button[type='submit']").click();
  await expectNotice(page, /Payment sub-log drafted/i);
  await logout(page);

  await login(page, users.admin);
  await gotoRoute(page, "/payments");
  await paymentQueueRowById(page, payment.id).getByRole("button", { name: "Details" }).click();
  let paymentDetail = page.locator(`[id="payment-detail-${payment.id}"]`);
  await paymentDetail.locator("[data-sublog-reject]").first().click();
  await page.locator(".dialog-input").fill("Bad receipt image");
  await page.locator(".dialog-card").getByRole("button", { name: /Continue/i }).click();
  await expectNotice(page, /Payment rejected/i);
  await expect(page.locator("#payment-rows")).toContainText(/PendingAccountant|Rejected/i);
  await logout(page);

  await login(page, users.accountant);
  await gotoRoute(page, "/payments");
  await paymentQueueRowById(page, payment.id).getByRole("button", { name: "Use" }).click();
  await page.locator("#payment-amount").fill("250");
  await page.locator("#payment-method").selectOption("CashTransaction");
  await page.locator("#payment-date").fill("2026-07-07");
  await page.locator("#payment-notes").fill(`${data.runId} final payment`);
  await page.locator("#payment-sublog-form button[type='submit']").click();
  await expectNotice(page, /Payment sub-log drafted/i);
  await logout(page);

  await login(page, users.admin);
  await gotoRoute(page, "/payments");
  await paymentQueueRowById(page, payment.id).getByRole("button", { name: "Details" }).click();
  paymentDetail = page.locator(`[id="payment-detail-${payment.id}"]`);
  await paymentDetail.locator("[data-sublog-approve]").first().click();
  await expectNotice(page, /Payment approved/i);
  await expect(paymentHistoryRowById(page, payment.id)).toContainText("Completed");
  await expect(paymentQueueRowById(page, payment.id)).toHaveCount(0);

  await selectOptionByText(page.locator("#payment-merchant"), data.merchant);
  await page.locator("#load-merchant-balance").click();
  await expect(page.locator("#merchant-balance-panel")).toContainText(/Remaining|Sales|Net collected/i);
});
