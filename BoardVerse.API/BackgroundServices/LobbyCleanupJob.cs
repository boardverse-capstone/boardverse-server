using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Background job tự động expire LobbyInvites quá 24h chưa được accept (BR-LOBBY-INVITE-08).
/// Chạy mỗi 60 giây.
///
/// LƯU Ý: Logic timeout lobby Open đã được chuyển sang <see cref="LobbyTimeoutJob"/>
/// (cluster-safe với FOR UPDATE SKIP LOCKED + atomic status flip).
/// Job này CHỈ xử lý expire invite; lobby timeout có job riêng.
/// </summary>
public class LobbyCleanupJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LobbyCleanupJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(60);

    public LobbyCleanupJob(IServiceScopeFactory scopeFactory, ILogger<LobbyCleanupJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var inviteRepo = scope.ServiceProvider.GetRequiredService<ILobbyInviteRepository>();
                var hubService = scope.ServiceProvider.GetRequiredService<BoardVerse.Services.IServices.ILobbyHubService>();

                await ExpireInvitesAsync(inviteRepo, hubService);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("LobbyCleanupJob stopped (host shutdown).");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LobbyCleanupJob failed");
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
    }

    /// <summary>
    /// GAP-R6-BJ-FIX Fix: dùng <see cref="ILobbyInviteRepository.ExpireBatchAsync"/>
    /// (atomic ExecuteUpdateAsync) thay vì load → mutate → save.
    /// Tránh cluster race: 2 instance pick cùng invite sẽ cùng set Expired + cùng
    /// RespondedAt → duplicate notify + push broadcast.
    /// </summary>
    private async Task ExpireInvitesAsync(ILobbyInviteRepository inviteRepo, BoardVerse.Services.IServices.ILobbyHubService hubService)
    {
        var now = DateTime.UtcNow;
        // Atomic UPDATE WHERE Status=Pending AND ExpiresAt<=now SET Status=Expired, RespondedAt=now.
        // ExecuteUpdateAsync returns rowsAffected — cluster-safe (Postgres MVCC).
        var affectedRows = await inviteRepo.ExpireBatchAsync(now, batchSize: 500);

        if (affectedRows == 0)
        {
            return;
        }

        _logger.LogInformation("Expired {Count} lobby invites", affectedRows);

        // Realtime notify: lấy distinct LobbyId từ các invite vừa expire để broadcast update.
        // (Approach đơn giản: query back LobbyId của các invite vừa đổi status. Có thể tối ưu
        //  sau bằng cách trả về LobbyId từ ExpireBatchAsync, nhưng trade-off query extra.)
        var expiredLobbyIds = await inviteRepo.GetLobbyIdsForExpiredInvitesAsync(now);
        foreach (var lobbyId in expiredLobbyIds)
        {
            try
            {
                await hubService.NotifyLobbyUpdated(lobbyId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify lobby {LobbyId} about expired invites", lobbyId);
            }
        }
    }
}