using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.IRepositories;
using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Background job tự động:
/// 1. Expire LobbyInvites quá 24h chưa được accept.
/// 2. Auto-cancel các lobby Open quá thời gian chờ mà chưa đạt MinPlayers (BR-08).
/// Chạy mỗi 60 giây.
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
                var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
                var inviteRepo = scope.ServiceProvider.GetRequiredService<ILobbyInviteRepository>();
                var lobbyRepo = scope.ServiceProvider.GetRequiredService<ILobbyRepository>();
                var hubService = scope.ServiceProvider.GetRequiredService<BoardVerse.Services.IServices.ILobbyHubService>();

                await ExpireInvitesAsync(db, hubService);
                await TimeoutLobbiesAsync(db, lobbyRepo, hubService);
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

    private async Task ExpireInvitesAsync(BoardVerseDbContext db, BoardVerse.Services.IServices.ILobbyHubService hubService)
    {
        var now = DateTime.UtcNow;
        var expired = await db.LobbyInvites
            .Where(i => i.Status == LobbyInviteStatus.Pending && i.ExpiresAt <= now)
            .ToListAsync();

        if (expired.Count == 0) return;

        foreach (var inv in expired)
        {
            inv.Status = LobbyInviteStatus.Expired;
            inv.RespondedAt = now;
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("Expired {Count} lobby invites", expired.Count);

        // Realtime notify các lobby có invite expired
        var lobbyIds = expired.Select(e => e.LobbyId).Distinct();
        foreach (var lobbyId in lobbyIds)
        {
            await hubService.NotifyLobbyUpdated(lobbyId);
        }
    }

    private async Task TimeoutLobbiesAsync(
        BoardVerseDbContext db,
        ILobbyRepository lobbyRepo,
        BoardVerse.Services.IServices.ILobbyHubService hubService)
    {
        var now = DateTime.UtcNow;

        // Tìm lobby Open + ScheduledStartTime - CancellationLeadTimeMinutes <= now
        var candidates = await db.Lobbies
            .Include(l => l.Members)
            .Where(l => l.Status == LobbyStatus.Open
                && l.ScheduledStartTime.HasValue
                && l.ScheduledStartTime.Value.AddMinutes(-l.CancellationLeadTimeMinutes) <= now)
            .ToListAsync();

        foreach (var lobby in candidates)
        {
            var activeCount = lobby.Members.Count(m => m.IsActive);
            if (activeCount < lobby.MinPlayers)
            {
                lobby.Status = LobbyStatus.TimeoutFailed;
                lobby.ClosedAt = now;
                lobby.ClosedReason = $"Lobby tự động hủy do không đủ {lobby.MinPlayers} người trước giờ hẹn.";

                // Cancel pending invites
                var pending = await db.LobbyInvites
                    .Where(i => i.LobbyId == lobby.Id && i.Status == LobbyInviteStatus.Pending)
                    .ToListAsync();
                foreach (var inv in pending)
                {
                    inv.Status = LobbyInviteStatus.Cancelled;
                    inv.RespondedAt = now;
                }

                _logger.LogInformation("Lobby {LobbyId} timed out (only {Active}/{Min} members)",
                    lobby.Id, activeCount, lobby.MinPlayers);
            }
        }

        if (candidates.Count > 0)
        {
            await db.SaveChangesAsync();

            foreach (var l in candidates.Where(c => c.Status == LobbyStatus.TimeoutFailed))
            {
                await hubService.NotifyLobbyTimeout(l.Id);
            }
        }
    }
}