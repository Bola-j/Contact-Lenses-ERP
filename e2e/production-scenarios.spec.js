const { test, expect } = require("@playwright/test");
const {
  installApiBase,
  login,
  logout,
  users,
  makeRunData,
  gotoRoute,
  ensureCoreData,
  createOperationDraft,
  runLatestOperationAction,
  latestOperationId,
  apiRequest,
  apiJson,
  expectOneTransitionSucceeds,
  expectNoNegativeStock
} = require("./support/helpers");

test.beforeEach(async ({ page }) => {
  await installApiBase(page);
  await login(page, users.admin);
});

async function seedMainStock(page, data, quantity) {
  await ensureCoreData(page, data);
  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "InventoryReceipt",
    skuText: data.product,
    quantity: String(quantity),
    lot: data.mainLot,
    expiry: data.expiry,
    supplier: `${data.runId} Supplier`,
    invoice: `${data.runId}-INV`
  });
  await runLatestOperationAction(page, "InventoryReceipt", /Confirm/i);
}

async function createWholesaleSaleDraft(page, data, quantity, paymentMethod = "CashHandToHand") {
  await createOperationDraft(page, {
    type: "WholesaleSale",
    skuText: data.product,
    quantity: String(quantity),
    price: "100",
    stockText: data.mainLot,
    merchantText: data.merchant,
    paymentMethod,
    sourceText: /Roxy|Main/i
  });
  return await latestOperationId(page, "WholesaleSale", "Draft");
}

test("production: last available stock can be reserved by only one concurrent sale", async ({ page }) => {
  const data = makeRunData("RACE");
  await seedMainStock(page, data, 1);

  const firstSaleId = await createWholesaleSaleDraft(page, data, 1);
  const secondSaleId = await createWholesaleSaleDraft(page, data, 1);

  const responses = await Promise.all([
    apiRequest(page, "POST", `/api/v1/operations/${firstSaleId}/confirm`),
    apiRequest(page, "POST", `/api/v1/operations/${secondSaleId}/confirm`)
  ]);

  expect(responses.filter((response) => response.ok()).length).toBe(1);
  expect(responses.filter((response) => [400, 409, 422].includes(response.status())).length).toBe(1);
  await expectNoNegativeStock(page);
});

test("production: double-submit on receipt confirmation mutates stock once", async ({ page }) => {
  const data = makeRunData("DBLREC");
  await ensureCoreData(page, data);
  await gotoRoute(page, "/operations");
  await createOperationDraft(page, {
    type: "InventoryReceipt",
    skuText: data.product,
    quantity: "2",
    lot: data.mainLot,
    expiry: data.expiry,
    supplier: `${data.runId} Supplier`,
    invoice: `${data.runId}-INV`
  });
  const receiptId = await latestOperationId(page, "InventoryReceipt", "Draft");

  await expectOneTransitionSucceeds(page, [
    apiRequest(page, "POST", `/api/v1/operations/${receiptId}/confirm`),
    apiRequest(page, "POST", `/api/v1/operations/${receiptId}/confirm`)
  ]);
  await expectNoNegativeStock(page);
});

test("production: stale-stock sale submission fails safely after another sale reserves the stock", async ({ page }) => {
  const data = makeRunData("STALE");
  await seedMainStock(page, data, 1);

  const staleSaleId = await createWholesaleSaleDraft(page, data, 1);
  const winningSaleId = await createWholesaleSaleDraft(page, data, 1);
  const winningResponse = await apiRequest(page, "POST", `/api/v1/operations/${winningSaleId}/confirm`);
  expect(winningResponse.ok()).toBeTruthy();

  const staleResponse = await apiRequest(page, "POST", `/api/v1/operations/${staleSaleId}/confirm`);
  expect([400, 409, 422]).toContain(staleResponse.status());
  await expectNoNegativeStock(page);
});

test("production: supply receive race increases destination stock once", async ({ page }) => {
  const data = makeRunData("SUPRACE");
  await seedMainStock(page, data, 4);

  await createOperationDraft(page, {
    type: "WarehouseTransfer",
    skuText: data.product,
    quantity: "2",
    stockText: data.mainLot,
    destinationText: /Retail|Online|Mohamed/i
  });
  const transferId = await latestOperationId(page, "WarehouseTransfer", "Draft");
  expect((await apiRequest(page, "POST", `/api/v1/operations/${transferId}/confirm`)).ok()).toBeTruthy();
  expect((await apiRequest(page, "POST", `/api/v1/operations/${transferId}/ship`)).ok()).toBeTruthy();

  await expectOneTransitionSucceeds(page, [
    apiRequest(page, "POST", `/api/v1/operations/${transferId}/receive`),
    apiRequest(page, "POST", `/api/v1/operations/${transferId}/receive`)
  ]);
  await expectNoNegativeStock(page);
});

test("production: payment approval race allows one terminal transition", async ({ page }) => {
  const data = makeRunData("PAYRACE");
  await seedMainStock(page, data, 2);
  const saleId = await createWholesaleSaleDraft(page, data, 1, "Installment");
  expect((await apiRequest(page, "POST", `/api/v1/operations/${saleId}/confirm`)).ok()).toBeTruthy();
  expect((await apiRequest(page, "POST", `/api/v1/operations/${saleId}/ship`)).ok()).toBeTruthy();
  expect((await apiRequest(page, "POST", `/api/v1/operations/${saleId}/complete`)).ok()).toBeTruthy();

  const { response: paymentListResponse, data: paymentList } = await apiJson(page, "GET", "/api/v1/payments?page=1&pageSize=50");
  expect(paymentListResponse.ok()).toBeTruthy();
  const paymentItems = Array.isArray(paymentList) ? paymentList : paymentList?.items || paymentList?.data || [];
  const payment = paymentItems.find((item) => item.operationId === saleId || item.operationId === String(saleId));
  expect(payment).toBeTruthy();

  const { response: usersResponse, data: userList } = await apiJson(page, "GET", "/api/v1/users?page=1&pageSize=100");
  expect(usersResponse.ok()).toBeTruthy();
  const userItems = Array.isArray(userList) ? userList : userList?.items || userList?.data || [];
  const accountant = userItems.find((user) => user.username === users.accountant.username || user.role === "Accountant");
  expect(accountant).toBeTruthy();
  const assignResponse = await apiRequest(page, "POST", `/api/v1/payments/${payment.id}/assign`, { accountantUserId: accountant.id });
  expect(assignResponse.ok()).toBeTruthy();

  await logout(page);
  await login(page, users.accountant);
  const draftResponse = await apiRequest(page, "POST", `/api/v1/payments/${payment.id}/sub-logs`, {
    amount: 100,
    paymentMethod: "CashTransaction",
    dateReceived: "2026-07-08",
    notes: `${data.runId} race draft`
  });
  expect([200, 201]).toContain(draftResponse.status());
  const drafted = await draftResponse.json();
  const subLogId = drafted.id || drafted.subLogId || drafted.subLogs?.[0]?.id || drafted.log?.subLogs?.[0]?.id;
  expect(subLogId).toBeTruthy();

  await logout(page);
  await login(page, users.admin);
  await expectOneTransitionSucceeds(page, [
    apiRequest(page, "POST", `/api/v1/payments/sub-logs/${subLogId}/approve`),
    apiRequest(page, "POST", `/api/v1/payments/sub-logs/${subLogId}/reject`, { reason: "Concurrent reject" })
  ]);
});




