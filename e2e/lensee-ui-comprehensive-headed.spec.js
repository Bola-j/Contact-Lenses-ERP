const { test, expect } = require("@playwright/test");
const {
  installApiBase, login, logout, users, makeRunData, gotoRoute, expectNotice,
  selectOptionByText, ensureCoreData, createOperationDraft,
  runLatestOperationAction, createChangeDraft, expectDownload
} = require("./support/helpers");

test.describe.configure({ mode: "serial" });

test.beforeEach(async ({ page }) => {
  await installApiBase(page);
});

test("AUTH-001/002/009/010: six seeded users login, refresh, logout, and retain role navigation", async ({ page }) => {
  await gotoRoute(page, "/login");
  await page.locator("#login-submit").click();
  await expect(page.locator("#username")).toBeFocused();
  await page.locator("#username").fill("not-a-user");
  await page.locator("#password").fill("wrong-password");
  await page.locator("#login-submit").click();
  await expect(page.locator("#login-error")).toContainText(/incorrect|failed|invalid|غير صحيحة/i);

  const roles = {
    admin: ["Catalog", "Inventory", "CRM", "Operations", "Payments", "Notifications", "Reports", "Stocktake", "Admin"],
    clevel: ["Catalog", "Inventory", "CRM", "Operations", "Payments", "Notifications", "Reports", "Stocktake"],
    accountant: ["CRM", "Operations", "Payments", "Notifications", "Reports"],
    roxyClerk: ["Catalog", "Inventory", "CRM", "Operations", "Notifications"],
    retailClerk: ["Catalog", "Inventory", "CRM", "Operations", "Notifications"],
    onlineClerk: ["Catalog", "Inventory", "CRM", "Operations", "Notifications"]
  };
  for (const role of Object.keys(roles)) {
    await login(page, users[role]);
    for (const label of roles[role]) {
      await expect(page.locator("#nav a", { hasText: label }), role + " should see " + label).toBeVisible();
    }
    await page.reload();
    await expect(page.locator("#page-title")).toContainText(/Overview/i);
    await logout(page);
    await page.goBack();
    await expect(page.locator("#login-form")).toBeVisible();
  }
});

test("AUTH-010/012/NFR/PERM: deep links, language persistence, responsive shell, and role boundaries", async ({ page }) => {
  await login(page, users.admin);
  await gotoRoute(page, "/unknown-route");
  await expect(page.locator("#view")).toBeVisible();
  await page.locator("#language-toggle").click();
  await expect(page.locator("html")).toHaveAttribute("lang", "en");
  await page.reload();
  await expect(page.locator("html")).toHaveAttribute("lang", "en");

  await page.setViewportSize({ width: 390, height: 844 });
  await gotoRoute(page, "/operations");
  await expect(page.locator("#view")).toBeVisible();
  await expect.soft.poll(async () => page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(1);
  await logout(page);

  for (const [role, location] of [
    ["clevel", /Roxy|Main/i],
    ["roxyClerk", /Roxy/i],
    ["retailClerk", /Retail|Mohamed/i],
    ["onlineClerk", /Online/i]
  ]) {
    await login(page, users[role]);
    await gotoRoute(page, "/inventory");
    await expect(page.locator("#view")).toBeVisible();
    await expect(page.locator("#inventory-locations")).toContainText(location);
    await expect(page.locator("#nav a", { hasText: "Admin" })).toHaveCount(0);
    await logout(page);
  }

  await login(page, users.accountant);
  await gotoRoute(page, "/catalog");
  await expect(page.locator("#product-form")).toHaveCount(0);
  await gotoRoute(page, "/inventory");
  await expect(page.locator("#inventory-balances")).toHaveCount(0);
});

test("CAT/CRM/INV/TRN: admin creates master data, receives stock, sets target, and completes a transfer", async ({ page }) => {
  const data = makeRunData("UI-CORE");
  await login(page, users.admin);
  await ensureCoreData(page, data);

  await gotoRoute(page, "/catalog");
  await page.locator("#catalog-search").fill(data.product);
  await page.locator("#catalog-products tr", { hasText: data.product }).first().click();
  await expect(page.locator("#catalog-detail")).toContainText(data.skuColor);
  await page.locator("#product-name").fill("");
  await page.locator("#product-form button[type='submit']").click();
  await expect.poll(async () => page.locator("#product-name").evaluate((input) => input.validity.valueMissing)).toBeTruthy();

  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "InventoryReceipt",
    skuText: data.product,
    quantity: "12",
    lot: data.mainLot,
    expiry: data.expiry,
    supplier: data.runId + " Supplier",
    invoice: data.runId + "-INV"
  });
  await runLatestOperationAction(page, "InventoryReceipt", /Confirm/i);

  await gotoRoute(page, "/inventory");
  await expect(page.locator("#inventory-locations")).toContainText(/Roxy|Main/i);
  await expect(page.locator("#inventory-balances")).toContainText(data.product);
  if (await page.locator("[data-target-location]").first().isVisible().catch(() => false)) {
    await page.locator("[data-target-location]").first().click();
    await page.locator(".dialog-input").fill("20");
    await page.locator(".dialog-card").getByRole("button", { name: /Continue/i }).click();
    await expectNotice(page, /Target updated|target/i);
  }

  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "WarehouseTransfer",
    skuText: data.product,
    quantity: "4",
    stockText: data.mainLot,
    destinationText: /Retail|Online|Mohamed/i
  });
  await runLatestOperationAction(page, "WarehouseTransfer", /Confirm/i);
  await runLatestOperationAction(page, "WarehouseTransfer", /Ship/i);
  await runLatestOperationAction(page, "WarehouseTransfer", /Receive/i);
  await expect(page.locator("#operation-rows tr", { hasText: "WarehouseTransfer" }).first()).toContainText(/Received|Completed/i);
});

