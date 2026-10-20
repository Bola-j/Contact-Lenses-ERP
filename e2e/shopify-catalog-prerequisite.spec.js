const fs = require("node:fs/promises");
const path = require("node:path");
const { test, expect } = require("@playwright/test");
const {
  users, installApiBase, login, makeRunData, createCatalogFixture, createSupplyReceipt
} = require("./support/helpers");

test.use({ channel: "chrome" });

const enabled = process.env.LENSEE_E2E_SHOPIFY_PREREQUISITE === "1";
const roleName = process.env.LENSEE_E2E_SHOPIFY_ROLE === "ERPAdmin" ? "ERPAdmin" : "Admin";
const credential = roleName === "ERPAdmin" ? users.erpAdmin : users.admin;
const stockLocation = process.env.LENSEE_E2E_SHOPIFY_STOCK_LOCATION || /TEST SubWarehouse/i;

test.describe(`Shopify catalog prerequisite (${roleName})`, () => {
  test.skip(!enabled, "Requires the isolated disposable Admin or ERPAdmin QA environment.");

  test.beforeEach(async ({ page }) => {
    await installApiBase(page);
    await login(page, credential);
  });

  test("create an ERP product and SKU through the UI and report the captured SKU response", async ({ page }) => {
    const data = makeRunData(`SHOPIFY-${roleName}`);
    const createdSku = await createCatalogFixture(page, data, { captureSkuResponse: true });

    expect(createdSku).toBeTruthy();
    expect(createdSku.skuCode).toEqual(expect.any(String));
    expect(createdSku.skuCode.trim()).not.toBe("");
    await expect(page.locator("#catalog-detail")).toContainText(createdSku.skuCode);
    console.log(`SHOPIFY_UI_CATALOG_SKU role=${roleName} skuCode=${createdSku.skuCode}`);
    const outputPath = process.env.LENSEE_E2E_SHOPIFY_PREREQUISITE_OUTPUT ||
      path.resolve(`artifacts/overnight/shopify-ui/catalog-prerequisite-${roleName.toLowerCase()}.json`);
    await fs.mkdir(path.dirname(outputPath), { recursive: true });
    await fs.writeFile(outputPath, `${JSON.stringify({ role: roleName, productName: data.product, skuCode: createdSku.skuCode, createdFrom: "real UI POST response" }, null, 2)}\n`, "utf8");

    const shipment = await createSupplyReceipt(page, {
      supplier: `${data.runId} supplier`,
      invoice: `${data.runId}-invoice`,
      skuText: createdSku.skuCode,
      quantity: "20",
      lot: `${data.runId}-LOT`,
      expiry: data.expiry,
      destinationText: stockLocation
    });

    const result = {
      role: roleName,
      productName: data.product,
      skuCode: createdSku.skuCode,
      stockReceipt: shipment.shipmentNumber,
      stockLocation: shipment.destinationLocationName || String(stockLocation),
      stockedPacks: 20,
      createdFrom: "real UI POST response"
    };
    await fs.writeFile(outputPath, `${JSON.stringify(result, null, 2)}\n`, "utf8");
  });
});
