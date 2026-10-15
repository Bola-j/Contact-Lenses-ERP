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

test("Operations return source is optional, stock disposition is per line, and settlement stays in Payments", async ({ page }) => {
  const locationId = "11111111-1111-1111-1111-111111111111";
  const subWarehouseId = "22222222-2222-2222-2222-222222222222";
  const retailLocationId = "33333333-3333-3333-3333-333333333333";
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    const json = (body, status = 200) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
    if (url.pathname === "/api/v1/auth/refresh") {
      return json({ accessToken: "return-ui-test", user: { userId: "test-admin", username: "admin", fullName: "Admin", role: "Admin" } });
    }
    if (url.pathname === "/api/v1/inventory/locations") {
      return json([
        { id: locationId, name: "Main Warehouse", locationType: "MainWarehouse", isActive: true },
        { id: subWarehouseId, name: "Online SubWarehouse", locationType: "SubWarehouse", isActive: true },
        { id: retailLocationId, name: "Retail Counter", locationType: "Retail", isActive: true }
      ]);
    }
    if (url.pathname === "/api/v1/crm/merchants") return json({ items: [], totalCount: 0 });
    if (url.pathname === "/api/v1/operations") return json({ items: [], totalCount: 0, totalPages: 1, page: 1 });
    if (url.pathname === "/api/v1/notifications/unread-count") return json({ count: 0 });
    return json([]);
  });

  await page.goto("/en/#/operations", { waitUntil: "domcontentloaded" });
  await expect(page.locator("#op-type")).toBeVisible();
  await expect(page.locator("#cod-charge-settings-form")).toHaveCount(0);
  await page.locator("#op-type").selectOption("Return");

  const sourceSale = page.locator("#op-return-source");
  await expect(sourceSale).toBeVisible();
  await expect(sourceSale).toHaveJSProperty("required", false);
  await expect(page.locator(".op-line-source-line-field").first()).toBeHidden();
  await expect(page.locator("#op-return-guidance")).toBeVisible();
  await expect(page.locator("#op-unlinked-return-guidance")).toBeVisible();
  await expect(page.locator("#op-payment")).toBeHidden();
  await expect(page.locator(".op-line-return-disposition-field").first()).toBeVisible();
  await expect(page.locator(".op-line-normal-return-qty")).toBeHidden();

  await page.locator("#op-type").selectOption("RetailSale");
  await expect(page.locator("#op-sales-channel")).toBeVisible();
  await expect(page.locator(".op-line-type option")).toHaveCount(3);
  await expect(page.locator(".op-line-type option[value='Paid']")).toHaveText(/Paid/);
  await expect(page.locator(".op-line-type option[value='Bonus']")).toHaveText(/Bonus/);
  await expect(page.locator(".op-line-type option[value='Offer']")).toHaveText(/Offer/);
  await expect(page.locator("#op-merchant")).toHaveJSProperty("required", false);
  await expect(page.locator("#op-buyer")).toBeVisible();
  await expect(page.locator("#op-buyer-phone")).toBeVisible();
  await page.locator(".op-line-type").first().selectOption("Bonus");
  await expect(page.locator(".op-line-price").first()).toHaveValue("0");
  await expect(page.locator(".op-line-price").first()).toBeDisabled();
  await page.locator(".op-line-type").first().selectOption("Offer");
  await expect(page.locator(".op-line-price").first()).toBeEnabled();
  await page.locator("#op-sales-channel").selectOption("Online");
  await expect(page.locator("#op-shipping-address")).toBeVisible();
  await expect(page.locator("#op-buyer-contact-verified")).toBeVisible();
  await expect(page.locator(".op-cod-override-field").first()).toBeVisible();
  await expect(page.locator("#op-source option")).toHaveCount(2);
  await expect(page.locator("#op-source option").nth(1)).toHaveAttribute("value", subWarehouseId);
  await page.locator("#op-online-payment").selectOption("Gateway");
  await expect(page.locator(".op-cod-override-field").first()).toBeHidden();
  await expect(page.locator("#op-online-reference")).toBeVisible();
  await expect(page.locator("#op-online-payment-verified")).toBeVisible();
  await expect(page.locator("#op-payment")).toBeVisible();
  await expect(page.locator("#op-merchant")).toHaveJSProperty("required", false);
  await expect(page.locator("#op-buyer")).toBeVisible();
  await expect(page.locator("#op-buyer-phone")).toBeVisible();

  await page.locator("#op-type").selectOption("WarehouseTransfer");
  await expect(sourceSale).toBeHidden();
  await expect(page.locator("#op-shipping-address")).toBeHidden();
});

