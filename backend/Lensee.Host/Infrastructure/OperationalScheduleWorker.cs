using Lensee.SharedKernel.Abstractions;
using Lensee.Modules.Notifications.Data;
using Lensee.Modules.Payments.Data;
using Lensee.Modules.Operations.Data;
using Lensee.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Infrastructure;

public sealed class OperationalScheduleWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public OperationalScheduleWorker(IServiceScopeFactory scopeFactory) { _scopeFactory = scopeFactory; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunScheduledJobsAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = CairoNow();
            var next = now.Date.AddDays(1);
            var delay = next - now;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, stoppingToken);
            if (stoppingToken.IsCancellationRequested) break;
            await RunScheduledJobsAsync(stoppingToken);
        }
    }

    private async Task RunScheduledJobsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var replenishment = scope.ServiceProvider.GetRequiredService<TargetReplenishmentService>();
        await replenishment.RunAsync("Scheduled", null, null, cancellationToken);
        var alerts = scope.ServiceProvider.GetRequiredService<OperationalAlertScheduler>();
        await alerts.RunAsync(cancellationToken);
    }

    private static DateTime CairoNow()
    {
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
        catch { zone = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
        return TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime;
    }
}

public sealed class OperationalAlertScheduler
{
    private readonly IClock _clock;
    private readonly PaymentsDbContext _payments;
    private readonly NotificationsDbContext _notifications;
    private readonly OperationsDbContext _operations;
    public OperationalAlertScheduler(IClock clock, PaymentsDbContext payments, NotificationsDbContext notifications, OperationsDbContext operations) { _clock = clock; _payments = payments; _notifications = notifications; _operations = operations; }
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = _clock.EgyptNow;
        var cutoff = now.AddHours(-24);
        var operations = await _operations.OperationLogs.Include(value => value.OperationVersions).Where(value => !value.IsDeleted && (value.OperationType == "Reserve" || value.OperationType == "WholesaleSale" || value.OperationType == "RetailSale") && (value.Status == "Reserved" || value.Status == "Shipped")).ToListAsync(cancellationToken);
        foreach (var operation in operations)
        {
            var transition = operation.OperationVersions.OrderByDescending(value => value.EditedAt).Select(value => (DateTime?)value.EditedAt).FirstOrDefault() ?? operation.CreatedAt;
            if (transition >= cutoff) continue;
            foreach (var role in new[] { LenseeRoles.Admin, LenseeRoles.CLevel })
            {
                var context = System.Text.Json.JsonSerializer.Serialize(new { operation.Status, transition });
                if (await _notifications.NotificationLogs.AnyAsync(value => value.AlertType == "UnresolvedReserves" && value.ReferenceId == operation.Id && value.TargetRole == role && value.ReferenceContextJson == context, cancellationToken)) continue;
                var id = Guid.NewGuid();
                _notifications.NotificationLogs.Add(new NotificationLog { Id = id, AlertType = "UnresolvedReserves", Message = $"{operation.OperationNumber} remains {operation.Status} for {(int)(now - transition).TotalHours} hour(s). Review and progress the operation.", ReferenceId = operation.Id, ReferenceType = "Operation", ReferenceCode = operation.OperationNumber, TargetRole = role, Channel = "InApp", CreatedAt = now, ReferenceContextJson = context, NotificationNumber = $"NOT-{id:N}".ToUpperInvariant() });
            }
        }
        await _notifications.SaveChangesAsync(cancellationToken);
        if (now.DayOfWeek != DayOfWeek.Friday) return;
        var weekKey = $"OPENPAY-{DateOnly.FromDateTime(now):yyyyMMdd}";
        var logs = await _payments.MainPaymentLogs.Where(value => !value.IsDeleted && value.Status != "Completed" && value.Status != "Cancelled" && value.Status != "Rejected").ToListAsync(cancellationToken);
        var statuses = string.Join(", ", logs.GroupBy(value => value.Status).OrderBy(value => value.Key).Select(value => $"{value.Key}: {value.Count()}"));
        var message = $"Open-payment weekly summary: {logs.Count} log(s), {logs.Select(value => value.MerchantId).Where(value => value.HasValue).Distinct().Count()} merchant(s), total {logs.Sum(value => value.TotalAmount):0.##}, paid {logs.Sum(value => value.AmountPaid):0.##}, remaining {logs.Sum(value => Math.Max(value.TotalAmount - value.AmountPaid, 0)):0.##}. Statuses: {(statuses.Length == 0 ? "none" : statuses)}.";
        foreach (var role in new[] { LenseeRoles.Admin, LenseeRoles.ERPAdmin, LenseeRoles.CLevel, LenseeRoles.Accountant })
        {
            if (await _notifications.NotificationLogs.AnyAsync(value => value.AlertType == "OpenPaymentWeeklySummary" && value.ReferenceCode == weekKey && value.TargetRole == role, cancellationToken)) continue;
            var id = Guid.NewGuid();
            _notifications.NotificationLogs.Add(new NotificationLog { Id = id, AlertType = "OpenPaymentWeeklySummary", Message = message, ReferenceType = "PaymentLog", ReferenceCode = weekKey, ReferenceTitle = "Open-payment weekly summary", TargetRole = role, Channel = "InApp", CreatedAt = now, NotificationNumber = $"NOT-{id:N}".ToUpperInvariant() });
        }
        await _notifications.SaveChangesAsync(cancellationToken);
    }
}
