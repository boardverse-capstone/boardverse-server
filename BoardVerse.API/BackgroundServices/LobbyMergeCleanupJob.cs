using BoardVerse.Services.IServices;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Phase 5 (G30): Background job tự động expire các LobbyMergeRequest đã quá hạn (Pending + ExpiresAt &lt; now).
/// Chạy mỗi 5 phút.
///
/// Cơ chế:
/// - Cron 5 phút gọi <see cref="ILobbyMergeService.ExpireOverdueRequestsAsync"/>
/// - Mỗi request đã expire: set Status = Expired + ghi LobbyMergeAuditLog
/// - Không broadcast SignalR cho expired requests (vì những request này đã quá 15 phút mà staff không duyệt)
/// - Jitter ±10% tránh thundering herd khi cluster scale
/// </summary>
public class LobbyMergeCleanupJob : BackgroundService
{
    /// <summary>
    /// G30: Cron chạy mỗi 5 phút.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Jitter ±10% để tránh thundering herd khi cluster scale cùng lúc.
    /// </summary>
    private static readonly Random JitterRng = new();

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LobbyMergeCleanupJob> _logger;

    public LobbyMergeCleanupJob(IServiceProvider serviceProvider, ILogger<LobbyMergeCleanupJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "LobbyMergeCleanupJob started (interval=5m, jitter=±10%).");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpireOverdueRequestsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("LobbyMergeCleanupJob stopped (host shutdown).");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LobbyMergeCleanupJob: unexpected error");
            }

            await Task.Delay(ApplyJitter(Interval), stoppingToken);
        }
    }

    /// <summary>
    /// G30: Lấy các LobbyMergeRequest đang Pending + đã quá ExpiresAt → set Expired.
    /// </summary>
    private async Task ExpireOverdueRequestsAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var mergeService = scope.ServiceProvider.GetRequiredService<ILobbyMergeService>();

        var expiredCount = await mergeService.ExpireOverdueRequestsAsync(stoppingToken);

        if (expiredCount > 0)
        {
            _logger.LogInformation(
                "LobbyMergeCleanupJob expired {Count} stale merge requests.",
                expiredCount);
        }
    }

    /// <summary>
    /// Jitter ±10% để tránh thundering herd khi cluster restart cùng lúc.
    /// </summary>
    private static TimeSpan ApplyJitter(TimeSpan baseInterval)
    {
        var jitterMs = (int)(baseInterval.TotalMilliseconds * 0.1);
        if (jitterMs <= 0) jitterMs = 100;
        var offset = JitterRng.Next(-jitterMs, jitterMs);
        return baseInterval + TimeSpan.FromMilliseconds(offset);
    }
}
