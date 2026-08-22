using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// GAP-R4-A8 Fix: Background job dọn dẹp OutboxEvents đã processed &gt; 30 ngày.
/// Chạy mỗi ngày lúc 03:00 SA. Tránh table grow indefinitely (1000 lobby/day × 5 events/lobby
/// = 900k rows / 6 tháng).
/// </summary>
public class OutboxCleanupJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxCleanupJob> _logger;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);

    public OutboxCleanupJob(IServiceScopeFactory scopeFactory, ILogger<OutboxCleanupJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxCleanupJob started. Interval = {Hours}h", CleanupInterval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("OutboxCleanupJob stopped (host shutdown).");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OutboxCleanupJob: error during cleanup");
            }

            await Task.Delay(CleanupInterval, stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
        var outboxRepo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();

        var cutoff = DateTime.UtcNow.AddDays(-30);
        var deleted = await outboxRepo.DeleteProcessedOlderThanAsync(cutoff, ct);
        if (deleted > 0)
        {
            _logger.LogInformation("OutboxCleanupJob: deleted {Count} processed events older than {Cutoff}", deleted, cutoff);
        }
    }
}