test("Return entitlement settlement is requested from Payments and keeps exception value held", async ({ page }) => {
  const entitlementId = "55555555-5555-5555-5555-555555555555";
  const settlementRequests = [];
  let settlementCreated = false;
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    const json = (body, status = 200) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
    if (url.pathname === "/api/v1/auth/refresh") return json({ accessToken: "return-ui-test", user: { userId: "test-admin", username: "admin", fullName: "Admin", role: "Admin" } });
    if (url.pathname === "/api/v1/crm/merchants") return json({ items: [], totalCount: 0 });
    if (url.pathname === "/api/v1/users") return json([]);
    if (url.pathname === "/api/v1/payments/finance-account-options") return json([{ id: "66666666-6666-6666-6666-666666666666", name: "Cash Drawer", type: "CashOnHand" }]);
    if (url.pathname === "/api/v1/payments/return-entitlements") return json([{
      id: entitlementId, returnOperationId: "77777777-7777-7777-7777-777777777777",
      originalSaleOperationId: "88888888-8888-8888-8888-888888888888", beneficiaryKind: "Anonymous",
      buyerName: "Walk-in buyer", paymentTrack: "Retail", normalAmount: 10, exceptionAmount: 5,
      sourcePayableAmount: 10, accountLevelPayableAmount: 0, unvaluedExceptionAmount: 5,
      settleableAmount: 10, exceptionHeldAmount: 5, settledAmount: 0, reservedAmount: settlementCreated ? 10 : 0,
      status: settlementCreated ? "Reserved" : "Held", originalPaymentStatus: "Completed"
    }]);
    if (url.pathname === `/api/v1/payments/return-entitlements/${entitlementId}/settlements` && route.request().method() === "POST") {
      settlementRequests.push(route.request().postDataJSON());
      settlementCreated = true;
      return json({ returnEntitlementId: entitlementId, entitlementStatus: "Reserved", reservedAmount: 10, settledAmount: 0, cashRecordId: "99999999-9999-9999-9999-999999999999", settlementStatus: "PendingAdminReview" }, 201);
    }
    return json({ items: [], totalCount: 0, totalPages: 1, page: 1 });
  });

  await page.goto("/en/#/payments", { waitUntil: "domcontentloaded" });
  await page.locator('[data-payment-view="review"]').click();
  const row = page.locator(`#return-entitlement-rows tr[data-return-entitlement-id="${entitlementId}"]`);
  await expect(row).toBeVisible();
  await expect(row).toContainText("5.00");
  await expect(row.locator("td").nth(4)).toContainText("10.00");
  await row.getByRole("button", { name: /settle return entitlement/i }).click();
  await expect(page.locator(".return-settlement-dialog")).toBeVisible();
  await expect(page.locator("#return-settlement-scope")).toHaveJSProperty("required", true);
  await expect(page.locator("#return-settlement-scope")).toHaveValue("");
  await expect(page.locator("#return-settlement-method option[value='NoSettlement']")).toHaveCount(0);
  await page.locator("#return-settlement-scope").selectOption("SourceOperation");
  await expect(page.locator("#return-settlement-amount")).toHaveAttribute("max", "10");
  await expect(page.locator("#return-settlement-math")).toContainText(/customer refund/i);
  await expect(page.locator("#return-settlement-finance-account option")).toHaveCount(2);
  await page.locator("#return-settlement-method").selectOption("CashHandToHand");
  await page.locator("#return-settlement-amount").fill("10");
  await page.locator("#return-settlement-finance-account").selectOption("66666666-6666-6666-6666-666666666666");
  await page.locator("#return-settlement-notes").fill("Physical goods received; excess amount held for review.");
  await page.locator(".return-settlement-dialog [type='submit']").click();
  await expect.poll(() => settlementRequests.length).toBe(1);
  expect(settlementRequests[0]).toMatchObject({
    amount: 10,
    settlementMethod: "CashHandToHand",
    settlementScope: "SourceOperation",
    financeAccountId: "66666666-6666-6666-6666-666666666666",
    transferFeeAmount: 0,
    notes: "Physical goods received; excess amount held for review."
  });
  await expect(page.locator("#notification-area")).toContainText(/return settlement request submitted for review/i);
  await expect(row).toContainText(/reserved/i);
  await expect(row).toContainText("5.00");
});

