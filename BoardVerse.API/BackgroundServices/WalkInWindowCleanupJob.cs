using BoardVerse.Services.IServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// §4.4: Quét mỗi 5 phút, tìm WalkInWindow đã hết hạn (WindowEnd &lt; now)
/// và chưa closed → tự động đóng để giải phóng tài nguyên.
///
/// BR-WALKIN-03: Walk-in Window auto-expire sau WindowEnd.
/// </summary>
public class WalkInWindowCleanupJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WalkInWindowCleanupJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public WalkInWindowCleanupJob(
        IServiceScopeFactory scopeFactory,
        ILogger<WalkInWindowCleanupJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WalkInWindowCleanupJob started. Interval: {Interval}", _interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("WalkInWindowCleanupJob stopped (host shutdown).");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WalkInWindowCleanupJob: Error during cleanup");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var walkInService = scope.ServiceProvider.GetRequiredService<IWalkInService>();

        _logger.LogDebug("WalkInWindowCleanupJob: Running expired windows cleanup");

        await walkInService.CleanupExpiredWindowsAsync(ct);

        _logger.LogDebug("WalkInWindowCleanupJob: Cleanup completed");
    }
}
