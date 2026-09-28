const { test, expect } = require("@playwright/test");
const { users, login, gotoRoute } = require("./support/helpers");

for (const [role, user] of [["Admin", users.admin], ["C-Level", users.clevel], ["Accountant", users.accountant]]) {
  test(`Finance withdrawal workspace follows permissions for ${role}`, async ({ page }) => {
    const logId = "10000000-0000-4000-8000-000000000001";
    const shipmentLog = {
      id: logId, supplyShipmentId: "20000000-0000-4000-8000-000000000002", shipmentNumber: "SUP-TEST-01",
      supplierName: "Factory Test", status: "Draft", notes: null, totalLandedCost: 150,
      scheduledAmount: 0, amountPaid: 0, outstandingBalance: 150,
      categoryTotals: { ProductCost: 100, CustomsCost: 20, ShipmentCost: 25, Handling: 5 },
      costs: [], installments: []
    };
    await page.route("**/api/v1/finance/supply-logs**", async (route) => {
      const url = new URL(route.request().url());
      const path = url.pathname;
      if (path.endsWith(`/${logId}`) && route.request().method() === "GET") return route.fulfill({ json: shipmentLog });
      if (path.endsWith(`/${logId}/costs`) && route.request().method() === "POST") {
        const body = route.request().postDataJSON();
        const cost = { id: `cost-${shipmentLog.costs.length + 1}`, ...body, status: "Active" };
        shipmentLog.costs.push(cost);
        shipmentLog.categoryTotals[body.category] = (shipmentLog.categoryTotals[body.category] || 0) + body.amount;
        shipmentLog.totalLandedCost += body.amount;
        shipmentLog.outstandingBalance += body.amount;
        return route.fulfill({ status: 201, json: cost });
      }
      if (path.endsWith(`/${logId}/installments`) && route.request().method() === "POST") {
        const installment = { id: `installment-${shipmentLog.installments.length + 1}`, ...route.request().postDataJSON(), status: "Draft" };
        shipmentLog.installments.push(installment);
        shipmentLog.scheduledAmount += installment.amount;
        return route.fulfill({ status: 201, json: installment });
      }
      if (path.includes(`/${logId}/installments/`) && path.endsWith("/post")) {
        const installmentId = path.split("/").at(-2);
        const installment = shipmentLog.installments.find((item) => item.id === installmentId);
        installment.status = "Posted";
        shipmentLog.amountPaid += installment.amount;
        shipmentLog.outstandingBalance -= installment.amount;
        return route.fulfill({ json: installment });
      }
      return route.fulfill({ json: { items: [shipmentLog], page: 1, pageSize: 50, totalCount: 1 } });
    });
    await page.route("**/api/v1/finance/accounts", (route) => route.fulfill({ json: [{ id: "30000000-0000-4000-8000-000000000003", name: "Cash desk", type: "CashOnHand", isActive: true }] }));
    await login(page, user);
    await expect(page.locator("#nav a[href='#/finance']")).toBeVisible();
    await gotoRoute(page, "/finance");
    await expect(page).toHaveURL(/#\/finance$/);
    await expect(page.locator(".finance-ledger-layout")).toBeVisible();
    await expect(page.locator("#finance-withdrawals")).toBeVisible();
    await expect(page.locator(".finance-supply-panel")).toBeVisible();
    await page.locator("[data-finance-supply-log]").click();
    await expect(page.locator("#finance-supply-detail")).toBeVisible();
    await expect(page.locator(".finance-supply-category-grid")).toContainText("Product cost");
    if (role === "Admin") {
      await expect(page.locator("#finance-withdrawal-form")).toBeVisible();
      await expect(page.locator(".finance-withdrawal-warning")).toBeVisible();
      await expect(page.getByRole("button", { name: "Post withdrawal" })).toBeVisible();
      await expect(page.locator("#finance-supply-cost-form")).toBeVisible();
      await expect(page.locator("#finance-supply-payment-form")).toBeVisible();
      await page.locator('#finance-supply-cost-form [name="amount"]').fill("10");
      await page.locator('#finance-supply-cost-form [name="businessDate"]').fill("2026-09-26");
      await page.locator('#finance-supply-cost-form button[type="submit"]').click();
      await expect(page.locator("#finance-supply-detail")).toContainText("10");
      await page.locator('#finance-supply-payment-form [name="financeAccountId"]').selectOption("30000000-0000-4000-8000-000000000003");
      await page.locator('#finance-supply-payment-form [name="amount"]').fill("20");
      await page.locator('#finance-supply-payment-form [name="businessDate"]').fill("2026-09-26");
      await page.locator('#finance-supply-payment-form button[type="submit"]').click();
      await expect(page.locator("#finance-supply-detail")).toContainText("Draft");
      await page.locator("[data-finance-supply-post]").click();
      // The confirmation helper uses a stable semantic action hook; the
      // visible label is localized and may vary between English/Arabic.
      await page.locator('[role="alertdialog"] [data-dialog-confirm]').click();
      await expect(page.locator("#finance-supply-detail")).toContainText("Posted");
    } else {
      await expect(page.locator("#finance-withdrawal-form")).toHaveCount(0);
      await expect(page.locator("#finance-supply-cost-form")).toHaveCount(0);
      await expect(page.locator("#finance-supply-payment-form")).toHaveCount(0);
    }
  });
}
