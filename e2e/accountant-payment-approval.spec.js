const { test, expect } = require("@playwright/test");

test("accountant can review pending Payments work without payout controls", async ({ page }) => {
  const adjustmentId = "33333333-3333-3333-3333-333333333333";
  const collectionId = "44444444-4444-4444-4444-444444444444";
  const paymentId = "66666666-6666-6666-6666-666666666666";
  const entitlementId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
  const waiverId = "cccccccc-cccc-cccc-cccc-cccccccccccc";
  let adjustmentApproved = false;
  let waiverApproved = false;
  let approvalRequests = 0;

  await page.route("**/*", async (route) => {
    const url = new URL(route.request().url());
    const method = route.request().method();
    const json = (body, status = 200) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
    if (url.pathname === "/health") return json({ status: "Healthy" });
    if (url.pathname === "/api/v1/auth/refresh") return json({ accessToken: "accountant-payment-token", user: { userId: "11111111-1111-1111-1111-111111111111", username: "accountant", fullName: "Mona Accountant", role: "Accountant" } });
    if (url.pathname === "/api/v1/notifications/unread-count") return json({ count: 0 });
    if (url.pathname === "/api/v1/crm/merchants") return json({ items: [{ id: "22222222-2222-2222-2222-222222222222", businessName: "Hany Optics" }] });
    if (url.pathname === "/api/v1/payments/finance-account-options" || url.pathname === "/api/v1/finance/accounts") return json([]);
    if (url.pathname === "/api/v1/payments/collection-work") return json([{
      id: collectionId,
      reference: "COL-ACCOUNTANT-01",
      amount: 120,
      paymentMethod: "Wallet",
      status: "PendingAdminReview",
      assignedToName: "Mona Accountant"
    }]);
    if (url.pathname === "/api/v1/payments/adjustments" && method === "GET") return json([
      { id: adjustmentId, operationNumber: "OP-ACCOUNTANT-01", adjustmentType: "BalanceReduction", amount: 25, status: adjustmentApproved ? "Approved" : "PendingApproval", createdByName: "Mona Accountant" },
      { id: "55555555-5555-5555-5555-555555555555", operationNumber: "OP-ACCOUNTANT-02", adjustmentType: "CashRefund", amount: 15, status: "Approved", createdByName: "Mona Accountant" }
    ]);
    if (url.pathname === `/api/v1/payments/adjustments/${adjustmentId}/approve` && method === "POST") {
      approvalRequests += 1;
      adjustmentApproved = true;
      return json({ id: adjustmentId, status: "Approved" });
    }
    if (url.pathname === "/api/v1/payments/return-entitlements" && method === "GET") return json([{
      id: entitlementId,
      returnOperationId: "dddddddd-dddd-dddd-dddd-dddddddddddd",
      beneficiaryKind: "Anonymous",
      buyerName: "Test buyer",
      exceptionAmount: 5,
      status: "Confirmed",
      waivers: [{ id: waiverId, scope: "OtherPayments", amount: 5, reason: "Verified customer settlement", status: waiverApproved ? "Approved" : "PendingReview", requestedByName: "Mona Accountant" }]
    }]);
    if (url.pathname === `/api/v1/payments/return-entitlement-waivers/${waiverId}/approve` && method === "POST") {
      waiverApproved = true;
      return json({ id: waiverId, status: "Approved" });
    }
    if (url.pathname === "/api/v1/payments/other-payments") return json({ items: [{ id: paymentId, operationId: "77777777-7777-7777-7777-777777777777", operationNumber: "OP-ACCOUNTANT-03", buyerName: "Hany Optics", totalAmount: 25, amountPaid: 0, remainingAmount: 25, paymentMethod: "CashHandToHand", status: "PendingAccountant" }], page: 1, pageSize: 50, totalCount: 1 });
    if (url.pathname === `/api/v1/payments/${paymentId}`) {
      await json({
        log: { id: paymentId, status: "PendingAccountant", totalAmount: 25, remainingAmount: 25 },
        subLogs: [{ id: "88888888-8888-8888-8888-888888888888", amount: 25, paymentMethod: "CashHandToHand", status: "PendingAdminReview", draftedAt: "2026-10-20T10:00:00", draftedByName: "Mona Accountant" }],
        cashRecords: [{ id: "99999999-9999-9999-9999-999999999999", paymentType: "CashReceived", amount: 25, paymentDate: "2026-10-20T10:00:00", status: "PendingAdminReview", createdByName: "Mona Accountant", financeAccountId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa" }],
        adjustments: [],
        stages: []
      });
      return;
    }
    if (url.pathname === "/api/v1/payments/merchant-account-payments" || url.pathname === "/api/v1/payments/merchant-account-payments/history" || url.pathname === "/api/v1/payments/other-payments/history" || url.pathname === "/api/v1/payments" || url.pathname === "/api/v1/payments/audit") return json({ items: [], page: 1, pageSize: 50, totalCount: 0 });
    if (url.pathname === "/api/v1/payments/kpis") return json({ totalActivity: {}, actuallyCollected: {}, remaining: {} });
    if (url.pathname.startsWith("/api/")) return json({});
    return route.continue();
  });

  await page.goto("/en/#/payments", { waitUntil: "domcontentloaded" });
  await page.getByRole("tab", { name: "Approval inbox" }).click();

  await expect(page.locator("#payment-review-rows")).toContainText("COL-ACCOUNTANT-01");
  await expect(page.locator("#payment-review-rows").getByRole("button", { name: "Approve" })).toBeVisible();
  await expect(page.locator("#payment-review-rows").getByRole("button", { name: "Reject" })).toBeVisible();
  await expect(page.locator("#financial-adjustment-inbox [data-inbox-adjustment-approve]")).toBeVisible();
  await expect(page.locator("#financial-adjustment-inbox [data-inbox-adjustment-reject]")).toBeVisible();
  await expect(page.locator("#refund-payout-options")).toHaveCount(0);
  await expect(page.locator("#financial-adjustment-inbox [data-inbox-adjustment-payout]")).toHaveCount(0);
  await expect(page.locator(`#return-entitlement-rows [data-return-waiver-approve="${waiverId}"]`)).toBeVisible();
  await expect(page.locator(`#return-entitlement-rows [data-return-waiver-reject="${waiverId}"]`)).toBeVisible();

  await page.locator("#financial-adjustment-inbox [data-inbox-adjustment-approve]").click();
  await page.locator(".dialog-card [data-dialog-confirm]").click();
  await expect.poll(() => approvalRequests).toBe(1);
  await expect(page.locator("#notification-area")).toContainText("Financial adjustment approved");
  await page.locator(`#return-entitlement-rows [data-return-waiver-approve="${waiverId}"]`).click();
  await expect.poll(() => waiverApproved).toBe(true);
  await expect(page.locator(`#return-entitlement-rows [data-return-waiver-approve="${waiverId}"]`)).toHaveCount(0);

  await page.waitForLoadState("networkidle");
  await page.getByRole("tab", { name: "Other payments" }).click();
  const paymentDetailsButton = page.locator(`#payment-rows [data-payment-detail="${paymentId}"]`);
  await paymentDetailsButton.click();
  await expect(paymentDetailsButton).toHaveText("Hide");
  await expect(page.locator(`#payment-detail-${paymentId} [data-sublog-approve]`)).toBeVisible();
  await expect(page.locator(`#payment-detail-${paymentId} [data-sublog-reject]`)).toBeVisible();
  await expect(page.locator(`#payment-detail-${paymentId} [data-cash-detail-approve]`)).toBeVisible();
  await expect(page.locator(`#payment-detail-${paymentId} [data-cash-detail-reject]`)).toBeVisible();
});
