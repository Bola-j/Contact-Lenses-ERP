const { test, expect } = require("@playwright/test");
const { installApiBase, gotoLogin, login } = require("./support/helpers");

test.beforeEach(async ({ page }) => {
  await installApiBase(page);
});

test("friendly identifiers: UUID-like visible text is masked without changing DOM data", async ({ page }) => {
  const uuid = "a9cc07e8-7c36-4eda-b2f1-a222d1d62b30";
  await gotoLogin(page);

  await page.evaluate((value) => {
    const row = document.createElement("div");
    row.dataset.internalId = value;
    row.textContent = `Saved record ${value}`;
    document.querySelector("#view").appendChild(row);
  }, uuid);

  await expect(page.locator("#view")).not.toContainText(uuid);
  await expect(page.locator("#view")).toContainText(/REF-[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}/);
  await expect(page.locator("#view div[data-internal-id]")).toHaveAttribute("data-internal-id", uuid);
});

test("report search pickers stay collapsed until queried and close when focus moves", async ({ page }) => {
  const auth = { accessToken: "test-token", user: { userId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", role: "Admin", locationId: null } };
  let signedIn = false;
  await page.route("**/api/v1/**", async (route) => {
    const path = new URL(route.request().url()).pathname;
    const json = (body, status = 200) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
    if (path === "/api/v1/auth/refresh") return signedIn ? json(auth) : json({ title: "Unauthorized" }, 401);
    if (path === "/api/v1/auth/login") {
      signedIn = true;
      return json(auth);
    }
    if (path === "/api/v1/notifications/unread-count") return json({ count: 0 });
    if (path === "/api/v1/reports/operations") return json([{ id: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", operationNumber: "OP-TEST-100", operationType: "RetailSale", status: "Completed", clientName: "Test merchant", quantity: 1, total: 50, createdAt: "2026-08-16T12:00:00Z" }]);
    if (["/api/v1/reports/stock", "/api/v1/reports/payments", "/api/v1/reports/supply", "/api/v1/reports/merchant-balances"].includes(path)) return json([]);
    if (path === "/api/v1/stocktakes") return json({ items: [], page: 1, pageSize: 100, totalCount: 0 });
    if (path === "/api/v1/reports/exports") return json({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    return json([]);
  });

  await login(page, { username: "test-admin", password: "test-password" });
  await page.goto("/#/reports");
  await expect(page.locator("#report-picker-operation-bill-search")).toBeVisible();

  const operationResults = page.locator("#report-picker-operation-bill-results");
  const paymentSearch = page.locator("#report-picker-payment-receipt-search");
  await expect(operationResults).toBeHidden();
  await page.locator("#report-picker-operation-bill-search").fill("OP");
  await expect(operationResults).toBeVisible();
  await paymentSearch.focus();
  await expect(operationResults).toBeHidden();
  await expect(page.locator("#report-picker-payment-receipt-results")).toBeHidden();
});
