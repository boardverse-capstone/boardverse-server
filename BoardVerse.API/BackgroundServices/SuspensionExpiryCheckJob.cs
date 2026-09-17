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

                // GAP-R6-BJ-SUSP Fix: dùng FOR UPDATE SKIP LOCKED + transaction.
                // Trước đây: load Suspended users → mutate AccountStatus → Add PlayerActionHistory →
                //   SaveChanges → 2 instance cluster pick cùng user → duplicate PlayerActionHistory insert.
                //   Mỗi user hết suspension có thể có 2+ audit log rows.
                // Sau: mở batch transaction, FOR UPDATE SKIP LOCKED lock rows đang xử lý.
                //   2 instance → instance A lock, instance B skip → mỗi user chỉ process đúng 1 lần.
                //   PlayerActionHistory INSERT nằm trong transaction → chỉ commit khi đã lock được row.
                await using var batchTx = await db.Database.BeginTransactionAsync(stoppingToken);

                var expired = await db.Users
                    .FromSqlRaw(
                        "SELECT * FROM \"Users\" WHERE \"AccountStatus\" = {0} " +
                        "AND \"LockoutEndDate\" IS NOT NULL " +
                        "AND \"LockoutEndDate\" <= {1} " +
                        "ORDER BY \"LockoutEndDate\" ASC LIMIT {2} " +
                        "FOR UPDATE SKIP LOCKED",
                        (int)UserAccountStatus.Suspended, now, BatchSize)
                    .ToListAsync(stoppingToken);

                if (expired.Count == 0)
                {
                    await batchTx.CommitAsync(stoppingToken);
                }
                else
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
                            ActionBy = null, // system
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
                    await batchTx.CommitAsync(stoppingToken);

                    _logger.LogInformation(
                        "SuspensionExpiryCheckJob reactivated {Count} users.", expired.Count);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("SuspensionExpiryCheckJob stopped (host shutdown).");
                break;
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
