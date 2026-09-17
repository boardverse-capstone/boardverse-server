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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("KarmaWindowExpiryJob stopped (host shutdown).");
                break;
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

        // GAP-R6-BJ-KARMA Fix: dùng ExecuteUpdateAsync atomic thay vì load → mutate → save.
        // Trước đây: 2 instance cluster pick cùng lobby → cả 2 set RatingOpenedAt = null → double
        //   close, duplicate log lines. Tuy idempotent (set null lần 2 không thay đổi gì) nhưng
        //   duplicate log noise và waste CPU.
        // Sau: ExecuteUpdateAsync WHERE RatingOpenedAt < cutoff AND Status=Closed → atomic flip.
        var expiredCount = await db.Lobbies
            .Where(l => l.RatingOpenedAt != null
                        && l.RatingOpenedAt < cutoff
                        && l.Status == LobbyStatus.Closed)
            .ExecuteUpdateAsync(l => l.SetProperty(x => x.RatingOpenedAt, (DateTime?)null),
                stoppingToken);

        if (expiredCount == 0)
            return;

        _logger.LogInformation(
            "KarmaWindowExpiryJob closed {Count} karma windows (RatingOpenedAt < {Cutoff}).",
            expiredCount, cutoff);
    }
}
