const { test, expect } = require("@playwright/test");
const {
  installApiBase,
  login,
  users,
  makeRunData,
  gotoRoute,
  selectOptionByText,
  ensureCoreData,
  selectOperationLineSku
} = require("./support/helpers");

test.beforeEach(async ({ page }) => {
  await installApiBase(page);
});

test("Operations return source is visible and required when no merchant is selected", async ({ page }) => {
  const locationId = "11111111-1111-1111-1111-111111111111";
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    const json = (body, status = 200) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
    if (url.pathname === "/api/v1/auth/refresh") {
      return json({ accessToken: "return-ui-test", user: { userId: "test-admin", username: "admin", fullName: "Admin", role: "Admin" } });
    }
    if (url.pathname === "/api/v1/inventory/locations") {
      return json([{ id: locationId, name: "Main Warehouse", locationType: "MainWarehouse", isActive: true }]);
    }
    if (url.pathname === "/api/v1/crm/merchants") return json({ items: [], totalCount: 0 });
    if (url.pathname === "/api/v1/operations") return json({ items: [], totalCount: 0, totalPages: 1, page: 1 });
    if (url.pathname === "/api/v1/notifications/unread-count") return json({ count: 0 });
    return json([]);
  });

  await page.goto("/en/#/operations", { waitUntil: "domcontentloaded" });
  await expect(page.locator("#op-type")).toBeVisible();
  await page.locator("#op-type").selectOption("Return");

  const sourceSale = page.locator("#op-return-source");
  await expect(sourceSale).toBeVisible();
  await expect(sourceSale).toHaveJSProperty("required", true);
  await expect(page.locator(".op-line-source-line-field").first()).toBeVisible();

  await page.locator("#op-type").selectOption("WarehouseTransfer");
  await expect(sourceSale).toBeHidden();
});

test("Operations return editor makes sale lineage conditional on merchant selection", async ({ page }) => {
  const data = makeRunData("RETURN-UI");
  await login(page, users.admin);
  await ensureCoreData(page, data);
  await gotoRoute(page, "/operations");

  await page.locator("#op-type").selectOption("Return");
  const sourceSale = page.locator("#op-return-source");
  await expect(sourceSale).toBeVisible();
  await expect(sourceSale).toHaveJSProperty("required", true);
  await expect(page.locator(".op-line-source-line-field").first()).toBeVisible();

  await selectOptionByText(page.locator("#op-merchant"), data.merchant);
  await expect(sourceSale).toHaveJSProperty("required", false);
  await selectOptionByText(page.locator("#op-source"), /Roxy|Main/i);
  const row = page.locator(".line-editor-row").first();
  await selectOperationLineSku(row, data.product);
  await expect(row.locator(".op-line-source-line-field")).toBeHidden();
  await expect.poll(async () => row.locator(".op-line-stock-option option").evaluateAll((options) =>
    options.some((option) => option.value === "__new_batch__")
  ), { timeout: 20_000 }).toBeTruthy();

  await page.locator("#op-merchant").selectOption("");
  await expect(sourceSale).toHaveJSProperty("required", true);
  await expect(row.locator(".op-line-source-line")).toHaveJSProperty("required", true);
});

test("Operations records initial payment method without choosing a Finance receiving account", async ({ page }) => {
  const data = makeRunData("OP-ACCOUNT-UI");
  const e2eApi = process.env.LENSEE_E2E_API_URL || "http://127.0.0.1:55000";
  await page.route(/\/api\//, async (route) => {
    const source = new URL(route.request().url());
    const response = await route.fetch({ url: `${e2eApi}${source.pathname}${source.search}` });
    await route.fulfill({ response });
  });
  await page.route("**/health", async (route) => {
    const response = await route.fetch({ url: `${e2eApi}/health` });
    await route.fulfill({ response });
  });
  await login(page, users.admin);
  await ensureCoreData(page, data);
  await gotoRoute(page, "/operations");

  await page.locator("#op-type").selectOption("RetailSale");
  const payment = page.locator("#op-payment");
  await expect(page.locator("#op-finance-account")).toHaveCount(0);
  await payment.selectOption("BankTransfer");
  await selectOptionByText(page.locator("#op-merchant"), data.merchant);
  await payment.selectOption("Wallet");
  await expect(payment).toHaveValue("Wallet");
  await expect(page.locator("#op-finance-account")).toHaveCount(0);
});
