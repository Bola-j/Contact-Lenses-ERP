using System.Text.Json;
using Lensee.Modules.Catalog.Data;
using Lensee.Modules.CRM.Data;
using Lensee.Modules.Inventory.Data;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

public sealed class MerchantExpiryRecallService
{
    public const string AlertType = "MerchantExpiryRecall";
    public const string Active = "Active";
    public const string Completed = "Completed";
    public const string NoStock = "NoStock";

    private static readonly string[] TargetRoles = [LenseeRoles.Admin, LenseeRoles.ERPAdmin, LenseeRoles.CLevel];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OperationsDbContext _operations;
    private readonly NotificationsDbContext _notifications;
    private readonly CrmDbContext _crm;
    private readonly CatalogDbContext _catalog;
    private readonly InventoryDbContext _inventory;
    private readonly MerchantBatchHistoryService _history;
    private readonly IClock _clock;

    public MerchantExpiryRecallService(
        OperationsDbContext operations,
        NotificationsDbContext notifications,
        CrmDbContext crm,
        CatalogDbContext catalog,
        InventoryDbContext inventory,
        MerchantBatchHistoryService history,
        IClock clock)
    {
        _operations = operations;
        _notifications = notifications;
        _crm = crm;
        _catalog = catalog;
        _inventory = inventory;
        _history = history;
        _clock = clock;
    }

    public async Task<MerchantExpiryRecallConfig> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        var config = await _notifications.AlertConfigs
            .FirstOrDefaultAsync(value => value.AlertType == AlertType, cancellationToken);
        if (config is null)
        {
            config = new AlertConfig
            {
                Id = Guid.NewGuid(),
                AlertType = AlertType,
                ThresholdValue = 24,
                ThresholdUnit = "Months",
                IsActive = true
            };
            _notifications.AlertConfigs.Add(config);
            await _notifications.SaveChangesAsync(cancellationToken);
        }

