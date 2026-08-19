namespace Lensee.Host.Infrastructure;

public sealed class MerchantExpiryRecallWorker : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromDays(1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MerchantExpiryRecallWorker> _logger;

    public MerchantExpiryRecallWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<MerchantExpiryRecallWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ScanAsync(stoppingToken);
        using var timer = new PeriodicTimer(ScanInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ScanAsync(stoppingToken);
        }
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<MerchantExpiryRecallService>();
            var result = await service.ScanAsync(cancellationToken);
            _logger.LogInformation(
                "Merchant expiry recall scan finished with {ActiveRecalls} active recalls, {CreatedRecalls} created recalls, and {NotificationChanges} notification changes.",
                result.ActiveRecalls,
                result.CreatedRecalls,
                result.NotificationChanges);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Merchant expiry recall scan failed.");
        }
    }
}
