const { test, expect } = require("@playwright/test");
const {
  installApiBase,
  login,
  logout,
  gotoLogin,
  gotoRoute,
  users,
  apiJson,
  apiRequest,
  expectApiForbidden
} = require("./support/helpers");

test.beforeEach(async ({ page }) => {
  await installApiBase(page);
});

test("auth: login, invalid credentials, and role-aware navigation", async ({ page }) => {
  await gotoLogin(page);
  await page.locator("#username").fill("not_a_real_user");
  await page.locator("#password").fill("wrong-password");
  await page.locator("#login-submit").click();
  await expect(page.locator("#login-error")).toContainText(/incorrect|failed|invalid|غير صحيحة/i);

  await login(page, users.admin);
  for (const label of ["Catalog", "Inventory", "CRM", "Operations", "Payments", "Notifications", "Reports", "Stocktake", "Admin"]) {
    await expect(page.locator("#nav a", { hasText: label })).toBeVisible();
  }
  await logout(page);

  await login(page, users.clevel);
  await expect(page.locator("#nav a", { hasText: "Reports" })).toBeVisible();
  await expect(page.locator("#nav a", { hasText: "Admin" })).toHaveCount(0);
  await gotoRoute(page, "/catalog");
  await expect(page.locator("#product-form")).toHaveCount(0);
  await logout(page);

  await login(page, users.accountant);
  await expect(page.locator("#nav a", { hasText: "Payments" })).toBeVisible();
  await expect(page.locator("#nav a", { hasText: "Inventory" })).toHaveCount(0);
  await expect(page.locator("#nav a", { hasText: "Catalog" })).toHaveCount(0);
  await logout(page);

  await login(page, users.clerk);
  await expect(page.locator("#nav a", { hasText: "Inventory" })).toBeVisible();
  await expect(page.locator("#nav a", { hasText: "Operations" })).toBeVisible();
  await expect(page.locator("#nav a", { hasText: "Admin" })).toHaveCount(0);
});

test("roles: every seeded role has the expected navigation and warehouse scope", async ({ page }) => {
  const expectedNav = {
    admin: ["Dashboard", "Catalog", "Inventory", "CRM", "Operations", "Payments", "Notifications", "Reports", "Stocktake", "Admin"],
    clevel: ["Dashboard", "Catalog", "Inventory", "CRM", "Operations", "Payments", "Notifications", "Reports", "Stocktake"],
    accountant: ["Dashboard", "CRM", "Operations", "Payments", "Notifications", "Reports"],
    roxyClerk: ["Dashboard", "Catalog", "Inventory", "CRM", "Operations", "Notifications"],
    retailClerk: ["Dashboard", "Catalog", "Inventory", "CRM", "Operations", "Notifications"],
    onlineClerk: ["Dashboard", "Catalog", "Inventory", "CRM", "Operations", "Notifications"]
  };

  const hiddenNav = {
    admin: [],
    clevel: ["Admin"],
    accountant: ["Catalog", "Inventory", "Stocktake", "Admin"],
    roxyClerk: ["Payments", "Reports", "Stocktake", "Admin"],
    retailClerk: ["Payments", "Reports", "Stocktake", "Admin"],
    onlineClerk: ["Payments", "Reports", "Stocktake", "Admin"]
  };

  for (const roleKey of Object.keys(expectedNav)) {
    await login(page, users[roleKey]);
    for (const label of expectedNav[roleKey]) {
      await expect(page.locator("#nav a", { hasText: label }), `${roleKey} should see ${label}`).toBeVisible();
    }
    for (const label of hiddenNav[roleKey]) {
      await expect(page.locator("#nav a", { hasText: label }), `${roleKey} should not see ${label}`).toHaveCount(0);
    }
    await logout(page);
  }

  const clerkLocations = [
    { user: users.roxyClerk, expected: /Roxy/i },
    { user: users.retailClerk, expected: /Mohamed|Retail/i },
    { user: users.onlineClerk, expected: /Online/i }
  ];

  for (const clerk of clerkLocations) {
    await login(page, clerk.user);
    const { response, data } = await apiJson(page, "GET", "/api/v1/inventory/locations");
    expect(response.ok()).toBeTruthy();
    expect(data).toHaveLength(1);
    expect(data[0].name).toMatch(clerk.expected);
    await logout(page);
  }
});

test("permissions: direct API requests are rejected for forbidden roles", async ({ page }) => {
  await login(page, users.clevel);
  await expectApiForbidden(page, "POST", "/api/v1/catalog/brands", { name: "Forbidden CLevel Brand" });
  await expectApiForbidden(page, "POST", "/api/v1/payments/initialize", { operationId: "00000000-0000-0000-0000-000000000000" });
  await logout(page);

  await login(page, users.accountant);
  await expectApiForbidden(page, "GET", "/api/v1/inventory/locations");
  await expectApiForbidden(page, "POST", "/api/v1/catalog/categories", { name: "Forbidden Accountant Category" });
  await logout(page);

  await login(page, users.clerk);
  await expectApiForbidden(page, "GET", "/api/v1/users");
  await expectApiForbidden(page, "POST", "/api/v1/alerts/run/low-stock");
});

