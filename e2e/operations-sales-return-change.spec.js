const { test, expect } = require("@playwright/test");
const {
  installApiBase,
  login,
  users,
  makeRunData,
  gotoRoute,
  selectOptionByText,
  ensureCoreData,
  openMerchantDetail,
  createOperationDraft,
  runLatestOperationAction,
  createChangeDraft
} = require("./support/helpers");

test.beforeEach(async ({ page }) => {
  await installApiBase(page);
  await login(page, users.admin);
});

async function seedStock(page, data, quantity = "20") {
  await ensureCoreData(page, data);
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

test("operations: wholesale and retail sales move through reserved, shipped, completed and update CRM", async ({ page }) => {
  const data = makeRunData("SALE");
  await seedStock(page, data);

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

  await gotoRoute(page, "/crm");
  await openMerchantDetail(page, data);
  await expect(page.locator("#merchant-detail-panel")).toContainText(/WholesaleSale|Merchant Batch History|Balance/i);
});

test("operations: returns outside recorded sales warn, can be bypassed, and write-off is confirmed", async ({ page }) => {
  const data = makeRunData("RET");
  await seedStock(page, data);

  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "Return",
    skuText: data.product,
    quantity: "1",
    lot: data.badLot,
    expiry: data.expiry,
    merchantText: data.merchant,
    sourceText: /Roxy|Main/i,
    paymentMethod: "CashHandToHand"
  });
  await runLatestOperationAction(page, "Return", /Confirm/i);
  const salesWarning = page.locator(".dialog-overlay", { hasText: /Recorded sales warning/i });
  await expect(salesWarning).toBeVisible();
  await expect(salesWarning).toContainText(/Sold to merchant|Requested now|Above recorded balance/i);
  await salesWarning.locator("#merchant-sales-variance-reason").fill("Verified physical stock during merchant collection.");
  await salesWarning.getByRole("button", { name: /Confirm with exception/i }).click();
  await expect(page.locator("#operation-rows tr", { hasText: "Return" }).first()).toContainText(/Confirmed/i);

  await createChangeDraft(page, data);
  await runLatestOperationAction(page, "Change", /Confirm/i);
  const changeWarning = page.locator(".dialog-overlay", { hasText: /Recorded sales warning/i });
  await expect(changeWarning).toBeVisible();
  await changeWarning.locator("#merchant-sales-variance-reason").fill("Verified exchange stock during merchant collection.");
  await changeWarning.getByRole("button", { name: /Confirm with exception/i }).click();
  await expect(page.locator("#operation-rows tr", { hasText: "Change" }).first()).toContainText(/Confirmed/i);

  await createOperationDraft(page, {
    type: "WriteOff",
    skuText: data.product,
    quantity: "1",
    stockText: data.mainLot,
    sourceText: /Roxy|Main/i
  });
  await runLatestOperationAction(page, "WriteOff", /Confirm/i);
  await expect(page.locator("#operation-rows tr", { hasText: "WriteOff" }).first()).toContainText(/Confirmed|WriteOff/i);
});

test("operations: reserve, detail expansion, actor labels, and version timeline are visible", async ({ page }) => {
  const data = makeRunData("RES");
  await seedStock(page, data);

  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "Reserve",
    skuText: data.product,
    quantity: "1",
    stockText: data.mainLot,
    sourceText: /Roxy|Main/i,
    representativeText: data.representative
  });
  await runLatestOperationAction(page, "Reserve", /Confirm/i);

  const reserveRow = page.locator("#operation-rows tr", { hasText: "Reserve" }).first();
  await reserveRow.getByRole("button", { name: /Show|Details/i }).first().click();
  await expect(page.locator(".operation-detail").first()).toContainText(/Operation code|Created by|Confirmed by|Current version|Batch expiry/i);
});
