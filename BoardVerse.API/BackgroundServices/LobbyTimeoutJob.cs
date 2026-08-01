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
            .Include(l => l.Cafe)
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
            .Include(l => l.Cafe)
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

        var transitioned = new List<(Guid LobbyId, Guid? CafeId, string CafeName, DateTime? ScheduledTime, string Reason, List<Guid> MemberIds)>();

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

                var reason = isOrphan ? "OrphanLobbyExpired" : "NotEnoughMembers";
                transitioned.Add((
                    lobby.Id,
                    lobby.CafeId,
                    lobby.Cafe?.Name ?? "Unknown Cafe",
                    lobby.ScheduledStartTime,
                    reason,
                    lobby.Members.Where(m => m.IsActive).Select(m => m.UserId).ToList()));

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

        // Realtime: notify từng lobby mà vừa timeout — task #9 dùng payload chi tiết cho mobile.
        // (NotifyLobbyTimeout giữ nguyên cho backward-compat với mobile client cũ;
        //  NotifyLobbyAutoCancelled bổ sung payload mới với cafeName/scheduledTime/reason.)
        // Mobile gap #9: Push FCM cho từng member đang hoạt động trong lobby
        // (app đã đóng/background vẫn nhận được notification để mở lại).
        var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();
        foreach (var t in transitioned)
        {
            try
            {
                await hubService.NotifyLobbyTimeout(t.LobbyId);
                await hubService.NotifyLobbyAutoCancelled(t.LobbyId, t.CafeId ?? Guid.Empty, t.CafeName, t.ScheduledTime, t.Reason);

                // FCM push cho tất cả members (kể cả host) — trừ khi list rỗng.
                if (t.MemberIds.Count > 0)
                {
                    var scheduledText = t.ScheduledTime.HasValue
                        ? t.ScheduledTime.Value.ToString("HH:mm dd/MM")
                        : "không xác định";
                    await pushService.SendToUsersAsync(t.MemberIds, new PushNotificationPayload
                    {
                        Type = "LobbyAutoCancelled",
                        Title = "Phòng chờ đã bị hủy",
                        Body = $"Phòng chờ tại {t.CafeName} ({scheduledText}) đã bị hủy do không đủ thành viên trước giờ hẹn.",
                        Data = new Dictionary<string, string>
                        {
                            { "lobbyId", t.LobbyId.ToString() },
                            { "cafeId", (t.CafeId ?? Guid.Empty).ToString() },
                            { "cafeName", t.CafeName },
                            { "scheduledTime", t.ScheduledTime?.ToString("o") ?? "" },
                            { "reason", t.Reason }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast LobbyAutoCancelled for {LobbyId}", t.LobbyId);
            }
        }

        _logger.LogInformation("Processed {Count} timed out lobbies.", transitioned.Count);
    }
}