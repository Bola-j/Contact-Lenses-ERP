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
    if (url.pathname === "/api/v1/reports/catalog") return json([]);
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

test("login failures use Arabic and English semantic error messages", async ({ page }) => {
  await page.route("**/api/v1/auth/login", (route) => route.fulfill({ status: 401, body: "Unauthorized" }));
  await page.addInitScript(() => localStorage.setItem("lensee.language", "ar"));
  await page.goto("/#/login", { waitUntil: "domcontentloaded" });
  await page.locator("#username").fill("wrong");
  await page.locator("#password").fill("wrong-password");
  await page.locator("#login-submit").click();
  await expect(page.locator("#login-error")).toHaveText("اسم المستخدم أو كلمة المرور غير صحيحة.");

  await page.locator("#login-language-toggle").click();
  await page.locator("#username").fill("wrong");
  await page.locator("#password").fill("wrong-password");
  await page.locator("#login-submit").click();
  await expect(page.locator("#login-error")).toHaveText("Username or password is incorrect.");
});

test("Arabic dashboard renders command and workspace copy from semantic keys", async ({ page }) => {
  await mockAuthenticatedFrontend(page);
  await page.addInitScript(() => localStorage.setItem("lensee.language", "ar"));
  await page.goto("/#/dashboard", { waitUntil: "domcontentloaded" });
  await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
  await expect(page.locator(".command-grid")).toContainText("قائمة العمليات");
  await expect(page.locator(".command-grid")).toContainText("متابعة المخزون");
  await expect(page.locator(".workspace-card-grid")).toContainText("النقد والبنوك والمحافظ");
});

test("Arabic Supply form renders semantic labels and keeps typed notes", async ({ page }) => {
  await mockAuthenticatedFrontend(page);
  await page.route("**/api/v1/inventory/locations", (route) => route.fulfill({ status: 200, contentType: "application/json", body: "[]" }));
  await page.addInitScript(() => localStorage.setItem("lensee.language", "ar"));
  await page.goto("/#/supply", { waitUntil: "domcontentloaded" });
  await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
  await expect(page.locator("#supply-detail h2")).toHaveText("تفاصيل الشحنة");
  await expect(page.locator("#supply-search")).toHaveAttribute("placeholder", "بحث في الشحنات");
  const notes = page.locator("#supply-notes");
  await notes.fill("Supplier agreed to deliver on Friday");
  await page.locator("#supply-add-line").click();
  await page.locator(".supply-line-notes").nth(1).fill("Second carton note");
  await page.locator("#supply-add-cost").click();
  await page.locator(".supply-cost-description").nth(1).fill("Broker document fee");
  await page.locator("#language-toggle").click();
  await expect(page.locator("html")).toHaveAttribute("dir", "ltr");
  await expect(page.locator("#supply-notes")).toHaveValue("Supplier agreed to deliver on Friday");
  await expect(page.locator(".supply-line-row")).toHaveCount(2);
  await expect(page.locator(".supply-line-notes").nth(1)).toHaveValue("Second carton note");
  await expect(page.locator(".supply-cost-row")).toHaveCount(2);
  await expect(page.locator(".supply-cost-description").nth(1)).toHaveValue("Broker document fee");
});

test("Reports keep filters when the presentation language changes", async ({ page }) => {
  await mockAuthenticatedFrontend(page);
  await page.goto("/#/reports", { waitUntil: "domcontentloaded" });
  await expect(page.locator("#report-filter-from")).toBeVisible();
  await page.locator("#report-filter-from").fill("2026-09-01");
  await page.locator("#report-filter-supply-status").selectOption("Draft");
  await page.locator("#language-toggle").click();
  await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
  await expect(page.locator("#report-filter-from")).toHaveValue("2026-09-01");
  await expect(page.locator("#report-filter-supply-status")).toHaveValue("Draft");
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

test("Arabic forms preserve user input and submit canonical English system values", async ({ page }) => {
  const currentUserId = "11111111-1111-1111-1111-111111111111";
  const locationId = "44444444-4444-4444-4444-444444444444";
  let submittedUser = null;

  await page.route("**/*", async (route) => {
    const url = new URL(route.request().url());
    const method = route.request().method();
    const json = (body, status = 200) => route.fulfill({ status, contentType: "application/json", body: JSON.stringify(body) });
    if (url.pathname === "/health") return json({ status: "Healthy" });
    if (url.pathname === "/api/v1/auth/refresh") {
      return json({ accessToken: "canonical-test-token", user: { userId: currentUserId, username: "admin", fullName: "Primary Admin", role: "Admin" } });
    }
    if (url.pathname === "/api/v1/notifications/unread-count") return json({ count: 0 });
    if (url.pathname === "/api/v1/users" && method === "GET") {
      return json([{ id: currentUserId, username: "admin", fullName: "Primary Admin", role: "Admin", locationId: null, isActive: true, isPrimaryAdmin: true, canDelete: false }]);
    }
    if (url.pathname === "/api/v1/inventory/locations" && method === "GET") {
      return json([{ id: locationId, name: "مخزن روكسي", locationType: "Retail", isActive: true }]);
    }
    if (url.pathname === "/api/v1/users" && method === "POST") {
      submittedUser = route.request().postDataJSON();
      return json({ id: "55555555-5555-5555-5555-555555555555", ...submittedUser }, 201);
    }
    if (url.pathname.startsWith("/api/")) return json({});
    return route.continue();
  });

  await page.addInitScript(() => localStorage.setItem("lensee.language", "ar"));
  await page.goto("/#/admin", { waitUntil: "domcontentloaded" });
  await page.locator("#admin-user-full-name").fill("أحمد حسن");
  await page.locator("#admin-user-username").fill("ahmed.hassan");
  await page.locator("#admin-user-role").selectOption("WarehouseClerk");
  await page.locator("#admin-user-location").selectOption(locationId);
  await page.locator("#admin-user-password").fill("Temporary123!");
  await page.locator("#admin-user-confirm-password").fill("Temporary123!");

  await page.locator("#language-toggle").click();
  await expect(page.locator("#admin-user-full-name")).toHaveValue("أحمد حسن");
  await page.locator("#language-toggle").click();
  await expect(page.locator("#admin-user-role")).toHaveValue("WarehouseClerk");

  await page.locator("#admin-create-user-form button[type='submit']").click();
  await expect.poll(() => submittedUser).not.toBeNull();
  expect(submittedUser).toMatchObject({
    fullName: "أحمد حسن",
    username: "ahmed.hassan",
    role: "WarehouseClerk",
    locationId
  });
});
