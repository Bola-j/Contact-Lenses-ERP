using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lensee.Host.Infrastructure;
using Lensee.Host.Services;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Finance.Data;
using Lensee.Modules.Identity.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Inventory.Services;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Operations.Data;
using Lensee.Modules.Payments.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Data;
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

    private static Task<HttpResponseMessage> PostPaymentAsync(HttpClient client, string requestUri)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PostPaymentJsonAsync<TRequest>(HttpClient client, string requestUri, TRequest body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }

    [Fact]
    public async Task FinanceOpeningBalance_AccountantCorrectionNeedsAdminApproval_AndPreservesReversalLineage()
    {
        await _factory.SeedAsync();
        var accountId = Guid.NewGuid();
        using (var accountScope = _factory.Services.CreateScope())
        {
            var accountFinance = accountScope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            accountFinance.FinanceAccounts.Add(new FinanceAccount
            {
                Id = accountId,
                Name = $"Opening cash {accountId:N}",
                Type = FinanceLedgerService.CashOnHand,
                IsActive = true,
                CreatedBy = Guid.NewGuid(),
                CreatedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
            });
            await accountFinance.SaveChangesAsync();
        }
        var creatorId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        using var creator = _factory.CreateClient();
        creator.AuthorizeAs(LenseeRoles.Accountant, creatorId, LenseePermissions.FinanceRead, LenseePermissions.FinanceOpeningCreate, LenseePermissions.FinanceOpeningCorrect);

        var created = await creator.PostAsJsonAsync("/api/v1/finance/opening-balances", new
        {
            financeAccountId = accountId,
            amount = 125m,
            direction = "Credit",
            asOfDate = "2026-01-01",
            description = "Treasury opening cash",
            correlationId = "finance-opening-test"
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draft = await created.Content.ReadFromJsonAsync<FinanceOpeningBalance>();
        Assert.NotNull(draft);
        Assert.Equal("Posted", draft!.Status);

        using var reviewer = _factory.CreateClient();
        reviewer.AuthorizeAs(LenseeRoles.Admin, reviewerId, LenseePermissions.FinanceRead, LenseePermissions.FinanceExpenseApprove);

        var correction = await creator.PostAsJsonAsync($"/api/v1/finance/opening-balances/{draft.Id}/correct", new
        {
            amount = 150m,
            direction = "Credit",
            asOfDate = "2026-01-01",
            description = "Corrected treasury opening cash",
            correlationId = "finance-opening-correction-test"
        });
        Assert.Equal(HttpStatusCode.Created, correction.StatusCode);
        var replacement = await correction.Content.ReadFromJsonAsync<FinanceOpeningBalance>();
        Assert.NotNull(replacement);
        Assert.Equal("PendingReview", replacement!.Status);

        var replacementApproved = await reviewer.PostAsync($"/api/v1/finance/opening-balances/{replacement.Id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, replacementApproved.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var entries = await finance.FinanceLedgerEntries.Where(value => value.FinanceAccountId == accountId).ToListAsync();
        Assert.Equal(3, entries.Count);
        Assert.Equal(150m, entries.Sum(value => value.Direction == FinanceLedgerService.Credit ? value.Amount : -value.Amount));
        Assert.Equal("Corrected", await finance.FinanceOpeningBalances.Where(value => value.Id == draft.Id).Select(value => value.Status).SingleAsync());
    }

    [Fact]
    public async Task WarehouseClerk_CrmAndCorrectionReadsAreScoped_AndCrmWritesAreDenied()
    {
        var seed = await _factory.SeedAsync();
        var scoped = await _factory.SeedLocationScopedCrmAndCorrectionsAsync(seed);
        using var clerk = _factory.CreateClient();
        clerk.AuthorizeAsAtLocation(
            LenseeRoles.WarehouseClerk,
            seed.MainLocationId,
            LenseePermissions.OperationsRead,
            LenseePermissions.OperationsWrite);

        var merchants = await clerk.GetFromJsonAsync<PagedContract<MerchantListContract>>("/api/v1/crm/merchants?pageSize=25");
        var ownMerchant = await clerk.GetAsync($"/api/v1/crm/merchants/{scoped.OwnMerchantId}");
        var foreignMerchant = await clerk.GetAsync($"/api/v1/crm/merchants/{scoped.ForeignMerchantId}");
        var representatives = await clerk.GetFromJsonAsync<IReadOnlyList<ScopedRepresentativeContract>>("/api/v1/crm/representatives");
        var updateMerchant = await clerk.PutAsJsonAsync($"/api/v1/crm/merchants/{scoped.OwnMerchantId}", new
        {
            businessName = "Updated but forbidden",
            contactPersonName = "Scoped Clerk",
            phoneNumbers = new[] { "01000000000" },
            businessType = "Merchant"
        });
        var createRepresentative = await clerk.PostAsJsonAsync("/api/v1/crm/representatives", new
        {
            name = "Forbidden representative",
            phoneNumbers = new[] { "01000000000" },
            type = "External",
            assignedLocationId = seed.MainLocationId
        });
        var ownCorrection = await clerk.GetAsync($"/api/v1/operations/corrections/{scoped.OwnProposalId}");
        var foreignCorrection = await clerk.GetAsync($"/api/v1/operations/corrections/{scoped.ForeignProposalId}");
        var ownLineage = await clerk.GetAsync($"/api/v1/operations/{scoped.OwnOperationId}/corrections");
        var foreignLineage = await clerk.GetAsync($"/api/v1/operations/{scoped.ForeignOperationId}/corrections");

        Assert.NotNull(merchants);
        Assert.Equal(scoped.OwnMerchantId, Assert.Single(merchants!.Items).Id);
        Assert.Equal(HttpStatusCode.OK, ownMerchant.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignMerchant.StatusCode);
        Assert.NotNull(representatives);
        Assert.Equal(scoped.OwnRepresentativeId, Assert.Single(representatives!).Id);
        Assert.Equal(HttpStatusCode.Forbidden, updateMerchant.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createRepresentative.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownCorrection.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignCorrection.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownLineage.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignLineage.StatusCode);
    }

    [Fact]
    public async Task FinalizedSale_CorrectionRequiresIndependentApproval_AndCreatesImmutableReversal()
    {
        var seed = await _factory.SeedAsync();
        var operationId = await _factory.CreateFinalizedWholesaleSaleAsync(seed);
        var requesterId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();
        using var requester = _factory.CreateClient();
        requester.AuthorizeAs(LenseeRoles.Accountant, requesterId, LenseePermissions.OperationsRead, LenseePermissions.OperationsCorrectionsRequest);

        var created = await requester.PostAsJsonAsync($"/api/v1/operations/{operationId}/corrections", new
        {
            reason = "Customer cancelled after settlement review.",
            createReplacementDraft = true
        });
        var proposal = await created.Content.ReadFromJsonAsync<OperationCorrectionContract>();

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(proposal);

        var submitted = await requester.PostAsync($"/api/v1/operations/corrections/{proposal!.Id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);

        using var selfApprover = _factory.CreateClient();
        selfApprover.AuthorizeAs(LenseeRoles.Admin, requesterId, LenseePermissions.OperationsCorrectionsApprove);
        var selfApproval = await selfApprover.PostAsync($"/api/v1/operations/corrections/{proposal!.Id}/approve", null);
        Assert.Equal(HttpStatusCode.Forbidden, selfApproval.StatusCode);

        using var reviewer = _factory.CreateClient();
        reviewer.AuthorizeAs(LenseeRoles.ERPAdmin, reviewerId, LenseePermissions.OperationsCorrectionsApprove, LenseePermissions.OperationsRead);
        var approved = await reviewer.PostAsync($"/api/v1/operations/corrections/{proposal.Id}/approve", null);
        var response = await approved.Content.ReadFromJsonAsync<OperationCorrectionContract>();

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.NotNull(response?.ReversalOperationId);
        Assert.NotNull(response?.ReplacementOperationId);

        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var shared = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
        Assert.Equal("Completed", (await operations.OperationLogs.SingleAsync(value => value.Id == operationId)).Status);
        Assert.Equal("Reversal", (await operations.OperationLogs.SingleAsync(value => value.Id == response!.ReversalOperationId)).RecordKind);
        Assert.Equal("Replacement", (await operations.OperationLogs.SingleAsync(value => value.Id == response.ReplacementOperationId)).RecordKind);
        Assert.Contains(await inventory.StockBalances.ToListAsync(), value => value.LocationId == seed.MainLocationId && value.SkuId == seed.SkuId && value.AvailableQty == 2);
        Assert.Contains(await shared.OutboxMessages.ToListAsync(), value => value.EventType == typeof(OperationCorrectionChangedEvent).AssemblyQualifiedName && value.Status == "Pending");
    }

    [Fact]
    public async Task HealthEndpoint_IsNotSubjectToTheGlobalRateLimit()
    {
        using var client = _factory.CreateClient();

        for (var request = 0; request < 121; request++)
        {
            var response = await client.GetAsync("/health");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
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
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead, LenseePermissions.PaymentsRead);

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
        var duplicateShip = await client.PostAsync($"/api/v1/operations/{operation.Id}/ship", null);
        Assert.NotEqual(HttpStatusCode.NoContent, duplicateShip.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, receive.StatusCode);
        Assert.Contains(main!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 6 && balance.ReservedInWarehousePacks == 0);
        Assert.Contains(online!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 4);
        var duplicateReceive = await client.PostAsync($"/api/v1/operations/{operation.Id}/receive", null);
        Assert.NotEqual(HttpStatusCode.NoContent, duplicateReceive.StatusCode);
    }

    [Fact]
    public async Task OperationLifecycle_WritesOneExplicitAuditPerSuccessfulMutation()
    {
        using var factory = new OperationsEndpointFactory(useRealAuditWriter: true);
        var seed = await factory.SeedAsync(withMainStock: true);
        using var client = factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "WarehouseTransfer",
            sourceLocationId = seed.MainLocationId,
            destinationLocationId = seed.OnlineLocationId,
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });
        var confirm = await client.PostAsync($"/api/v1/operations/{operation.Id}/confirm", null);
        var ship = await client.PostAsync($"/api/v1/operations/{operation.Id}/ship", null);
        var receive = await client.PostAsync($"/api/v1/operations/{operation.Id}/receive", null);

        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, ship.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, receive.StatusCode);
        var auditActions = await factory.GetOperationAuditActionsAsync(operation.Id);
        Assert.Equal(4, auditActions.Count);
        Assert.Equal(1, auditActions.Count(action => action == "Create"));
        Assert.Equal(1, auditActions.Count(action => action == "Confirm"));
        Assert.Equal(1, auditActions.Count(action => action == "Ship"));
        Assert.Equal(1, auditActions.Count(action => action == "Receive"));
    }

    [Fact]
    public async Task DraftMutationLifecycle_WritesOneExplicitAuditForUpdateReviseAndCancel()
    {
        using var factory = new OperationsEndpointFactory(useRealAuditWriter: true);
        var seed = await factory.SeedAsync();
        using var client = factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "InventoryReceipt",
            destinationLocationId = seed.MainLocationId,
            receipt = new { supplierName = "Audit supplier", invoiceNumber = "AUD-1" },
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, lotNumber = "AUDIT", expiryDate = "2028-06-01" } }
        });
        var update = await client.PutAsJsonAsync($"/api/v1/operations/{operation.Id}", new
        {
            operationType = "InventoryReceipt",
            destinationLocationId = seed.MainLocationId,
            receipt = new { supplierName = "Audit supplier", invoiceNumber = "AUD-2" },
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, lotNumber = "AUDIT", expiryDate = "2028-06-01" } }
        });
        var revise = await client.PostAsJsonAsync($"/api/v1/operations/{operation.Id}/revise", new
        {
            operation = new
            {
                operationType = "InventoryReceipt",
                destinationLocationId = seed.MainLocationId,
                receipt = new { supplierName = "Audit supplier", invoiceNumber = "AUD-3" },
                lines = new[] { new { skuId = seed.SkuId, packQuantity = 3, lotNumber = "AUDIT", expiryDate = "2028-06-01" } }
            },
            reason = "Correct the receipt draft."
        });
        var cancel = await client.PostAsJsonAsync($"/api/v1/operations/{operation.Id}/cancel", new { reason = "Draft receipt no longer required." });

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Equal(HttpStatusCode.OK, revise.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        var auditActions = await factory.GetOperationAuditActionsAsync(operation.Id);
        Assert.Equal(4, auditActions.Count);
        Assert.Equal(1, auditActions.Count(action => action == "Create"));
        Assert.Equal(1, auditActions.Count(action => action == "Update"));
        Assert.Equal(1, auditActions.Count(action => action == "Revise"));
        Assert.Equal(1, auditActions.Count(action => action == "Cancel"));
    }

    [Fact]
    public async Task WarehouseTransfer_UsesShortDatedUnexpiredBatchesByFefo()
    {
        var seed = await _factory.SeedAsync();
        var shortExpiry = _factory.GetEgyptToday().AddDays(7);
        await _factory.ReceiveMainStockAsync(seed.MainLocationId, seed.SkuId, "SHORT", shortExpiry, 4);
        await _factory.ReceiveMainStockAsync(seed.MainLocationId, seed.SkuId, "VALID", new DateOnly(2028, 6, 1), 5);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "WarehouseTransfer",
            sourceLocationId = seed.MainLocationId,
            destinationLocationId = seed.OnlineLocationId,
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 3, lotNumber = "SHORT", expiryDate = shortExpiry } }
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
        var cancel = await client.PostAsJsonAsync($"/api/v1/operations/{operation.Id}/cancel", new { reason = "Transfer request withdrawn." });
        var balances = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");

        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        Assert.Contains(balances!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 10 && balance.ReservedInWarehousePacks == 0);
    }

    [Fact]
    public async Task ReplenishmentReserve_CreatesDraftTransferForTargetShortage()
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
        Assert.Contains(operations!.Items, operation => operation.OperationType == "WarehouseTransfer" && operation.Status == "Draft");
        Assert.Contains(main!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 10 && balance.ReservedInWarehousePacks == 0);
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
        var shortExpiry = _factory.GetEgyptToday().AddDays(7);
        await _factory.ReceiveMainStockAsync(seed.MainLocationId, seed.SkuId, "SHORT", shortExpiry, 5);
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
        Assert.Contains(main!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 10 && balance.ReservedInWarehousePacks == 0);
        Assert.Equal(0, alerts);
    }

    [Fact]
    public async Task ScheduledReplenishment_IsIdempotentForTheSameCairoDay()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        await _factory.SetTargetBalanceAsync(seed.OnlineLocationId, seed.SkuId, available: 0, target: 4);

        TargetReplenishmentRunResult first;
        TargetReplenishmentRunResult second;
        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<TargetReplenishmentService>();
            first = await service.RunAsync("Scheduled", null, null, CancellationToken.None);
        }
        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<TargetReplenishmentService>();
            second = await service.RunAsync("Scheduled", null, null, CancellationToken.None);
        }

        Assert.Equal(1, first.CreatedOperations);
        Assert.False(first.AlreadyCompleted);
        Assert.Equal(0, second.CreatedOperations);
        Assert.True(second.AlreadyCompleted);
    }

    [Fact]
    public async Task InventoryTransferBlockedBatches_DoesNotShowShortDatedUnexpiredStock()
    {
        var seed = await _factory.SeedAsync();
        var shortExpiry = _factory.GetEgyptToday().AddDays(7);
        await _factory.ReceiveMainStockAsync(seed.MainLocationId, seed.SkuId, "SHORT", shortExpiry, 5);
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
    public async Task MerchantAccountSale_DoesNotRequireReceivingFinanceAccount()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "MerchantAccount",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", unitPrice = 125, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        Assert.Equal(merchantId, operation.ClientId);
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
    public async Task CompletedSaleKeepsReceivableUntilPaymentsCollectionIsApproved()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead, LenseePermissions.PaymentsRead, LenseePermissions.PaymentsApprove);

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

        var afterCompletion = await client.GetFromJsonAsync<MerchantBalanceContract>($"/api/v1/payments/merchants/{merchantId}/balance");
        Assert.Equal(0m, afterCompletion!.PaymentsReceived);
        Assert.Equal(200m, afterCompletion.Balance);
    }

    [Fact]
    public async Task AnonymousCompletedCashSale_RemainsOtherPaymentsWithoutCreatingMerchant()
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
        Assert.Null(detail!.ClientId);
        Assert.Equal(buyerName, detail.ClientName);

        var paymentLog = Assert.Single(logs!.Items, log => log.OperationId == operation.Id);
        Assert.Null(paymentLog.MerchantId);
        Assert.Equal("CashHandToHand", paymentLog.PaymentMethod);
        Assert.DoesNotContain(merchants!.Items, item => item.BusinessName == buyerName);
    }

    [Fact]
    public async Task CashRefundForWrongCashSale_ReopensMerchantReceivable()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead, LenseePermissions.PaymentsRead, LenseePermissions.PaymentsWrite, LenseePermissions.PaymentsDraft, LenseePermissions.PaymentsApprove, LenseePermissions.PaymentsAdjustmentsRequest, LenseePermissions.PaymentsAdjustmentsApprove);

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
        var collectionResponse = await PostPaymentJsonAsync(client, $"/api/v1/payments/merchant-accounts/{merchantId}/collections", new
        {
            amount = 200m,
            paymentMethod = "CashHandToHand",
            financeAccountId = await _factory.GetFinanceAccountIdAsync(),
            submitForReview = true,
            sourceOperationId = operation.Id
        });
        Assert.Equal(HttpStatusCode.Created, collectionResponse.StatusCode);
        var collection = await collectionResponse.Content.ReadFromJsonAsync<JsonElement>();
        var collectionId = collection.GetProperty("id").GetGuid();
        var collectionApproval = await PostPaymentAsync(client, $"/api/v1/payments/merchant-account-collections/{collectionId}/approve");
        Assert.Equal(HttpStatusCode.OK, collectionApproval.StatusCode);
        var adjustmentRequesterId = Guid.NewGuid();
        client.AuthorizeAs(LenseeRoles.Admin, adjustmentRequesterId, LenseePermissions.PaymentsRead, LenseePermissions.PaymentsAdjustmentsRequest);
        var refundRequest = await PostPaymentJsonAsync(client, "/api/v1/payments/adjustments", new
        {
            merchantId,
            operationId = operation.Id.ToString(),
            adjustmentType = "CashRefund",
            amount = 200m,
            notes = "Wrong cash sale correction"
        });
        using var approver = _factory.CreateClient();
        approver.AuthorizeAs(LenseeRoles.Admin, Guid.NewGuid(), LenseePermissions.PaymentsRead, LenseePermissions.PaymentsAdjustmentsApprove);
        var refundAdjustment = refundRequest.StatusCode == HttpStatusCode.Created
            ? await refundRequest.Content.ReadFromJsonAsync<FinancialAdjustmentContract>()
            : null;
        var refund = refundAdjustment is not null
            ? await PostPaymentAsync(approver, $"/api/v1/payments/adjustments/{refundAdjustment.Id}/approve")
            : refundRequest;
        var payout = refundAdjustment is not null
            ? await PostPaymentJsonAsync(approver, $"/api/v1/payments/adjustments/{refundAdjustment.Id}/payout", new { amount = 200m, paymentMethod = "CashHandToHand", financeAccountId = await _factory.GetFinanceAccountIdAsync() })
            : refundRequest;
        var balance = await client.GetFromJsonAsync<MerchantBalanceContract>($"/api/v1/payments/merchants/{merchantId}/balance");

        Assert.Equal(HttpStatusCode.Created, refundRequest.StatusCode);
        Assert.Equal(HttpStatusCode.OK, refund.StatusCode);
        Assert.Equal(HttpStatusCode.OK, payout.StatusCode);
        Assert.Equal(200m, balance!.SaleTotal);
        Assert.Equal(200m, balance.PaymentsReceived);
        Assert.Equal(200m, balance.CashRefunded);
        Assert.Equal(200m, balance.Balance);
        using (var scope = _factory.Services.CreateScope())
        {
            var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            var payoutRecord = await payments.CashRecords.SingleAsync(value => value.FinancialAdjustmentId == refundAdjustment!.Id);
            Assert.Contains(await finance.FinanceLedgerEntries.ToListAsync(), value => value.SourceType == "RefundPayout" && value.SourceId == payoutRecord.Id && value.Direction == FinanceLedgerService.Debit && value.Amount == 200m);
        }
    }

    [Fact]
    public async Task FinancialAdjustment_AcceptsOperationNumberReferenceAndCLevelApproval()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        var adminRequesterId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, adminRequesterId, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead, LenseePermissions.PaymentsRead, LenseePermissions.PaymentsAdjustmentsRequest);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "Installment",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });
        var detail = await client.GetFromJsonAsync<OperationDetailContract>($"/api/v1/operations/{operation.Id}");

        await client.PostAsync($"/api/v1/operations/{operation.Id}/confirm", null);
        await client.PostAsync($"/api/v1/operations/{operation.Id}/ship", null);
        await client.PostAsync($"/api/v1/operations/{operation.Id}/complete", null);

        var adjustment = await PostPaymentJsonAsync(client, "/api/v1/payments/adjustments", new
        {
            merchantId,
            operationId = detail!.OperationNumber,
            adjustmentType = "BalanceReduction",
            amount = 100m,
            notes = "Operation code reference"
        });

        var adjustmentRecord = await adjustment.Content.ReadFromJsonAsync<FinancialAdjustmentContract>();
        using var approver = _factory.CreateClient();
        approver.AuthorizeAs(LenseeRoles.CLevel, Guid.NewGuid(), LenseePermissions.PaymentsRead, LenseePermissions.PaymentsAdjustmentsApprove);
        var approval = adjustmentRecord is null
            ? adjustment
            : await PostPaymentAsync(approver, $"/api/v1/payments/adjustments/{adjustmentRecord.Id}/approve");

        Assert.Equal(HttpStatusCode.Created, adjustment.StatusCode);
        Assert.True(approval.StatusCode == HttpStatusCode.OK, await approval.Content.ReadAsStringAsync());

        var adminOwnRequest = await PostPaymentJsonAsync(client, "/api/v1/payments/adjustments", new
        {
            merchantId,
            operationId = detail.OperationNumber,
            adjustmentType = "AdditionalCharge",
            amount = 25m,
            notes = "Admin approval contract"
        });
        var adminOwnAdjustment = await adminOwnRequest.Content.ReadFromJsonAsync<FinancialAdjustmentContract>();
        using var adminApprover = _factory.CreateClient();
        adminApprover.AuthorizeAs(LenseeRoles.Admin, adminRequesterId, LenseePermissions.PaymentsRead, LenseePermissions.PaymentsAdjustmentsApprove);
        var adminOwnApproval = adminOwnAdjustment is null
            ? adminOwnRequest
            : await PostPaymentAsync(adminApprover, $"/api/v1/payments/adjustments/{adminOwnAdjustment.Id}/approve");

        Assert.Equal(HttpStatusCode.Created, adminOwnRequest.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adminOwnApproval.StatusCode);
    }

    [Fact]
    public async Task FinancialAdjustmentApprovalInbox_ReturnsPendingAcrossMerchants()
    {
        await _factory.SeedAsync();
        var firstMerchantId = Guid.NewGuid();
        var secondMerchantId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            payments.FinancialAdjustments.AddRange(
                new FinancialAdjustment { Id = Guid.NewGuid(), MerchantId = firstMerchantId, AdjustmentType = "BalanceReduction", Amount = 12m, Status = "PendingApproval", CreatedBy = Guid.NewGuid(), CreatedAt = now, LineageKind = "SourceLinked" },
                new FinancialAdjustment { Id = Guid.NewGuid(), MerchantId = secondMerchantId, AdjustmentType = "CashRefund", Amount = 8m, Status = "PendingApproval", CreatedBy = Guid.NewGuid(), CreatedAt = now.AddSeconds(1), LineageKind = "SourceLinked" },
                new FinancialAdjustment { Id = Guid.NewGuid(), MerchantId = firstMerchantId, AdjustmentType = "BalanceReduction", Amount = 5m, Status = "Approved", CreatedBy = Guid.NewGuid(), CreatedAt = now.AddSeconds(2), LineageKind = "SourceLinked" });
            await payments.SaveChangesAsync();
        }

        using var reviewer = _factory.CreateClient();
        reviewer.AuthorizeAs(LenseeRoles.Admin, Guid.NewGuid(), LenseePermissions.PaymentsRead);
        var pending = await reviewer.GetFromJsonAsync<FinancialAdjustmentContract[]>("/api/v1/payments/adjustments?pendingOnly=true");

        Assert.NotNull(pending);
        Assert.Equal(2, pending!.Length);
        Assert.All(pending, item => Assert.Equal("PendingApproval", item.Status));
        Assert.Contains(pending, item => item.MerchantId == firstMerchantId);
        Assert.Contains(pending, item => item.MerchantId == secondMerchantId);
    }

    [Fact]
    public async Task MerchantCollection_TargetsSelectedSale_AndClosureProposalUsesSeparateContextReads()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin,
            LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead,
            LenseePermissions.PaymentsRead, LenseePermissions.PaymentsDraft, LenseePermissions.PaymentsApprove);
        var sale = await CreateOperationAsync(client, new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "MerchantAccount",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });
        await client.PostAsync($"/api/v1/operations/{sale.Id}/confirm", null);
        await client.PostAsync($"/api/v1/operations/{sale.Id}/ship", null);
        await client.PostAsync($"/api/v1/operations/{sale.Id}/receive", null);

        var collectionResponse = await PostPaymentJsonAsync(client, $"/api/v1/payments/merchant-accounts/{merchantId}/collections", new
        {
            sourceOperationId = sale.Id,
            amount = 200m,
            paymentMethod = "CashHandToHand",
            financeAccountId = await _factory.GetFinanceAccountIdAsync(),
            submitForReview = true
        });
        var collection = await collectionResponse.Content.ReadFromJsonAsync<MerchantCollectionDraftContract>();
        var approval = collection is null
            ? collectionResponse
            : await PostPaymentAsync(client, $"/api/v1/payments/merchant-account-collections/{collection.Id}/approve");
        var eligible = await client.GetFromJsonAsync<JsonElement[]>($"/api/v1/payments/merchant-accounts/{merchantId}/financial-closure/eligible");
        var openMerchantPayments = await client.GetFromJsonAsync<PagedContract<PaymentLogContract>>($"/api/v1/payments/merchant-account-payments?openOnly=true&merchantId={merchantId}");
        var proposalResponse = await PostPaymentJsonAsync(client, $"/api/v1/payments/merchant-accounts/{merchantId}/financial-closure/proposals", new { operationIds = new[] { sale.Id }, notes = "Fully settled sale" });
        var proposal = await proposalResponse.Content.ReadFromJsonAsync<JsonElement>();
        var proposalId = proposal.ValueKind == JsonValueKind.Object && proposal.TryGetProperty("id", out var idNode) ? idNode.GetGuid() : Guid.Empty;
        var review = proposalId == Guid.Empty
            ? proposalResponse
            : await PostPaymentJsonAsync(client, $"/api/v1/payments/financial-closure/proposals/{proposalId}/review", new { approvedOperationIds = new[] { sale.Id }, rejectionReason = (string?)null });
        var orders = await client.GetFromJsonAsync<JsonElement[]>($"/api/v1/payments/merchant-accounts/{merchantId}/orders");
        var closedOrder = orders?.SingleOrDefault(value => value.GetProperty("operationId").GetGuid() == sale.Id);
        var operationDetail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/operations/{sale.Id}");

        Assert.Equal(HttpStatusCode.Created, collectionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        Assert.NotNull(eligible);
        Assert.Contains(eligible!, value => value.GetProperty("id").GetGuid() == sale.Id);
        Assert.DoesNotContain(openMerchantPayments!.Items, value => value.OperationId == sale.Id);
        Assert.True(proposalResponse.StatusCode == HttpStatusCode.Created, await proposalResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        Assert.Equal("FinanciallyClosed", closedOrder?.GetProperty("financialClosureStatus").GetString());
        Assert.Equal("FinanciallyClosed", operationDetail.GetProperty("financialClosureStatus").GetString());
    }

    [Fact]
    public async Task MerchantSale_BalanceReductionSettlesObligationAndEnablesFinancialClosure()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var requester = _factory.CreateClient();
        requester.AuthorizeAs(LenseeRoles.Admin,
            LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead,
            LenseePermissions.PaymentsRead, LenseePermissions.PaymentsDraft, LenseePermissions.PaymentsAdjustmentsRequest);
        var sale = await CreateOperationAsync(requester, new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "MerchantAccount",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });
        await requester.PostAsync($"/api/v1/operations/{sale.Id}/confirm", null);
        await requester.PostAsync($"/api/v1/operations/{sale.Id}/ship", null);
        await requester.PostAsync($"/api/v1/operations/{sale.Id}/receive", null);

        var reductionResponse = await PostPaymentJsonAsync(requester, "/api/v1/payments/adjustments", new
        {
            merchantId,
            operationId = sale.Id.ToString(),
            adjustmentType = "BalanceReduction",
            amount = 200m,
            notes = "Approved commercial settlement reduction"
        });
        var reduction = await reductionResponse.Content.ReadFromJsonAsync<FinancialAdjustmentContract>();
        using var approver = _factory.CreateClient();
        approver.AuthorizeAs(LenseeRoles.Admin, Guid.NewGuid(), LenseePermissions.PaymentsRead, LenseePermissions.PaymentsAdjustmentsApprove);
        var approval = reduction is null ? reductionResponse : await PostPaymentAsync(approver, $"/api/v1/payments/adjustments/{reduction.Id}/approve");
        var eligible = await requester.GetFromJsonAsync<JsonElement[]>($"/api/v1/payments/merchant-accounts/{merchantId}/financial-closure/eligible");

        Assert.Equal(HttpStatusCode.Created, reductionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);
        Assert.Contains(eligible!, value => value.GetProperty("id").GetGuid() == sale.Id && value.GetProperty("remainingAmount").GetDecimal() == 0m);
    }

    [Fact]
    public async Task PaymentOperationResolution_ResolvesMerchantFromOperationNumber()
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
            paymentMethod = "Installment",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        var response = await client.GetAsync($"/api/v1/payments/operations/resolve?reference={Uri.EscapeDataString(operation.OperationNumber)}");
        var resolved = await response.Content.ReadFromJsonAsync<PaymentOperationResolutionContract>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(resolved);
        Assert.Equal(operation.Id, resolved!.OperationId);
        Assert.Equal(merchantId, resolved.MerchantId);
        Assert.Equal("Operation", resolved.RecordType);
        Assert.Equal(operation.Id, resolved.CanonicalId);
        Assert.Equal("MerchantAccount", resolved.Scope);
        Assert.Equal(operation.Status, resolved.CurrentStatus);
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
            LenseePermissions.PaymentsWrite,
            LenseePermissions.PaymentsApprove);

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

        await PostPaymentJsonAsync(admin, $"/api/v1/payments/{log.Id}/assign", new { accountantUserId = (Guid?)null });
        var draft = await PostPaymentJsonAsync(accountant, $"/api/v1/payments/{log.Id}/sub-logs", new
        {
            amount = 120m,
            paymentMethod = "CashTransaction",
            transactionReference = "BANK-20260702-001",
            dateReceived = "2026-07-02",
            notes = "First installment",
            financeAccountId = await _factory.GetFinanceAccountIdAsync()
        });
        var draftedDetail = await draft.Content.ReadFromJsonAsync<PaymentLogDetailContract>();
        var draftedSubLog = Assert.Single(draftedDetail!.SubLogs);
        var beforeApproval = await admin.GetFromJsonAsync<MerchantBalanceContract>($"/api/v1/payments/merchants/{merchantId}/balance");

        // The unified collection command is a compatibility facade and must
        // resolve installment-sublog IDs to the same canonical approval path.
        var approve = await PostPaymentAsync(admin, $"/api/v1/payments/collections/{draftedSubLog.Id}/approve");
        var afterApproval = await admin.GetFromJsonAsync<MerchantBalanceContract>($"/api/v1/payments/merchants/{merchantId}/balance");

        Assert.Equal(200m, log.TotalAmount);
        Assert.Equal("PendingAccountant", log.Status);
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

        var response = await PostPaymentJsonAsync(client, $"/api/v1/payments/{logId}/sub-logs", new
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

        var response = await PostPaymentJsonAsync(client, $"/api/v1/payments/{logId}/sub-logs", new
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

        var unregistered = await PostPaymentJsonAsync(client, "/api/v1/payments/initialize", new { operationId = anonymousSale.Id, paymentMethod = "CashHandToHand" });
        var unknownMethod = await PostPaymentJsonAsync(client, "/api/v1/payments/initialize", new { operationId = registeredSale.Id, paymentMethod = "Crypto" });

        Assert.Equal(HttpStatusCode.BadRequest, unregistered.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownMethod.StatusCode);
    }

    [Fact]
    public async Task CashRecord_RejectsInvalidPayloads()
    {
        await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.PaymentsRead, LenseePermissions.PaymentsWrite, LenseePermissions.PaymentsDraft);

        var zeroAmount = await PostPaymentJsonAsync(client, "/api/v1/payments/cash-records", new { operationId = Guid.NewGuid().ToString(), paymentType = "CashReceived", amount = 0m });
        var badType = await PostPaymentJsonAsync(client, "/api/v1/payments/cash-records", new { operationId = Guid.NewGuid().ToString(), paymentType = "Crypto", amount = 1m });
        var unknownOperation = await PostPaymentJsonAsync(client, "/api/v1/payments/cash-records", new { operationId = Guid.NewGuid().ToString(), paymentType = "CashReceived", amount = 1m });

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
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.PaymentsRead, LenseePermissions.PaymentsAdjustmentsRequest);
        var operation = await CreateOperationAsync(client, new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "Installment",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        var zeroAmount = await PostPaymentJsonAsync(client, "/api/v1/payments/adjustments", new { merchantId, adjustmentType = "MerchantCredit", amount = 0m });
        var badType = await PostPaymentJsonAsync(client, "/api/v1/payments/adjustments", new { merchantId, adjustmentType = "Crypto", amount = 1m });
        var missingMerchant = await PostPaymentJsonAsync(client, "/api/v1/payments/adjustments", new { merchantId = Guid.NewGuid(), operationId = operation.Id.ToString(), adjustmentType = "MerchantCredit", amount = 1m });
        var wrongMerchantOperation = await PostPaymentJsonAsync(client, "/api/v1/payments/adjustments", new { merchantId = otherMerchantId, operationId = operation.Id.ToString(), adjustmentType = "MerchantCredit", amount = 1m });
        var refundWithoutOperation = await PostPaymentJsonAsync(client, "/api/v1/payments/adjustments", new { merchantId, adjustmentType = "CashRefund", amount = 1m, notes = "Merchant account refund without a source sale" });

        Assert.Equal(HttpStatusCode.BadRequest, zeroAmount.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingMerchant.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrongMerchantOperation.StatusCode);
        Assert.Equal(HttpStatusCode.Created, refundWithoutOperation.StatusCode);
    }

    [Fact]
    public async Task SaleDraft_RequiresPositivePriceUnlessLineIsBonus()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead, LenseePermissions.PaymentsRead);

        var missingPrice = await client.PostAsJsonAsync("/api/v1/operations", new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "CashHandToHand",
            financeAccountId = await _factory.GetFinanceAccountIdAsync(),
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", unitPrice = 0, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });
        var bonus = await client.PostAsJsonAsync("/api/v1/operations", new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "CashHandToHand",
            financeAccountId = await _factory.GetFinanceAccountIdAsync(),
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
    public async Task ReserveWithRepresentative_IsRetiredAndCannotBeCreated()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var representativeId = await _factory.CreateRepresentativeAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var reserve = await client.PostAsJsonAsync("/api/v1/operations", new
        {
            operationType = "Reserve",
            sourceLocationId = seed.MainLocationId,
            representativeId,
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, entryMode = "Packs", lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, reserve.StatusCode);
    }

    [Fact]
    public async Task Return_ReceivesMerchantStockAndUpdatesBatchHistory()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead, LenseePermissions.PaymentsRead);

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
        var balanceBeforeReturn = await client.GetFromJsonAsync<MerchantBalanceContract>($"/api/v1/payments/merchants/{merchantId}/balance");
        var sourceLine = (await client.GetFromJsonAsync<IReadOnlyList<SourceSaleLineContract>>($"/api/v1/operations/source-sales/{sale.Id}/lines"))!.Single();
        var sourceBatchId = sourceLine.SourceBatches.Single().SourceBatchId;

        var returnOperation = await CreateOperationAsync(client, new
        {
            operationType = "Return",
            sourceLocationId = seed.MainLocationId,
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01", sourceOperationId = sale.Id, sourceOperationLineId = sourceLine.SourceOperationLineId, sourceBatchId } }
        });

        var confirm = await client.PostAsync($"/api/v1/operations/{returnOperation.Id}/confirm", null);
        var balanceAfterReturn = await client.GetFromJsonAsync<MerchantBalanceContract>($"/api/v1/payments/merchants/{merchantId}/balance");
        var main = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");
        var batchHistoryResponse = await client.GetAsync($"/api/v1/crm/merchants/{merchantId}/batch-history");
        var batchHistoryBody = await batchHistoryResponse.Content.ReadAsStringAsync();
        var batchHistory = JsonSerializer.Deserialize<IReadOnlyList<MerchantBatchHistoryContract>>(batchHistoryBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Equal(balanceBeforeReturn!.Balance - 200m, balanceAfterReturn!.Balance);
        Assert.Equal(200m, balanceAfterReturn.ReturnTotal);
        Assert.Contains(main!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 8);
        Assert.Contains(batchHistory!, row => row.SkuId == seed.SkuId && row.LotNumber == "MAIN-A" && row.ExpiryDate == new DateOnly(2028, 6, 1) && row.SoldQuantity == 4 && row.ReturnedQuantity == 2);
        Assert.DoesNotContain("eligib", batchHistoryBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("returnable", batchHistoryBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("overReturned", batchHistoryBody, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(LenseeRoles.Admin)]
    [InlineData(LenseeRoles.ERPAdmin)]
    public async Task MerchantReturnWithoutSaleLink_CanBeReceivedIntoANewBatch(string role)
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        await _factory.SeedCompletedMerchantSaleAsync(merchantId, seed.SkuId, "SOLD-BATCH", new DateOnly(2028, 6, 1), 4);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(role, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

        var operation = await CreateOperationAsync(client, new
        {
            operationType = "Return",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "MerchantAccount",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, entryMode = "Packs", unitPrice = 100, lotNumber = "NEW-RECEIVING-BATCH", expiryDate = "2028-07-01" } }
        });

        var confirm = await client.PostAsync($"/api/v1/operations/{operation.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
    }

    [Fact]
    public async Task ConfirmedMerchantReturn_CreditsSourceSaleAndReducesMerchantBalance()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        using var requester = _factory.CreateClient();
        requester.AuthorizeAs(LenseeRoles.Admin,
            LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead,
            LenseePermissions.PaymentsRead);

        var sale = await CreateOperationAsync(requester, new
        {
            operationType = "WholesaleSale",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "Installment",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });
        await requester.PostAsync($"/api/v1/operations/{sale.Id}/confirm", null);
        await requester.PostAsync($"/api/v1/operations/{sale.Id}/ship", null);
        await requester.PostAsync($"/api/v1/operations/{sale.Id}/receive", null);
        var sourceLine = (await requester.GetFromJsonAsync<IReadOnlyList<SourceSaleLineContract>>($"/api/v1/operations/source-sales/{sale.Id}/lines"))!.Single();
        var sourceBatchId = sourceLine.SourceBatches.Single().SourceBatchId;
        var returnOperation = await CreateOperationAsync(requester, new
        {
            operationType = "Return",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "MerchantAccount",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01", sourceOperationId = sale.Id, sourceOperationLineId = sourceLine.SourceOperationLineId, sourceBatchId } }
        });
        Assert.Equal(HttpStatusCode.NoContent, (await requester.PostAsync($"/api/v1/operations/{returnOperation.Id}/confirm", null)).StatusCode);
        var afterReturn = await requester.GetFromJsonAsync<MerchantBalanceContract>($"/api/v1/payments/merchants/{merchantId}/balance");
        var merchantOrders = await requester.GetFromJsonAsync<JsonElement[]>($"/api/v1/payments/merchant-accounts/{merchantId}/orders");
        var saleBalance = merchantOrders!.Single(value => value.GetProperty("operationId").GetGuid() == sale.Id);
        var closureEligible = await requester.GetFromJsonAsync<JsonElement[]>($"/api/v1/payments/merchant-accounts/{merchantId}/financial-closure/eligible");

        Assert.Equal(100m, afterReturn!.Balance);
        Assert.Equal(100m, afterReturn.ReturnTotal);
        Assert.Equal(100m, saleBalance.GetProperty("acceptedReturns").GetDecimal());
        Assert.Equal(100m, saleBalance.GetProperty("remaining").GetDecimal());
        Assert.Contains(closureEligible!, value => value.GetProperty("id").GetGuid() == sale.Id && value.GetProperty("remainingAmount").GetDecimal() == 0m);
    }

    [Fact]
    public async Task NonMerchantReturnWithoutSourceSale_IsRejected()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite);
        var response = await client.PostAsJsonAsync("/api/v1/operations", new
        {
            operationType = "Return",
            sourceLocationId = seed.MainLocationId,
            paymentMethod = "CashHandToHand",
            financeAccountId = await _factory.GetFinanceAccountIdAsync(),
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", unitPrice = 100, lotNumber = "NONMERCHANT", expiryDate = "2028-06-01" } }
        });

        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("non-merchant returned line", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonMerchantReturn_UsesFinalizedSaleLineAndCanReceiveAtAnotherLocation()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        await _factory.ReceiveMainStockAsync(seed.OnlineLocationId, seed.SkuId, "ONLINE-SALE", new DateOnly(2028, 6, 1), 2);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead, LenseePermissions.PaymentsRead);

        var sale = await CreateOperationAsync(client, new
        {
            operationType = "RetailSale",
            sourceLocationId = seed.OnlineLocationId,
            buyerName = "Walk-in return customer",
            paymentMethod = "CashHandToHand",
            financeAccountId = await _factory.GetFinanceAccountIdAsync(),
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, entryMode = "Packs", unitPrice = 100, lotNumber = "ONLINE-SALE", expiryDate = "2028-06-01" } }
        });
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/operations/{sale.Id}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/operations/{sale.Id}/ship", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/operations/{sale.Id}/complete", null)).StatusCode);
        var eligibleSaleLines = await client.GetFromJsonAsync<IReadOnlyList<SourceSaleLineContract>>($"/api/v1/operations/source-sales/{sale.Id}/lines");
        var saleLine = Assert.Single(eligibleSaleLines!);

        var returned = await CreateOperationAsync(client, new
        {
            operationType = "Return",
            sourceLocationId = seed.MainLocationId,
            buyerName = "Walk-in return customer",
            paymentMethod = "CashHandToHand",
            financeAccountId = await _factory.GetFinanceAccountIdAsync(),
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", unitPrice = 100, lotNumber = "RETURN-NEW-LOT", expiryDate = "2028-07-01", sourceOperationId = sale.Id, sourceOperationLineId = saleLine.SourceOperationLineId, sourceBatchId = saleLine.SourceBatches.Single().SourceBatchId } }
        });

        var confirm = await client.PostAsync($"/api/v1/operations/{returned.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        var paymentRows = await client.GetFromJsonAsync<JsonElement>("/api/v1/payments/other-payments?page=1&pageSize=50");
        var salePayment = Assert.Single(paymentRows.GetProperty("items").EnumerateArray().Where(value => value.GetProperty("operationId").GetGuid() == sale.Id));
        Assert.Equal(200m, salePayment.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(0m, salePayment.GetProperty("amountPaid").GetDecimal());
        Assert.Equal(100m, salePayment.GetProperty("balanceReductions").GetDecimal());
        Assert.Equal(100m, salePayment.GetProperty("remainingAmount").GetDecimal());
    }

    [Fact]
    public async Task MerchantExpiryRecall_ScanIsIdempotentAndRolesAreScoped()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        var expiry = _factory.GetEgyptToday().AddMonths(24);
        await _factory.SeedCompletedMerchantSaleAsync(merchantId, seed.SkuId, "RECALL-24", expiry, 3);

        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<MerchantExpiryRecallService>();
            var first = await service.ScanAsync();
            var second = await service.ScanAsync();
            Assert.Equal(1, first.CreatedRecalls);
            Assert.Equal(0, second.CreatedRecalls);
        }

        using var cLevel = _factory.CreateClient();
        cLevel.AuthorizeAs(LenseeRoles.CLevel, LenseePermissions.OperationsRead);
        var read = await cLevel.GetAsync("/api/v1/merchant-expiry-recalls?status=Active");
        var recalls = await read.Content.ReadFromJsonAsync<IReadOnlyList<MerchantExpiryRecallContract>>();
        var action = await cLevel.PostAsJsonAsync($"/api/v1/merchant-expiry-recalls/{recalls!.Single().Id}/no-stock", new { note = "Checked" });

        using var clerk = _factory.CreateClient();
        clerk.AuthorizeAs(LenseeRoles.WarehouseClerk, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite);
        var forbiddenRead = await clerk.GetAsync("/api/v1/merchant-expiry-recalls");

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, action.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenRead.StatusCode);
        Assert.Equal(3, await _factory.GetNotificationCountAsync(MerchantExpiryRecallService.AlertType));
    }

    [Fact]
    public async Task MerchantExpiryRecall_RespectsDisabledConfigAndIncludesTwentyFourMonthBoundary()
    {
        var seed = await _factory.SeedAsync();
        var merchantId = await _factory.CreateMerchantAsync();
        var today = _factory.GetEgyptToday();
        await _factory.SeedCompletedMerchantSaleAsync(merchantId, seed.SkuId, "BOUNDARY", today.AddMonths(24), 1);
        await _factory.SeedCompletedMerchantSaleAsync(merchantId, seed.SkuId, "OUTSIDE", today.AddMonths(24).AddDays(1), 1);

        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<MerchantExpiryRecallService>();
            await service.UpdateConfigAsync(24, "Months", false);
            var disabled = await service.ScanAsync();
            Assert.Equal(0, disabled.CreatedRecalls);

            await service.UpdateConfigAsync(24, "Months", true);
            var enabled = await service.ScanAsync();
            Assert.Equal(1, enabled.CreatedRecalls);
        }

        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead);
        var recalls = await client.GetFromJsonAsync<IReadOnlyList<MerchantExpiryRecallContract>>("/api/v1/merchant-expiry-recalls?status=Active");
        Assert.Single(recalls!);
        Assert.Equal("BOUNDARY", recalls!.Single().LotNumber);
    }

    [Fact]
    public async Task MerchantExpiryRecall_NoStockRequiresNoteAndReopensOnlyAfterNewSale()
    {
        var seed = await _factory.SeedAsync();
        var merchantId = await _factory.CreateMerchantAsync();
        var expiry = _factory.GetEgyptToday().AddMonths(6);
        await _factory.SeedCompletedMerchantSaleAsync(merchantId, seed.SkuId, "NO-STOCK", expiry, 2);
        await _factory.ScanMerchantExpiryRecallsAsync();

        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.ERPAdmin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite);
        var recalls = await client.GetFromJsonAsync<IReadOnlyList<MerchantExpiryRecallContract>>("/api/v1/merchant-expiry-recalls?status=Active");
        var recallId = recalls!.Single().Id;
        var missingNote = await client.PostAsJsonAsync($"/api/v1/merchant-expiry-recalls/{recallId}/no-stock", new { note = "" });
        var closed = await client.PostAsJsonAsync($"/api/v1/merchant-expiry-recalls/{recallId}/no-stock", new { note = "Merchant shelf and back room checked." });
        await _factory.ScanMerchantExpiryRecallsAsync();
        var stillClosed = await client.GetFromJsonAsync<IReadOnlyList<MerchantExpiryRecallContract>>("/api/v1/merchant-expiry-recalls?status=NoStock");

        await _factory.SeedCompletedMerchantSaleAsync(merchantId, seed.SkuId, "NO-STOCK", expiry, 1);
        await _factory.ScanMerchantExpiryRecallsAsync();
        var reopened = await client.GetFromJsonAsync<IReadOnlyList<MerchantExpiryRecallContract>>("/api/v1/merchant-expiry-recalls?status=Active");

        Assert.Equal(HttpStatusCode.BadRequest, missingNote.StatusCode);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Contains(stillClosed!, recall => recall.Id == recallId);
        Assert.Contains(reopened!, recall => recall.Id == recallId && recall.SoldQuantity == 3);
    }

    [Theory]
    [InlineData(LenseeRoles.Admin)]
    [InlineData(LenseeRoles.ERPAdmin)]
    public async Task MerchantReturn_AboveRecordedSalesCanBeOverriddenByOperationsAdmin(string role)
    {
        var seed = await _factory.SeedAsync();
        var merchantId = await _factory.CreateMerchantAsync();
        var expiry = _factory.GetEgyptToday().AddMonths(6);
        await _factory.SeedCompletedMerchantSaleAsync(merchantId, seed.SkuId, "RECALL-VARIANCE", expiry, 2);

        using var client = _factory.CreateClient();
        client.AuthorizeAs(role, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite);
        var draft = await CreateOperationAsync(client, new
        {
            operationType = "Return",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "MerchantAccount",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 3, entryMode = "Packs", unitPrice = 100, lotNumber = "NEW-PHYSICAL-LOT", expiryDate = expiry.ToString("yyyy-MM-dd") } }
        });
        var confirmation = await client.PostAsync($"/api/v1/operations/{draft.Id}/confirm", null);
        using var conflictDocument = JsonDocument.Parse(await confirmation.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, confirmation.StatusCode);
        Assert.Equal("MerchantSalesVariance", conflictDocument.RootElement.GetProperty("code").GetString());
        Assert.True(conflictDocument.RootElement.GetProperty("canBypass").GetBoolean());
        var warning = conflictDocument.RootElement.GetProperty("warnings")[0];
        Assert.Equal(2, warning.GetProperty("soldQuantity").GetInt32());
        Assert.Equal(3, warning.GetProperty("requestedQuantity").GetInt32());

        var missingReason = await client.PostAsJsonAsync($"/api/v1/operations/{draft.Id}/confirm", new
        {
            acknowledgeSalesVariance = true,
            salesVarianceReason = "   "
        });
        using var missingReasonDocument = JsonDocument.Parse(await missingReason.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, missingReason.StatusCode);
        Assert.True(missingReasonDocument.RootElement.GetProperty("canBypass").GetBoolean());

        var overrideResponse = await client.PostAsJsonAsync($"/api/v1/operations/{draft.Id}/confirm", new
        {
            acknowledgeSalesVariance = true,
            salesVarianceReason = "Physical merchant count verified by administrator."
        });
        Assert.True(overrideResponse.StatusCode == HttpStatusCode.NoContent, await overrideResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MerchantSalesVarianceOverride_IsDeniedToWarehouseClerk()
    {
        var seed = await _factory.SeedAsync();
        var merchantId = await _factory.CreateMerchantAsync();
        var expiry = _factory.GetEgyptToday().AddMonths(6);
        await _factory.SeedCompletedMerchantSaleAsync(merchantId, seed.SkuId, "CLERK-VARIANCE", expiry, 1);

        using var admin = _factory.CreateClient();
        admin.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite);
        var draft = await CreateOperationAsync(admin, new
        {
            operationType = "Return",
            sourceLocationId = seed.MainLocationId,
            merchantId,
            paymentMethod = "MerchantAccount",
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 2, entryMode = "Packs", unitPrice = 100, lotNumber = "CLERK-NEW-LOT", expiryDate = expiry.ToString("yyyy-MM-dd") } }
        });

        using var clerk = _factory.CreateClient();
        clerk.AuthorizeAsAtLocation(LenseeRoles.WarehouseClerk, seed.MainLocationId, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite);
        var response = await clerk.PostAsJsonAsync($"/api/v1/operations/{draft.Id}/confirm", new
        {
            acknowledgeSalesVariance = true,
            salesVarianceReason = "Attempted unauthorized override."
        });
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("MerchantSalesVariance", document.RootElement.GetProperty("code").GetString());
        Assert.False(document.RootElement.GetProperty("canBypass").GetBoolean());
    }

    [Fact]
    public async Task ExpiredMerchantRecallReturn_PostsReturnInAndWriteOffWithoutSellableStock()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        var merchantId = await _factory.CreateMerchantAsync();
        var expiry = _factory.GetEgyptToday().AddDays(-1);
        await _factory.SeedCompletedMerchantSaleAsync(merchantId, seed.SkuId, "EXPIRED-RETURN", expiry, 2);
        await _factory.ScanMerchantExpiryRecallsAsync();

        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);
        var recalls = await client.GetFromJsonAsync<IReadOnlyList<MerchantExpiryRecallContract>>("/api/v1/merchant-expiry-recalls?status=Active");
        var recall = recalls!.Single();
        var draftResponse = await client.PostAsJsonAsync($"/api/v1/merchant-expiry-recalls/{recall.Id}/return-draft", new { receivingLocationId = seed.MainLocationId, quantity = 1, notes = "Expired physical return" });
        var draft = await draftResponse.Content.ReadFromJsonAsync<MerchantRecallDraftContract>();
        var before = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");
        var confirm = await client.PostAsync($"/api/v1/operations/{draft!.OperationId}/confirm", null);
        var after = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");
        var partial = await client.GetFromJsonAsync<IReadOnlyList<MerchantExpiryRecallContract>>("/api/v1/merchant-expiry-recalls?status=Active");
        var secondDraftResponse = await client.PostAsJsonAsync($"/api/v1/merchant-expiry-recalls/{recall.Id}/return-draft", new { receivingLocationId = seed.MainLocationId, quantity = 1, notes = "Final expired physical return" });
        var secondDraft = await secondDraftResponse.Content.ReadFromJsonAsync<MerchantRecallDraftContract>();
        var secondConfirm = await client.PostAsync($"/api/v1/operations/{secondDraft!.OperationId}/confirm", null);
        var transactions = await _factory.GetInventoryTransactionTypesAsync(seed.SkuId);
        var completed = await client.GetFromJsonAsync<IReadOnlyList<MerchantExpiryRecallContract>>("/api/v1/merchant-expiry-recalls?status=Completed");

        Assert.Equal(HttpStatusCode.Created, draftResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Equal(before!.Items.Single(item => item.SkuId == seed.SkuId).AvailablePacks, after!.Items.Single(item => item.SkuId == seed.SkuId).AvailablePacks);
        Assert.Contains(partial!, item => item.Id == recall.Id && item.ReturnedQuantity == 1);
        Assert.Equal(HttpStatusCode.Created, secondDraftResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondConfirm.StatusCode);
        Assert.Equal(2, transactions.Count(value => value == InventoryTransactionTypes.ReturnIn));
        Assert.Equal(2, transactions.Count(value => value == InventoryTransactionTypes.WriteOff));
        Assert.Contains(completed!, item => item.Id == recall.Id);
    }

    [Fact]
    public async Task Change_CreationIsRetiredAndHistoricalRecordsRemainReadOnly()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite);
        var response = await client.PostAsJsonAsync("/api/v1/operations", new
        {
            operationType = "Change",
            sourceLocationId = seed.MainLocationId,
            lines = new[] { new { skuId = seed.SkuId, packQuantity = 1, entryMode = "Packs", unitPrice = 100, lotNumber = "MAIN-A", expiryDate = "2028-06-01" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("OperationType", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteOff_IsAvailableToErpAdminAndConsumesStock()
    {
        var seed = await _factory.SeedAsync(withMainStock: true);
        using var clerk = _factory.CreateClient();
        clerk.AuthorizeAsAtLocation(LenseeRoles.WarehouseClerk, seed.MainLocationId, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite);
        using var admin = _factory.CreateClient();
        admin.AuthorizeAs(LenseeRoles.ERPAdmin, LenseePermissions.OperationsRead, LenseePermissions.OperationsWrite, LenseePermissions.InventoryRead);

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
    public async Task StocktakeReads_AreScopedForWarehouseClerk_AndGlobalForAdmin()
    {
        var seed = await _factory.SeedAsync();
        using var admin = _factory.CreateClient();
        admin.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.InventoryRead, LenseePermissions.InventoryWrite);

        var ownCreate = await admin.PostAsJsonAsync("/api/v1/stocktakes", new { locationId = seed.MainLocationId });
        var otherCreate = await admin.PostAsJsonAsync("/api/v1/stocktakes", new { locationId = seed.OnlineLocationId });
        var own = await ownCreate.Content.ReadFromJsonAsync<StocktakeDetailContract>();
        var other = await otherCreate.Content.ReadFromJsonAsync<StocktakeDetailContract>();

        using var clerk = _factory.CreateClient();
        clerk.AuthorizeAsAtLocation(LenseeRoles.WarehouseClerk, seed.MainLocationId, LenseePermissions.InventoryRead);
        var list = await clerk.GetFromJsonAsync<PagedContract<StocktakeListContract>>("/api/v1/stocktakes?pageSize=25");
        var ownRead = await clerk.GetAsync($"/api/v1/stocktakes/{own!.Id}");
        var foreignRead = await clerk.GetAsync($"/api/v1/stocktakes/{other!.Id}");
        var adminList = await admin.GetFromJsonAsync<PagedContract<StocktakeListContract>>("/api/v1/stocktakes?pageSize=25");
        var adminForeignRead = await admin.GetAsync($"/api/v1/stocktakes/{other.Id}");

        Assert.Equal(HttpStatusCode.Created, ownCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, otherCreate.StatusCode);
        Assert.NotNull(list);
        Assert.Equal(own.Id, Assert.Single(list!.Items).Id);
        Assert.Equal(HttpStatusCode.OK, ownRead.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
        Assert.NotNull(adminList);
        Assert.Contains(adminList!.Items, item => item.Id == own.Id);
        Assert.Contains(adminList.Items, item => item.Id == other.Id);
        Assert.Equal(HttpStatusCode.OK, adminForeignRead.StatusCode);
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
            lines = new[] { new { skuId = seed.SkuId, physicalCount = 1, physicalPackCount = 1, physicalPieceCount = 2 } }
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
            lines = new[] { new { skuId = seed.SkuId, physicalCount = 1, physicalPackCount = 1, physicalPieceCount = 2 } }
        });
        var invalid = await client.PutAsJsonAsync($"/api/v1/stocktakes/{stocktake.Id}/lines", new
        {
            lines = new[] { new { skuId = seed.SkuId, physicalCount = 1, physicalPackCount = -1, physicalPieceCount = 0 } }
        });
        var detail = await client.GetFromJsonAsync<StocktakeDetailContract>($"/api/v1/stocktakes/{stocktake.Id}");

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var line = Assert.Single(detail!.Lines);
        Assert.Equal(seed.SkuId, line.SkuId);
        Assert.Equal(1, line.PhysicalCount);
        Assert.Equal(1, line.PhysicalPackCount);
        Assert.Equal(2, line.PhysicalPieceCount);
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
    public async Task SupplyDraft_AllowsArrivalWithoutOperationalPricingAndArrivalDoesNotPostStock()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.SupplyRead, LenseePermissions.SupplyWrite);

        var create = await client.PostAsJsonAsync("/api/v1/supply/shipments", new
        {
            supplierName = "Imported Supplier",
            destinationLocationId = seed.MainLocationId,
            lines = new[] { new { skuId = seed.SkuId, quantity = 5, lotNumber = "LOT-ARRIVAL", expiryDate = "2028-06-01" } }
        });
        var createBody = await create.Content.ReadAsStringAsync();
        using var created = JsonDocument.Parse(createBody);
        var shipmentId = created.RootElement.GetProperty("id").GetGuid();

        var confirm = await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/confirm", null);
        var detail = await client.GetFromJsonAsync<SupplyShipmentContract>($"/api/v1/supply/shipments/{shipmentId}");

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Equal("Arrived", detail!.Status);
        Assert.Null(detail.InventoryReceiptOperationId);
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
    public async Task SupplyCreate_RejectsDuplicateLinesAndInvalidQuantities()
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
                new { skuId = seed.SkuId, quantity = 1, lotNumber = "LOT-A", expiryDate = "2028-06-01" },
                new { skuId = seed.SkuId, quantity = 2, lotNumber = "LOT-A", expiryDate = "2028-06-01" }
            }
        });
        var body = await response.Content.ReadFromJsonAsync<ValidationProblemContract>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Lines[1]", body.Errors.Keys);
    }

    [Fact]
    public async Task SupplyConfirmAndPhysicalReceivingCreateInventoryReceipt()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.SupplyRead, LenseePermissions.SupplyWrite, LenseePermissions.InventoryRead);

        var create = await client.PostAsJsonAsync("/api/v1/supply/shipments", new
        {
            supplierName = "Imported Supplier",
            invoiceNumber = "IMP-1",
            destinationLocationId = seed.MainLocationId,
            lines = new[] { new { skuId = seed.SkuId, quantity = 5, lotNumber = "LOT-RECEIVE", expiryDate = "2028-06-01" } }
        });
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var shipmentId = created.RootElement.GetProperty("id").GetGuid();

        var update = await client.PutAsJsonAsync($"/api/v1/supply/shipments/{shipmentId}", new
        {
            supplierName = "Imported Supplier",
            invoiceNumber = "IMP-1",
            destinationLocationId = seed.MainLocationId,
            lines = new[] { new { skuId = seed.SkuId, quantity = 5, lotNumber = "LOT-RECEIVE", expiryDate = "2028-06-01" } }
        });
        var confirm = await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/confirm", null);
        var arrivedDetail = await client.GetFromJsonAsync<SupplyShipmentContract>($"/api/v1/supply/shipments/{shipmentId}");
        var createSession = await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions", null);
        using var receiving = JsonDocument.Parse(await createSession.Content.ReadAsStringAsync());
        var sessionId = receiving.RootElement.GetProperty("id").GetGuid();
        var shipmentLineId = receiving.RootElement.GetProperty("lines")[0].GetProperty("shipmentLineId").GetGuid();
        var saveLines = await client.PutAsJsonAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions/{sessionId}/lines", new[] { new { shipmentLineId, receivedQuantity = 5, lotNumber = "LOT-RECEIVE", expiryDate = "2028-06-01" } });
        var receive = await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions/{sessionId}/confirm", null);
        var detail = await client.GetFromJsonAsync<SupplyShipmentContract>($"/api/v1/supply/shipments/{shipmentId}");
        var balances = await client.GetFromJsonAsync<PagedContract<OperationStockBalanceContract>>($"/api/v1/inventory/stock-balances?locationId={seed.MainLocationId}");
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Equal("Arrived", arrivedDetail!.Status);
        Assert.Equal(HttpStatusCode.Created, createSession.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, saveLines.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, receive.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var receivingSession = await operations.SupplyReceivingSessions.SingleAsync(value => value.Id == sessionId);
        var receiptOperation = await operations.OperationLogs
            .Include(value => value.OperationVersions)
            .SingleAsync(value => value.Id == receivingSession.InventoryReceiptOperationId);

        Assert.Equal("Received", detail!.Status);
        Assert.Contains(balances!.Items, balance => balance.SkuId == seed.SkuId && balance.AvailablePacks == 5);
        Assert.Contains(await _factory.GetInventoryTransactionTypesAsync(seed.SkuId), transactionType => transactionType == InventoryTransactionTypes.SupplyIn);
        Assert.Single(receiptOperation.OperationVersions);
        Assert.Equal(receiptOperation.OperationVersions.Single().Id, receiptOperation.CurrentVersionId);
    }

    [Fact]
    public async Task SupplyReceiving_RevalidatesActiveSkuState()
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

        await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/confirm", null);
        var createSession = await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions", null);
        using var session = JsonDocument.Parse(await createSession.Content.ReadAsStringAsync());
        var sessionId = session.RootElement.GetProperty("id").GetGuid();
        var shipmentLineId = session.RootElement.GetProperty("lines")[0].GetProperty("shipmentLineId").GetGuid();
        await client.PutAsJsonAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions/{sessionId}/lines", new[] { new { shipmentLineId, receivedQuantity = 5 } });
        var confirm = await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions/{sessionId}/confirm", null);

        Assert.Equal(HttpStatusCode.BadRequest, confirm.StatusCode);
    }

    [Fact]
    public async Task SupplyReceiving_PartialReceiptTracksExactOutstandingLineageAndResumesToCompletion()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.SupplyRead, LenseePermissions.SupplyWrite, LenseePermissions.InventoryRead);

        var create = await client.PostAsJsonAsync("/api/v1/supply/shipments", new
        {
            supplierName = "Partial receipt supplier",
            destinationLocationId = seed.MainLocationId,
            lines = new[] { new { skuId = seed.SkuId, quantity = 5, unitPrice = (decimal?)100m, lotNumber = "LOT-PARTIAL", expiryDate = "2028-06-01" } },
            costs = Array.Empty<object>()
        });
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var shipmentId = created.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/confirm", null)).StatusCode);

        var first = await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions", null);
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var firstSessionId = firstJson.RootElement.GetProperty("id").GetGuid();
        var shipmentLineId = firstJson.RootElement.GetProperty("lines")[0].GetProperty("shipmentLineId").GetGuid();
        var duplicateStart = await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions", null);
        Assert.Equal(HttpStatusCode.OK, duplicateStart.StatusCode);
        using (var duplicateSession = JsonDocument.Parse(await duplicateStart.Content.ReadAsStringAsync()))
        {
            Assert.Equal(firstSessionId, duplicateSession.RootElement.GetProperty("id").GetGuid());
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions/{firstSessionId}/lines", new[] { new { shipmentLineId, receivedQuantity = 2 } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions/{firstSessionId}/confirm", null)).StatusCode);

        var partial = await client.GetFromJsonAsync<SupplyShipmentContract>($"/api/v1/supply/shipments/{shipmentId}");
        Assert.Equal("PartiallyReceived", partial!.Status);
        Assert.Equal(2, partial.Receiving.Quantities.Single().CumulativeReceived);
        Assert.Equal(3, partial.Receiving.Quantities.Single().OutstandingQuantity);
        Assert.Single(partial.Receiving.History);

        var second = await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions", null);
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var secondSessionId = secondJson.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions/{secondSessionId}/lines", new[] { new { shipmentLineId, receivedQuantity = 3 } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions/{secondSessionId}/confirm", null)).StatusCode);

        var completed = await client.GetFromJsonAsync<SupplyShipmentContract>($"/api/v1/supply/shipments/{shipmentId}");
        Assert.Equal("Received", completed!.Status);
        Assert.Equal(5, completed.Receiving.Quantities.Single().CumulativeReceived);
        Assert.Equal(0, completed.Receiving.Quantities.Single().OutstandingQuantity);
        Assert.Equal(2, completed.Receiving.History.Count);
        Assert.All(completed.Receiving.History, receipt => Assert.NotNull(receipt.InventoryReceiptOperationId));
    }

    [Fact]
    public async Task SupplyDetailSummary_IncludesOpenReceivingSessionForReceivingWorkspace()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.SupplyRead, LenseePermissions.SupplyWrite, LenseePermissions.SupplyReceive);

        var create = await client.PostAsJsonAsync("/api/v1/supply/shipments", new
        {
            supplierName = "Receiving summary supplier",
            destinationLocationId = seed.MainLocationId,
            lines = new[] { new { skuId = seed.SkuId, quantity = 3 } }
        });
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var shipmentId = created.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/confirm", null)).StatusCode);
        var started = await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions", null);
        using var session = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        var sessionId = session.RootElement.GetProperty("id").GetGuid();

        var summary = await client.GetFromJsonAsync<SupplyShipmentContract>($"/api/v1/supply/shipments/{shipmentId}?includeCollections=false");

        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        Assert.Empty(summary!.Lines);
        Assert.Single(summary.Receiving.Quantities);
        Assert.Contains(summary.Receiving.OpenSessions, open => open.Id == sessionId);
    }

    [Fact]
    public async Task Supply_WarehouseClerkCanDiscoverAndReceiveOnlyAssignedLocationShipment()
    {
        var seed = await _factory.SeedAsync();
        using var admin = _factory.CreateClient();
        admin.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.SupplyRead, LenseePermissions.SupplyWrite);
        var create = await admin.PostAsJsonAsync("/api/v1/supply/shipments", new
        {
            supplierName = "Assigned warehouse supplier",
            destinationLocationId = seed.MainLocationId,
            lines = new[] { new { skuId = seed.SkuId, quantity = 2 } }
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var shipmentId = created.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/v1/supply/shipments/{shipmentId}/confirm", null)).StatusCode);

        using var clerk = _factory.CreateClient();
        clerk.AuthorizeAsAtLocation(LenseeRoles.WarehouseClerk, seed.MainLocationId, LenseePermissions.SupplyRead, LenseePermissions.SupplyReceive);
        var list = await clerk.GetFromJsonAsync<IReadOnlyList<JsonElement>>("/api/v1/supply/shipments");
        var detail = await clerk.GetAsync($"/api/v1/supply/shipments/{shipmentId}?includeCollections=false");
        var sessionResponse = await clerk.PostAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions", null);
        using var session = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync());
        var sessionId = session.RootElement.GetProperty("id").GetGuid();
        var lineId = session.RootElement.GetProperty("lines")[0].GetProperty("shipmentLineId").GetGuid();
        var save = await clerk.PutAsJsonAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions/{sessionId}/lines", new[] { new { shipmentLineId = lineId, receivedQuantity = 2, lotNumber = "CLERK-LOT", expiryDate = "2028-06-01", notes = "Verified at receiving" } });
        var confirm = await clerk.PostAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions/{sessionId}/confirm", null);

        Assert.Contains(list!, row => row.GetProperty("id").GetGuid() == shipmentId);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.Created, sessionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, save.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);

        using var otherClerk = _factory.CreateClient();
        otherClerk.AuthorizeAsAtLocation(LenseeRoles.WarehouseClerk, seed.OnlineLocationId, LenseePermissions.SupplyRead, LenseePermissions.SupplyReceive);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherClerk.GetAsync($"/api/v1/supply/shipments/{shipmentId}")).StatusCode);
        var otherLocationList = await otherClerk.GetFromJsonAsync<IReadOnlyList<JsonElement>>("/api/v1/supply/shipments");
        Assert.DoesNotContain(otherLocationList!, row => row.GetProperty("id").GetGuid() == shipmentId);

        using var financeForbidden = _factory.CreateClient();
        financeForbidden.AuthorizeAsAtLocation(LenseeRoles.WarehouseClerk, seed.MainLocationId, LenseePermissions.SupplyRead, LenseePermissions.SupplyReceive);
        Assert.Equal(HttpStatusCode.Forbidden, (await financeForbidden.GetAsync($"/api/v1/finance/supply-logs/by-shipment/{shipmentId}")).StatusCode);
    }

    [Fact]
    public async Task SupplyReceiving_SavingOnePageDoesNotResetOmittedSessionLines()
    {
        var seed = await _factory.SeedAsync();
        using var client = _factory.CreateClient();
        client.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.SupplyRead, LenseePermissions.SupplyWrite);
        var created = await client.PostAsJsonAsync("/api/v1/supply/shipments", new { supplierName = "Paged supplier", destinationLocationId = seed.MainLocationId, lines = new[] { new { skuId = seed.SkuId, quantity = 4 } } });
        using var shipmentJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var shipmentId = shipmentJson.RootElement.GetProperty("id").GetGuid();
        await client.PostAsync($"/api/v1/supply/shipments/{shipmentId}/confirm", null);
        using var receiver = _factory.CreateClient();
        receiver.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.SupplyRead, LenseePermissions.SupplyReceive);
        var sessionResponse = await receiver.PostAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions", null);
        using var session = JsonDocument.Parse(await sessionResponse.Content.ReadAsStringAsync());
        var sessionId = session.RootElement.GetProperty("id").GetGuid();
        var lineId = session.RootElement.GetProperty("lines")[0].GetProperty("shipmentLineId").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await receiver.PutAsJsonAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions/{sessionId}/lines", new[] { new { shipmentLineId = lineId, receivedQuantity = 2, lotNumber = "PAGE-1", expiryDate = "2028-06-01" } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await receiver.PutAsJsonAsync($"/api/v1/supply/shipments/{shipmentId}/receiving-sessions/{sessionId}/lines", Array.Empty<object>())).StatusCode);
        using var scope = _factory.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        Assert.Equal(2, await operations.SupplyReceivingLines.Where(line => line.ReceivingSessionId == sessionId && line.ShipmentLineId == lineId).Select(line => line.ReceivedQuantity).SingleAsync());
    }

    [Fact]
    public async Task SupplyFinance_CostCorrectionPreservesHistoryAndRequiresReason()
    {
        var seed = await _factory.SeedAsync();
        using var admin = _factory.CreateClient();
        admin.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.SupplyRead, LenseePermissions.SupplyWrite, LenseePermissions.FinanceRead, LenseePermissions.FinanceExpenseCreate);
        var created = await admin.PostAsJsonAsync("/api/v1/supply/shipments", new { supplierName = "Finance correction supplier", destinationLocationId = seed.MainLocationId, lines = new[] { new { skuId = seed.SkuId, quantity = 1 } } });
        using var shipment = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var shipmentId = shipment.RootElement.GetProperty("id").GetGuid();
        var log = await admin.GetAsync($"/api/v1/finance/supply-logs/by-shipment/{shipmentId}");
        using var logJson = JsonDocument.Parse(await log.Content.ReadAsStringAsync());
        var logId = logJson.RootElement.GetProperty("id").GetGuid();
        var costResponse = await admin.PostAsJsonAsync($"/api/v1/finance/supply-logs/{logId}/costs", new { category = "ProductCost", amount = 500m, businessDate = "2026-09-26", notes = "Original cost" });
        using var costJson = JsonDocument.Parse(await costResponse.Content.ReadAsStringAsync());
        var costId = costJson.RootElement.GetProperty("id").GetGuid();

        var missingReason = await admin.PostAsJsonAsync($"/api/v1/finance/supply-logs/{logId}/costs/{costId}/correct", new { category = "ProductCost", amount = 450m, businessDate = "2026-09-26", notes = "Replacement" });
        var correction = await admin.PostAsJsonAsync($"/api/v1/finance/supply-logs/{logId}/costs/{costId}/correct", new { category = "ProductCost", amount = 450m, businessDate = "2026-09-26", notes = "Replacement", reason = "Factory invoice updated" });

        Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);
        Assert.Equal(HttpStatusCode.Created, correction.StatusCode);
        var refreshed = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/finance/supply-logs/{logId}");
        Assert.Equal(450m, refreshed.GetProperty("totalLandedCost").GetDecimal());
        var costs = refreshed.GetProperty("costs").EnumerateArray().ToArray();
        Assert.Equal(2, costs.Length);
        Assert.Contains(costs, item => item.GetProperty("id").GetGuid() == costId && item.GetProperty("status").GetString() == "Corrected");
        Assert.Contains(costs, item => item.GetProperty("reversesCostEntryId").ValueKind == JsonValueKind.String && item.GetProperty("reversesCostEntryId").GetGuid() == costId && item.GetProperty("correctionNote").GetString() == "Factory invoice updated");
    }

    [Fact]
    public async Task SupplyFinance_InstallmentCreationRejectsMethodThatDoesNotMatchAccount()
    {
        var seed = await _factory.SeedAsync();
        var bankAccountId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            finance.FinanceAccounts.Add(new FinanceAccount
            {
                Id = bankAccountId,
                Name = "Supply payment bank",
                Type = FinanceLedgerService.BankAccount,
                IsActive = true,
                CreatedBy = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow
            });
            await finance.SaveChangesAsync();
        }

        using var admin = _factory.CreateClient();
        admin.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.SupplyRead, LenseePermissions.SupplyWrite, LenseePermissions.FinanceRead, LenseePermissions.FinanceExpenseCreate);
        var created = await admin.PostAsJsonAsync("/api/v1/supply/shipments", new { supplierName = "Installment validation supplier", destinationLocationId = seed.MainLocationId, lines = new[] { new { skuId = seed.SkuId, quantity = 1 } } });
        using var shipment = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var shipmentId = shipment.RootElement.GetProperty("id").GetGuid();
        var log = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/finance/supply-logs/by-shipment/{shipmentId}");
        var response = await admin.PostAsJsonAsync($"/api/v1/finance/supply-logs/{log.GetProperty("id").GetGuid()}/installments", new
        {
            financeAccountId = bankAccountId,
            amount = 100m,
            movementMethod = "CashHandToHand",
            businessDate = "2026-09-27"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SupplyFinance_InstallmentPostAndCorrectionPreserveLedgerLineage()
    {
        var seed = await _factory.SeedAsync();
        using var admin = _factory.CreateClient();
        admin.AuthorizeAs(LenseeRoles.Admin, LenseePermissions.SupplyRead, LenseePermissions.SupplyWrite, LenseePermissions.FinanceRead, LenseePermissions.FinanceExpenseCreate);
        var created = await admin.PostAsJsonAsync("/api/v1/supply/shipments", new { supplierName = "Installment lifecycle supplier", destinationLocationId = seed.MainLocationId, lines = new[] { new { skuId = seed.SkuId, quantity = 1 } } });
        using var shipment = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var shipmentId = shipment.RootElement.GetProperty("id").GetGuid();
        var log = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/finance/supply-logs/by-shipment/{shipmentId}");
        var logId = log.GetProperty("id").GetGuid();
        var accountId = await _factory.GetFinanceAccountIdAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
            finance.FinanceLedgerEntries.Add(new FinanceLedgerEntry
            {
                Id = Guid.NewGuid(),
                FinanceAccountId = accountId,
                Direction = FinanceLedgerService.Credit,
                Amount = 1000m,
                Category = "TreasuryOpeningBalance",
                MovementMethod = "FinanceOpeningBalance",
                SourceType = "SupplyFinanceLifecycleTest",
                SourceId = Guid.NewGuid(),
                BusinessDate = new DateOnly(2026, 9, 27),
                Status = "Posted",
                CreatedBy = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow
            });
            await finance.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync($"/api/v1/finance/supply-logs/{logId}/costs", new { category = "SupplierPurchase", amount = 500m, businessDate = "2026-09-27" })).StatusCode);

        var installment = await admin.PostAsJsonAsync($"/api/v1/finance/supply-logs/{logId}/installments", new { financeAccountId = accountId, amount = 200m, movementMethod = "CashHandToHand", businessDate = "2026-09-27" });
        using var installmentJson = JsonDocument.Parse(await installment.Content.ReadAsStringAsync());
        var installmentId = installmentJson.RootElement.GetProperty("id").GetGuid();
        var posted = await admin.PostAsync($"/api/v1/finance/supply-logs/{logId}/installments/{installmentId}/post", null);
        var correction = await admin.PostAsJsonAsync($"/api/v1/finance/supply-logs/{logId}/installments/{installmentId}/correct", new { financeAccountId = accountId, amount = 150m, movementMethod = "CashHandToHand", businessDate = "2026-09-27", reason = "Supplier payment corrected" });

        Assert.Equal(HttpStatusCode.Created, installment.StatusCode);
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
        Assert.Equal(HttpStatusCode.Created, correction.StatusCode);
        using var correctionJson = JsonDocument.Parse(await correction.Content.ReadAsStringAsync());
        Assert.Equal("Draft", correctionJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(installmentId, correctionJson.RootElement.GetProperty("reversesInstallmentId").GetGuid());
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

    private async Task<OperationDetailContract> CreateOperationAsync(HttpClient client, object request)
    {
        // Merchant immediate-sale fixtures must model the production contract:
        // a real movement selects an active receiving Finance account.
        var payload = JsonSerializer.SerializeToNode(request)!.AsObject();
        if (payload["merchantId"] is not null && payload["paymentMethod"]?.GetValue<string>() is "Installment" or "Installlaugment")
            payload["paymentMethod"] = null;
        if (payload["financeAccountId"] is null &&
            payload["paymentMethod"]?.GetValue<string>() is "CashHandToHand" or "CashTransaction" or "BankTransfer" or "Wallet")
        {
            payload["financeAccountId"] = await _factory.GetFinanceAccountIdAsync();
        }
        var response = await client.PostAsJsonAsync("/api/v1/operations", payload);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"Expected Created but got {response.StatusCode}: {body}");
        return (await response.Content.ReadFromJsonAsync<OperationDetailContract>())!;
    }
}

