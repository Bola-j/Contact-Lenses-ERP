using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Inventory.Services;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Lensee.Tests;

public sealed class OperationsEndpointContractTests : IClassFixture<OperationsEndpointFactory>
{
    private readonly OperationsEndpointFactory _factory;

    public OperationsEndpointContractTests(OperationsEndpointFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InventoryReceipt_RejectsNonMainDestination()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite);

        var response = await client.PostAsJsonAsync("/api/v1/operations", new
        {
            operationType = "InventoryReceipt",
            destinationLocationId = seed.OnlineLocationId,
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1 } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanConfirmInventoryReceiptIntoMainWarehouse()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "InventoryReceipt",
            destinationLocationId = seed.MainLocationId,
            receipt = new { supplierName = "Supplier", invoiceNumber = "INV-1" },
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 8, lotNumber = "MAIN-1", expiryDate = "2028-06-01" } }
        });

        var confirm = await client.PostAsync($"/api/v1/operations/{operation.Id}/confirm", null);
        var balances = await client.GetFromJsonAsync<PagedContract<StockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");

        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Contains(balances!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 8);
    }

    [Fact]
    public async Task WarehouseTransfer_ConfirmReserveShipReceive_MovesPacksBetweenWarehouses()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "WarehouseTransfer",
            sourceLocationId = seed.MainLocationId,
            destinationLocationId = seed.OnlineLocationId,
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 4, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        var confirm = await client.PostAsync($"/api/v1/operations/{operation.Id}/confirm", null);
        var afterReserve = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");
        var ship = await client.PostAsync($"/api/v1/operations/{operation.Id}/ship", null);
        var afterShipMain = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");
        var afterShipOnline = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.OnlineLocationId}");
        var receive = await client.PostAsync($"/api/v1/operations/{operation.Id}/receive", null);
        var main = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");
        var online = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.OnlineLocationId}");

        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Contains(afterReserve!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 6 && balance.ReservedInWarehousePacks == 4);
        Assert.Equal(HttpStatusCode.NoContent, ship.StatusCode);
        Assert.Contains(afterShipMain!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 6 && balance.ReservedInWarehousePacks == 0);
        Assert.DoesNotContain(afterShipOnline!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks > 0);
        Assert.Equal(HttpStatusCode.NoContent, receive.StatusCode);
        Assert.Contains(main!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 6 && balance.ReservedInWarehousePacks == 0);
        Assert.Contains(online!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 4);
    }

    [Fact]
    public async Task WarehouseTransfer_UsesShortDatedUnexpiredBatchesByFefo()
    {
        var seed = await _factory.SeedAsync();
        await _factory.ReceiveMainStockAsync(seed.MainLocationId, seed.SkuId, "SHORT", new DateOnly(2026, 9, 1), 4);
        await _factory.ReceiveMainStockAsync(seed.MainLocationId, seed.SkuId, "VALID", new DateOnly(2028, 6, 1), 5);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "WarehouseTransfer",
            sourceLocationId = seed.MainLocationId,
            destinationLocationId = seed.OnlineLocationId,
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 3, lotNumber = "SHORT", expiryDate = "2026-09-01" } }
        });

        await client.PostAsync($"/api/v1/operations/{operation.Id}/confirm", null);
        await client.PostAsync($"/api/v1/operations/{operation.Id}/receive", null);
        var batches = await client.GetFromJsonAsync<PagedContract<BatchContract>>($"/api/v1/inventory/batches?locationId={seed.MainLocationId}&includeEmpty=true");

        Assert.Contains(batches!.Items, batch => batch.LotNumber == "SHORT" && batch.PackQuantity == 1);
        Assert.Contains(batches.Items, batch => batch.LotNumber == "VALID" && batch.PackQuantity == 5);
    }

    [Fact]
    public async Task WarehouseTransfer_RejectsWhenOnlyExpiredBatchesExist()
    {
        var seed = await _factory.SeedAsync();
        await _factory.ReceiveMainStockAsync(seed.MainLocationId, seed.SkuId, "EXPIRED", new DateOnly(2026, 1, 1), 4);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "WarehouseTransfer",
            sourceLocationId = seed.MainLocationId,
            destinationLocationId = seed.OnlineLocationId,
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 3, lotNumber = "EXPIRED", expiryDate = "2026-01-01" } }
        });

        var confirm = await client.PostAsync($"/api/v1/operations/{operation.Id}/confirm", null);

        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
    }

    [Fact]
    public async Task CancelReservedTransfer_ReleasesMainWarehouseReservation()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "WarehouseTransfer",
            sourceLocationId = seed.MainLocationId,
            destinationLocationId = seed.OnlineLocationId,
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 3, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        await client.PostAsync($"/api/v1/operations/{operation.Id}/confirm", null);
        var cancel = await client.PostAsync($"/api/v1/operations/{operation.Id}/cancel", null);
        var balances = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");

        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        Assert.Contains(balances!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 10 && balance.ReservedInWarehousePacks == 0);
    }

    [Fact]
    public async Task ReplenishmentReserve_CreatesReservedTransferForTargetShortage()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        await _factory.SetTargetBalanceAsync(seed.OnlineLocationId, seed.SkuId, available: 2, target: 7);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var reserve = await client.PostAsJsonAsync("/api/v1/operations/replenishment/reserve", new { });
        var response = await reserve.Content.ReadFromJsonAsync<ReplenishmentReserveContract>();
        var operations = await client.GetFromJsonAsync<PagedContract<OperationListContract>>("/api/v1/operations?pageSize=10");
        var main = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");
        var online = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.OnlineLocationId}");

        Assert.Equal(HttpStatusCode.OK, reserve.StatusCode);
        Assert.Equal(1, response!.CreatedOperations);
        Assert.Equal(0, response.UnfilledPacks);
        Assert.Contains(operations!.Items, operation => operation.OperationType == "WarehouseTransfer" && operation.Status == "Reserved");
        Assert.Contains(main!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 5 && balance.ReservedInWarehousePacks == 5);
        Assert.Contains(online!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 2);
    }

    [Fact]
    public async Task ReplenishmentRows_CountReservedIncomingAgainstTarget()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        await _factory.SetTargetBalanceAsync(seed.OnlineLocationId, seed.SkuId, available: 2, target: 7);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var before = await client.GetFromJsonAsync<IReadOnlyList<ReplenishmentRowContract>>("/api/v1/operations/replenishment");
        await client.PostAsJsonAsync("/api/v1/operations/replenishment/reserve", new { });
        var after = await client.GetFromJsonAsync<IReadOnlyList<ReplenishmentRowContract>>("/api/v1/operations/replenishment");

        Assert.Contains(before!, row => row.DestinationLocationId == seed.OnlineLocationId && row.ShortagePacks == 5 && row.IncomingPacks == 0);
        Assert.Contains(after!, row => row.DestinationLocationId == seed.OnlineLocationId && row.ShortagePacks == 0 && row.IncomingPacks == 5);
    }

    [Fact]
    public async Task ReplenishmentReserve_UsesShortDatedUnexpiredStock()
    {
        var seed = await _factory.SeedAsync();
        await _factory.ReceiveMainStockAsync(seed.MainLocationId, seed.SkuId, "SHORT", new DateOnly(2026, 9, 1), 5);
        await _factory.SetTargetBalanceAsync(seed.OnlineLocationId, seed.SkuId, available: 0, target: 4);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var reserve = await client.PostAsJsonAsync("/api/v1/operations/replenishment/reserve", new { });
        var response = await reserve.Content.ReadFromJsonAsync<ReplenishmentReserveContract>();
        var operations = await client.GetFromJsonAsync<PagedContract<OperationListContract>>("/api/v1/operations?pageSize=10");

        Assert.Equal(HttpStatusCode.OK, reserve.StatusCode);
        Assert.Equal(1, response!.CreatedOperations);
        Assert.Equal(0, response.UnfilledPacks);
        Assert.DoesNotContain(operations!.Items, operation => operation.Status == "Cancelled");
    }

    [Fact]
    public async Task DailyReplenishment_DoesNotDropMainBelowTarget_AndCreatesLowMainStockAlert()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        await _factory.SetTargetBalanceAsync(seed.MainLocationId, seed.SkuId, available: 10, target: 8);
        await _factory.SetTargetBalanceAsync(seed.OnlineLocationId, seed.SkuId, available: 0, target: 5);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var reserve = await client.PostAsJsonAsync("/api/v1/operations/replenishment/daily-reset", new { });
        var response = await reserve.Content.ReadFromJsonAsync<ReplenishmentReserveContract>();
        var main = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");
        var alerts = await _factory.GetNotificationCountAsync("TargetReplenishmentLowMainStock");

        Assert.Equal(HttpStatusCode.OK, reserve.StatusCode);
        Assert.Equal(1, response!.CreatedOperations);
        Assert.Equal(3, response.UnfilledPacks);
        Assert.Contains(main!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 8 && balance.ReservedInWarehousePacks == 2);
        Assert.Equal(2, alerts);
    }

    [Fact]
    public async Task InventoryTransferBlockedBatches_DoesNotShowShortDatedUnexpiredStock()
    {
        var seed = await _factory.SeedAsync();
        await _factory.ReceiveMainStockAsync(seed.MainLocationId, seed.SkuId, "SHORT", new DateOnly(2026, 9, 1), 5);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.InventoryRead);

        var rows = await client.GetFromJsonAsync<IReadOnlyList<TransferBlockedBatchContract>>("/api/v1/inventory/transfer-blocked-batches");

        Assert.DoesNotContain(rows!, row => row.SkuId == seed.SkuId && row.LotNumber == "SHORT");
    }

    [Fact]
    public async Task InventoryTransferBlockedBatches_ShowsAlreadyExpiredStock()
    {
        var seed = await _factory.SeedAsync();
        await _factory.ReceiveMainStockAsync(seed.MainLocationId, seed.SkuId, "EXPIRED", new DateOnly(2026, 1, 1), 2);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.InventoryRead);

        var rows = await client.GetFromJsonAsync<IReadOnlyList<TransferBlockedBatchContract>>("/api/v1/inventory/transfer-blocked-batches");

        Assert.Contains(rows!, row =>
            row.SkuId == seed.SkuId &&
            row.LotNumber == "EXPIRED" &&
            row.PackQuantity == 2 &&
            row.Reason == "Expired");
    }

    [Fact]
    public async Task WholesaleSale_RequiresMerchantAndConsumesMainPacks()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "CashHandToHand",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 3, entryMode = "Packs", unitPrice = 125, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        var confirm = await client.PostAsync($"/api/v1/operations/{operation.Id}/confirm", null);
        var afterReserve = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");
        var ship = await client.PostAsync($"/api/v1/operations/{operation.Id}/ship", null);
        var receive = await client.PostAsync($"/api/v1/operations/{operation.Id}/receive", null);
        var main = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");
        var merchant = await client.GetFromJsonAsync<MerchantDetailContract>($"/api/v1/crm/merchants/{merchantId}");

        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Contains(afterReserve!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 7 && balance.ReservedInWarehousePacks == 3);
        Assert.Equal(HttpStatusCode.NoContent, ship.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, receive.StatusCode);
        Assert.Contains(main!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 7);
        Assert.Contains(merchant!.RecentOperations, item => item.Id == operation.Id && item.OperationType == "WholesaleSale" && item.Quantity == 3 && item.Total == 375);
    }

    [Fact]
    public async Task WholesaleSale_UsesSelectedBatchExpiryInsteadOfFefo()
    {
        var seed = await _factory.SeedAsync();
        await _factory.ReceiveMainStockAsync(seed.MainLocationId, seed.SkuId, "EARLY", new DateOnly(2027, 1, 1), 4);
        await _factory.ReceiveMainStockAsync(seed.MainLocationId, seed.SkuId, "LATE", new DateOnly(2028, 6, 1), 5);
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "CashHandToHand",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 3, entryMode = "Packs", unitPrice = 100, lotNumber = "LATE", expiryDate = "2028-06-01" } }
        });

        await client.PostAsync($"/api/v1/operations/{operation.Id}/confirm", null);
        await client.PostAsync($"/api/v1/operations/{operation.Id}/ship", null);
        await client.PostAsync($"/api/v1/operations/{operation.Id}/complete", null);
        var batches = await client.GetFromJsonAsync<PagedContract<BatchContract>>($"/api/v1/inventory/batches?locationId={seed.MainLocationId}&includeEmpty=true");

        Assert.Contains(batches!.Items, batch => batch.LotNumber == "EARLY" && batch.PackQuantity == 4);
        Assert.Contains(batches.Items, batch => batch.LotNumber == "LATE" && batch.PackQuantity == 2);
    }

    [Fact]
    public async Task CompletedCashSale_CreatesCashRecordAndZeroMerchantBalance()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead, LenseePermissions.PaymentsRead);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "CashHandToHand",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        await client.PostAsync($"/api/v1/operations/{operation.Id}/confirm", null);
        await client.PostAsync($"/api/v1/operations/{operation.Id}/ship", null);
        var complete = await client.PostAsync($"/api/v1/operations/{operation.Id}/complete", null);
        var logs = await client.GetFromJsonAsync<PagedContract<PaymentLogContract>>("/api/v1/payments?pageSize=10");

        Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        var paymentLog = Assert.Single(logs!.Items, log => log.OperationId == operation.Id);
        Assert.Equal("CashHandToHand", paymentLog.PaymentMethod);
        Assert.Equal("PendingAccountant", paymentLog.Status);
        Assert.Equal(0m, paymentLog.AmountPaid);

        var beforeApproval = await client.GetFromJsonAsync<MerchantBalanceContract>($"/api/v1/payments/merchants/{merchantId}/balance");
        Assert.Equal(200m, beforeApproval!.Balance);

        using var accountant = _factory.CreateClient();
        accountant.AuthorizeAs(LenseeRoles.Accountant, LenseePermissions.PaymentsRead, LenseePermissions.PaymentsApprove);
        var approval = await accountant.PostAsync($"/api/v1/payments/cash-receipts/{paymentLog.Id}/approve", null);
        var afterApproval = await client.GetFromJsonAsync<MerchantBalanceContract>($"/api/v1/payments/merchants/{merchantId}/balance");

        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        Assert.Equal(200m, afterApproval!.PaymentsReceived);
        Assert.Equal(0m, afterApproval.Balance);
    }

    [Fact]
    public async Task AnonymousCompletedCashSale_CreatesOtherMerchantIdentityAndPaymentLog()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        await _factory.ReceiveMainStockAsync(seed.OnlineLocationId, seed.SkuId, "MAIN-A", new DateOnly(2028, 6, 1), 2);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(
            LenseeRoles.Admin,
            LenseePermissions.OperationsRead,
            LenseePermissions.OperationsWrite,
            LenseePermissions.InventoryRead,
            LenseePermissions.PaymentsRead);

        const string buyerName = "Walk In Cash Buyer";
        var operation = await CreateOperationAsync(client, new
        {
            operationType = "RetailSale",
            sourceLocationId = seed.OnlineLocationId,
            buyerName,
            paymentMethod = "CashHandToHand",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, entryMode = "Packs", unitPrice = 125, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        await client.PostAsync($"/api/v1/operations/{operation.Id}/confirm", null);
        await client.PostAsync($"/api/v1/operations/{operation.Id}/ship", null);
        var complete = await client.PostAsync($"/api/v1/operations/{operation.Id}/complete", null);
        var detail = await client.GetFromJsonAsync<OperationDetailContract>($"/api/v1/operations/{operation.Id}");
        var logs = await client.GetFromJsonAsync<PagedContract<PaymentLogContract>>("/api/v1/payments?pageSize=20");
        var merchants = await client.GetFromJsonAsync<PagedContract<MerchantListContract>>($"/api/v1/crm/merchants?includeInactive=true&pageSize=20&search={Uri.EscapeDataString(buyerName)}");

        Assert.Equal(HttpStatusCode.NoContent, complete.StatusCode);
        Assert.NotNull(detail);
        Assert.NotNull(detail!.ClientId);
        Assert.Equal(buyerName, detail.ClientName);

        var paymentLog = Assert.Single(logs!.Items, log => log.OperationId == operation.Id);
        Assert.Equal(detail.ClientId!.Value, paymentLog.MerchantId);
        Assert.Equal("CashHandToHand", paymentLog.PaymentMethod);

        var merchant = Assert.Single(merchants!.Items, item => item.Id == detail.ClientId.Value);
        Assert.Equal(buyerName, merchant.BusinessName);
        Assert.Equal("Other", merchant.BusinessType);
    }

    [Fact]
    public async Task CashRefundForWrongCashSale_ReturnsMerchantBalanceToZero()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead, LenseePermissions.PaymentsRead, LenseePermissions.PaymentsWrite, LenseePermissions.PaymentsApprove);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "CashHandToHand",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        await client.PostAsync($"/api/v1/operations/{operation.Id}/confirm", null);
        await client.PostAsync($"/api/v1/operations/{operation.Id}/ship", null);
        await client.PostAsync($"/api/v1/operations/{operation.Id}/complete", null);
        var logs = await client.GetFromJsonAsync<PagedContract<PaymentLogContract>>("/api/v1/payments?pageSize=10");
        var paymentLog = Assert.Single(logs!.Items, log => log.OperationId == operation.Id);
        var approval = await client.PostAsync($"/api/v1/payments/cash-receipts/{paymentLog.Id}/approve", null);
        var refund = await client.PostAsJsonAsync("/api/v1/payments/cash-records", new
        {
            operationId = operation.Id.ToString(),
            paymentType = "CashRefund",
            amount = 200m,
            notes = "Wrong cash sale correction"
        });
        var balance = await client.GetFromJsonAsync<MerchantBalanceContract>($"/api/v1/payments/merchants/{merchantId}/balance");

        Assert.Equal(HttpStatusCode.Created, refund.StatusCode);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        Assert.Equal(200m, balance!.SaleTotal);
        Assert.Equal(200m, balance.PaymentsReceived);
        Assert.Equal(200m, balance.CashRefunded);
        Assert.Equal(0m, balance.Balance);
    }

    [Fact]
    public async Task FinancialAdjustment_AcceptsOperationNumberReference()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead, LenseePermissions.PaymentsRead, LenseePermissions.PaymentsWrite);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "Installment",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });
        var detail = await client.GetFromJsonAsync<OperationDetailContract>($"/api/v1/operations/{operation.Id}");

        var adjustment = await client.PostAsJsonAsync("/api/v1/payments/adjustments", new
        {
            merchantId,
            operationId = detail!.OperationNumber,
            adjustmentType = "MerchantCredit",
            amount = 100m,
            notes = "Operation code reference"
        });

        Assert.Equal(HttpStatusCode.Created, adjustment.StatusCode);
    }

    [Fact]
    public async Task InstallmentSale_RequiresAdminApprovalBeforeBalanceIsReduced()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var admin = _factory.CreateClient();
        admin.AuthorizeAs(
            LenseeRoles.Admin,
            LenseePermissions.OperationsRead,
            LenseePermissions.OperationsWrite,
            LenseePermissions.InventoryRead,
            LenseePermissions.PaymentsRead,
            LenseePermissions.PaymentsWrite);

        var operation = await CreateOperationAsync(admin, new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "Installment",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        await admin.PostAsync($"/api/v1/operations/{operation.Id}/confirm", null);
        await admin.PostAsync($"/api/v1/operations/{operation.Id}/ship", null);
        await admin.PostAsync($"/api/v1/operations/{operation.Id}/complete", null);

        var logs = await admin.GetFromJsonAsync<PagedContract<PaymentLogContract>>("/api/v1/payments?pageSize=10");
        var log = Assert.Single(logs!.Items, item => item.OperationId == operation.Id);
        using var accountant = _factory.CreateClient();
        accountant.AuthorizeAs(LenseeRoles.Accountant, LenseePermissions.PaymentsRead, LenseePermissions.PaymentsDraft);

        await admin.PostAsJsonAsync($"/api/v1/payments/{log.Id}/assign", new { accountantUserId = (Guid?)null });
        var draft = await accountant.PostAsJsonAsync($"/api/v1/payments/{log.Id}/sub-logs", new
        {
            amount = 120m,
            paymentMethod = "CashTransaction",
            dateReceived = "2026-07-02",
            notes = "First installment"
        });
        var draftedDetail = await draft.Content.ReadFromJsonAsync<PaymentLogDetailContract>();
        var draftedSubLog = Assert.Single(draftedDetail!.SubLogs);
        var beforeApproval = await admin.GetFromJsonAsync<MerchantBalanceContract>($"/api/v1/payments/merchants/{merchantId}/balance");

        var approve = await admin.PostAsync($"/api/v1/payments/sub-logs/{draftedSubLog.Id}/approve", null);
        var afterApproval = await admin.GetFromJsonAsync<MerchantBalanceContract>($"/api/v1/payments/merchants/{merchantId}/balance");

        Assert.Equal(200m, log.TotalAmount);
        Assert.Equal("PendingAdmin", log.Status);
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        Assert.Equal(200m, beforeApproval!.Balance);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.Equal(120m, afterApproval!.PaymentsReceived);
        Assert.Equal(80m, afterApproval.Balance);
    }

    [Fact]
    public async Task PaymentSubLog_RejectsUnknownPaymentMethod()
    {
        await _factory.SeedAsync();
        var logId = await _factory.CreatePaymentLogAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Accountant, LenseePermissions.PaymentsRead, LenseePermissions.PaymentsDraft);

        var response = await client.PostAsJsonAsync($"/api/v1/payments/{logId}/sub-logs", new
        {
            amount = 10m,
            paymentMethod = "Crypto"
        });
        var subLogCount = await _factory.CountPaymentSubLogsAsync(logId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, subLogCount);
    }

    [Fact]
    public async Task PaymentSubLog_RejectsZeroAmount()
    {
        await _factory.SeedAsync();
        var logId = await _factory.CreatePaymentLogAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Accountant, LenseePermissions.PaymentsRead, LenseePermissions.PaymentsDraft);

        var response = await client.PostAsJsonAsync($"/api/v1/payments/{logId}/sub-logs", new
        {
            amount = 0m,
            paymentMethod = "Installment"
        });
        var subLogCount = await _factory.CountPaymentSubLogsAsync(logId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, subLogCount);
    }

    [Fact]
    public async Task PaymentInitialize_RejectsUnregisteredMerchantAndUnknownMethod()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.PaymentsRead, LenseePermissions.PaymentsWrite);

        var anonymousSale = await CreateOperationAsync(client, new
        {
            operationType = "RetailSale",
            sourceLocationId = seed.OnlineLocationId,
            buyerName = "Walk In",
            paymentMethod = "CashHandToHand",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });
        var registeredSale = await CreateOperationAsync(client, new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "Installment",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        var unregistered = await client.PostAsJsonAsync("/api/v1/payments/initialize", new { operationId = anonymousSale.Id, paymentMethod = "CashHandToHand" });
        var unknownMethod = await client.PostAsJsonAsync("/api/v1/payments/initialize", new { operationId = registeredSale.Id, paymentMethod = "Crypto" });

        Assert.Equal(HttpStatusCode.BadRequest, unregistered.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownMethod.StatusCode);
    }

    [Fact]
    public async Task CashRecord_RejectsInvalidPayloads()
    {
        await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.PaymentsRead, LenseePermissions.PaymentsWrite);

        var zeroAmount = await client.PostAsJsonAsync("/api/v1/payments/cash-records", new { operationId = Guid.NewGuid().ToString(), paymentType = "CashReceived", amount = 0m });
        var badType = await client.PostAsJsonAsync("/api/v1/payments/cash-records", new { operationId = Guid.NewGuid().ToString(), paymentType = "Crypto", amount = 1m });
        var unknownOperation = await client.PostAsJsonAsync("/api/v1/payments/cash-records", new { operationId = Guid.NewGuid().ToString(), paymentType = "CashReceived", amount = 1m });

        Assert.Equal(HttpStatusCode.BadRequest, zeroAmount.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownOperation.StatusCode);
    }

    [Fact]
    public async Task FinancialAdjustment_RejectsInvalidPayloads()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        var otherMerchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.PaymentsRead, LenseePermissions.PaymentsWrite);
        var operation = await CreateOperationAsync(client, new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "Installment",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        var zeroAmount = await client.PostAsJsonAsync("/api/v1/payments/adjustments", new { merchantId, adjustmentType = "MerchantCredit", amount = 0m });
        var badType = await client.PostAsJsonAsync("/api/v1/payments/adjustments", new { merchantId, adjustmentType = "Crypto", amount = 1m });
        var missingMerchant = await client.PostAsJsonAsync("/api/v1/payments/adjustments", new { merchantId = Guid.NewGuid(), adjustmentType = "MerchantCredit", amount = 1m });
        var wrongMerchantOperation = await client.PostAsJsonAsync("/api/v1/payments/adjustments", new { merchantId = otherMerchantId, operationId = operation.Id.ToString(), adjustmentType = "MerchantCredit", amount = 1m });
        var refundWithoutOperation = await client.PostAsJsonAsync("/api/v1/payments/adjustments", new { merchantId, adjustmentType = "CashRefund", amount = 1m });

        Assert.Equal(HttpStatusCode.BadRequest, zeroAmount.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingMerchant.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongMerchantOperation.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, refundWithoutOperation.StatusCode);
    }

    [Fact]
    public async Task SaleDraft_RequiresPositivePriceUnlessLineIsBonus()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var missingPrice = await client.PostAsJsonAsync("/api/v1/operations", new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "CashHandToHand",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", unitPrice = 0, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });
        var bonus = await client.PostAsJsonAsync("/api/v1/operations", new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "CashHandToHand",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", unitPrice = 0, isBonus = true, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, missingPrice.StatusCode);
        Assert.Equal(HttpStatusCode.Created, bonus.StatusCode);
    }

    [Fact]
    public async Task SaleDraft_AllowsSameSkuPaidAndBonusLines()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "CashHandToHand",
            lines = new[]
            {
                new { skuId = seed.SkuId, packQuantity = 2, entryMode = "Packs", unitPrice = 125, isBonus = false, lotNumber = "MAIN-A", expiryDate = "2028-06-01" },
                new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", unitPrice = 0, isBonus = true, lotNumber = "MAIN-A", expiryDate = "2028-06-01" }
            }
        });

        var confirm = await client.PostAsync($"/api/v1/operations/{operation.Id}/confirm", null);
        var ship = await client.PostAsync($"/api/v1/operations/{operation.Id}/ship", null);
        var receive = await client.PostAsync($"/api/v1/operations/{operation.Id}/receive", null);
        var main = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");

        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, ship.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, receive.StatusCode);
        Assert.Contains(main!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 7);
    }

    [Fact]
    public async Task SaleDraft_RejectsUnknownPaymentMethod()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var response = await client.PostAsJsonAsync("/api/v1/operations", new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "Crypto",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DraftOperation_RejectsEmptyDuplicateAndNonSaleBonusLines()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var empty = await client.PostAsJsonAsync("/api/v1/operations", new
        {
            operationType = "InventoryReceipt",
            destinationLocationId = seed.MainLocationId,
            lines = Array.Empty<object>()
        });
        var duplicate = await client.PostAsJsonAsync("/api/v1/operations", new
        {
            operationType = "WarehouseTransfer",
            sourceLocationId = seed.MainLocationId,
            destinationLocationId = seed.OnlineLocationId,
            lines = new[]
            {
                new { skuId = seed.SkuId, packQuantity = 1, lotNumber = "MAIN-A", expiryDate = "2028-06-01" },
                new { skuId = seed.SkuId, packQuantity = 1, lotNumber = "MAIN-A", expiryDate = "2028-06-01" }
            }
        });
        var nonSaleBonus = await client.PostAsJsonAsync("/api/v1/operations", new
        {
            operationType = "WarehouseTransfer",
            sourceLocationId = seed.MainLocationId,
            destinationLocationId = seed.OnlineLocationId,
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, isBonus = true, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, nonSaleBonus.StatusCode);
    }

    [Fact]
    public async Task DraftOperation_RejectsMissingRoleSpecificReferences()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var reserve = await client.PostAsJsonAsync("/api/v1/operations", new
        {
            operationType = "Reserve",
            sourceLocationId = seed.MainLocationId,
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });
        var retailInstallment = await client.PostAsJsonAsync("/api/v1/operations", new
        {
            operationType = "RetailSale",
            sourceLocationId = seed.OnlineLocationId,
            paymentMethod = "Installment",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, reserve.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, retailInstallment.StatusCode);
    }

    [Fact]
    public async Task ReserveWithRepresentative_ReservesAndCancelReleasesStock()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var representativeId = await _factory.CreateRepresentativeAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "Reserve",
            sourceLocationId = seed.MainLocationId,
            representativeId,
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, entryMode = "Packs", lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        var confirm = await client.PostAsync($"/api/v1/operations/{operation.Id}/confirm", null);
        var afterReserve = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");
        var ship = await client.PostAsync($"/api/v1/operations/{operation.Id}/ship", null);
        var afterShip = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");
        var receive = await client.PostAsync($"/api/v1/operations/{operation.Id}/receive", null);
        var detail = await client.GetFromJsonAsync<OperationDetailContract>($"/api/v1/operations/{operation.Id}");
        var cancel = await client.PostAsync($"/api/v1/operations/{operation.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Contains(afterReserve!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 8 && balance.ReservedInWarehousePacks == 2);
        Assert.Equal(HttpStatusCode.NoContent, ship.StatusCode);
        Assert.Contains(afterShip!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 8 && balance.ReservedInWarehousePacks == 0 && balance.ReservedWithRepPacks == 2);
        Assert.Equal(HttpStatusCode.NoContent, receive.StatusCode);
        Assert.Equal("Confirmed", detail!.Status);
        Assert.Equal(HttpStatusCode.BadRequest, cancel.StatusCode);
    }

    [Fact]
    public async Task Return_ReceivesMerchantStockAndReducesEligibility()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var sale = await CreateOperationAsync(client, new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "CashHandToHand",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 4, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });
        await client.PostAsync($"/api/v1/operations/{sale.Id}/confirm", null);
        await client.PostAsync($"/api/v1/operations/{sale.Id}/ship", null);
        await client.PostAsync($"/api/v1/operations/{sale.Id}/receive", null);

        var returnOperation = await CreateOperationAsync(client, new
        {
            operationType = "Return",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "CashHandToHand",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        var confirm = await client.PostAsync($"/api/v1/operations/{returnOperation.Id}/confirm", null);
        var main = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");
        var eligibility = await client.GetFromJsonAsync<IReadOnlyList<MerchantEligibilityContract>>($"/api/v1/crm/merchants/{merchantId}/eligibility");

        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Contains(main!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 8);
        Assert.Contains(eligibility!, row => row.SkuId == seed.SkuId && row.LotNumber == "MAIN-A" && row.ExpiryDate == new DateOnly(2028, 6, 1) && row.SoldQty == 4 && row.ReturnedQty == 2 && row.ReturnableQty == 2);
    }

    [Fact]
    public async Task Return_CanConfirmNonEligibleStockAndShowsWarning()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var returnOperation = await CreateOperationAsync(client, new
        {
            operationType = "Return",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "CashHandToHand",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, entryMode = "Packs", unitPrice = 100, lotNumber = "UNKNOWN", expiryDate = "2028-06-01" } }
        });

        var beforeConfirm = await client.GetFromJsonAsync<OperationDetailContract>($"/api/v1/operations/{returnOperation.Id}");
        var warningGate = await client.PostAsync($"/api/v1/operations/{returnOperation.Id}/confirm", null);
        var warningBody = await warningGate.Content.ReadFromJsonAsync<OperationWarningGateContract>();
        var confirm = await client.PostAsJsonAsync($"/api/v1/operations/{returnOperation.Id}/confirm", new { overrideEligibilityWarnings = true });
        var afterConfirm = await client.GetFromJsonAsync<OperationDetailContract>($"/api/v1/operations/{returnOperation.Id}");
        var eligibility = await client.GetFromJsonAsync<IReadOnlyList<MerchantEligibilityContract>>($"/api/v1/crm/merchants/{merchantId}/eligibility");

        Assert.Equal(HttpStatusCode.Conflict, warningGate.StatusCode);
        Assert.Contains(warningBody!.Warnings, warning => warning.WarningType == "ReturnEligibility" && warning.RequestedQuantity == 2 && warning.EligibleQuantity == 0);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Contains(beforeConfirm!.Warnings, warning => warning.WarningType == "ReturnEligibility" && warning.RequestedQuantity == 2 && warning.EligibleQuantity == 0);
        Assert.Contains(afterConfirm!.Warnings, warning => warning.WarningType == "ReturnEligibility" && warning.RequestedQuantity == 2 && warning.EligibleQuantity == 0);
        Assert.Contains(eligibility!, row => row.SkuId == seed.SkuId && row.LotNumber == "UNKNOWN" && row.ExpiryDate == new DateOnly(2028, 6, 1) && row.SoldQty == 0 && row.ReturnedQty == 2 && row.OverReturnedQty == 2);
    }

    [Fact]
    public async Task Change_ReceivesReturnedSideAndIssuesReplacementSide()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var sale = await CreateOperationAsync(client, new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "CashHandToHand",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 3, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });
        await client.PostAsync($"/api/v1/operations/{sale.Id}/confirm", null);
        await client.PostAsync($"/api/v1/operations/{sale.Id}/ship", null);
        await client.PostAsync($"/api/v1/operations/{sale.Id}/receive", null);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "Change",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "CashHandToHand",
            lines = new[]
            {
                new { skuId = seed.SkuId, section = "ChangeOut", packQuantity = 1, entryMode = "Packs", unitPrice = 100, lotNumber = (string?)"MAIN-A", expiryDate = (string?)"2028-06-01" },
                new { skuId = seed.SkuId, section = "ChangeIn", packQuantity = 2, entryMode = "Packs", unitPrice = 100, lotNumber = (string?)null, expiryDate = (string?)null }
            }
        });

        var confirm = await client.PostAsync($"/api/v1/operations/{operation.Id}/confirm", null);
        var main = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");

        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Contains(main!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 6);
    }

    [Fact]
    public async Task WriteOff_IsAdminOnlyAndConsumesStock()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        using var clerk = _factory.CreateClient();
        clerk.AuthorizeAsAtLocation(LenseeRoles.WarehouseClerk, seed.MainLocationId, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite);
        using var admin = _factory.CreateClient();
        admin.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var clerkResponse = await clerk.PostAsJsonAsync("/api/v1/operations", new
        {
            operationType = "WriteOff",
            sourceLocationId = seed.MainLocationId,
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });
        var operation = await CreateOperationAsync(admin, new
        {
            operationType = "WriteOff",
            sourceLocationId = seed.MainLocationId,
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, entryMode = "Packs", lotNumber = "MAIN-A", expiryDate = "2028-06-01", notes = "Damaged" } }
        });
        var confirm = await admin.PostAsync($"/api/v1/operations/{operation.Id}/confirm", null);
        var main = await admin.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");

        Assert.Equal(HttpStatusCode.BadRequest, clerkResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Contains(main!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 8);
    }

    [Fact]
    public async Task CLevelAndAccountant_CannotMutateOperations()
    {
        var seed = await _factory.SeedAsync();
        using var cLevel = _factory.CreateClient();
        cLevel.AuthorizeAs(LenseeRoles.CLevel, LenseePermissions.OperationsRead);
        using var accountant = _factory.CreateClient();
        accountant.AuthorizeAs(LenseeRoles.Accountant, LenseePermissions.OperationsRead);

        var cLevelResponse = await cLevel.PostAsJsonAsync("/api/v1/operations", new { operationType = "InventoryReceipt", destinationLocationId = seed.MainLocationId, lines = new[] { new { skuId = seed.SkuId, packQuantity = 1 } } });
        var accountantResponse = await accountant.PostAsJsonAsync("/api/v1/operations", new { operationType = "InventoryReceipt", destinationLocationId = seed.MainLocationId, lines = new[] { new { skuId = seed.SkuId, packQuantity = 1 } } });

        Assert.Equal(HttpStatusCode.Forbidden, cLevelResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, accountantResponse.StatusCode);
    }

    [Fact]
    public async Task DraftOperation_CanBeUpdatedRepeatedlyWithoutConcurrencyFailure()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "InventoryReceipt",
            destinationLocationId = seed.MainLocationId,
            receipt = new { supplierName = "Supplier", invoiceNumber = "INV-1" },
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, lotNumber = "MAIN-1", expiryDate = "2028-06-01" } }
        });

        var firstUpdate = await client.PutAsJsonAsync($"/api/v1/operations/{operation.Id}", new
        {
            operationType = "InventoryReceipt",
            destinationLocationId = seed.MainLocationId,
            receipt = new { supplierName = "Supplier", invoiceNumber = "INV-2" },
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 3, lotNumber = "MAIN-2", expiryDate = "2028-07-01" } }
        });
        var secondUpdate = await client.PutAsJsonAsync($"/api/v1/operations/{operation.Id}", new
        {
            operationType = "InventoryReceipt",
            destinationLocationId = seed.MainLocationId,
            receipt = new { supplierName = "Supplier", invoiceNumber = "INV-3" },
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 4, lotNumber = "MAIN-3", expiryDate = "2028-08-01" } }
        });
        var detail = await client.GetFromJsonAsync<OperationDetailContract>($"/api/v1/operations/{operation.Id}");

        Assert.True(firstUpdate.StatusCode == HttpStatusCode.NoContent, await firstUpdate.Content.ReadAsStringAsync());
        Assert.True(secondUpdate.StatusCode == HttpStatusCode.NoContent, await secondUpdate.Content.ReadAsStringAsync());
        Assert.Contains(detail!.Versions!, version => version.Reason == "Draft update");
        Assert.True(detail.Versions!.Count >= 3);
    }

    [Fact]
    public async Task ReviseOperation_RequiresReason()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "InventoryReceipt",
            destinationLocationId = seed.MainLocationId,
            receipt = new { supplierName = "Supplier", invoiceNumber = "INV-1" },
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, lotNumber = "MAIN-1", expiryDate = "2028-06-01" } }
        });

        var revise = await client.PostAsJsonAsync($"/api/v1/operations/{operation.Id}/revise", new
        {
            operation = new
            {
                operationType = "InventoryReceipt",
                destinationLocationId = seed.MainLocationId,
                receipt = new { supplierName = "Supplier", invoiceNumber = "INV-2" },
                lines = new[] { new { skuId = seed.SkuId, packQuantity = 3, lotNumber = "MAIN-1", expiryDate = "2028-06-01" } }
            },
            reason = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, revise.StatusCode);
    }

    [Fact]
    public async Task StocktakeLines_RejectUnknownSku()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.InventoryRead, LenseePermissions.InventoryWrite);

        var create = await client.PostAsJsonAsync("/api/v1/stocktakes", new { locationId = seed.MainLocationId });
        var stocktake = await create.Content.ReadFromJsonAsync<StocktakeDetailContract>();
        var response = await client.PutAsJsonAsync($"/api/v1/stocktakes/{stocktake!.Id}/lines", new
        {
            lines = new[] { new { skuId = Guid.NewGuid(), physicalCount = 1 } }
        });
        var detail = await client.GetFromJsonAsync<StocktakeDetailContract>($"/api/v1/stocktakes/{stocktake.Id}");

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(detail!.Lines);
    }

    [Fact]
    public async Task StocktakeLines_RejectSkuWhenProductIsInactive()
    {
        var seed = await _factory.SeedAsync();
        await _factory.DeactivateProductForSkuAsync(seed.SkuId);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.InventoryRead, LenseePermissions.InventoryWrite);

        var create = await client.PostAsJsonAsync("/api/v1/stocktakes", new { locationId = seed.MainLocationId });
        var stocktake = await create.Content.ReadFromJsonAsync<StocktakeDetailContract>();
        var response = await client.PutAsJsonAsync($"/api/v1/stocktakes/{stocktake!.Id}/lines", new
        {
            lines = new[] { new { skuId = seed.SkuId, physicalCount = 1 } }
        });
        var detail = await client.GetFromJsonAsync<StocktakeDetailContract>($"/api/v1/stocktakes/{stocktake.Id}");

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(detail!.Lines);
    }

    [Fact]
    public async Task StocktakeLines_RejectEmptyDuplicateAndBlankSkuLines()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.InventoryRead, LenseePermissions.InventoryWrite);

        var create = await client.PostAsJsonAsync("/api/v1/stocktakes", new { locationId = seed.MainLocationId });
        var stocktake = await create.Content.ReadFromJsonAsync<StocktakeDetailContract>();
        var empty = await client.PutAsJsonAsync($"/api/v1/stocktakes/{stocktake!.Id}/lines", new { lines = Array.Empty<object>() });
        var duplicate = await client.PutAsJsonAsync($"/api/v1/stocktakes/{stocktake.Id}/lines", new
        {
            lines = new[]
            {
                new { skuId = seed.SkuId, physicalCount = 1, lotNumber = "A", expiryDate = "2028-06-01" },
                new { skuId = seed.SkuId, physicalCount = 2, lotNumber = "A", expiryDate = "2028-06-01" }
            }
        });
        var blankSku = await client.PutAsJsonAsync($"/api/v1/stocktakes/{stocktake.Id}/lines", new
        {
            lines = new[] { new { skuId = Guid.Empty, physicalCount = 1 } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, blankSku.StatusCode);
    }

    [Fact]
    public async Task StocktakeLines_RejectNegativePhysicalCountWithoutClearingExistingLines()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.InventoryRead, LenseePermissions.InventoryWrite);

        var create = await client.PostAsJsonAsync("/api/v1/stocktakes", new { locationId = seed.MainLocationId });
        var stocktake = await create.Content.ReadFromJsonAsync<StocktakeDetailContract>();
        var valid = await client.PutAsJsonAsync($"/api/v1/stocktakes/{stocktake!.Id}/lines", new
        {
            lines = new[] { new { skuId = seed.SkuId, physicalCount = 1 } }
        });
        var invalid = await client.PutAsJsonAsync($"/api/v1/stocktakes/{stocktake.Id}/lines", new
        {
            lines = new[] { new { skuId = seed.SkuId, physicalCount = -1 } }
        });
        var detail = await client.GetFromJsonAsync<StocktakeDetailContract>($"/api/v1/stocktakes/{stocktake.Id}");

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var line = Assert.Single(detail!.Lines);
        Assert.Equal(seed.SkuId, line.SkuId);
        Assert.Equal(1, line.PhysicalCount);
    }

    [Fact]
    public async Task Stocktake_RejectsNonDraftEditAndConfirmWithoutLines()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.InventoryRead, LenseePermissions.InventoryWrite);

        var createEmpty = await client.PostAsJsonAsync("/api/v1/stocktakes", new { locationId = seed.MainLocationId });
        var emptyStocktake = await createEmpty.Content.ReadFromJsonAsync<StocktakeDetailContract>();
        var confirmEmpty = await client.PostAsync($"/api/v1/stocktakes/{emptyStocktake!.Id}/confirm", null);

        var create = await client.PostAsJsonAsync("/api/v1/stocktakes", new { locationId = seed.MainLocationId });
        var stocktake = await create.Content.ReadFromJsonAsync<StocktakeDetailContract>();
        await client.PutAsJsonAsync($"/api/v1/stocktakes/{stocktake!.Id}/lines", new
        {
            lines = new[] { new { skuId = seed.SkuId, physicalCount = 1 } }
        });
        var confirm = await client.PostAsync($"/api/v1/stocktakes/{stocktake.Id}/confirm", null);
        var editConfirmed = await client.PutAsJsonAsync($"/api/v1/stocktakes/{stocktake.Id}/lines", new
        {
            lines = new[] { new { skuId = seed.SkuId, physicalCount = 2 } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, confirmEmpty.StatusCode);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, editConfirmed.StatusCode);
    }

    [Fact]
    public async Task SupplyDraft_AllowsBlankUnitPriceButBlocksConfirmation()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.SupplyRead, LenseePermissions.SupplyWrite);

        var create = await client.PostAsJsonAsync("/api/v1/supply/shipments", new
        {
            supplierName = "Imported Supplier",
            destinationLocationId = seed.MainLocationId,
            lines = new[] { new { skuId = seed.SkuId, quantity = 5, unitPrice = (decimal?)null } },
            costs = new[] { new { costType = "Customs", description = "Port customs", amount = 20m } }
        });
        var createBody = await create.Content.ReadAsStringAsync();
        using var created = JsonDocument.Parse(createBody);
        var shipmentId = created.RootElement.GetProperty("id").GetGuid();

        var confirm = await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/confirm", null);
        var body = await confirm.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(JsonValueKind.Null, created.RootElement.GetProperty("lines")[0].GetProperty("unitPrice").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
        Assert.Contains("Every SKU line needs a unit price", body);
    }

    [Fact]
    public async Task SupplyCreate_RejectsMalformedAndOutOfRangeFields()
    {
        await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.SupplyRead, LenseePermissions.SupplyWrite);

        var response = await client.PostAsJsonAsync("/api/v1/supply/shipments", new
        {
            supplierName = (string?)null,
            invoiceNumber = new string('I', 101),
            destinationLocationId = Guid.Empty,
            notes = new string('N', 4001),
            lines = (object[]?)null,
            costs = (object[]?)null
        });
        var body = await response.Content.ReadFromJsonAsync<ValidationProblemContract>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SupplierName", body!.Errors.Keys);
        Assert.Contains("InvoiceNumber", body.Errors.Keys);
        Assert.Contains("DestinationLocationId", body.Errors.Keys);
        Assert.Contains("Notes", body.Errors.Keys);
        Assert.Contains("Lines", body.Errors.Keys);
    }

    [Fact]
    public async Task SupplyCreate_RejectsInvalidPriceCostAndDuplicateLines()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.SupplyRead, LenseePermissions.SupplyWrite);

        var response = await client.PostAsJsonAsync("/api/v1/supply/shipments", new
        {
            supplierName = "Imported Supplier",
            destinationLocationId = seed.MainLocationId,
            lines = new[]
            {
                new { skuId = seed.SkuId, quantity = 1, unitPrice = (decimal?)0m, lotNumber = "LOT-A", expiryDate = "2028-06-01" },
                new { skuId = seed.SkuId, quantity = 2, unitPrice = (decimal?)10m, lotNumber = "LOT-A", expiryDate = "2028-06-01" }
            },
            costs = new[] { new { costType = "Brokerage", description = new string('D', 256), amount = -1m } }
        });
        var body = await response.Content.ReadFromJsonAsync<ValidationProblemContract>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Lines[0].UnitPrice", body!.Errors.Keys);
        Assert.Contains("Lines[1]", body.Errors.Keys);
        Assert.Contains("Costs[0].CostType", body.Errors.Keys);
        Assert.Contains("Costs[0].Description", body.Errors.Keys);
        Assert.Contains("Costs[0].Amount", body.Errors.Keys);
    }

    [Fact]
    public async Task SupplyConfirm_WithCompletedPricesCreatesInventoryReceiptAndAllocatesCosts()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.SupplyRead, LenseePermissions.SupplyWrite, LenseePermissions.InventoryRead);

        var create = await client.PostAsJsonAsync("/api/v1/supply/shipments", new
        {
            supplierName = "Imported Supplier",
            invoiceNumber = "IMP-1",
            destinationLocationId = seed.MainLocationId,
            lines = new[] { new { skuId = seed.SkuId, quantity = 5, unitPrice = (decimal?)null } },
            costs = new[] { new { costType = "Freight", description = "Sea freight", amount = 20m } }
        });
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var shipmentId = created.RootElement.GetProperty("id").GetGuid();

        var update = await client.PutAsJsonAsync($"/api/v1/supply/shipments/{shipmentId}", new
        {
            supplierName = "Imported Supplier",
            invoiceNumber = "IMP-1",
            destinationLocationId = seed.MainLocationId,
            lines = new[] { new { skuId = seed.SkuId, quantity = 5, unitPrice = (decimal?)100m } },
            costs = new[] { new { costType = "Freight", description = "Sea freight", amount = 20m } }
        });
        var confirm = await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/confirm", null);
        var detail = await client.GetFromJsonAsync<SupplyShipmentContract>($"/api/v1/supply/shipments/{shipmentId}");
        var balances = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Equal("Received", detail!.Status);
        Assert.NotNull(detail.InventoryReceiptOperationId);
        Assert.Equal(500m, detail.ProductSubtotal);
        Assert.Equal(20m, detail.CostSubtotal);
        Assert.Equal(520m, detail.LandedTotal);
        Assert.Equal(20m, detail.Lines.Single().AllocatedCost);
        Assert.Equal(104m, detail.Lines.Single().LandedUnitCost);
        Assert.Contains(balances!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 5);
        Assert.Contains(await _factory.GetInventoryTransactionTypesAsync(seed.SkuId), transactionType => transactionType == InventoryTransactionTypes.SupplyIn);
    }

    [Fact]
    public async Task SupplyConfirm_RevalidatesActiveSkuState()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.SupplyRead, LenseePermissions.SupplyWrite);

        var create = await client.PostAsJsonAsync("/api/v1/supply/shipments", new
        {
            supplierName = "Imported Supplier",
            destinationLocationId = seed.MainLocationId,
            lines = new[] { new { skuId = seed.SkuId, quantity = 5, unitPrice = (decimal?)100m } },
            costs = Array.Empty<object>()
        });
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var shipmentId = created.RootElement.GetProperty("id").GetGuid();
        await _factory.DeactivateProductForSkuAsync(seed.SkuId);

        var confirm = await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/confirm", null);

        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
    }

    [Fact]
    public async Task SupplyEndpoints_RejectErpAdminWithoutSupplyPermission()
    {
        await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.ERPAdmin, LenseePermissions.InventoryRead, LenseePermissions.OperationsRead);

        var response = await client.GetAsync("/api/v1/supply/shipments");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<OperationDetailContract> CreateOperationAsync(HttpClient client, object request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/operations", request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Expected Created but got {response.StatusCode}: {body}");
        return (await response.Content.ReadFromJsonAsync<OperationDetailContract>())!;
    }
}

