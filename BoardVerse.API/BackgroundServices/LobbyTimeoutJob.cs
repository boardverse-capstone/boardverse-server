using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Background job xử lý các phòng chờ bị hủy tự động khi chưa đủ người.
/// BR-08: Tự động chuyển OPEN → TIMEOUT_FAILED nếu trước giờ hẹn X phút
/// mà số lượng thành viên vẫn chưa đạt quy mô tối thiểu của tựa game.
/// </summary>
public class LobbyTimeoutJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LobbyTimeoutJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    /// <summary>Lobby không có ScheduledStartTime mà tồn tại quá thời gian này → coi như timeout.</summary>
    private static readonly TimeSpan OrphanLobbyTimeout = TimeSpan.FromHours(24);

    public LobbyTimeoutJob(IServiceProvider serviceProvider, ILogger<LobbyTimeoutJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LobbyTimeoutJob started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessTimedOutLobbiesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in LobbyTimeoutJob");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ProcessTimedOutLobbiesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
        var hubService = scope.ServiceProvider.GetRequiredService<ILobbyHubService>();

        var now = DateTime.UtcNow;

        // Case 1: Lobby có ScheduledStartTime → so với (ScheduledStartTime - CancellationLeadTimeMinutes)
        var scheduledTimedOut = await db.Lobbies
            .Include(l => l.Members)
            .Include(l => l.GameTemplate)
            .Where(l => l.Status == LobbyStatus.Open &&
                        l.ScheduledStartTime != null &&
                        l.ScheduledStartTime.Value.AddMinutes(-l.CancellationLeadTimeMinutes) <= now)
            .ToListAsync(stoppingToken);

        // Case 2 (fix): Lobby không có ScheduledStartTime (orphan) mà tồn tại > 24 giờ → coi như timeout,
        // tránh bị kẹt mãi mãi ở OPEN khi Host quên đặt giờ.
        var orphanCutoff = now - OrphanLobbyTimeout;
        var orphanTimedOut = await db.Lobbies
            .Include(l => l.Members)
            .Include(l => l.GameTemplate)
            .Where(l => l.Status == LobbyStatus.Open &&
                        l.ScheduledStartTime == null &&
                        l.CreatedAt <= orphanCutoff)
            .ToListAsync(stoppingToken);

        var timedOutLobbies = scheduledTimedOut
            .Concat(orphanTimedOut)
            .DistinctBy(l => l.Id)
            .ToList();

        if (timedOutLobbies.Count == 0)
            return;

        _logger.LogInformation("Found {Count} lobbies to check for timeout (scheduled={Scheduled}, orphan={Orphan})",
            timedOutLobbies.Count, scheduledTimedOut.Count, orphanTimedOut.Count);

        var transitioned = new List<Guid>();

        foreach (var lobby in timedOutLobbies)
        {
            var minPlayers = lobby.GameTemplate?.MinPlayers ?? 2;

            var isOrphan = lobby.ScheduledStartTime == null;
            var memberShortage = lobby.Members.Count < minPlayers;

            // Orphan luôn timeout (kể cả đủ người) vì không thể check-in.
            // Scheduled timeout chỉ apply khi thiếu người.
            if (isOrphan || memberShortage)
            {
                lobby.Status = LobbyStatus.TimeoutFailed;
                transitioned.Add(lobby.Id);

                if (isOrphan)
                {
                    _logger.LogInformation(
                        "Lobby {LobbyId} timed out as orphan (no ScheduledStartTime, age > {CutoffHours}h)",
                        lobby.Id, OrphanLobbyTimeout.TotalHours);
                }
                else
                {
                    _logger.LogInformation(
                        "Lobby {LobbyId} timed out with {MemberCount} members (min: {MinPlayers})",
                        lobby.Id, lobby.Members.Count, minPlayers);
                }
            }
        }

        if (transitioned.Count == 0)
            return;

        await db.SaveChangesAsync(stoppingToken);

        // Realtime: notify từng lobby mà vừa timeout.
        foreach (var lobbyId in transitioned)
        {
            try
            {
                await hubService.NotifyLobbyTimeout(lobbyId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast LobbyTimeout for {LobbyId}", lobbyId);
            }
        }

        _logger.LogInformation("Processed {Count} timed out lobbies.", transitioned.Count);
    }
}