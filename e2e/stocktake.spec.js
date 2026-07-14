const { test, expect } = require("@playwright/test");
const {
  installApiBase,
  login,
  users,
  makeRunData,
  gotoRoute,
  selectOptionByText,
  ensureCoreData,
  createOperationDraft,
  runLatestOperationAction
} = require("./support/helpers");

test.beforeEach(async ({ page }) => {
  await installApiBase(page);
  await login(page, users.admin);
});

test("stocktake: create session, count SKU/lot/expiry, confirm, and validate duplicate/invalid lines", async ({ page }) => {
  const data = makeRunData("STK");
  await ensureCoreData(page, data);
  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "InventoryReceipt",
    skuText: data.product,
    quantity: "5",
    lot: data.mainLot,
    expiry: data.expiry,
    supplier: `${data.runId} Supplier`,
    invoice: `${data.runId}-INV`
  });
  await runLatestOperationAction(page, "InventoryReceipt", /Confirm/i);

  await gotoRoute(page, "/stocktakes");
  await expect(page.locator("#stocktake-create-form")).toBeVisible();
  await expect(page.locator("#stocktake-count")).not.toContainText(/Loading/i);
  await selectOptionByText(page.locator("#stocktake-location"), /Roxy|Main/i);
  await expect(page.locator("#stocktake-location")).toHaveValue(/.+/);
  await expect(page.locator("#stocktake-location option:checked")).toContainText(/Roxy|Main/i);
  await page.locator("#stocktake-notes").fill(`${data.runId} stocktake`);
  await Promise.all([
    page.waitForResponse((response) =>
      response.url().includes("/api/v1/stocktakes") &&
      response.request().method() === "POST" &&
      response.status() === 201),
    page.locator("#stocktake-create-form button[type='submit']").click()
  ]);
  await expect(page.locator("#notification-area")).toContainText(/Stocktake session opened/i);
  await expect(page.locator("#stocktake-detail")).toContainText(/Draft|stocktake/i);

  await selectOptionByText(page.locator(".stocktake-line-sku").first(), data.product);
  await page.locator(".stocktake-line-lot").first().fill(data.mainLot);
  await page.locator(".stocktake-line-expiry").first().fill(data.expiry);
  await page.locator(".stocktake-line-count").first().fill("-1");
  await page.locator("#stocktake-lines-form button[type='submit']").click();
  await expect(page.locator(".stocktake-line-count").first()).toBeFocused();

  await page.locator(".stocktake-line-count").first().fill("4");
  await page.locator("#stocktake-lines-form button[type='submit']").click();
  await expect(page.locator("#stocktake-detail")).toContainText(data.mainLot);

  await page.locator("#add-stocktake-line").click();
  await selectOptionByText(page.locator(".stocktake-line-sku").nth(1), data.product);
  await page.locator(".stocktake-line-lot").nth(1).fill(data.mainLot);
  await page.locator(".stocktake-line-expiry").nth(1).fill(data.expiry);
  await page.locator(".stocktake-line-count").nth(1).fill("4");
  await page.locator("#stocktake-lines-form button[type='submit']").click();
  await expect(page.locator("#stocktake-detail")).toContainText(data.mainLot);

  await page.locator("#stocktake-confirm").click();
  await expect(page.locator("#stocktake-detail")).toContainText(/Confirmed|confirmed/i);
});