test("Merchant CashRefund settlement does not ask for a Finance account", async ({ page }) => {
  const entitlementId = "55555555-5555-5555-5555-555555555556";
  const settlementRequests = [];
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    const json = (body, status = 200) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
    if (url.pathname === "/api/v1/auth/refresh") return json({ accessToken: "return-ui-test", user: { userId: "test-admin", username: "admin", fullName: "Admin", role: "Admin" } });
    if (url.pathname === "/api/v1/crm/merchants") return json({ items: [], totalCount: 0 });
    if (url.pathname === "/api/v1/users") return json([]);
    if (url.pathname === "/api/v1/payments/finance-account-options") return json([]);
    if (url.pathname === "/api/v1/payments/return-entitlements") return json([{
      id: entitlementId, returnOperationId: "77777777-7777-7777-7777-777777777778",
      originalSaleOperationId: "88888888-8888-8888-8888-888888888889", merchantId: "99999999-9999-9999-9999-999999999999",
      merchantName: "Test Merchant", beneficiaryKind: "Merchant", sourceAvailable: 0, valuationAvailable: 30,
      unvaluedHeld: 0, reservedAmount: 0, settledAmount: 0, waivedAmount: 0, status: "Held"
    }]);
    if (url.pathname === `/api/v1/payments/return-entitlements/${entitlementId}/settlements` && route.request().method() === "POST") {
      settlementRequests.push(route.request().postDataJSON());
      return json({ returnEntitlementId: entitlementId, entitlementStatus: "Reserved", reservedAmount: 30, settledAmount: 0, financialAdjustmentId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", settlementStatus: "PendingAdminReview" }, 201);
    }
    return json({ items: [], totalCount: 0, totalPages: 1, page: 1 });
  });

  await page.goto("/en/#/payments", { waitUntil: "domcontentloaded" });
  await page.locator('[data-payment-view="review"]').click();
  const row = page.locator(`#return-entitlement-rows tr[data-return-entitlement-id="${entitlementId}"]`);
  await expect(row).toBeVisible();
  await row.getByRole("button", { name: /settle return entitlement/i }).click();
  await expect(page.locator("#return-settlement-scope option[value='MerchantAccount']")).toHaveCount(1);
  await page.locator("#return-settlement-scope").selectOption("MerchantAccount");
  await page.locator("#return-settlement-method").selectOption("CashRefund");
  await expect(page.locator(".return-settlement-finance-account-field")).toBeHidden();
  await page.locator("#return-settlement-amount").fill("30");
  await page.locator(".return-settlement-dialog [type='submit']").click();
  await expect.poll(() => settlementRequests.length).toBe(1);
  expect(settlementRequests[0]).toMatchObject({ amount: 30, settlementMethod: "CashRefund", settlementScope: "MerchantAccount", financeAccountId: null });
});

test("Exception waiver request is scoped, held for review, and reviewed by another authorized user", async ({ page }) => {
  const entitlementId = "55555555-5555-5555-5555-555555555557";
  const waiverId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
  const waiverRequests = [];
  let role = "Accountant";
  let userId = "accountant-requester";
  let waiverStatus = "PendingReview";
  let sourceAvailable = 10;
  let valuationAvailable = 4;
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    const json = (body, status = 200) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
    if (url.pathname === "/api/v1/auth/refresh") return json({ accessToken: "return-ui-test", user: { userId, username: role.toLowerCase(), fullName: role, role } });
    if (url.pathname === "/api/v1/crm/merchants") return json({ items: [], totalCount: 0 });
    if (url.pathname === "/api/v1/users") return json([]);
    if (url.pathname === "/api/v1/payments/finance-account-options") return json([]);
    if (url.pathname === "/api/v1/payments/return-entitlements") return json([{
      id: entitlementId, returnOperationId: "77777777-7777-7777-7777-777777777779",
      originalSaleOperationId: "88888888-8888-8888-8888-888888888890", beneficiaryKind: "Anonymous",
      sourceAvailable, valuationAvailable, unvaluedHeld: 4, reservedAmount: 0, settledAmount: 0, waivedAmount: 0,
      waivers: waiverRequests.map((request) => ({ ...request, status: waiverStatus }))
    }]);
    if (url.pathname === `/api/v1/payments/return-entitlements/${entitlementId}/waivers` && route.request().method() === "POST") {
      const body = route.request().postDataJSON();
      waiverRequests.push({ id: waiverId, ...body, requestedById: userId, requestedByName: role });
      return json({ id: waiverId, status: "PendingReview", ...body }, 201);
    }
    if (url.pathname === `/api/v1/payments/return-entitlement-waivers/${waiverId}/approve`) {
      waiverStatus = "Approved";
      return json({ id: waiverId, status: waiverStatus });
    }
    return json({ items: [], totalCount: 0, totalPages: 1, page: 1 });
  });

  await page.goto("/en/#/payments", { waitUntil: "domcontentloaded" });
  await page.locator('[data-payment-view="review"]').click();
  const row = page.locator(`#return-entitlement-rows tr[data-return-entitlement-id="${entitlementId}"]`);
  await expect(row).toBeVisible();
  await expect(row).toContainText(/held until finance records a valuation/i);
  await row.getByRole("button", { name: /request waiver/i }).click();
  await page.locator("#return-waiver-scope").selectOption("OtherPayments");
  await expect(page.locator("#return-waiver-amount")).toHaveAttribute("max", "4");
  await page.locator("#return-waiver-amount").fill("4");
  await page.locator("#return-waiver-reason").fill("Customer settlement exception reviewed with supporting evidence.");
  await page.locator(".return-settlement-dialog [type='submit']").click();
  await expect.poll(() => waiverRequests.length).toBe(1);
  expect(waiverRequests[0]).toMatchObject({ scope: "OtherPayments", amount: 4, reason: "Customer settlement exception reviewed with supporting evidence." });
  await expect(row).toContainText(/pending review/i);
  await expect(row.getByRole("button", { name: /approve waiver/i })).toHaveCount(0);

  role = "Admin";
  userId = "admin-reviewer";
  sourceAvailable = 0;
  valuationAvailable = 0;
  await page.reload({ waitUntil: "domcontentloaded" });
  await page.locator('[data-payment-view="review"]').click();
  const reviewedRow = page.locator(`#return-entitlement-rows tr[data-return-entitlement-id="${entitlementId}"]`);
  await expect(reviewedRow.getByRole("button", { name: /settle return entitlement/i })).toHaveCount(0);
  await expect(reviewedRow.getByRole("button", { name: /approve waiver/i })).toBeVisible();
  await reviewedRow.getByRole("button", { name: /approve waiver/i }).click();
  await expect(reviewedRow).toContainText(/approved/i);
});

