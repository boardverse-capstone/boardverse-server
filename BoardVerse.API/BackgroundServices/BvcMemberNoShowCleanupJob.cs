using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// M2/C2.13 — BVC member no-show cleanup background job.
/// <para>
/// Scaffold job chạy mỗi 30 phút. Hiện tại chỉ log "tick" — chưa có concept
/// "BVC pre-selection" trong codebase (M2-4 controller batch sẽ bổ sung nếu cần).
/// </para>
/// <para>
/// Background job này được register trong Program.cs với skip flag cho Testing env
/// để không phá integration tests.
/// </para>
/// docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.13.
/// (2026-10-01)
/// </summary>
public class BvcMemberNoShowCleanupJob : BackgroundService
{
    private readonly ILogger<BvcMemberNoShowCleanupJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(30);

    public BvcMemberNoShowCleanupJob(ILogger<BvcMemberNoShowCleanupJob> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "BvcMemberNoShowCleanupJob started. Running every {Interval} minutes.",
            _interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation(
                    "BVC no-show cleanup tick at {Time}. (scaffold — no-op until BVC pre-selection concept lands)",
                    DateTime.UtcNow);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("BvcMemberNoShowCleanupJob stopped (host shutdown).");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BvcMemberNoShowCleanupJob: error during tick.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}