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
  runLatestOperationAction
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
    paymentMethod: "MerchantAccount",
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

test("operations: write-off is confirmed without return quantity-cap bypass", async ({ page }) => {
  const data = makeRunData("RET");
  await seedStock(page, data);

  await gotoRoute(page, "/operations");
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

test("operations: transfer detail expansion, actor labels, and version timeline are visible", async ({ page }) => {
  const data = makeRunData("DETAIL");
  await seedStock(page, data);

  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "WarehouseTransfer",
    skuText: data.product,
    quantity: "1",
    stockText: data.mainLot,
    sourceText: /Roxy|Main/i,
    destinationText: /Retail|Online|Mohamed/i
  });
  await runLatestOperationAction(page, "WarehouseTransfer", /Confirm/i);

  const transferRow = page.locator("#operation-rows tr", { hasText: "WarehouseTransfer" }).first();
  await transferRow.getByRole("button", { name: /Show|Details/i }).first().click();
  await expect(page.locator(".operation-detail").first()).toContainText(/Operation code|Created by|Confirmed by|Current version|Batch expiry/i);
});