        return new MerchantExpiryRecallConfig(config.ThresholdValue ?? 24, config.ThresholdUnit ?? "Months", config.IsActive);
    }

    public async Task<MerchantExpiryRecallConfig> UpdateConfigAsync(int thresholdValue, string? thresholdUnit, bool isActive, CancellationToken cancellationToken = default)
    {
        if (thresholdValue is < 1 or > 120 || !string.Equals(thresholdUnit?.Trim(), "Months", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Merchant expiry recall threshold must be between 1 and 120 months.");
        }

        var config = await _notifications.AlertConfigs
            .FirstOrDefaultAsync(value => value.AlertType == AlertType, cancellationToken);
        if (config is null)
        {
            config = new AlertConfig { Id = Guid.NewGuid(), AlertType = AlertType };
            _notifications.AlertConfigs.Add(config);
        }

        config.ThresholdValue = thresholdValue;
        config.ThresholdUnit = "Months";
        config.IsActive = isActive;
        await _notifications.SaveChangesAsync(cancellationToken);
        return new MerchantExpiryRecallConfig(thresholdValue, "Months", isActive);
    }

    public async Task<MerchantExpiryRecallScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        var config = await GetConfigAsync(cancellationToken);
        if (!config.IsActive)
        {
            return new MerchantExpiryRecallScanResult(0, 0, 0);
        }

        var now = _clock.EgyptNow;
        var today = DateOnly.FromDateTime(now);
        var threshold = today.AddMonths(config.ThresholdValue);
        var history = await _history.LoadAsync(cancellationToken: cancellationToken);
        var historyByKey = history.ToDictionary(row => RecallKey(row.Key));
        var recalls = await _operations.MerchantExpiryRecalls.ToListAsync(cancellationToken);
        var recallByKey = recalls.ToDictionary(RecallKey);
        var created = 0;
        var reopened = new HashSet<Guid>();

        foreach (var row in history.Where(row => row.Key.ExpiryDate.HasValue && row.Key.ExpiryDate.Value <= threshold))
        {
            var key = RecallKey(row.Key);
            if (!recallByKey.TryGetValue(key, out var recall))
            {
                if (row.RecordedBalanceQuantity <= 0)
                {
                    continue;
                }

                recall = new MerchantExpiryRecall
                {
                    Id = Guid.NewGuid(),
                    MerchantId = row.Key.MerchantId,
                    SkuId = row.Key.SkuId,
                    LotNumber = row.Key.LotNumber ?? string.Empty,
                    ExpiryDate = row.Key.ExpiryDate!.Value,
                    Status = Active,
                    SoldQuantity = row.SoldQuantity,
                    ReturnedQuantity = row.ReturnedQuantity,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _operations.MerchantExpiryRecalls.Add(recall);
                recalls.Add(recall);
                recallByKey[key] = recall;
                created++;
                reopened.Add(recall.Id);
                continue;
            }

            recall.SoldQuantity = row.SoldQuantity;
            recall.ReturnedQuantity = row.ReturnedQuantity;
            recall.UpdatedAt = now;

            if (row.RecordedBalanceQuantity <= 0)
            {
                Complete(recall, now);
                continue;
            }

            var hasNewSaleAfterNoStock = recall.Status == NoStock && row.SoldQuantity > recall.ResolvedSoldQuantity.GetValueOrDefault();
            if (recall.Status == Completed || hasNewSaleAfterNoStock)
            {
                Reopen(recall);
                reopened.Add(recall.Id);
            }
        }

        foreach (var recall in recalls)
        {
            if (!historyByKey.TryGetValue(RecallKey(recall), out var row))
            {
                if (recall.Status == Active)
                {
                    Complete(recall, now);
                }
                continue;
            }

            recall.SoldQuantity = row.SoldQuantity;
            recall.ReturnedQuantity = row.ReturnedQuantity;
            recall.UpdatedAt = now;
            if (row.RecordedBalanceQuantity <= 0 && recall.Status == Active)
            {
                Complete(recall, now);
            }
        }

        await _operations.SaveChangesAsync(cancellationToken);
        var activeRecalls = recalls.Where(recall => recall.Status == Active).ToList();
        var notificationChanges = await SynchronizeNotificationsAsync(recalls, reopened, cancellationToken);
        return new MerchantExpiryRecallScanResult(activeRecalls.Count, created, notificationChanges);
    }

    public async Task<IReadOnlyList<MerchantExpiryRecallView>> ListAsync(string? status, Guid? merchantId, CancellationToken cancellationToken = default)
    {
        var query = _operations.MerchantExpiryRecalls.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(recall => recall.Status == status.Trim());
        }
        if (merchantId.HasValue)
        {
            query = query.Where(recall => recall.MerchantId == merchantId.Value);
        }

        var recalls = await query
            .OrderBy(recall => recall.Status == Active ? 0 : 1)
            .ThenBy(recall => recall.ExpiryDate)
            .ThenByDescending(recall => recall.UpdatedAt)
            .ToListAsync(cancellationToken);
        var merchantIds = recalls.Select(recall => recall.MerchantId).Distinct().ToArray();
        var skuIds = recalls.Select(recall => recall.SkuId).Distinct().ToArray();
        var merchants = await _crm.Merchants.AsNoTracking()
            .Where(merchant => merchantIds.Contains(merchant.Id))
            .ToDictionaryAsync(merchant => merchant.Id, merchant => merchant.BusinessName, cancellationToken);
        var skus = await _catalog.Skus.AsNoTracking().Include(sku => sku.Product)
            .Where(sku => skuIds.Contains(sku.Id))
            .ToDictionaryAsync(sku => sku.Id, sku => new { sku.SkuCode, ProductName = sku.Product.Name }, cancellationToken);
        var today = DateOnly.FromDateTime(_clock.EgyptNow);

        return recalls.Select(recall =>
        {
            merchants.TryGetValue(recall.MerchantId, out var merchantName);
            skus.TryGetValue(recall.SkuId, out var sku);
            return new MerchantExpiryRecallView(
                recall.Id,
                recall.MerchantId,
                merchantName ?? "Unknown merchant",
                recall.SkuId,
                sku?.SkuCode,
                sku?.ProductName,
                EmptyToNull(recall.LotNumber),
                recall.ExpiryDate,
                recall.ExpiryDate.DayNumber - today.DayNumber,
                recall.Status,
                recall.SoldQuantity,
                recall.ReturnedQuantity,
                recall.CreatedAt,
                recall.UpdatedAt,
                recall.ResolvedAt,
                recall.ResolvedBy,
                recall.ResolutionNote);
        }).ToList();
    }

    public async Task<MerchantRecallCommandResult<MerchantRecallReturnDraft>> CreateReturnDraftAsync(
        Guid recallId,
        Guid receivingLocationId,
        int quantity,
        string? notes,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        var recall = await _operations.MerchantExpiryRecalls.FirstOrDefaultAsync(value => value.Id == recallId, cancellationToken);
        if (recall is null)
        {
            return MerchantRecallCommandResult<MerchantRecallReturnDraft>.Missing();
        }
        if (recall.Status != Active)
        {
            return MerchantRecallCommandResult<MerchantRecallReturnDraft>.Invalid("status", "Only an active merchant expiry recall can start a return.");
        }
        if (quantity <= 0)
        {
            return MerchantRecallCommandResult<MerchantRecallReturnDraft>.Invalid("quantity", "Return quantity must be greater than zero.");
        }

        var location = await _inventory.Locations.AsNoTracking()
            .FirstOrDefaultAsync(value => value.Id == receivingLocationId && value.IsActive, cancellationToken);
        if (location is null)
        {
            return MerchantRecallCommandResult<MerchantRecallReturnDraft>.Invalid("receivingLocationId", "Receiving warehouse must exist and be active.");
        }

        var history = await _history.LoadAsync(recall.MerchantId, cancellationToken: cancellationToken);
        var row = history.FirstOrDefault(value => RecallKey(value.Key) == RecallKey(recall));
        if (row is null)
        {
            return MerchantRecallCommandResult<MerchantRecallReturnDraft>.Invalid("recall", "Recorded merchant batch history is no longer available for this recall.");
        }

        var merchant = await _crm.Merchants.AsNoTracking().FirstOrDefaultAsync(value => value.Id == recall.MerchantId && !value.IsDeleted, cancellationToken);
        var sku = await _catalog.Skus.AsNoTracking().Include(value => value.Product).FirstOrDefaultAsync(value => value.Id == recall.SkuId, cancellationToken);
        if (merchant is null || sku is null)
        {
            return MerchantRecallCommandResult<MerchantRecallReturnDraft>.Invalid("recall", "The merchant or SKU referenced by this recall is no longer available.");
        }

        var now = _clock.EgyptNow;
        var operation = new OperationLog
        {
            Id = Guid.NewGuid(),
            OperationNumber = $"OP-{now:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}",
            OperationType = "Return",
            Status = "Draft",
            SourceLocationId = receivingLocationId,
            ClientId = merchant.Id,
            ClientName = merchant.BusinessName,
            MerchantExpiryRecallId = recall.Id,
            Notes = string.IsNullOrWhiteSpace(notes) ? "Merchant expiry recall" : notes.Trim(),
            CreatedBy = actorId,
            CreatedAt = now
        };
        var line = new OperationLine
        {
            Id = Guid.NewGuid(),
            OperationId = operation.Id,
            SkuId = recall.SkuId,
            ProductNameSnapshot = sku.Product.Name,
            SkuCodeSnapshot = sku.SkuCode,
            MerchantNameSnapshot = merchant.BusinessName,
            Section = "Standard",
            Quantity = quantity,
            EntryMode = "Packs",
            UnitPrice = 0,
            LineTotal = 0,
            LotNumber = EmptyToNull(recall.LotNumber),
            ExpiryDate = recall.ExpiryDate,
            LineNotes = string.IsNullOrWhiteSpace(notes) ? "Merchant expiry recall" : notes.Trim()
        };
        operation.OperationLines.Add(line);

        await SharedDbTransaction.ExecuteAsync(_operations, async () =>
        {
            _operations.OperationLogs.Add(operation);
            await _operations.SaveChangesAsync(cancellationToken);

            var version = new OperationVersion
            {
                Id = Guid.NewGuid(),
                OperationId = operation.Id,
                VersionNumber = 1,
                SnapshotData = JsonSerializer.Serialize(new
                {
                    operation.OperationType,
                    operation.Status,
                    operation.SourceLocationId,
                    operation.DestinationLocationId,
                    operation.ClientId,
                    operation.ClientName,
                    operation.RepresentativeId,
                    operation.PaymentMethod,
                    operation.Notes,
                    Lines = new[]
                    {
                        new
                        {
                            line.SkuId,
                            SkuCode = line.SkuCodeSnapshot,
                            ProductName = line.ProductNameSnapshot,
                            line.Section,
                            line.Quantity,
                            line.EntryMode,
                            line.BonusQuantity,
                            line.UnitPrice,
                            line.LineTotal,
                            line.LotNumber,
                            line.ExpiryDate,
                            Notes = line.LineNotes
                        }
                    },
                    TransferAllocations = Array.Empty<object>()
                }, JsonOptions),
                Reason = "Merchant expiry recall return draft",
                EditedBy = actorId,
                EditedAt = now
            };
            _operations.OperationVersions.Add(version);
            await _operations.SaveChangesAsync(cancellationToken);
            operation.CurrentVersionId = version.Id;
            await _operations.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        return MerchantRecallCommandResult<MerchantRecallReturnDraft>.Success(
            new MerchantRecallReturnDraft(operation.Id, operation.OperationNumber, operation.Status));
    }

    public async Task<MerchantRecallCommandResult<MerchantExpiryRecallView>> RecordNoStockAsync(
        Guid recallId,
        string? note,
        Guid actorId,
        CancellationToken cancellationToken = default)
    {
        var recall = await _operations.MerchantExpiryRecalls.FirstOrDefaultAsync(value => value.Id == recallId, cancellationToken);
        if (recall is null)
        {
            return MerchantRecallCommandResult<MerchantExpiryRecallView>.Missing();
        }
        if (recall.Status != Active)
        {
            return MerchantRecallCommandResult<MerchantExpiryRecallView>.Invalid("status", "Only an active merchant expiry recall can be resolved as no stock.");
        }
        if (string.IsNullOrWhiteSpace(note))
        {
            return MerchantRecallCommandResult<MerchantExpiryRecallView>.Invalid("note", "A note is required when the merchant has no stock.");
        }

        var now = _clock.EgyptNow;
        recall.Status = NoStock;
        recall.ResolvedSoldQuantity = recall.SoldQuantity;
        recall.ResolvedAt = now;
        recall.ResolvedBy = actorId;
        recall.ResolutionNote = note.Trim();
        recall.UpdatedAt = now;
        await _operations.SaveChangesAsync(cancellationToken);
        await MarkNotificationsReadAsync(recall.Id, cancellationToken);
        var view = (await ListAsync(null, recall.MerchantId, cancellationToken)).Single(value => value.Id == recall.Id);
        return MerchantRecallCommandResult<MerchantExpiryRecallView>.Success(view);
    }

    public async Task ApplyConfirmedReturnAsync(OperationLog operation, CancellationToken cancellationToken = default)
    {
        if (operation.MerchantExpiryRecallId is not { } recallId)
        {
            return;
        }

        var recall = await _operations.MerchantExpiryRecalls.FirstOrDefaultAsync(value => value.Id == recallId, cancellationToken);
        if (recall is null)
        {
            return;
        }

        recall.ReturnedQuantity += operation.OperationLines.Sum(line => line.Quantity);
        recall.UpdatedAt = _clock.EgyptNow;
        if (recall.ReturnedQuantity >= recall.SoldQuantity)
        {
            Complete(recall, _clock.EgyptNow);
            recall.ResolvedBy = operation.ConfirmedBy;
        }
    }

    public async Task SynchronizeResolvedNotificationAsync(Guid recallId, CancellationToken cancellationToken = default)
    {
        var status = await _operations.MerchantExpiryRecalls.AsNoTracking()
            .Where(recall => recall.Id == recallId)
            .Select(recall => recall.Status)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.Equals(status, Active, StringComparison.Ordinal))
        {
            await MarkNotificationsReadAsync(recallId, cancellationToken);
        }
    }

    private async Task<int> SynchronizeNotificationsAsync(IReadOnlyCollection<MerchantExpiryRecall> recalls, IReadOnlySet<Guid> reopened, CancellationToken cancellationToken)
    {
        var recallIds = recalls.Select(recall => recall.Id).ToArray();
        var existing = await _notifications.NotificationLogs
            .Where(notification => notification.AlertType == AlertType && notification.ReferenceId.HasValue && recallIds.Contains(notification.ReferenceId.Value))
            .ToListAsync(cancellationToken);
        var active = recalls.Where(recall => recall.Status == Active).ToList();
        var merchantIds = active.Select(recall => recall.MerchantId).Distinct().ToArray();
        var skuIds = active.Select(recall => recall.SkuId).Distinct().ToArray();
        var merchants = await _crm.Merchants.AsNoTracking().Where(value => merchantIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, value => value.BusinessName, cancellationToken);
        var skus = await _catalog.Skus.AsNoTracking().Include(value => value.Product).Where(value => skuIds.Contains(value.Id)).ToDictionaryAsync(value => value.Id, value => new { value.SkuCode, ProductName = value.Product.Name }, cancellationToken);
        var now = _clock.EgyptNow;
        var changes = 0;

        foreach (var recall in recalls.Where(recall => recall.Status != Active))
        {
            foreach (var notification in existing.Where(value => value.ReferenceId == recall.Id && !value.IsRead))
            {
                notification.IsRead = true;
                changes++;
            }
        }

        foreach (var recall in active)
        {
            merchants.TryGetValue(recall.MerchantId, out var merchantName);
            skus.TryGetValue(recall.SkuId, out var sku);
            var label = sku?.SkuCode ?? "Unknown SKU";
            var state = recall.ExpiryDate < DateOnly.FromDateTime(now) ? "expired" : "approaching expiry";
            var message = $"Merchant batch recall: {merchantName ?? "Unknown merchant"} received {label}, lot {EmptyToNull(recall.LotNumber) ?? "No lot"}, expiring {recall.ExpiryDate:yyyy-MM-dd}. The batch is {state}. Confirm the physical quantity before starting a return.";
            var title = $"{merchantName ?? "Unknown merchant"} / {label} / {recall.ExpiryDate:yyyy-MM-dd}";
            var context = JsonSerializer.Serialize(new { recall.MerchantId, recall.SkuId, LotNumber = EmptyToNull(recall.LotNumber), recall.ExpiryDate }, JsonOptions);

            foreach (var role in TargetRoles)
            {
                var notification = existing.FirstOrDefault(value => value.ReferenceId == recall.Id && value.TargetRole == role);
                if (notification is null)
                {
                    var id = Guid.NewGuid();
                    notification = new NotificationLog
                    {
                        Id = id,
                        NotificationNumber = $"NOT-{id:N}".ToUpperInvariant(),
                        AlertType = AlertType,
                        Message = message,
                        ReferenceId = recall.Id,
                        ReferenceType = AlertType,
                        ReferenceCode = $"MRC-{recall.Id:N}".ToUpperInvariant(),
                        ReferenceTitle = title,
                        ReferenceContextJson = context,
                        TargetRole = role,
                        Channel = "InApp",
                        CreatedAt = now
                    };
                    _notifications.NotificationLogs.Add(notification);
                    existing.Add(notification);
                    changes++;
                }
                else
                {
                    notification.Message = message;
                    notification.ReferenceTitle = title;
                    notification.ReferenceContextJson = context;
                    if (reopened.Contains(recall.Id))
                    {
                        notification.IsRead = false;
                        notification.CreatedAt = now;
                    }
                    changes++;
                }
            }
        }

        if (changes > 0)
        {
            await _notifications.SaveChangesAsync(cancellationToken);
        }
        return changes;
    }

    private async Task MarkNotificationsReadAsync(Guid recallId, CancellationToken cancellationToken)
    {
        var notifications = await _notifications.NotificationLogs
            .Where(value => value.AlertType == AlertType && value.ReferenceId == recallId && !value.IsRead)
            .ToListAsync(cancellationToken);
        foreach (var notification in notifications)
        {
            notification.IsRead = true;
        }
        if (notifications.Count > 0)
        {
            await _notifications.SaveChangesAsync(cancellationToken);
        }
    }

    private static void Complete(MerchantExpiryRecall recall, DateTime now)
    {
        recall.Status = Completed;
        recall.ResolvedAt = now;
        recall.ResolutionNote = "All recorded sales are accounted for by confirmed returns.";
        recall.ResolvedSoldQuantity = recall.SoldQuantity;
    }

    private static void Reopen(MerchantExpiryRecall recall)
    {
        recall.Status = Active;
        recall.ResolvedAt = null;
        recall.ResolvedBy = null;
        recall.ResolutionNote = null;
        recall.ResolvedSoldQuantity = null;
    }

    private static RecallIdentity RecallKey(MerchantBatchHistoryKey key) =>
        new(key.MerchantId, key.SkuId, key.LotNumber ?? string.Empty, key.ExpiryDate ?? DateOnly.MinValue);

    private static RecallIdentity RecallKey(MerchantExpiryRecall recall) =>
        new(recall.MerchantId, recall.SkuId, recall.LotNumber, recall.ExpiryDate);

    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private sealed record RecallIdentity(Guid MerchantId, Guid SkuId, string LotNumber, DateOnly ExpiryDate);
}

