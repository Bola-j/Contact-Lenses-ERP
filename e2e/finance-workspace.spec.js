const { test, expect } = require("@playwright/test");
const { users, login, gotoRoute } = require("./support/helpers");

for (const [role, user] of [["Admin", users.admin], ["C-Level", users.clevel]]) {
  test(`Finance module stays hidden in the UI for ${role}`, async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem("lensee.language", "en"));
    await login(page, user);
    await expect(page.locator("#nav a[href='#/finance']")).toHaveCount(0);
    await gotoRoute(page, "/finance");
    await expect(page).toHaveURL(/#\/dashboard$/);
    await expect(page.locator("#finance-account-form, #finance-opening-form, #finance-expense-form, #finance-withdrawal-form")).toHaveCount(0);
  });
}
