const { test, expect } = require("@playwright/test");
const {
  installApiBase,
  login,
  users,
  makeRunData,
  gotoRoute,
  expectNotice,
  selectOptionByText,
  createCatalogFixture,
  createCrmFixture,
  openMerchantDetail
} = require("./support/helpers");

test.beforeEach(async ({ page }) => {
  await installApiBase(page);
  await login(page, users.admin);
});

test("catalog: create, validate, update, deactivate, and reactivate product/SKU lifecycle", async ({ page }) => {
  const data = makeRunData("CAT");
  await createCatalogFixture(page, data);

  await gotoRoute(page, "/catalog");
  await page.locator("#catalog-search").fill(data.product);
  await page.locator("#catalog-products tr", { hasText: data.product }).first().click();
  await expect(page.locator("#catalog-detail")).toContainText(data.skuColor);

  await page.locator("#product-name").fill("");
  await page.locator("#product-form button[type='submit']").click();
  await expect.poll(async () => await page.locator("#product-name").evaluate((input) => input.validity.valueMissing)).toBeTruthy();

  await page.locator("#catalog-products tr", { hasText: data.product }).first().click();
  await page.locator("#edit-product").click();
  await page.locator("#product-name").fill(`${data.product} Updated`);
  await page.locator("#product-form button[type='submit']").click();
  await expectNotice(page, /Product saved/i);
  await page.locator("#catalog-search").fill(`${data.product} Updated`);
  await expect(page.locator("#catalog-products")).toContainText("Updated");

  await page.locator("#catalog-products tr", { hasText: "Updated" }).first().click();
  await page.locator("#toggle-product").click();
  await expectNotice(page, /Product deactivated|Product updated|Product saved|Product status updated/i);
  await page.locator("#catalog-include-inactive").check();
  await expect(page.locator("#catalog-detail")).toContainText(/Inactive|Reactivate/i);
  await page.locator("#toggle-product").click();
  await expect(page.locator("#catalog-detail")).toContainText(/Active|Deactivate/i);

  const skuToggle = page.locator("[data-toggle-sku]").first();
  await expect(skuToggle).toBeVisible();
  await skuToggle.click();
  await expect(page.locator("#catalog-detail")).toContainText(/Inactive|Reactivate/i);
  await page.locator("[data-toggle-sku]").first().click();
  await expect(page.locator("#catalog-detail")).toContainText(/Active|Deactivate/i);
});

test("crm: merchant and representative lifecycle, validation, notes, and profile panels", async ({ page }) => {
  const data = makeRunData("CRM");
  await createCrmFixture(page, data);

  await gotoRoute(page, "/crm");
  await page.locator("#merchant-name").fill("");
  await page.locator("#merchant-form button[type='submit']").click();
  await expect.poll(async () => await page.locator("#merchant-name").evaluate((input) => input.validity.valueMissing)).toBeTruthy();

  await page.locator("#merchant-rows tr", { hasText: data.merchant }).getByRole("button", { name: /Edit/i }).click();
  await expect(page.locator("#merchant-save-button")).toContainText(/Update merchant/i);
  await page.locator("#merchant-name").fill(`${data.merchant} Updated`);
  await page.locator("#merchant-form button[type='submit']").click();
  await expectNotice(page, /Merchant (created|saved|updated)/i);
  const updatedMerchant = `${data.merchant} Updated`;
  await expect(page.locator("#merchant-rows")).toContainText(updatedMerchant);

  const merchantRow = page.locator("#merchant-rows tr", { hasText: updatedMerchant }).first();
  await merchantRow.getByRole("button", { name: /Deactivate/i }).click();
  await expect(merchantRow).toContainText(/Inactive|Reactivate/i);
  await merchantRow.getByRole("button", { name: /Reactivate/i }).click();
  await expect(merchantRow).toContainText(/Active|Deactivate/i);

  await page.locator("#rep-rows tr", { hasText: data.representative }).getByRole("button", { name: /Edit/i }).click();
  await expect(page.locator("#rep-save-button")).toContainText(/Update representative/i);
  await page.locator("#rep-name").fill(`${data.representative} Updated`);
  await page.locator("#rep-form button[type='submit']").click();
  await expectNotice(page, /Representative (created|saved|updated)/i);
  const updatedRepresentative = `${data.representative} Updated`;
  const representativeRow = page.locator("#rep-rows tr", { hasText: updatedRepresentative }).first();
  await representativeRow.getByRole("button", { name: /Deactivate/i }).click();
  await expect(representativeRow).toContainText(/Inactive|Reactivate/i);

  await openMerchantDetail(page, { ...data, merchant: updatedMerchant });
  await expect(page.locator("#merchant-detail-panel")).toContainText(/Eligibility ledger|Recent operations|Balance/i);
  await merchantRow.getByRole("button", { name: /Add note/i }).click();
  await page.locator(".dialog-input").fill(`${data.runId} note <script>alert(1)</script>`);
  await page.locator(".dialog-card").getByRole("button", { name: /Continue/i }).click();
  await expectNotice(page, /Note added/i);
});
