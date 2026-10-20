const fs = require("node:fs/promises");
const path = require("node:path");
const { test, expect } = require("@playwright/test");
const { users, installApiBase, login, gotoRoute } = require("./support/auth");

test.use({ channel: "chrome" });
test.beforeEach(async ({ page }) => installApiBase(page));

const roleName = process.env.LENSEE_E2E_SHOPIFY_ROLE === "ERPAdmin" ? "ERPAdmin" : "Admin";
const credential = roleName === "ERPAdmin" ? users.erpAdmin : users.admin;

test.describe(`Shopify governance UI (${roleName})`, () => {
  test.skip(process.env.LENSEE_E2E_SHOPIFY_CERTIFY !== "1", "Requires the isolated Shopify QA environment and its configured e2e account.");

  test(`${roleName} reviews queue, reconciliation and mappings in English and Arabic without writing business data`, async ({ page }) => {
    const businessMutations = [];
    const shopifyReads = new Set();
    page.on("request", (request) => {
      if (!request.url().includes("/api/v1/integrations/shopify/")) return;
      if (request.method() === "GET") shopifyReads.add(new URL(request.url()).pathname);
      if (!["GET", "HEAD", "OPTIONS"].includes(request.method())) businessMutations.push(`${request.method()} ${new URL(request.url()).pathname}`);
    });

    await login(page, credential);
    const reconciliationResponsePromise = page.waitForResponse((response) =>
      response.request().method() === "GET" && new URL(response.url()).pathname === "/api/v1/integrations/shopify/reconciliation");
    const eventsResponsePromise = page.waitForResponse((response) =>
      response.request().method() === "GET" && new URL(response.url()).pathname === "/api/v1/integrations/shopify/events");
    await page.goto("/en/#/integrations");
    const [reconciliationResponse, eventsResponse] = await Promise.all([reconciliationResponsePromise, eventsResponsePromise]);
    const [reconciliation, events] = await Promise.all([reconciliationResponse.json(), eventsResponse.json()]);
    await expect(page.locator("#page-title")).toContainText(/Online intake|Shopify intake/i);
    await expect(page.getByRole("heading", { name: "Queue health" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Order reconciliation" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Shopify variant links" })).toBeVisible();
    await expect(page.locator("#shopify-sku-status option[value='NeedsPackConfiguration']")).toHaveText("Set pieces per pack");
    await expect(page.locator("#shopify-queue-alerts")).toHaveAttribute("role", "status");
    const refreshableEventCount = reconciliation.available === true
      ? events.items.filter((event) => Boolean(event.shopifyOrderId)).length
      : 0;
    await expect(page.locator("#shopify-event-list [data-shopify-refresh]")).toHaveCount(refreshableEventCount);
    const retryableEventCount = events.items.filter((event) => event.status === "RequiresAttention").length;
    await expect(page.locator("#shopify-event-list [data-shopify-retry]")).toHaveCount(retryableEventCount);
    if (retryableEventCount > 0) await expect(page.locator("#shopify-event-list [data-shopify-retry]")).toHaveText(/Replay captured delivery/);
    await expect(page.locator("#shopify-event-list")).not.toContainText(/\b(?:undefined|null)\b/i);
    await expect(page.locator("#shopify-reconciliation")).not.toContainText(/\{\s*"/);

    const visibleText = await page.locator("#view").innerText();
    expect(visibleText).not.toMatch(/\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b/i);
    await expect(page.locator("#shopify-reconciliation-run")).toBeVisible();
    const artifactDirectory = path.resolve("artifacts/overnight/shopify-ui");
    await fs.mkdir(artifactDirectory, { recursive: true });
    await page.screenshot({ path: path.join(artifactDirectory, `${roleName}-governance-en.png`), fullPage: true });

    await page.locator("#language-toggle").click();
    await expect(page).toHaveURL(/\/ar\//);
    const importedDraftCount = events.items.filter((event) => event.detail === "Shopify order was imported as an unallocated draft.").length;
    await expect(page.getByRole("heading", { name: "حالة قائمة المعالجة" })).toBeVisible();
    await expect(page.locator("#shopify-sku-status option[value='NeedsPackConfiguration']")).toHaveText("تحديد عدد القطع في العبوة");
    if (refreshableEventCount > 0) await expect(page.locator("#shopify-event-list [data-shopify-refresh]")).toHaveText(/تحديث من Shopify/);
    if (retryableEventCount > 0) await expect(page.locator("#shopify-event-list [data-shopify-retry]")).toHaveText(/إعادة تشغيل الحمولة المستلمة/);
    if (importedDraftCount > 0) await expect(page.locator("#shopify-event-list")).toContainText("تم استيراد طلب Shopify كمسودة غير مخصصة للمخزون.");
    await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
    await page.screenshot({ path: path.join(artifactDirectory, `${roleName}-governance-ar.png`), fullPage: true });

    await gotoRoute(page, "/integrations");
    await expect(page.locator("#shopify-reconciliation-run")).toBeVisible();
    for (const endpoint of ["/events", "/queue-health", "/reconciliation", "/variant-links"]) {
      expect(shopifyReads.has(`/api/v1/integrations/shopify${endpoint}`), `Missing real UI read for ${endpoint}`).toBeTruthy();
    }
    expect(businessMutations).toEqual([]);
  });
});
