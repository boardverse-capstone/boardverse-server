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
    /// <summary>GAP-R6-BJ-OOM Fix: batch cap 500/tick. Tránh load 100k invites vào memory nếu spike.</summary>
    private const int BatchSize = 500;

    /// <summary>Jitter ±10% để tránh thundering herd khi cluster scale.</summary>
    private static readonly Random JitterRng = new();

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
        _logger.LogInformation("LobbyInviteExpiryJob started (interval=15m, batchSize={Batch}).", BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ExpirePendingInvitesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("LobbyInviteExpiryJob stopped (host shutdown).");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in LobbyInviteExpiryJob");
            }

            await Task.Delay(ApplyJitter(_interval), stoppingToken);
        }
    }

    private async Task ExpirePendingInvitesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var inviteRepository = scope.ServiceProvider.GetRequiredService<ILobbyInviteRepository>();
        var hubService = scope.ServiceProvider.GetRequiredService<BoardVerse.Services.IServices.ILobbyHubService>();

        var now = DateTime.UtcNow;
        // GAP-R6-BJ-FIX Fix: dùng ExpireBatchAsync (atomic ExecuteUpdateAsync) thay vì
        // load → mutate → save. Cluster-safe, không race với LobbyCleanupJob.
        var affectedRows = await inviteRepository.ExpireBatchAsync(now, BatchSize, stoppingToken);

        if (affectedRows == 0)
        {
            return;
        }

        _logger.LogInformation(
            "LobbyInviteExpiryJob expired {Count} invites (now={Now:O}).",
            affectedRows,
            now);

        // Realtime notify: broadcast update cho các lobby có invite vừa expire.
        var expiredLobbyIds = await inviteRepository.GetLobbyIdsForExpiredInvitesAsync(now, stoppingToken);
        foreach (var lobbyId in expiredLobbyIds)
        {
            try
            {
                await hubService.NotifyLobbyUpdated(lobbyId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LobbyInviteExpiryJob: failed to notify lobby {LobbyId}", lobbyId);
            }
        }
    }

    /// <summary>
    /// GAP-R6-LOW-JITTER Fix: jitter ±10% để tránh thundering herd khi cluster restart cùng lúc.
    /// </summary>
    private TimeSpan ApplyJitter(TimeSpan baseInterval)
    {
        var jitterMs = (int)(baseInterval.TotalMilliseconds * 0.1);
        if (jitterMs <= 0) jitterMs = 100;
        var offset = JitterRng.Next(-jitterMs, jitterMs);
        return baseInterval + TimeSpan.FromMilliseconds(offset);
    }
}