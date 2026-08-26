const { test, expect } = require("@playwright/test");
const { installApiBase, login, gotoRoute, expectNotice, users } = require("./support/helpers");

test.beforeEach(async ({ page }) => {
  await installApiBase(page);
  await login(page);
});

test("audit and accounts: duplicate usernames explain the conflict, protected accounts stay protected, and new activity is traceable", async ({ page }) => {
  const runId = `audit-user-${Date.now()}`;
  const fullName = `Audit User ${Date.now()}`;

  await gotoRoute(page, "/admin");
  const form = page.locator("#admin-create-user-form");
  await form.locator("[name='fullName']").fill(fullName);
  await form.locator("[name='username']").fill(runId);
  await form.locator("[name='role']").selectOption("Accountant");
  await form.locator("[name='password']").fill("Password123!");
  await form.locator("[name='confirmPassword']").fill("Password123!");
  await form.getByRole("button", { name: "Create employee account" }).click();
  await expectNotice(page, /created/i);

  await form.locator("[name='fullName']").fill(`${fullName} duplicate`);
  await form.locator("[name='username']").fill(runId.toUpperCase());
  await form.locator("[name='role']").selectOption("Accountant");
  await form.locator("[name='password']").fill("Password123!");
  await form.locator("[name='confirmPassword']").fill("Password123!");
  await form.getByRole("button", { name: "Create employee account" }).click();
  await expectNotice(page, "This username is already in use. Choose a different username.");

  await expect(page.locator("[data-admin-user-row]", { hasText: users.admin.username }).locator("[data-admin-delete-user]")).toBeDisabled();

  await gotoRoute(page, "/audit");
  await expect(page.locator("#audit-rows")).toContainText(/Create|POST/i);
  await page.locator("[data-audit-detail]").first().click();
  await expect(page.locator("#audit-detail")).toContainText(/Actor|Section|Record/i);

  await gotoRoute(page, "/admin");
  const createdRow = page.locator("[data-admin-user-row]", { hasText: runId });
  page.once("dialog", (dialog) => dialog.accept());
  await createdRow.locator("[data-admin-delete-user]").click();
  await expectNotice(page, /deleted/i);
});