public sealed record MerchantExpiryRecallConfig(int ThresholdValue, string ThresholdUnit, bool IsActive);
public sealed record MerchantExpiryRecallScanResult(int ActiveRecalls, int CreatedRecalls, int NotificationChanges);
public sealed record MerchantRecallReturnDraft(Guid OperationId, string OperationNumber, string Status);

public sealed record MerchantExpiryRecallView(
    Guid Id,
    Guid MerchantId,
    string MerchantName,
    Guid SkuId,
    string? SkuCode,
    string? ProductName,
    string? LotNumber,
    DateOnly ExpiryDate,
    int DaysToExpiry,
    string Status,
    int SoldQuantity,
    int ReturnedQuantity,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ResolvedAt,
    Guid? ResolvedBy,
    string? ResolutionNote);

public sealed record MerchantRecallCommandResult<T>(T? Value, bool NotFound, IReadOnlyDictionary<string, string[]> Errors)
{
    public static MerchantRecallCommandResult<T> Success(T value) => new(value, false, new Dictionary<string, string[]>());
    public static MerchantRecallCommandResult<T> Missing() => new(default, true, new Dictionary<string, string[]>());
    public static MerchantRecallCommandResult<T> Invalid(string field, string message) => new(default, false, new Dictionary<string, string[]> { [field] = [message] });
}
