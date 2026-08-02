using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Background job tự động đánh dấu các LobbyInvite đã quá hạn 24h.
/// BR-LOBBY-INVITE-08: Invite hết hạn sau 24h. Cron job quét và set status = Expired.
/// Chạy mỗi 15 phút để giảm tải DB.
/// </summary>
public class LobbyInviteExpiryJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LobbyInviteExpiryJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(15);

    public LobbyInviteExpiryJob(IServiceProvider serviceProvider, ILogger<LobbyInviteExpiryJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LobbyInviteExpiryJob started (interval=15m).");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpirePendingInvitesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in LobbyInviteExpiryJob");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ExpirePendingInvitesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var inviteRepository = scope.ServiceProvider.GetRequiredService<ILobbyInviteRepository>();

        var now = DateTime.UtcNow;
        var expired = await inviteRepository.GetExpiredPendingAsync(now);

        if (expired.Count == 0)
        {
            return;
        }

        foreach (var invite in expired)
        {
            invite.Status = LobbyInviteStatus.Expired;
            invite.RespondedAt = now;
        }

        await inviteRepository.SaveChangesAsync();

        _logger.LogInformation(
            "LobbyInviteExpiryJob expired {Count} invites (now={Now:O}).",
            expired.Count,
            now);
    }
}