using BoardVerse.Services.IServices;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// BR-RISK-01: Recompute riskScore cho tất cả user active mỗi giờ.
/// Lấy cảm hứng từ RiskScoreRecomputeJob pattern (BR §XVIII.9).
/// </summary>
public class RiskScoreRecomputeJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RiskScoreRecomputeJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);
    private const int BatchSize = 200;

    public RiskScoreRecomputeJob(IServiceProvider serviceProvider, ILogger<RiskScoreRecomputeJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "RiskScoreRecomputeJob started (interval={Interval}h, batchSize={BatchSize}).",
            _interval.TotalHours, BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IPlayerRiskScoreService>();
                var processed = await service.RecomputeBatchAsync(BatchSize, DateTime.UtcNow, stoppingToken);
                _logger.LogInformation(
                    "RiskScoreRecomputeJob tick: processed {Count} users.", processed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RiskScoreRecomputeJob tick failed");
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

        _logger.LogInformation("RiskScoreRecomputeJob stopped.");
    }
}
