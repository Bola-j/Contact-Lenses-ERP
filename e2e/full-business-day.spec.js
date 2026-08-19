const { test, expect } = require("@playwright/test");
const {
  installApiBase,
  login,
  logout,
  users,
  makeRunData,
  gotoRoute,
  selectOptionByText,
  ensureCoreData,
  openMerchantDetail,
  createOperationDraft,
  createSupplyReceipt,
  runLatestOperationAction,
  createChangeDraft,
  expectDownload
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

  await createOperationDraft(page, {
    type: "WholesaleSale",
    skuText: data.product,
    quantity: "2",
    price: "125",
    stockText: data.mainLot,
    merchantText: data.merchant,
    paymentMethod: "Installment",
    sourceText: /Roxy|Main/i
  });
  await runLatestOperationAction(page, "WholesaleSale", /Confirm/i);
  await runLatestOperationAction(page, "WholesaleSale", /Ship/i);
  await runLatestOperationAction(page, "WholesaleSale", /Complete/i);

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
  await expect(page.locator("#payment-rows")).toContainText("Installment");
  await selectOptionByText(page.locator("#payment-accountant"), /accountant/i);
  await page.locator("#payment-rows tr", { hasText: "Installment" }).first().getByRole("button", { name: "Assign" }).click();
  await expect(page.locator("#notification-area")).toContainText(/assigned|Payment log/i);
  await logout(page);

  await login(page, users.accountant);
  await gotoRoute(page, "/payments");
  await page.locator("#payment-rows tr", { hasText: "Installment" }).first().getByRole("button", { name: "Use" }).click();
  await page.locator("#payment-amount").fill("250");
  await page.locator("#payment-method").selectOption("CashTransaction");
  await page.locator("#payment-date").fill("2026-07-07");
  await page.locator("#payment-notes").fill(`${data.runId} full payment`);
  await page.locator("#payment-sublog-form button[type='submit']").click();
  await expect(page.locator("#notification-area")).toContainText(/Payment sub-log drafted/i);
  await logout(page);

  await login(page, users.admin);
  await gotoRoute(page, "/payments");
  await page.locator("#payment-rows tr", { hasText: "PendingAdminReview" }).first().getByRole("button", { name: "Details" }).click();
  await expect(page.locator("[data-sublog-approve]").first()).toBeVisible();
  await page.locator("[data-sublog-approve]").first().click({ force: true });
  await expect(page.locator("#notification-area")).toContainText(/Payment approved/i);

  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "Return",
    skuText: data.product,
    quantity: "1",
    lot: data.mainLot,
    expiry: data.expiry,
    merchantText: data.merchant,
    sourceText: /Roxy|Main/i,
    paymentMethod: "CashHandToHand"
  });
  await runLatestOperationAction(page, "Return", /Confirm/i);
  await createChangeDraft(page, data);
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
  await selectOptionByText(page.locator(".stocktake-line-sku").first(), data.product);
  await page.locator(".stocktake-line-lot").first().fill(data.mainLot);
  await page.locator(".stocktake-line-expiry").first().fill(data.expiry);
  await page.locator(".stocktake-line-count").first().fill("1");
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