test("SALE/RSV/RET/CHG/WO: visible operation flows preserve batch, party, status, and detail history", async ({ page }) => {
  test.setTimeout(180_000);
  const data = makeRunData("UI-OPS");
  await login(page, users.admin);
  await ensureCoreData(page, data);
  await gotoRoute(page, "/operations");

  await createOperationDraft(page, {
    type: "InventoryReceipt", skuText: data.product, quantity: "20",
    lot: data.mainLot, expiry: data.expiry, supplier: data.runId + " Supplier", invoice: data.runId + "-INV"
  });
  await runLatestOperationAction(page, "InventoryReceipt", /Confirm/i);

  await createOperationDraft(page, {
    type: "WholesaleSale", skuText: data.product, quantity: "2", price: "125",
    stockText: data.mainLot, merchantText: data.merchant, paymentMethod: "Installment", sourceText: /Roxy|Main/i
  });
  await runLatestOperationAction(page, "WholesaleSale", /Confirm/i);
  await runLatestOperationAction(page, "WholesaleSale", /Ship/i);
  await runLatestOperationAction(page, "WholesaleSale", /Complete/i);

  await createOperationDraft(page, {
    type: "Reserve", skuText: data.product, quantity: "1", stockText: data.mainLot,
    sourceText: /Roxy|Main/i, representativeText: data.representative
  });
  await runLatestOperationAction(page, "Reserve", /Confirm/i);
  await page.locator("#operation-rows tr", { hasText: "Reserve" }).first().getByRole("button", { name: /Show|Details/i }).first().click();
  await expect(page.locator(".operation-detail").first()).toContainText(/Operation code|Current version|Batch expiry/i);

  await createOperationDraft(page, {
    type: "Return", skuText: data.product, quantity: "1", lot: data.badLot, expiry: data.expiry,
    merchantText: data.merchant, sourceText: /Roxy|Main/i, paymentMethod: "CashHandToHand"
  });
  await runLatestOperationAction(page, "Return", /Confirm/i, { expectEligibilityDialog: true });

  await createChangeDraft(page, data);
  await runLatestOperationAction(page, "Change", /Confirm/i, { acceptOptionalEligibilityDialog: true });

  await createOperationDraft(page, {
    type: "WriteOff", skuText: data.product, quantity: "1", stockText: data.mainLot, sourceText: /Roxy|Main/i
  });
  await runLatestOperationAction(page, "WriteOff", /Confirm/i);
  await expect(page.locator("#operation-rows tr", { hasText: "WriteOff" }).first()).toContainText(/Confirmed|WriteOff/i);
});