test("Return approval submission requires a reason", async ({ page }) => {
  const operationId = "44444444-4444-4444-4444-444444444444";
  const submittedBodies = [];
  let confirmAttempts = 0;
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    const json = (body, status = 200) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
    if (url.pathname === "/api/v1/auth/refresh") return json({ accessToken: "return-ui-test", user: { userId: "test-admin", username: "admin", fullName: "Admin", role: "Admin" } });
    if (url.pathname === "/api/v1/inventory/locations") return json([{ id: "11111111-1111-1111-1111-111111111111", name: "Main Warehouse", locationType: "MainWarehouse", isActive: true }]);
    if (url.pathname === "/api/v1/operations" && route.request().method() === "GET") return json({ items: [{ id: operationId, operationNumber: "RET-TEST", operationType: "Return", status: "Draft", sourceLocationName: "Main Warehouse", createdAt: new Date().toISOString(), workflow: { status: "Draft", actions: [{ key: "submit-approval", requiresReason: true }] } }], totalCount: 1, totalPages: 1, page: 1 });
    if (url.pathname === `/api/v1/operations/${operationId}/submit-approval`) {
      submittedBodies.push(route.request().postDataJSON());
      confirmAttempts += 1;
      return json({ operationId, status: "PendingApproval" });
    }
    if (url.pathname === "/api/v1/crm/merchants") return json({ items: [], totalCount: 0 });
    if (url.pathname === "/api/v1/notifications/unread-count") return json({ count: 0 });
    return json([]);
  });

  await page.goto("/en/#/operations", { waitUntil: "domcontentloaded" });
  const action = page.locator(`[data-op-id="${operationId}"][data-op-action="submit-approval"]`);
  await expect(action).toBeVisible();
  await action.click();
  await page.locator("[data-dialog-confirm]").click();
  await expect(page.locator(".dialog-input")).toBeVisible();
  await page.locator(".dialog-input").fill("Batch mismatch inspected and submitted for review.");
  await page.locator(".dialog-card [type='submit']").click();
  await expect.poll(() => confirmAttempts).toBe(1);
  expect(submittedBodies[0]).toMatchObject({ reason: "Batch mismatch inspected and submitted for review." });
});

test("Operations return editor allows an unlinked merchant or retail return", async ({ page }) => {
  const data = makeRunData("RETURN-UI");
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

  await page.locator("#op-type").selectOption("Return");
  const sourceSale = page.locator("#op-return-source");
  await expect(sourceSale).toBeVisible();
  await expect(sourceSale).toHaveJSProperty("required", false);
  await expect(page.locator(".op-line-source-line-field").first()).toBeHidden();
  await expect(page.locator("#op-buyer")).toBeVisible();

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
  await expect(sourceSale).toHaveJSProperty("required", false);
  await expect(row.locator(".op-line-source-line-field")).toBeHidden();
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
  await expect(payment.locator("option[value='BankTransfer']")).toHaveCount(0);
  await expect(payment.locator("option[value='MerchantAccount']")).toHaveJSProperty("hidden", true);
  await selectOptionByText(page.locator("#op-merchant"), data.merchant);
  await expect(payment.locator("option[value='MerchantAccount']")).toHaveJSProperty("hidden", false);
  await payment.selectOption("Wallet");
  await expect(payment).toHaveValue("Wallet");
  await expect(page.locator("#op-finance-account")).toHaveCount(0);
});
