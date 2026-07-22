const { test, expect } = require("@playwright/test");
const { installApiBase, login, users, gotoRoute } = require("./support/helpers");

test.beforeEach(async ({ page }) => {
  await installApiBase(page);
});

test("mobile: warehouse clerk navigation, inventory, and operations remain usable", async ({ page }) => {
  await login(page, users.clerk);

  await test.step("Mobile shell exposes clerk routes and hides admin-only surfaces", async () => {
    await expect(page.locator(".sidebar")).toBeVisible();
    await expect(page.locator("#nav a", { hasText: "Inventory" })).toBeVisible();
    await expect(page.locator("#nav a", { hasText: "Operations" })).toBeVisible();
    await expect(page.locator("#nav a", { hasText: "Supply" })).toHaveCount(0);
    await expect(page.locator("#nav a", { hasText: "Admin" })).toHaveCount(0);
  });

  await test.step("Inventory page loads without blocking table access", async () => {
    await gotoRoute(page, "/inventory");
    await expect(page.locator("#inventory-balances")).toBeVisible();
    await expect(page.locator(".table-wrap").first()).toBeVisible();
  });

  await test.step("Operations page shows scoped clerk workspace", async () => {
    await gotoRoute(page, "/operations");
    await expect(page.locator("#operation-rows")).toBeVisible();
    await expect(page.locator("#operation-form")).toBeVisible();
    await expect(page.locator("#page-title")).toContainText("Operations");
  });
});