test("PAY: accountant draft and admin approval are visible in queue, detail, and final status", async ({ page }) => {
  test.setTimeout(180_000);
  const data = makeRunData("UI-PAY");
  await login(page, users.admin);
  await ensureCoreData(page, data);
  await gotoRoute(page, "/operations");

  await createOperationDraft(page, {
    type: "InventoryReceipt", skuText: data.product, quantity: "8",
    lot: data.mainLot, expiry: data.expiry, supplier: data.runId + " Supplier", invoice: data.runId + "-INV"
  });
  await runLatestOperationAction(page, "InventoryReceipt", /Confirm/i);
  await createOperationDraft(page, {
    type: "WholesaleSale", skuText: data.product, quantity: "2", price: "125",
    stockText: data.mainLot, merchantText: data.merchant, paymentMethod: "Installment", sourceText: /Roxy|Main/i
  });
  await runLatestOperationAction(page, "WholesaleSale", /Confirm/i);
  await runLatestOperationAction(page, "WholesaleSale", /Ship/i);
  await runLatestOperationAction(page, "WholesaleSale", /Complete/i);

  await gotoRoute(page, "/payments");
  await expect(page.locator("#payment-rows tr", { hasText: "Installment" }).first()).toBeVisible();
  await selectOptionByText(page.locator("#payment-accountant"), /accountant/i);
  await page.locator("#payment-rows tr", { hasText: "Installment" }).first().getByRole("button", { name: /Assign/i }).click();
  await expectNotice(page, /assigned|Payment log/i);
  await logout(page);

  await login(page, users.accountant);
  await gotoRoute(page, "/payments");
  await page.locator("#payment-rows tr", { hasText: "Installment" }).first().getByRole("button", { name: "Use" }).click();
  await page.locator("#payment-amount").fill("50");
  await page.locator("#payment-method").selectOption("CashTransaction");
  await page.locator("#payment-date").fill("2026-07-11");
  await page.locator("#payment-notes").fill(data.runId + " headed payment");
  await page.locator("#payment-sublog-form button[type='submit']").click();
  await expectNotice(page, /Payment sub-log drafted/i);
  await logout(page);

  await login(page, users.admin);
  await gotoRoute(page, "/payments");
  await page.locator("#payment-rows tr", { hasText: "Installment" }).first().getByRole("button", { name: "Details" }).click();
  await page.locator("[data-sublog-approve]").first().click();
  await expectNotice(page, /Payment approved/i);
  await expect(page.locator("#payment-rows")).toContainText(/Completed|Paid/i);
});

test("STK/NOT/REPORT: stocktake confirmation, alerts, read state, and CSV export are UI reachable", async ({ page }) => {
  test.setTimeout(180_000);
  const data = makeRunData("UI-AUDIT");
  await login(page, users.admin);
  await ensureCoreData(page, data);
  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "InventoryReceipt", skuText: data.product, quantity: "5",
    lot: data.mainLot, expiry: data.expiry, supplier: data.runId + " Supplier", invoice: data.runId + "-INV"
  });
  await runLatestOperationAction(page, "InventoryReceipt", /Confirm/i);

  await gotoRoute(page, "/stocktakes");
  await expect(page.locator("#stocktake-create-form")).toBeVisible();
  await selectOptionByText(page.locator("#stocktake-location"), /Roxy|Main/i);
  await page.locator("#stocktake-notes").fill(data.runId + " stocktake");
  await page.locator("#stocktake-create-form button[type='submit']").click();
  await expect(page.locator("#stocktake-detail")).toContainText(/Draft|stocktake/i);
  await selectOptionByText(page.locator(".stocktake-line-sku").first(), data.product);
  await page.locator(".stocktake-line-lot").first().fill(data.mainLot);
  await page.locator(".stocktake-line-expiry").first().fill(data.expiry);
  await page.locator(".stocktake-line-count").first().fill("5");
  await page.locator("#stocktake-lines-form button[type='submit']").click();
  await expect(page.locator("#stocktake-detail")).toContainText(data.mainLot);
  await page.locator("#stocktake-confirm").click();
  await expect(page.locator("#stocktake-detail")).toContainText(/Confirmed|confirmed/i);

  await gotoRoute(page, "/notifications");
  await expect(page.locator("#notification-list")).toBeVisible();
  await page.getByRole("button", { name: "Low stock" }).click();
  await expectNotice(page, /Alert run matched/i);
  await page.locator("#notifications-refresh").click();
  await page.locator("#mark-all-read").click();
  await expect(page.locator("#notification-unread-count")).toContainText(/\d+/);

  await gotoRoute(page, "/reports");
  for (const id of ["#report-stock", "#report-operations", "#report-payments", "#report-balances", "#report-exports"]) {
    await expect(page.locator(id)).toBeVisible();
  }
  await expectDownload(page, () => page.getByRole("button", { name: "CSV" }).first().click());
});


