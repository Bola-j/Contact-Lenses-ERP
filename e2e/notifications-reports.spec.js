const { test, expect } = require("@playwright/test");
const {
  installApiBase,
  login,
  users,
  makeRunData,
  gotoRoute,
  expectNotice,
  expectDownload,
  ensureCoreData,
  createOperationDraft,
  runLatestOperationAction
} = require("./support/helpers");

test.beforeEach(async ({ page }) => {
  await installApiBase(page);
  await login(page, users.admin);
});

test("notifications: filters, pagination/read state, action links, and manual alert triggers", async ({ page }) => {
  await gotoRoute(page, "/notifications");
  await expect(page.locator("#notification-list")).toBeVisible();

  await page.getByRole("button", { name: "Low stock" }).click();
  await expectNotice(page, /Alert run matched/i);
  await page.getByRole("button", { name: "Outstanding balances" }).click();
  await expectNotice(page, /Alert run matched/i);
  await expect(page.locator("#notification-type-filter")).toContainText(/Low stock|Outstanding balances/i);
  const firstNotificationType = await page.locator("#notification-type-filter option").evaluateAll((options) =>
    options.map((option) => option.value).find(Boolean));
  expect(firstNotificationType).toBeTruthy();
  await page.locator("#notification-type-filter").selectOption(firstNotificationType);
  await expect(page.locator("#notification-count")).toContainText(/visible/i);

  await page.locator("#notification-unread-filter").check();
  await page.locator("#notifications-refresh").click();
  await expect(page.locator("#notification-list")).toBeVisible();
  if (await page.locator("#notification-pagination").isVisible().catch(() => false) && await page.locator("#notifications-next").isEnabled().catch(() => false)) {
    await page.locator("#notifications-next").click();
    await expect(page.locator("#notifications-page-label")).toContainText(/Page/i);
    await page.locator("#notifications-prev").click();
    await expect(page.locator("#notifications-page-label")).toContainText(/Page 1/i);
  }

  if (await page.locator("[data-toggle-notification]").count()) {
    await page.locator("[data-toggle-notification]").first().click();
    await expect(page.locator(".notification-details").first()).toBeVisible();
    await expect(page.locator(".notification-details").first()).toContainText(/Event location|Reference|Status/i);
    await expect(page.locator(".notification-card").first()).toContainText(/Open inventory|Open payments|Open operations|Open reports|Open CRM|Open stocktakes/i);
    await page.locator("[data-read-notification]").first().click().catch(() => undefined);
  }
  await page.locator("#mark-all-read").click();
  await expect(page.locator("#notification-unread-count")).toContainText(/\d+/);
});

test("reports: tables, CSV/PDF downloads, and export log surface", async ({ page }) => {
  const data = makeRunData("RPT");
  await ensureCoreData(page, data);
  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "InventoryReceipt",
    skuText: data.product,
    quantity: "3",
    lot: data.mainLot,
    expiry: data.expiry,
    supplier: `${data.runId} Supplier`,
    invoice: `${data.runId}-INV`
  });
  await runLatestOperationAction(page, "InventoryReceipt", /Confirm/i);

  await gotoRoute(page, "/reports");
  for (const id of ["#report-stock", "#report-operations", "#report-payments", "#report-balances", "#report-exports"]) {
    await expect(page.locator(id)).toBeVisible();
  }
  await expectDownload(page, () => page.getByRole("button", { name: "CSV" }).first().click());

  const operationSelect = page.locator("#report-operation-select");
  if (await operationSelect.locator("option").count() > 1) {
    await operationSelect.selectOption({ index: 1 });
    await expectDownload(page, () => page.locator("[data-pdf-report='operation-bill']").click());
  }
  const merchantSelect = page.locator("#report-merchant-select");
  if (await merchantSelect.locator("option").count() > 1) {
    await merchantSelect.selectOption({ index: 1 });
    await expectDownload(page, () => page.locator("[data-pdf-report='merchant-statement']").click());
  }
  await expect(page.locator("#report-exports")).toBeVisible();
});
