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

test("notifications: filters, read state, details, and manual alert triggers", async ({ page }) => {
  await gotoRoute(page, "/notifications");
  await expect(page.locator("#notification-list")).toBeVisible();

  await page.getByRole("button", { name: "Low stock" }).click();
  await expectNotice(page, /Alert run matched/i);
  await expect(page.locator("#notification-type-filter")).toContainText(/Low stock/i);
  const firstNotificationType = await page.locator("#notification-type-filter option").evaluateAll((options) =>
    options.map((option) => option.value).find(Boolean));
  expect(firstNotificationType).toBeTruthy();
  await page.locator("#notification-type-filter").selectOption(firstNotificationType);
  await expect(page.locator("#notification-count")).toContainText(/visible/i);

  // The manual scan may produce an already-read alert when it deduplicates an
  // existing low-stock condition. Exercise the card while the type filter is
  // active, then verify the unread-only filter independently; otherwise the
  // test can hold a locator for a card that that filter legitimately removes.
  const notificationCard = page.locator(".notification-card").first();
  await expect(notificationCard).toBeVisible();
  await notificationCard.locator("[data-toggle-notification]").click();
  await expect(notificationCard.locator(".notification-details")).toBeVisible();
  await expect(notificationCard.locator(".notification-details")).toContainText(/Event location|Reference|Status/i);
  const markRead = notificationCard.locator("[data-read-notification]");
  if (await markRead.count()) {
    await markRead.click();
  }

  await page.locator("#notification-unread-filter").check();
  await expect(page.locator("#notification-list")).toBeVisible();
  await page.locator("#notification-unread-filter").uncheck();
  await expect(notificationCard).toBeVisible();
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
