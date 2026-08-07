using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Background job tự động đóng cửa sổ đánh giá Karma sau khi hết thời gian cho phép.
///
/// K-01: Karma window expiry job.
/// BR: RatingOpenedAt được mở khi lobby → Closed (bởi KarmaWindowJob).
/// Job này đóng window bằng cách set RatingOpenedAt = null khi đã quá RatingWindowDuration.
///
/// Default: 48 giờ kể từ lúc RatingOpenedAt được set.
/// </summary>
public class KarmaWindowExpiryJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KarmaWindowExpiryJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Thời gian cho phép đánh giá Karma kể từ khi window được mở.
    /// Mặc định 48 giờ — có thể override bằng SystemConfiguration sau.
    /// </summary>
    public static readonly TimeSpan RatingWindowDuration = TimeSpan.FromHours(48);

    public KarmaWindowExpiryJob(IServiceProvider serviceProvider, ILogger<KarmaWindowExpiryJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("KarmaWindowExpiryJob started. RatingWindowDuration={Hours}h.", RatingWindowDuration.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredWindowsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in KarmaWindowExpiryJob");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ProcessExpiredWindowsAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();

        var now = DateTime.UtcNow;
        var cutoff = now - RatingWindowDuration;

        // Query các lobby có RatingOpenedAt != null và RatingOpenedAt < cutoff
        // Chỉ xét lobby đang Closed (hoặc terminal status) vì chỉ những lobby này mới có RatingOpenedAt set
        var expiredWindows = await db.Lobbies
            .Where(l => l.RatingOpenedAt != null
                        && l.RatingOpenedAt < cutoff
                        && l.Status == LobbyStatus.Closed)
            .ToListAsync(stoppingToken);

        if (expiredWindows.Count == 0)
            return;

        _logger.LogInformation("Found {Count} karma windows to close (RatingOpenedAt < {Cutoff}).",
            expiredWindows.Count, cutoff);

        foreach (var lobby in expiredWindows)
        {
            var openedAt = lobby.RatingOpenedAt;
            lobby.RatingOpenedAt = null; // Đóng window

            _logger.LogInformation(
                "Closed karma window for lobby {LobbyId}. Was open for {Duration:F1} hours.",
                lobby.Id,
                openedAt.HasValue ? (now - openedAt.Value).TotalHours : 0);
        }

        await db.SaveChangesAsync(stoppingToken);
        _logger.LogInformation("Processed {Count} expired karma windows.", expiredWindows.Count);
    }
}