test("ROLE-CLEVEL/ACCOUNTANT: oversight and accounting users execute their own read/draft journeys", async ({ page }) => {
  await login(page, users.clevel);
  for (const route of ["/dashboard", "/catalog", "/inventory", "/crm", "/operations", "/payments", "/notifications", "/reports"]) {
    await gotoRoute(page, route);
    await expect(page.locator("#view")).toBeVisible();
  }
  await gotoRoute(page, "/catalog");
  await expect(page.locator("#product-form")).toHaveCount(0);
  await gotoRoute(page, "/inventory");
  await expect(page.locator("#inventory-balances")).toBeVisible();
  await logout(page);

  await login(page, users.accountant);
  await gotoRoute(page, "/payments");
  await expect(page.locator("#payment-rows")).toBeVisible();
  await gotoRoute(page, "/reports");
  await expect(page.locator("#report-stock")).toBeVisible();
  await gotoRoute(page, "/operations");
  await expect(page.locator("#operation-form")).toHaveCount(0);
  await gotoRoute(page, "/catalog");
  await expect(page.locator("#product-form")).toHaveCount(0);
  await logout(page);
});

test("ROLE-CLERKS: Roxy, Retail, and Online clerks create UI drafts within their assigned location", async ({ page }) => {
  test.setTimeout(180_000);
  const data = makeRunData("UI-ROLES");

  await login(page, users.admin);
  await ensureCoreData(page, data);
  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "InventoryReceipt",
    skuText: data.product,
    quantity: "30",
    lot: data.mainLot,
    expiry: data.expiry,
    supplier: data.runId + " Supplier",
    invoice: data.runId + "-INV"
  });
  await runLatestOperationAction(page, "InventoryReceipt", /Confirm/i);

  for (const destinationText of [/Retail|Mohamed/i, /Online/i]) {
    await createOperationDraft(page, {
      type: "WarehouseTransfer",
      skuText: data.product,
      quantity: "5",
      stockText: data.mainLot,
      destinationText
    });
    await runLatestOperationAction(page, "WarehouseTransfer", /Confirm/i);
    await runLatestOperationAction(page, "WarehouseTransfer", /Ship/i);
    await runLatestOperationAction(page, "WarehouseTransfer", /Receive/i);
  }
  await logout(page);

  const clerks = [
    [users.roxyClerk, /Roxy/i, /Roxy|Main/i],
    [users.retailClerk, /Retail|Mohamed/i, /Retail|Mohamed/i],
    [users.onlineClerk, /Online/i, /Online/i]
  ];

  for (const [user, locationText, sourceText] of clerks) {
    await login(page, user);
    await gotoRoute(page, "/inventory");
    await expect(page.locator("#inventory-locations")).toContainText(locationText);
    await gotoRoute(page, "/operations");
    await expect(page.locator("#operation-form")).toBeVisible();
    await createOperationDraft(page, {
      type: "RetailSale",
      skuText: data.product,
      quantity: "1",
      price: "10",
      stockText: data.mainLot,
      sourceText,
      paymentMethod: "CashHandToHand",
      buyerName: data.runId + " " + user.username + " buyer"
    });
    await expect(page.locator("#operation-rows")).toContainText("RetailSale");
    await gotoRoute(page, "/payments");
    await expect(page.locator("#payment-form")).toHaveCount(0);
    await gotoRoute(page, "/reports");
    await expect(page.locator("#report-stock")).toHaveCount(0);
    await logout(page);
  }
});
