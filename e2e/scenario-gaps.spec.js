const { test, expect } = require("@playwright/test");
const {
  installApiBase,
  login,
  logout,
  users,
  makeRunData,
  gotoRoute,
  apiJson,
  apiRequest,
  expectApiForbidden
} = require("./support/helpers");

test.beforeEach(async ({ page }) => {
  await installApiBase(page);
});

test("scenario DAY-02: admin maintains user lifecycle, password state, and duplicate validation", async ({ page }) => {
  const data = makeRunData("USER");
  const username = `${data.runId.toLowerCase()}_accountant`;
  const firstPassword = "TempUser123!";
  const secondPassword = "TempUser456!";

  await login(page, users.admin);

  const weakPassword = await apiRequest(page, "POST", "/api/v1/users", {
    username: `${username}_weak`,
    password: "short",
    fullName: `${data.runId} Weak User`,
    role: "Accountant",
    locationId: null
  });
  expect(weakPassword.status()).toBe(400);

  const { response: createdResponse, data: created } = await apiJson(page, "POST", "/api/v1/users", {
    username,
    password: firstPassword,
    fullName: `${data.runId} Accountant`,
    role: "Accountant",
    locationId: null
  });
  expect(createdResponse.status()).toBe(201);
  expect(created.username).toBe(username);
  expect(created.role).toBe("Accountant");
  expect(created.isActive).toBe(true);

  const duplicate = await apiRequest(page, "POST", "/api/v1/users", {
    username,
    password: firstPassword,
    fullName: `${data.runId} Duplicate`,
    role: "Accountant",
    locationId: null
  });
  expect(duplicate.status()).toBe(409);

  const listed = await apiJson(page, "GET", "/api/v1/users");
  expect(listed.response.ok()).toBeTruthy();
  expect(listed.data.some((user) => user.id === created.id && user.username === username)).toBeTruthy();

  await logout(page);
  await login(page, { username, password: firstPassword });
  await expect(page.locator("#nav a", { hasText: "Payments" })).toBeVisible();
  await expect(page.locator("#nav a", { hasText: "Admin" })).toHaveCount(0);
  await logout(page);

  await login(page, users.admin);
  const weakChange = await apiRequest(page, "PATCH", `/api/v1/users/${created.id}/password`, { newPassword: "short" });
  expect(weakChange.status()).toBe(400);

  const changePassword = await apiRequest(page, "PATCH", `/api/v1/users/${created.id}/password`, { newPassword: secondPassword });
  expect(changePassword.status()).toBe(204);
  await logout(page);

  await page.goto("/#/login");
  await page.locator("#username").fill(username);
  await page.locator("#password").fill(firstPassword);
  await page.locator("#login-submit").click();
  await expect(page.locator("#login-error")).toContainText(/incorrect|failed|invalid/i);

  await login(page, { username, password: secondPassword });
  await logout(page);

  await login(page, users.admin);
  const deactivate = await apiJson(page, "PATCH", `/api/v1/users/${created.id}/deactivate`);
  expect(deactivate.response.ok()).toBeTruthy();
  expect(deactivate.data.isActive).toBe(false);
  await logout(page);

  await page.goto("/#/login");
  await page.locator("#username").fill(username);
  await page.locator("#password").fill(secondPassword);
  await page.locator("#login-submit").click();
  await expect(page.locator("#login-error")).toContainText(/incorrect|failed|invalid/i);

  await login(page, users.admin);
  const reactivate = await apiJson(page, "PATCH", `/api/v1/users/${created.id}/activate`);
  expect(reactivate.response.ok()).toBeTruthy();
  expect(reactivate.data.isActive).toBe(true);
  await logout(page);

  await login(page, { username, password: secondPassword });
  await expect(page.locator("#page-title")).toContainText("Overview");
});

test("scenario DAY-02 authorization: non-admin roles cannot maintain users", async ({ page }) => {
  const data = makeRunData("FORBIDUSER");
  const body = {
    username: `${data.runId.toLowerCase()}_clerk`,
    password: "TempUser123!",
    fullName: `${data.runId} Forbidden User`,
    role: "WarehouseClerk",
    locationId: "11111111-1111-1111-1111-111111111111"
  };

  for (const credential of [users.clevel, users.accountant, users.roxyClerk]) {
    await login(page, credential);
    await expectApiForbidden(page, "GET", "/api/v1/users");
    await expectApiForbidden(page, "POST", "/api/v1/users", body);
    await gotoRoute(page, "/admin");
    await expect(page.locator("#nav a", { hasText: "Admin" })).toHaveCount(0);
    await logout(page);
  }
});

test("scenario DAY-01 session: parallel logins remain usable after one logout", async ({ browser }) => {
  const first = await browser.newPage();
  const second = await browser.newPage();
  await installApiBase(first);
  await installApiBase(second);

  try {
    await login(first, users.admin);
    await login(second, users.admin);

    const beforeLogout = await apiRequest(second, "GET", "/api/v1/auth/me");
    expect(beforeLogout.ok()).toBeTruthy();

    await logout(first);

    const afterLogout = await apiRequest(second, "GET", "/api/v1/auth/me");
    expect(afterLogout.ok()).toBeTruthy();
    await gotoRoute(second, "/dashboard");
    await expect(second.locator("#page-title")).toContainText("Overview");
  } finally {
    await first.close();
    await second.close();
  }
});

test("scenario DAY-01 session: invalid stored tokens cannot access protected APIs", async ({ page }) => {
  await page.goto("/#/login");
  await page.evaluate(() => {
    window.localStorage.setItem("lensee.auth", JSON.stringify({
      accessToken: "invalid-access-token",
      refreshToken: "invalid-refresh-token",
      user: { userId: "00000000-0000-0000-0000-000000000000", role: "Admin", locationId: null }
    }));
  });

  const response = await apiRequest(page, "GET", "/api/v1/users");
  expect([401, 403]).toContain(response.status());
});
