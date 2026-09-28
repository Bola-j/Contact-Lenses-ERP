const { test, expect } = require("@playwright/test");

async function mockApi(page, authenticated = false) {
  await page.route("**/api/**", (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ count: 0, items: [] }) }));
  await page.route("**/health", (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ status: "Healthy" }) }));
  await page.route("**/api/v1/auth/refresh", (route) => route.fulfill({
    status: authenticated ? 200 : 401,
    contentType: "application/json",
    body: authenticated ? JSON.stringify({ accessToken: "prefix-test", user: { userId: "1", username: "admin", fullName: "Admin", role: "Admin" } }) : ""
  }));
}

test("language prefixes own the language and normalize legacy URLs without loops", async ({ page }) => {
  await mockApi(page);
  await page.goto("/ar/#/dashboard", { waitUntil: "domcontentloaded" });
  await expect(page.locator("html")).toHaveAttribute("lang", "ar-EG");
  await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
  await expect(page).toHaveURL(/\/ar\/#\/login$/);

  await page.goto("/#/dashboard", { waitUntil: "domcontentloaded" });
  await expect(page).toHaveURL(/\/en\/#\/login$/);
  await page.goBack();
  await expect(page).toHaveURL(/\/(?:ar|en)\/#\/login$/);
});

test("toggle preserves complete URL-owned state and creates navigable history", async ({ page }) => {
  await mockApi(page, true);
  await page.goto("/en/?source=x#/reports?x=1&ref=A%2FB", { waitUntil: "domcontentloaded" });
  await expect(page.locator("html")).toHaveAttribute("dir", "ltr");
  await page.locator("#language-toggle").click();
  await expect(page).toHaveURL(/\/ar\/\?source=x#\/reports\?x=1&ref=A%2FB$/);
  await expect(page.locator("html")).toHaveAttribute("dir", "rtl");
  await page.goBack();
  await expect(page).toHaveURL(/\/en\/\?source=x#\/reports\?x=1&ref=A%2FB$/);
});

test("prefixed assets are root resources and unknown static files are not SPA HTML", async ({ page, request }) => {
  await mockApi(page);
  await page.goto("/en/#/login", { waitUntil: "domcontentloaded" });
  const [script, stylesheet, missingScript, missingStyle] = await Promise.all([
    request.get("/app.js"), request.get("/styles.css"), request.get("/missing-prefix-asset.js"), request.get("/missing-prefix-asset.css")
  ]);
  expect(script.headers()["content-type"]).toContain("javascript");
  expect(stylesheet.headers()["content-type"]).toContain("text/css");
  expect(missingScript.status()).toBe(404);
  expect(missingStyle.status()).toBe(404);
});

test("recognized prefixes canonicalize and unknown language paths stay unsupported", async ({ page, request }) => {
  await mockApi(page);
  await page.goto("/AR/foo#/dashboard", { waitUntil: "domcontentloaded" });
  await expect(page).toHaveURL(/\/ar\/#\/login$/);
  await expect(page.locator("html")).toHaveAttribute("dir", "rtl");

  await page.goto("/en/", { waitUntil: "domcontentloaded" });
  await expect(page).toHaveURL(/\/en\/#\/login$/);
  await expect(page.locator("html")).toHaveAttribute("dir", "ltr");

  const unsupported = await request.get("/fr/#/dashboard");
  expect(unsupported.status()).toBe(404);
});
