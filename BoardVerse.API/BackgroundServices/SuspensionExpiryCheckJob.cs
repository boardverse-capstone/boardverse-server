using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// BR-RISK-06: Tự động mở khóa tài khoản hết hạn suspension (User.LockoutEndDate &lt; now).
/// Chạy mỗi giờ.
/// </summary>
public class SuspensionExpiryCheckJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SuspensionExpiryCheckJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(1);
    private const int BatchSize = 100;

    public SuspensionExpiryCheckJob(IServiceProvider serviceProvider, ILogger<SuspensionExpiryCheckJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SuspensionExpiryCheckJob started (interval={Interval}h).", _interval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
                var now = DateTime.UtcNow;

                var expired = await db.Users
                    .Where(u => u.AccountStatus == UserAccountStatus.Suspended
                        && u.LockoutEndDate != null
                        && u.LockoutEndDate <= now)
                    .Take(BatchSize)
                    .ToListAsync(stoppingToken);

                if (expired.Count > 0)
                {
                    foreach (var user in expired)
                    {
                        var previousStatus = user.AccountStatus;
                        user.AccountStatus = UserAccountStatus.Active;
                        user.BlockReason = null;
                        user.BlockedAt = null;
                        user.LockoutEndDate = null;
                        user.UpdatedAt = now;

                        db.PlayerActionHistories.Add(new PlayerActionHistory
                        {
                            Id = Guid.NewGuid(),
                            UserId = user.Id,
                            ActionType = AdminActionType.AccountStatusChange,
                            ActionBy = Guid.Empty, // system
                            Reason = ApiErrorMessages.AdminModeration.SystemSuspensionExpiredReason,
                            Metadata = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                previousStatus = previousStatus.ToString(),
                                newStatus = user.AccountStatus.ToString(),
                                autoExpired = true,
                                lockoutEndDate = user.LockoutEndDate
                            }),
                            CreatedAt = now
                        });
                    }

                    await db.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation(
                        "SuspensionExpiryCheckJob reactivated {Count} users.", expired.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SuspensionExpiryCheckJob tick failed");
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

        _logger.LogInformation("SuspensionExpiryCheckJob stopped.");
    }
}