public sealed class OperationsEndpointFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"operations-contracts-{Guid.NewGuid()}";
    private readonly Guid _shopifyOnlineLocationId = Guid.NewGuid();
    private readonly bool _useRealAuditWriter;
    public const string ShopifyWebhookSecret = "ShopifyContractWebhookSecret123!";
    public const string ShopifyLegacyWebhookPathSecret = "ShopifyContractLegacyPathSecret123456";
    public const string ShopifyStoreDomain = "lensee-contracts.myshopify.com";

    public OperationsEndpointFactory()
        : this(useRealAuditWriter: false)
    {
    }

    internal OperationsEndpointFactory(bool useRealAuditWriter) => _useRealAuditWriter = useRealAuditWriter;

    public Guid ShopifyOnlineLocationId => _shopifyOnlineLocationId;

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
                ["Jwt:Audience"] = "Lensee.App",
                ["Shopify:Enabled"] = "true",
                ["Shopify:WebhookSecret"] = ShopifyWebhookSecret,
                ["Shopify:LegacyWebhookPathSecret"] = ShopifyLegacyWebhookPathSecret,
                ["Shopify:OnlineLocationId"] = _shopifyOnlineLocationId.ToString(),
                ["Shopify:StoreDomain"] = ShopifyStoreDomain,
                ["Shopify:CodGatewayNames:0"] = "Cash on Delivery"
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
            services.RemoveAll<DbContextOptions<FinanceDbContext>>();
            services.RemoveAll<DbContextOptions<SharedDbContext>>();
            services.RemoveAll<IAuditLogWriter>();
            services.AddDbContext<CatalogDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<CrmDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<IdentityDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<InventoryDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<NotificationsDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<OperationsDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<PaymentsDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<FinanceDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            services.AddDbContext<SharedDbContext>(options => options.UseInMemoryDatabase(_databaseName));
            if (_useRealAuditWriter)
            {
                services.AddScoped<IAuditLogWriter, AuditLogWriter>();
            }
            else
            {
                services.AddSingleton<IAuditLogWriter, NoOpAuditLogWriter>();
            }

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
        var finance = scope.ServiceProvider.GetRequiredService<FinanceDbContext>();
        var ledger = scope.ServiceProvider.GetRequiredService<StockLedgerService>();
        var mainLocationId = Guid.NewGuid();
        var onlineLocationId = _shopifyOnlineLocationId;
        var categoryId = Guid.NewGuid();
        var brandId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var skuId = Guid.NewGuid();

        operations.SupplyShipmentHistoryLogs.RemoveRange(operations.SupplyShipmentHistoryLogs);
        operations.SupplyShipmentCosts.RemoveRange(operations.SupplyShipmentCosts);
        operations.SupplyShipmentLines.RemoveRange(operations.SupplyShipmentLines);
        operations.SupplyShipments.RemoveRange(operations.SupplyShipments);
        operations.OperationVersions.RemoveRange(operations.OperationVersions);
        operations.ShopifyOrderLinks.RemoveRange(operations.ShopifyOrderLinks);
        operations.ShopifyWebhookEvents.RemoveRange(operations.ShopifyWebhookEvents);
        operations.OperationLines.RemoveRange(operations.OperationLines);
        operations.InventoryReceiptHeaders.RemoveRange(operations.InventoryReceiptHeaders);
        operations.OperationLogs.RemoveRange(operations.OperationLogs);
        operations.MerchantExpiryRecalls.RemoveRange(operations.MerchantExpiryRecalls);
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
        // This class shares one in-memory factory.  Financial assertions must start
        // from an empty ledger rather than inherit unrelated prior test effects.
        finance.FinanceLedgerEntries.RemoveRange(finance.FinanceLedgerEntries);
        finance.FinanceOpeningBalances.RemoveRange(finance.FinanceOpeningBalances);
        finance.FinanceExpenses.RemoveRange(finance.FinanceExpenses);
        finance.CLevelWithdrawals.RemoveRange(finance.CLevelWithdrawals);
        finance.PaymentMovementRegistries.RemoveRange(finance.PaymentMovementRegistries);
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
        await finance.SaveChangesAsync();
        if (!await finance.FinanceAccounts.AnyAsync())
        {
            finance.FinanceAccounts.Add(new FinanceAccount
            {
                Id = Guid.NewGuid(),
                Name = "Test Cash",
                Type = "CashOnHand",
                IsActive = true,
                CreatedBy = Guid.NewGuid(),
                CreatedAt = DateTime.UtcNow
            });
            await finance.SaveChangesAsync();
        }
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
            OpenedExpiryRate = "Monthly",
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

    public async Task<Guid> GetFinanceAccountIdAsync()
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FinanceDbContext>().FinanceAccounts
            .Where(value => value.IsActive)
            .Select(value => value.Id)
            .FirstAsync();
    }

    public async Task<LocationScopedCrmCorrectionSeed> SeedLocationScopedCrmAndCorrectionsAsync(OperationsSeed seed)
    {
        using var scope = Services.CreateScope();
        var crm = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var now = DateTime.UtcNow;
        var ownMerchantId = Guid.NewGuid();
        var foreignMerchantId = Guid.NewGuid();
        var ownRepresentativeId = Guid.NewGuid();
        var foreignRepresentativeId = Guid.NewGuid();
        var ownOperationId = Guid.NewGuid();
        var foreignOperationId = Guid.NewGuid();
        var ownProposalId = Guid.NewGuid();
        var foreignProposalId = Guid.NewGuid();

        crm.Merchants.AddRange(
            new Merchant
            {
                Id = ownMerchantId,
                BusinessName = "Main location merchant",
                ContactPersonName = "Main contact",
                PhoneNumbers = [],
                BusinessType = "Merchant",
                Status = "Active",
                CreatedAt = now,
                UpdatedAt = now
            },
            new Merchant
            {
                Id = foreignMerchantId,
                BusinessName = "Online location merchant",
                ContactPersonName = "Online contact",
                PhoneNumbers = [],
                BusinessType = "Merchant",
                Status = "Active",
                CreatedAt = now,
                UpdatedAt = now
            });
        crm.Representatives.AddRange(
            new Representative
            {
                Id = ownRepresentativeId,
                Name = "Main location representative",
                PhoneNumbers = [],
                Type = "External",
                AssignedLocationId = seed.MainLocationId,
                Status = "Active"
            },
            new Representative
            {
                Id = foreignRepresentativeId,
                Name = "Online location representative",
                PhoneNumbers = [],
                Type = "External",
                AssignedLocationId = seed.OnlineLocationId,
                Status = "Active"
            });
        operations.OperationLogs.AddRange(
            new OperationLog
            {
                Id = ownOperationId,
                OperationNumber = "OP-SCOPE-MAIN",
                OperationType = "RetailSale",
                Status = "Completed",
                SourceLocationId = seed.MainLocationId,
                ClientId = ownMerchantId,
                CreatedBy = Guid.NewGuid(),
                CreatedAt = now
            },
            new OperationLog
            {
                Id = foreignOperationId,
                OperationNumber = "OP-SCOPE-ONLINE",
                OperationType = "RetailSale",
                Status = "Completed",
                SourceLocationId = seed.OnlineLocationId,
                ClientId = foreignMerchantId,
                CreatedBy = Guid.NewGuid(),
                CreatedAt = now
            });
        operations.OperationCorrectionProposals.AddRange(
            new OperationCorrectionProposal
            {
                Id = ownProposalId,
                OperationId = ownOperationId,
                Status = "PendingApproval",
                Reason = "Main location correction",
                RequesterId = Guid.NewGuid(),
                RequestedAt = now
            },
            new OperationCorrectionProposal
            {
                Id = foreignProposalId,
                OperationId = foreignOperationId,
                Status = "PendingApproval",
                Reason = "Online location correction",
                RequesterId = Guid.NewGuid(),
                RequestedAt = now
            });

        await crm.SaveChangesAsync();
        await operations.SaveChangesAsync();
        return new LocationScopedCrmCorrectionSeed(
            ownMerchantId,
            foreignMerchantId,
            ownRepresentativeId,
            foreignRepresentativeId,
            ownOperationId,
            foreignOperationId,
            ownProposalId,
            foreignProposalId);
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

    public async Task<Guid> CreateFinalizedWholesaleSaleAsync(OperationsSeed seed)
    {
        using var scope = Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var operationId = Guid.NewGuid();
        operations.OperationLogs.Add(new OperationLog
        {
            Id = operationId,
            OperationNumber = $"TEST-CORRECTION-{operationId:N}",
            OperationType = "WholesaleSale",
            Status = "Completed",
            SourceLocationId = seed.MainLocationId,
            ClientId = Guid.NewGuid(),
            ClientName = "Correction merchant",
            CreatedBy = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            ConfirmedBy = Guid.NewGuid(),
            ConfirmedAt = DateTime.UtcNow,
            OperationLines =
            [
                new OperationLine
                {
                    Id = Guid.NewGuid(),
                    SkuId = seed.SkuId,
                    SkuCodeSnapshot = "CORRECTION-SKU",
                    ProductNameSnapshot = "Correction product",
                    Section = "Standard",
                    Quantity = 2,
                    EntryMode = "Packs",
                    LotNumber = "CORRECTION-LOT",
                    ExpiryDate = new DateOnly(2028, 6, 1)
                }
            ]
        });
        await operations.SaveChangesAsync();
        return operationId;
    }

    public async Task<int> CountPaymentSubLogsAsync(Guid paymentLogId)
    {
        using var scope = Services.CreateScope();
        var payments = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        return await payments.InstallmentSubLogs.CountAsync(value => value.MainLogId == paymentLogId);
    }

    public async Task<IReadOnlyList<string>> GetOperationAuditActionsAsync(Guid operationId)
    {
        using var scope = Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await identity.AuditLogs
            .Where(value => value.EntityType == "Operation" && value.EntityId == operationId)
            .Select(value => value.Action)
            .ToListAsync();
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

    public async Task SeedCompletedMerchantSaleAsync(Guid merchantId, Guid skuId, string lotNumber, DateOnly expiryDate, int quantity)
    {
        using var scope = Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<OperationsDbContext>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var crm = scope.ServiceProvider.GetRequiredService<CrmDbContext>();
        var sku = await catalog.Skus.Include(value => value.Product).SingleAsync(value => value.Id == skuId);
        var merchant = await crm.Merchants.SingleAsync(value => value.Id == merchantId);
        var sourceLocationId = await inventory.Locations.OrderBy(value => value.Id).Select(value => value.Id).FirstAsync();
        var sourceBatch = await inventory.InventoryBatches.FirstOrDefaultAsync(value =>
            value.LocationId == sourceLocationId && value.SkuId == skuId && value.LotNumber == lotNumber && value.ExpiryDate == expiryDate);
        if (sourceBatch is null)
        {
            inventory.InventoryBatches.Add(new InventoryBatch
            {
                Id = Guid.NewGuid(),
                LocationId = sourceLocationId,
                SkuId = skuId,
                LotNumber = lotNumber,
                ExpiryDate = expiryDate,
                Quantity = 0,
                CreatedAt = DateTime.UtcNow
            });
            await inventory.SaveChangesAsync();
        }
        var operation = new OperationLog
        {
            Id = Guid.NewGuid(),
            OperationNumber = $"TEST-SALE-{Guid.NewGuid():N}",
            OperationType = "WholesaleSale",
            Status = "Completed",
            SourceLocationId = sourceLocationId,
            ClientId = merchantId,
            ClientName = merchant.BusinessName,
            PaymentMethod = "MerchantAccount",
            CreatedBy = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            ConfirmedAt = DateTime.UtcNow,
            OperationLines =
            [
                new OperationLine
                {
                    Id = Guid.NewGuid(),
                    SkuId = skuId,
                    SkuCodeSnapshot = sku.SkuCode,
                    ProductNameSnapshot = sku.Product.Name,
                    MerchantNameSnapshot = merchant.BusinessName,
                    Section = "Standard",
                    Quantity = quantity,
                    EntryMode = "Packs",
                    LotNumber = lotNumber,
                    ExpiryDate = expiryDate
                }
            ]
        };
        operations.OperationLogs.Add(operation);
        await operations.SaveChangesAsync();
    }

    public async Task ScanMerchantExpiryRecallsAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MerchantExpiryRecallService>().ScanAsync();
    }

    public DateOnly GetEgyptToday()
    {
        using var scope = Services.CreateScope();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        return DateOnly.FromDateTime(clock.EgyptNow);
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

public sealed record LocationScopedCrmCorrectionSeed(
    Guid OwnMerchantId,
    Guid ForeignMerchantId,
    Guid OwnRepresentativeId,
    Guid ForeignRepresentativeId,
    Guid OwnOperationId,
    Guid ForeignOperationId,
    Guid OwnProposalId,
    Guid ForeignProposalId);

public sealed class OperationDetailContract
{
    public Guid Id { get; set; }
    public string OperationNumber { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? ClientId { get; set; }
    public string? ClientName { get; set; }
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

public sealed record StocktakeListContract(Guid Id, Guid LocationId);

public sealed record StocktakeLineContract(Guid Id, Guid SkuId, int PhysicalCount, int PhysicalPackCount = 0, int PhysicalPieceCount = 0);

public sealed class SupplyShipmentContract
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal ProductSubtotal { get; set; }
    public decimal CostSubtotal { get; set; }
    public decimal LandedTotal { get; set; }
    public Guid? InventoryReceiptOperationId { get; set; }
    public IReadOnlyList<SupplyLineContract> Lines { get; set; } = [];
    public SupplyReceivingSummaryContract Receiving { get; set; } = new();
}

public sealed record SupplyLineContract(decimal? UnitPrice, decimal LineSubtotal, decimal AllocatedCost, decimal LandedUnitCost);

public sealed class SupplyReceivingSummaryContract
{
    public IReadOnlyList<SupplyReceivingQuantityContract> Quantities { get; set; } = [];
    public IReadOnlyList<SupplyReceivingSessionContract> OpenSessions { get; set; } = [];
    public IReadOnlyList<SupplyReceivingSessionContract> History { get; set; } = [];
}

public sealed record SupplyReceivingQuantityContract(Guid ShipmentLineId, int CumulativeReceived, int OutstandingQuantity);
public sealed record SupplyReceivingSessionContract(Guid Id, string Status, DateTime CreatedAt, DateTime? ConfirmedAt, Guid? InventoryReceiptOperationId);

public sealed record OperationStockBalanceContract(Guid LocationId, Guid SkuId, int AvailablePacks, int ReservedInWarehousePacks, int ReservedWithRepPacks);

public sealed record BatchContract(string? LotNumber, int PackQuantity);

public sealed record OperationListContract(Guid Id, string OperationType, string Status);

public sealed record ReplenishmentRowContract(Guid DestinationLocationId, Guid SkuId, int AvailablePacks, int IncomingPacks, int TargetPacks, int ShortagePacks);

public sealed record ReplenishmentReserveContract(int CreatedOperations, int UnfilledPacks);

public sealed record TransferBlockedBatchContract(Guid SkuId, string? LotNumber, int PackQuantity, DateOnly? MinimumTransferExpiryDate, string Reason);

public sealed record MerchantBatchHistoryContract(Guid SkuId, string? LotNumber, DateOnly? ExpiryDate, int SoldQuantity, int ReturnedQuantity, string ExpiryStatus);

public sealed record MerchantExpiryRecallContract(Guid Id, Guid MerchantId, Guid SkuId, string? LotNumber, DateOnly ExpiryDate, string Status, int SoldQuantity, int ReturnedQuantity);

public sealed record MerchantRecallDraftContract(Guid OperationId, string OperationNumber, string Status);

public sealed record PaymentLogContract(
    Guid Id,
    Guid OperationId,
    Guid? MerchantId,
    decimal TotalAmount,
    decimal AmountPaid,
    decimal RemainingAmount,
    string PaymentMethod,
    string Status,
    Guid? AssignedTo,
    DateTime LastModifiedAt);

public sealed record MerchantListContract(Guid Id, string BusinessName, string BusinessType, string Status);

public sealed record ScopedRepresentativeContract(Guid Id, string Name, string Status);

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

public sealed record FinancialAdjustmentContract(
    Guid Id,
    Guid MerchantId,
    Guid? OperationId,
    string AdjustmentType,
    decimal Amount,
    string Status,
    string? Notes,
    Guid CreatedBy,
    string? CreatedByName,
    DateTime CreatedAt);

public sealed record PaymentOperationResolutionContract(
    Guid OperationId,
    string OperationNumber,
    Guid? MerchantId,
    string? MerchantName,
    string OperationType,
    string RecordType,
    Guid CanonicalId,
    string Scope,
    string? PaymentReference,
    string CurrentStatus,
    IReadOnlyList<string> PermittedNextActions);

public sealed record MerchantCollectionDraftContract(Guid Id, string Status, decimal Amount);

public sealed record OperationCorrectionContract(Guid Id, Guid OperationId, string Status, Guid? ReversalOperationId, Guid? ReplacementOperationId);

public sealed record SourceSaleLineContract(
    Guid SourceOperationId,
    string SourceOperationNumber,
    Guid SourceOperationLineId,
    Guid SkuId,
    string SkuCode,
    string ProductName,
    string EntryMode,
    int OriginalSoldQuantity,
    int ReturnedOrExchangedQuantity,
    int RemainingEligibleQuantity,
    string? LotNumber,
    DateOnly? ExpiryDate,
    IReadOnlyList<SourceSaleBatchContract> SourceBatches);

public sealed record SourceSaleBatchContract(Guid SourceBatchId, string? LotNumber, DateOnly? ExpiryDate);

public sealed record MerchantBalanceContract(
    Guid MerchantId,
    decimal SaleTotal,
    decimal ReturnTotal,
    decimal ChangeNet,
    decimal PaymentsReceived,
    decimal CashRefunded,
    decimal Balance);
