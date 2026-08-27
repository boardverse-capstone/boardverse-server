using BoardVerse.Services.IServices;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// R-01: Tự động dismiss các PlayerAlert Open quá 30 ngày chưa được acknowledge.
/// Tránh dashboard admin ngập alert stale.
/// </summary>
public class AlertExpiryCleanupJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AlertExpiryCleanupJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(24);
    private const int MaxAgeDays = 30;
    private const int BatchSize = 100;

    public AlertExpiryCleanupJob(IServiceProvider serviceProvider, ILogger<AlertExpiryCleanupJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AlertExpiryCleanupJob started (interval={Interval}h, maxAgeDays={MaxAge}).",
            _interval.TotalHours, MaxAgeDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var alertService = scope.ServiceProvider.GetRequiredService<IPlayerAlertService>();
                var dismissed = await alertService.DismissStaleAlertsAsync(MaxAgeDays, BatchSize, stoppingToken);
                if (dismissed > 0)
                {
                    _logger.LogInformation(
                        "AlertExpiryCleanupJob dismissed {Count} stale alerts.", dismissed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("AlertExpiryCleanupJob stopped (host shutdown).");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AlertExpiryCleanupJob tick failed");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("AlertExpiryCleanupJob stopped.");
    }
}
