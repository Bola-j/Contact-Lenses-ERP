const { test, expect } = require("@playwright/test");

test("payments: one collection workspace shows reconciled merchant figures and review work", async ({ page }) => {
  const merchantId = "22222222-2222-2222-2222-222222222222";
  const collectionId = "33333333-3333-3333-3333-333333333333";
  const financeAccountId = "66666666-6666-6666-6666-666666666666";
  let rejectionBody = null;
  let collectionSubmitted = false;
  await page.route("**/*", async (route) => {
    const url = new URL(route.request().url());
    const method = route.request().method();
    const json = (body) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
    if (url.pathname === "/health") return json({ status: "Healthy" });
    if (url.pathname === "/api/v1/auth/refresh") return json({ accessToken: "payment-ui-token", user: { userId: "11111111-1111-1111-1111-111111111111", username: "admin", fullName: "Primary Admin", role: "Admin" } });
    if (url.pathname === "/api/v1/notifications/unread-count") return json({ count: 0 });
    if (url.pathname === "/api/v1/crm/merchants") return json({ items: [{ id: merchantId, businessName: "Hany Optics" }] });
    if (url.pathname === "/api/v1/users") return json([]);
    if (url.pathname === "/api/v1/finance/accounts") return json([{ id: financeAccountId, name: "Main Cash", type: "CashOnHand" }]);
    if (url.pathname === "/api/v1/payments/merchant-account-payments") return json({ items: [{ id: "44444444-4444-4444-4444-444444444444", operationId: "55555555-5555-5555-5555-555555555555", merchantId, operationNumber: "OP-20260912-001", operationType: "WholesaleSale", buyerName: "Hany Optics", totalAmount: 1000, amountPaid: 0, remainingAmount: 1000, paymentMethod: "CashHandToHand", status: "PendingAccountant", initializedByName: "Admin", lastModifiedAt: "2026-09-12T10:00:00" }], page: 1, pageSize: 50, totalCount: 1 });
    if (url.pathname === "/api/v1/payments/other-payments") return json({ items: [], page: 1, pageSize: 50, totalCount: 0 });
    if (url.pathname === "/api/v1/payments/merchant-account-payments/history") return json({ items: [], page: 1, pageSize: 200, totalCount: 0 });
    if (url.pathname === "/api/v1/payments/other-payments/history") return json({ items: [], page: 1, pageSize: 200, totalCount: 0 });
    if (url.pathname === "/api/v1/payments") return json({ items: [], page: 1, pageSize: 50, totalCount: 0 });
    if (url.pathname === "/api/v1/payments/audit") return json({ items: [], page: 1, pageSize: 100, totalCount: 0 });
    if (url.pathname === `/api/v1/payments/merchant-accounts/${merchantId}` && collectionSubmitted) {
      return route.fulfill({ status: 503, contentType: "application/json", body: JSON.stringify({ title: "Temporary refresh failure" }) });
    }
    if (url.pathname === `/api/v1/payments/merchant-accounts/${merchantId}`) return json({
      merchantId,
      businessName: "Hany Optics",
      netCollected: 800,
      balance: { amountDue: 350, creditAvailable: 75, pendingCollections: 120, reservedRefunds: 25 },
      breakdown: { saleTotal: 1500, paymentsReceived: 1000, cashRefunded: 200, returnTotal: 300, balanceReductions: 50, additionalCharges: 100 },
      classification: { grade: "B", score: 72, flags: [] }
    });
    if (url.pathname === `/api/v1/payments/merchant-accounts/${merchantId}/statement`) return json([]);
    if (url.pathname === `/api/v1/payments/merchant-accounts/${merchantId}/orders`) return json([]);
    if (url.pathname === "/api/v1/payments/collection-work") return json([{
      id: collectionId,
      reference: "COL-333333333333",
      amount: 120,
      paymentMethod: "Wallet",
      status: "PendingAdminReview",
      draftedAt: "2026-09-12T10:00:00",
      assignedToName: "Mona Accountant",
      rejectionReason: null
    }]);
    if (method === "POST" && url.pathname === `/api/v1/payments/merchant-accounts/${merchantId}/collections`) {
      collectionSubmitted = true;
      return route.fulfill({
        status: 201,
        contentType: "application/json",
        body: JSON.stringify({
          id: "77777777-7777-7777-7777-777777777777",
          reference: "COL-777777777777",
          merchantId,
          amount: 80,
          paymentMethod: "CashHandToHand",
          status: "PendingAdminReview"
        })
      });
    }
    if (url.pathname === `/api/v1/payments/collections/${collectionId}/reject`) {
      rejectionBody = JSON.parse(route.request().postData() || "{}");
      return json({});
    }
    if (url.pathname.startsWith("/api/")) return json({});
    return route.continue();
  });

  await page.addInitScript(() => localStorage.setItem("lensee.language", "en"));
  await page.goto("/#/payments", { waitUntil: "domcontentloaded" });
  await page.locator("#load-merchant-balance").click();

  await expect(page.locator("#unified-collection-form")).toHaveCount(1);
  await page.locator("#payment-merchant-search").fill("Hany");
  await expect(page.locator("#payment-merchant option")).toHaveCount(1);
  await expect(page.locator("#payment-merchant")).toHaveValue(merchantId);
  await expect(page.locator("#unified-collection-card")).toBeHidden();
  await expect(page.locator("#payment-queue-section")).toBeHidden();
  await expect(page.locator("#payment-review-section")).toBeHidden();
  await expect(page.locator("#merchant-balance-panel")).toContainText("Remaining owed");
  await expect(page.locator("#merchant-balance-panel")).toContainText("350.00");
  await expect(page.locator("#merchant-balance-panel")).toContainText("Total sales");
  await expect(page.locator("#merchant-balance-panel")).toContainText("1,500.00");
  await expect(page.locator("#merchant-balance-panel")).toContainText("Net collected");
  await expect(page.locator("#merchant-balance-panel")).toContainText("800.00");
  await expect(page.locator("#merchant-balance-panel")).toContainText("Refunds");
  await expect(page.locator("#merchant-balance-panel")).toContainText("Accepted return value");
  await expect(page.locator("#merchant-balance-panel")).toContainText("Additional charges");
  await expect(page.locator("#merchant-balance-panel")).toContainText("Amount reductions");
  await expect(page.locator("#merchant-balance-panel")).not.toContainText("Merchant credit");
  await page.locator("#merchant-account-details-toggle").click();
  await expect(page.locator("#merchant-account-detail-panel")).toContainText("Good · 72.00");
  await expect(page.locator("#merchant-account-detail-panel")).not.toContainText("Account healthB");
  await expect(page.locator("#merchant-collection-draft-rows")).toContainText("Mona Accountant");
  await expect(page.locator("#merchant-collection-draft-rows")).toContainText("Approve");
  await expect(page.locator("#merchant-payment-rows")).toContainText("Cash hand to hand");
  await expect(page.locator("#merchant-payment-rows")).not.toContainText("CashHandToHand");
  await expect(page.locator("#merchant-payment-rows")).toContainText("Wholesale sale");
  await expect(page.locator("#merchant-payment-rows")).not.toContainText("WholesaleSale");
  await page.getByRole("tab", { name: "Other payments" }).click();
  await expect(page.locator("#merchant-payment-section")).toBeHidden();
  await expect(page.locator("#payment-queue-section")).toBeVisible();
  await page.locator("#other-collection-toggle").click();
  await expect(page.locator("#unified-collection-card")).toBeVisible();
  await expect(page.locator("#collection-source-kind")).toHaveValue("OtherPayments");
  await expect(page.locator("#merchant-payment-section h2")).toContainText("Merchant account payments");
  await expect(page.locator("#payment-queue-section h2")).toContainText("Other payments");
  await expect(page.locator("#payment-ledger-section h2")).toContainText("Other payment history");
  await page.getByRole("tab", { name: "Merchant account payments" }).click();
  await expect(page.locator("#merchant-payment-section")).toBeVisible();
  await expect(page.locator("#payment-queue-section")).toBeHidden();
  await page.locator("#merchant-payment-rows [data-payment-use]").click();
  await expect(page.locator("#collection-source-kind")).toHaveValue("MerchantAccount");
  await expect(page.locator("#collection-merchant")).toHaveValue(merchantId);
  await page.locator("#collection-amount").fill("80");
  await page.locator("#collection-method").selectOption("CashHandToHand");
  await page.locator("#collection-finance-account").selectOption(financeAccountId);
  await page.locator("#unified-collection-form").getByRole("button", { name: "Send collection for approval" }).click();
  await expect.poll(() => collectionSubmitted).toBe(true);
  await expect(page.locator("#notification-area")).toContainText("Collection sent for Admin approval");
  await expect(page.locator("#unified-collection-error")).toBeHidden();
  await expect(page.locator("body")).not.toContainText("The workspace request failed.");

  await page.locator("#merchant-collection-draft-rows").getByRole("button", { name: "Reject" }).click();
  await expect(page.locator(".dialog-card")).toContainText("Reject collection");
  await expect(page.locator(".dialog-card textarea.dialog-input")).toBeVisible();
  await page.locator(".dialog-card").getByRole("button", { name: "Continue" }).click();
  await expect(page.locator(".dialog-card textarea.dialog-input")).toHaveAttribute("aria-invalid", "true");
  await page.locator(".dialog-card textarea.dialog-input").fill("Receipt amount does not match");
  await page.locator(".dialog-card").getByRole("button", { name: "Continue" }).click();
  await expect.poll(() => rejectionBody?.reason).toBe("Receipt amount does not match");

  await page.setViewportSize({ width: 412, height: 915 });
  await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth)).toBe(true);
});
