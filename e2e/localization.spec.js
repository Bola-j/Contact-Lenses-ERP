const { test, expect } = require("@playwright/test");

async function mockAuthenticatedFrontend(page) {
  await page.route("**/*", async (route) => {
    const url = new URL(route.request().url());
    const json = (body) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify(body) });
    if (url.pathname === "/health") return json({ status: "Healthy" });
    if (url.pathname === "/api/v1/auth/refresh") {
      return json({
        accessToken: "localization-test-token",
        user: { userId: "11111111-1111-1111-1111-111111111111", username: "admin", fullName: "Primary Admin", role: "Admin" }
      });
    }
    if (url.pathname === "/api/v1/notifications/unread-count") return json({ count: 0 });
    if (url.pathname === "/api/v1/audit") {
      return json({
        items: [{
          id: "22222222-2222-2222-2222-222222222222",
          happenedAt: "2026-08-20T10:15:00Z",
          actorName: "Amina Hassan",
          actorType: "Admin",
          summary: "Created employee account Ahmed.",
          recordName: "Ahmed",
          entityType: "User",
          section: "admin"
        }],
        page: 1,
        pageSize: 50,
        totalCount: 1
      });
    }
    if (url.pathname === "/api/v1/integrations/shopify/status") return json({ isConfigured: true, isLegacyWebhookConfigured: false });
    if (url.pathname === "/api/v1/integrations/shopify/events") {
      return json({
        items: [{
          id: "33333333-3333-3333-3333-333333333333",
          status: "RequiresAttention",
          topic: "orders/create",
          receivedAt: "2026-08-20T10:30:00Z",
          detail: "Delivery accepted for processing.",
          verificationMode: "Hmac",
          shopifyOrderId: "1001",
          shopDomain: "example.myshopify.com",
          attemptCount: 1,
          payloadAvailable: true
        }],
        page: 1,
        pageSize: 25,
        totalCount: 1
      });
    }
    if (url.pathname === "/api/v1/integrations/shopify/sku-readiness/products") return json([]);
    if (url.pathname === "/api/v1/integrations/shopify/sku-readiness") return json({ items: [], page: 1, pageSize: 50, totalCount: 0 });
    if (url.pathname.startsWith("/api/")) return json({});
    return route.continue();
  });
}

test("language switch keeps login content and document direction bilingual", async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem("lensee.language", "ar"));
  await page.goto("/#/login", { waitUntil: "domcontentloaded" });

  await expect(page.locator("html")).toHaveAttribute("lang", "ar-EG");
  await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
  await expect(page.locator("#login-form")).toContainText("تسجيل الدخول");
  await expect(page.locator("#login-language-toggle")).toHaveText("English");

  await page.locator("#login-language-toggle").click();

  await expect(page.locator("html")).toHaveAttribute("lang", "en");
  await expect(page.locator("html")).toHaveAttribute("dir", "ltr");
  await expect(page.locator("#login-form")).toContainText("Sign in");
  await expect(page.locator("#login-language-toggle")).toHaveText("العربية");
});

test("Arabic covers audit and Shopify workspaces and restores their English copy", async ({ page }) => {
  await mockAuthenticatedFrontend(page);
  await page.addInitScript(() => localStorage.setItem("lensee.language", "ar"));
  await page.goto("/#/audit", { waitUntil: "domcontentloaded" });

  await expect(page.locator("#page-title")).toHaveText("سجل التدقيق");
  await expect(page.locator("#audit-count")).toHaveText("1 حدث");
  await expect(page.locator("#audit-rows")).toContainText("تم إنشاء حساب الموظف Ahmed.");
  await expect(page.locator("#audit-search")).toHaveAttribute("placeholder", "شخص أو اسم سجل أو إجراء أو قيمة محفوظة");

  await page.evaluate(() => { location.hash = "/integrations"; });
  await expect(page.locator("#page-title")).toHaveText("استلام الطلبات الإلكترونية");
  await expect(page.locator("#shopify-queue-count")).toHaveText("1 حدث");
  await expect(page.locator("#shopify-event-list")).toContainText("يحتاج مراجعة");
  await expect(page.locator("#shopify-event-list")).toContainText("تم قبول الطلب للمعالجة.");
  await expect(page.locator("#shopify-sku-readiness")).toContainText("لا توجد رموز أصناف ERP نشطة تطابق هذا العرض.");

  await page.locator("#language-toggle").click();
  await expect(page.locator("html")).toHaveAttribute("dir", "ltr");
  await expect(page.locator("#page-title")).toHaveText("Online intake");
  await expect(page.locator("#shopify-event-list")).toContainText("RequiresAttention");
  await expect(page.locator("#shopify-event-list")).toContainText("Delivery accepted for processing.");
});