public sealed class OperationsEndpointFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"operations-contracts-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=lensee_operations_contract_tests;Username=test;Password=test",
                ["Jwt:Secret"] = "OperationsContractTestsNeedASecret123!",
                ["Jwt:Issuer"] = "Lensee",
                ["Jwt:Audience"] = "Lensee.App"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<CatalogDbContext>>();
            services.RemoveAll<DbContextOptions<CrmDbContext>>();
            services.RemoveAll<DbContextOptions<IdentityDbContext>>();
            services.RemoveAll<DbContextOptions<InventoryDbContext>>();
            services.RemoveAll<DbContextOptions<NotificationsDbContext>>();
            services.RemoveAll<DbContextOptions<OperationsDbContext>>();
            services.RemoveAll<DbContextOptions<PaymentsDbContext>>();
            services.RemoveAll<IAuditLogWriter>();
            services.AddDbContext<CatalogDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<CrmDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<IdentityDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<InventoryDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<NotificationsDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<OperationsDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<PaymentsDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddSingleton<IAuditLogWriter, NoOpAuditLogWriter>();

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.TestScheme;
                options.DefaultChallengeScheme = TestAuthHandler.TestScheme;
                options.DefaultForbidScheme = TestAuthHandler.TestScheme;
            }).AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.TestScheme, _ => { });
        });
    }

    public async Task<OperationsSeed> SeedAsync(bool withMainStock = false)
    {
        using var scope = Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var crm = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var ledger = scope.ServiceProvider.GetRequiredService<StockLedgerService>();
        var mainLocationId = Guid.NewGuid();
        var onlineLocationId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var skuId = Guid.NewGuid();

        operations.SupplyShipmentHistoryLogs.RemoveRange(operations.SupplyShipmentHistoryLogs);
        operations.SupplyShipmentCosts.RemoveRange(operations.SupplyShipmentCosts);
        operations.SupplyShipmentLines.RemoveRange(operations.SupplyShipmentLines);
        operations.SupplyShipments.RemoveRange(operations.SupplyShipments);
        operations.OperationVersions.RemoveRange(operations.OperationVersions);
        operations.OperationLines.RemoveRange(operations.OperationLines);
        operations.InventoryReceiptHeaders.RemoveRange(operations.InventoryReceiptHeaders);
        operations.OperationLogs.RemoveRange(operations.OperationLogs);
        crm.MerchantNotes.RemoveRange(crm.MerchantNotes);
        crm.Merchants.RemoveRange(crm.Merchants);
        crm.Representatives.RemoveRange(crm.Representatives);
        identity.RefreshTokens.RemoveRange(identity.RefreshTokens);
        identity.Users.RemoveRange(identity.Users);
        notifications.NotificationLogs.RemoveRange(notifications.NotificationLogs);
        notifications.AlertConfigs.RemoveRange(notifications.AlertConfigs);
        payments.InstallmentSubLogs.RemoveRange(payments.InstallmentSubLogs);
        payments.MainPaymentLogs.RemoveRange(payments.MainPaymentLogs);
        payments.CashRecords.RemoveRange(payments.CashRecords);
        inventory.StockTransactions.RemoveRange(inventory.StockTransactions);
        inventory.InventoryBatches.RemoveRange(inventory.InventoryBatches);
        inventory.StockBalances.RemoveRange(inventory.StockBalances);
        inventory.Locations.RemoveRange(inventory.Locations);
        catalog.Skus.RemoveRange(catalog.Skus);
        catalog.Products.RemoveRange(catalog.Products);
        catalog.Brands.RemoveRange(catalog.Brands);
        catalog.Categories.RemoveRange(catalog.Categories);
        await operations.SaveChangesAsync();
        await crm.SaveChangesAsync();
        await identity.SaveChangesAsync();
        await notifications.SaveChangesAsync();
        await payments.SaveChangesAsync();
        await inventory.SaveChangesAsync();
        await catalog.SaveChangesAsync();

        inventory.Locations.AddRange(
            new Location { Id = mainLocationId, Name = $"Roxy {mainLocationId:N}", LocationType = "MainWarehouse", IsActive = true },
            new Location { Id = onlineLocationId, Name = $"Online {onlineLocationId:N}", LocationType = "Online", IsActive = true });
        catalog.Categories.Add(new Category { Id = categoryId, Name = $"Lenses {categoryId:N}" });
        catalog.Brands.Add(new Brand { Id = brandId, Name = $"Lansee {brandId:N}" });
        catalog.Products.Add(new Product
        {
            Id = productId,
            CategoryId = categoryId,
            BrandId = brandId,
            Name = $"Monthly Lens {productId:N}",
            ProductType = "Lens",
            ExpiryType = "Batch",
            OpenedExpiryDuration = null,
            PiecesPerPack = 2,
            SellMode = "Both",
            ClinicalParams = "{}",
            ExtendedAttributes = "{}",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        catalog.Skus.Add(new Sku { Id = skuId, ProductId = productId, SkuCode = $"LEN-{skuId:N}", ColorName = "Hazel", IsActive = true });

        await catalog.SaveChangesAsync();
        await inventory.SaveChangesAsync();

        if (withMainStock)
        {
            await ledger.ReceiveAsync(mainLocationId, skuId, 10, Guid.NewGuid(), "MAIN-A", new DateOnly(2028, 6, 1));
        }

        return new OperationsSeed(mainLocationId, onlineLocationId, skuId);
    }

    public async Task SetTargetBalanceAsync(Guid locationId, Guid skuId, int available, int target)
    {
        using var scope = Services.CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var balance = await inventory.StockBalances.FirstOrDefaultAsync(value => value.LocationId == locationId && value.SkuId == skuId);
        if (balance is null)
        {
            inventory.StockBalances.Add(new StockBalance
            {
                Id = Guid.NewGuid(),
                LocationId = locationId,
                SkuId = skuId,
                AvailableQty = available,
                TargetQty = target,
                LastUpdated = DateTime.UtcNow
            });
        }
        else
        {
            balance.AvailableQty = available;
            balance.TargetQty = target;
            balance.LastUpdated = DateTime.UtcNow;
        }

        await inventory.SaveChangesAsync();
    }

    public async Task ReceiveMainStockAsync(Guid locationId, Guid skuId, string lotNumber, DateOnly expiryDate, int quantity)
    {
        using var scope = Services.CreateScope();
        var ledger = scope.ServiceProvider.GetRequiredService<StockLedgerService>();
        await ledger.ReceiveAsync(locationId, skuId, quantity, Guid.NewGuid(), lotNumber, expiryDate);
    }

    public async Task<int> GetNotificationCountAsync(string alertType)
    {
        using var scope = Services.CreateScope();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        return await notifications.NotificationLogs.CountAsync(notification => notification.AlertType == alertType);
    }

    public async Task<Guid> CreatePaymentLogAsync()
    {
        using var scope = Services.CreateScope();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var id = Guid.NewGuid();
        payments.MainPaymentLogs.Add(new MainPaymentLog
        {
            Id = id,
            OperationId = Guid.NewGuid(),
            MerchantId = Guid.NewGuid(),
            TotalAmount = 100m,
            AmountPaid = 0m,
            PaymentMethod = "Installment",
            Status = "PendingAccountant",
            InitializedBy = Guid.NewGuid(),
            InitializedAt = DateTime.UtcNow,
            LastModifiedAt = DateTime.UtcNow
        });
        await payments.SaveChangesAsync();
        return id;
    }

    public async Task<int> CountPaymentSubLogsAsync(Guid paymentLogId)
    {
        using var scope = Services.CreateScope();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        return await payments.InstallmentSubLogs.CountAsync(value => value.MainLogId == paymentLogId);
    }

    public async Task DeactivateProductForSkuAsync(Guid skuId)
    {
        using var scope = Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var sku = await catalog.Skus.Include(value => value.Product).SingleAsync(value => value.Id == skuId);
        sku.Product.IsActive = false;
        sku.Product.DeletedAt = DateTime.UtcNow;
        await catalog.SaveChangesAsync();
    }

    public async Task<Guid> CreateMerchantAsync()
    {
        using var scope = Services.CreateScope();
        var crm = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var id = Guid.NewGuid();
        crm.Merchants.Add(new Merchant
        {
            Id = id,
            BusinessName = $"Merchant {id:N}",
            ContactPersonName = "Buyer",
            PhoneNumbers = [],
            BusinessType = "Merchant",
            Status = "Active",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await crm.SaveChangesAsync();
        return id;
    }

    public async Task<Guid> CreateRepresentativeAsync()
    {
        using var scope = Services.CreateScope();
        var crm = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var id = Guid.NewGuid();
        crm.Representatives.Add(new Representative
        {
            Id = id,
            Name = $"Rep {id:N}",
            PhoneNumbers = [],
            Type = "External",
            Status = "Active"
        });
        await crm.SaveChangesAsync();
        return id;
    }

    public async Task<IReadOnlyList<string>> GetInventoryTransactionTypesAsync(Guid skuId)
    {
        using var scope = Services.CreateScope();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            return await inventory.StockTransactions
            .Where(transaction => transaction.SkuId == skuId)
            .OrderBy(transaction => transaction.CreatedAt)
            .Select(transaction => transaction.TransactionType)
            .ToListAsync();
    }
}

public sealed record OperationsSeed(Guid MainLocationId, Guid OnlineLocationId, Guid SkuId);

public sealed class OperationDetailContract
{
    public Guid Id { get; set; }
    public string OperationNumber { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? ClientId { get; set; }
    public string? ClientName { get; set; }
    public IReadOnlyList<OperationWarningContract> Warnings { get; set; } = [];
    public IReadOnlyList<OperationVersionContract>? Versions { get; set; }
}

public sealed class OperationVersionContract
{
    public Guid Id { get; set; }
    public int VersionNumber { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class StocktakeDetailContract
{
    public Guid Id { get; set; }
    public IReadOnlyList<StocktakeLineContract> Lines { get; set; } = [];
}

public sealed record StocktakeLineContract(Guid Id, Guid SkuId, int PhysicalCount);

public sealed class SupplyShipmentContract
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal ProductSubtotal { get; set; }
    public decimal CostSubtotal { get; set; }
    public decimal LandedTotal { get; set; }
    public Guid? InventoryReceiptOperationId { get; set; }
    public IReadOnlyList<SupplyLineContract> Lines { get; set; } = [];
}

public sealed record SupplyLineContract(decimal? UnitPrice, decimal LineSubtotal, decimal AllocatedCost, decimal LandedUnitCost);

public sealed record OperationWarningContract(string WarningType, Guid SkuId, string SkuCode, string ProductName, string? LotNumber, DateOnly? ExpiryDate, int RequestedQuantity, int EligibleQuantity, string Message);

public sealed record OperationWarningGateContract(string Title, string Detail, IReadOnlyList<OperationWarningContract> Warnings);

public sealed record OperationStockBalanceContract(Guid LocationId, Guid SkuId, int AvailablePacks, int ReservedInWarehousePacks, int ReservedWithRepPacks);

public sealed record BatchContract(string? LotNumber, int PackQuantity);

public sealed record OperationListContract(Guid Id, string OperationType, string Status);

public sealed record ReplenishmentRowContract(Guid DestinationLocationId, Guid SkuId, int AvailablePacks, int IncomingPacks, int TargetPacks, int ShortagePacks);

public sealed record ReplenishmentReserveContract(int CreatedOperations, int UnfilledPacks);

public sealed record TransferBlockedBatchContract(Guid SkuId, string? LotNumber, int PackQuantity, DateOnly? MinimumTransferExpiryDate, string Reason);

public sealed record MerchantEligibilityContract(Guid SkuId, string? LotNumber, DateOnly? ExpiryDate, int SoldQty, int ReturnableQty, int ReturnedQty, int OverReturnedQty);

public sealed record PaymentLogContract(
    Guid Id,
    Guid OperationId,
    Guid MerchantId,
    decimal TotalAmount,
    decimal AmountPaid,
    decimal RemainingAmount,
    string PaymentMethod,
    string Status,
    Guid? AssignedTo,
    DateTime LastModifiedAt);

public sealed record MerchantListContract(Guid Id, string BusinessName, string BusinessType, string Status);

public sealed record PaymentLogDetailContract(PaymentLogContract Log, IReadOnlyList<PaymentSubLogContract> SubLogs, string? Notes);

public sealed record PaymentSubLogContract(
    Guid Id,
    decimal Amount,
    string? PaymentMethod,
    DateOnly DateReceived,
    string Status,
    Guid DraftedBy,
    DateTime DraftedAt,
    Guid? ConfirmedBy,
    DateTime? ConfirmedAt,
    string? RejectionReason,
    string? Notes);

public sealed record MerchantBalanceContract(
    Guid MerchantId,
    decimal SaleTotal,
    decimal ReturnTotal,
    decimal ChangeNet,
    decimal PaymentsReceived,
    decimal CashRefunded,
    decimal Balance);
