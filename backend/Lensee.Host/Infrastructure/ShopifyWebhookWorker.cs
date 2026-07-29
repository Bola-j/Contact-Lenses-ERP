using Microsoft.Extensions.Hosting;

namespace Lensee.Host.Infrastructure;

public sealed class ShopifyWebhookWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ShopifyWebhookWorker> _logger;

    public ShopifyWebhookWorker(IServiceScopeFactory scopeFactory, ILogger<ShopifyWebhookWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var integration = scope.ServiceProvider.GetRequiredService<ShopifyIntegrationService>();
                if (integration.IsConfigured)
                {
                    foreach (var eventId in await integration.ClaimDueEventsAsync(stoppingToken))
                    {
                        await integration.ProcessQueuedEventAsync(eventId, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Shopify webhook worker pass failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

public sealed class ShopifyPayloadRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ShopifyPayloadRetentionWorker> _logger;

    public ShopifyPayloadRetentionWorker(IServiceScopeFactory scopeFactory, ILogger<ShopifyPayloadRetentionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ShopifyIntegrationService>().PurgeExpiredPayloadsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Shopify payload retention pass failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