test("roles: API function matrix matches every seeded user's permissions", async ({ page }) => {
  const readChecks = {
    admin: [
      "/api/v1/users",
      "/api/v1/catalog/categories",
      "/api/v1/inventory/locations",
      "/api/v1/crm/merchants?pageSize=1",
      "/api/v1/operations?pageSize=1",
      "/api/v1/payments?pageSize=1",
      "/api/v1/reports/exports?pageSize=1",
      "/api/v1/stocktakes?pageSize=1",
      "/api/v1/notifications/unread-count"
    ],
    clevel: [
      "/api/v1/catalog/categories",
      "/api/v1/inventory/locations",
      "/api/v1/crm/merchants?pageSize=1",
      "/api/v1/operations?pageSize=1",
      "/api/v1/payments?pageSize=1",
      "/api/v1/reports/exports?pageSize=1",
      "/api/v1/stocktakes?pageSize=1",
      "/api/v1/notifications/unread-count"
    ],
    accountant: [
      "/api/v1/crm/merchants?pageSize=1",
      "/api/v1/operations?pageSize=1",
      "/api/v1/payments?pageSize=1",
      "/api/v1/reports/exports?pageSize=1",
      "/api/v1/notifications/unread-count"
    ],
    roxyClerk: [
      "/api/v1/catalog/categories",
      "/api/v1/inventory/locations",
      "/api/v1/crm/merchants?pageSize=1",
      "/api/v1/operations?pageSize=1",
      "/api/v1/stocktakes?pageSize=1",
      "/api/v1/notifications/unread-count"
    ],
    retailClerk: [
      "/api/v1/catalog/categories",
      "/api/v1/inventory/locations",
      "/api/v1/crm/merchants?pageSize=1",
      "/api/v1/operations?pageSize=1",
      "/api/v1/stocktakes?pageSize=1",
      "/api/v1/notifications/unread-count"
    ],
    onlineClerk: [
      "/api/v1/catalog/categories",
      "/api/v1/inventory/locations",
      "/api/v1/crm/merchants?pageSize=1",
      "/api/v1/operations?pageSize=1",
      "/api/v1/stocktakes?pageSize=1",
      "/api/v1/notifications/unread-count"
    ]
  };

  const forbiddenReads = {
    admin: [],
    clevel: ["/api/v1/users"],
    accountant: ["/api/v1/catalog/categories", "/api/v1/inventory/locations", "/api/v1/stocktakes?pageSize=1", "/api/v1/users"],
    roxyClerk: ["/api/v1/payments?pageSize=1", "/api/v1/reports/exports?pageSize=1", "/api/v1/users"],
    retailClerk: ["/api/v1/payments?pageSize=1", "/api/v1/reports/exports?pageSize=1", "/api/v1/users"],
    onlineClerk: ["/api/v1/payments?pageSize=1", "/api/v1/reports/exports?pageSize=1", "/api/v1/users"]
  };

  const forbiddenWrites = {
    admin: [],
    clevel: [
      ["POST", "/api/v1/catalog/brands", { name: "Forbidden C-Level Brand" }],
      ["POST", "/api/v1/crm/merchants", { businessName: "Forbidden", contactPersonName: "Forbidden" }],
      ["POST", "/api/v1/payments/initialize", { operationId: "00000000-0000-0000-0000-000000000000" }],
      ["POST", "/api/v1/alerts/run/low-stock", null]
    ],
    accountant: [
      ["POST", "/api/v1/catalog/brands", { name: "Forbidden Accountant Brand" }],
      ["POST", "/api/v1/inventory/receipts", {}],
      ["POST", "/api/v1/crm/merchants", { businessName: "Forbidden", contactPersonName: "Forbidden" }],
      ["POST", "/api/v1/operations", {}],
      ["POST", "/api/v1/payments/initialize", { operationId: "00000000-0000-0000-0000-000000000000" }],
      ["POST", "/api/v1/alerts/run/low-stock", null]
    ],
    roxyClerk: [
      ["POST", "/api/v1/catalog/brands", { name: "Forbidden Clerk Brand" }],
      ["POST", "/api/v1/inventory/receipts", {}],
      ["POST", "/api/v1/payments/initialize", { operationId: "00000000-0000-0000-0000-000000000000" }],
      ["POST", "/api/v1/alerts/run/low-stock", null]
    ],
    retailClerk: [
      ["POST", "/api/v1/catalog/brands", { name: "Forbidden Clerk Brand" }],
      ["POST", "/api/v1/inventory/receipts", {}],
      ["POST", "/api/v1/payments/initialize", { operationId: "00000000-0000-0000-0000-000000000000" }],
      ["POST", "/api/v1/alerts/run/low-stock", null]
    ],
    onlineClerk: [
      ["POST", "/api/v1/catalog/brands", { name: "Forbidden Clerk Brand" }],
      ["POST", "/api/v1/inventory/receipts", {}],
      ["POST", "/api/v1/payments/initialize", { operationId: "00000000-0000-0000-0000-000000000000" }],
      ["POST", "/api/v1/alerts/run/low-stock", null]
    ]
  };

  for (const roleKey of Object.keys(readChecks)) {
    await login(page, users[roleKey]);

    for (const path of readChecks[roleKey]) {
      const response = await apiRequest(page, "GET", path);
      expect(response.ok(), `${roleKey} should read ${path}, got ${response.status()}`).toBeTruthy();
    }

    for (const path of forbiddenReads[roleKey]) {
      await expectApiForbidden(page, "GET", path);
    }

    for (const [method, path, body] of forbiddenWrites[roleKey]) {
      await expectApiForbidden(page, method, path, body);
    }

    await logout(page);
  }

  await login(page, users.admin);
  for (const [method, path, body] of [
    ["POST", "/api/v1/catalog/brands", { name: "" }],
    ["POST", "/api/v1/inventory/receipts", {}],
    ["POST", "/api/v1/payments/initialize", { operationId: "00000000-0000-0000-0000-000000000000" }],
    ["POST", "/api/v1/alerts/run/low-stock", null]
  ]) {
    const response = await apiRequest(page, method, path, body);
    expect([400, 404, 409, 422].includes(response.status()) || response.ok(), `Admin should pass authorization for ${path}, got ${response.status()}`).toBeTruthy();
  }
});
