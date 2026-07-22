using BoardVerse.Services.IServices;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Tự động expire các friend request Pending quá hạn (BR-FRIEND-05).
/// Mặc định chạy mỗi giờ, expire sau 30 ngày.
/// </summary>
public class FriendRequestExpiryJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FriendRequestExpiryJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);
    private const int ExpiryDays = 30;

    public FriendRequestExpiryJob(IServiceProvider serviceProvider, ILogger<FriendRequestExpiryJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("FriendRequestExpiryJob started (interval={Interval}, expiry={Days}d).",
            _interval, ExpiryDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var friendService = scope.ServiceProvider.GetRequiredService<IFriendService>();
                var expired = await friendService.ExpireOldPendingRequestsAsync(ExpiryDays);
                if (expired > 0)
                {
                    _logger.LogInformation("FriendRequestExpiryJob: expired {Count} pending requests.", expired);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in FriendRequestExpiryJob");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }
}
