using BoardVerse.Core.IRepositories;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// GAP-R6-FCM-CLEANUP Fix: Hard-delete stale DeviceTokens mỗi ngày.
/// Điều kiện xóa: (IsInvalidated = true) HOẶC (LastUsedAt &lt; now - 180 ngày).
///
/// Lý do:
///  - FCM gửi tới invalid token → lỗi → FcmPushNotificationService đánh IsInvalidated.
///  - User gỡ app hoặc không mở app 180 ngày → token cũ, không còn giá trị.
///  - Bảng DeviceTokens có thể grow lên vài triệu row nếu không cleanup.
///
/// BR §17.6 Audit Log: không cần ghi log cho cleanup thường ngày (không phải admin action).
/// </summary>
public class DeviceTokenCleanupJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeviceTokenCleanupJob> _logger;

    /// <summary>Cleanup interval: 24 giờ.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>Token stale nếu LastUsedAt &lt; now - 180 ngày.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(180);

    public DeviceTokenCleanupJob(IServiceProvider serviceProvider, ILogger<DeviceTokenCleanupJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DeviceTokenCleanupJob started (interval={Hours}h, staleAfter={Days}d).",
            Interval.TotalHours, StaleAfter.TotalDays);

        // Initial delay nhẹ — service mới start còn warm-up DB connection.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("DeviceTokenCleanupJob stopped (host shutdown).");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeviceTokenCleanupJob tick failed");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunCleanupAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDeviceTokenRepository>();

        var staleCutoff = DateTime.UtcNow - StaleAfter;
        var deleted = await repo.DeleteStaleTokensAsync(staleCutoff, ct);

        if (deleted > 0)
        {
            _logger.LogInformation(
                "DeviceTokenCleanupJob deleted {Count} stale/invalidated tokens (cutoff={Cutoff:O})",
                deleted, staleCutoff);
        }
        else
        {
            _logger.LogDebug("DeviceTokenCleanupJob: nothing to clean");
        }
    }
}