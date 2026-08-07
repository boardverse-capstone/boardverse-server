using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Messages;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// Background job xử lý các phòng chờ bị hủy tự động khi chưa đủ người.
/// BR-08: Tự động chuyển OPEN → TIMEOUT_FAILED nếu trước giờ hẹn X phút
/// mà số lượng thành viên vẫn chưa đạt quy mô tối thiểu của tựa game.
/// BR-LOBBY-READY-03: FULL + 20 phút không ai Ready → TIMEOUT_FAILED (LobbyReadyTimeout).
/// BR-LOBBY-READY-04: Scheduled-timeout đếm readyCount thay vì memberCount.
/// </summary>
public class LobbyTimeoutJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LobbyTimeoutJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1);

    /// <summary>Lobby không có ScheduledStartTime mà tồn tại quá thời gian này → coi như timeout.</summary>
    private static readonly TimeSpan OrphanLobbyTimeout = TimeSpan.FromHours(24);

    /// <summary>BR-LOBBY-READY-03: Số phút tối đa sau Full mà không có ai Ready → timeout.</summary>
    public static readonly TimeSpan ReadyTimeoutWindow = TimeSpan.FromMinutes(Lobby.ReadyTimeoutMinutes);

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
        // GAP #16 fix: cluster-safe — dùng FOR UPDATE SKIP LOCKED để nhiều instance không pick trùng.
        var scheduledTimedOut = await db.Lobbies
            .FromSqlRaw(
                "SELECT * FROM \"Lobbies\" WHERE \"Status\" = {0} " +
                "AND \"ScheduledStartTime\" IS NOT NULL " +
                "AND \"ScheduledStartTime\" - (\"CancellationLeadTimeMinutes\" * INTERVAL '1 minute') <= {1} " +
                "FOR UPDATE SKIP LOCKED",
                LobbyStatus.Open.ToString(), now)
            .Include(l => l.Members)
            .Include(l => l.GameTemplate)
            .Include(l => l.Cafe)
            .AsSplitQuery()
            .ToListAsync(stoppingToken);

        // Case 2 (fix): Lobby không có ScheduledStartTime (orphan) mà tồn tại > 24 giờ → coi như timeout,
        // tránh bị kẹt mãi mãi ở OPEN khi Host quên đặt giờ.
        var orphanCutoff = now - OrphanLobbyTimeout;
        var orphanTimedOut = await db.Lobbies
            .FromSqlRaw(
                "SELECT * FROM \"Lobbies\" WHERE \"Status\" = {0} " +
                "AND \"ScheduledStartTime\" IS NULL " +
                "AND \"CreatedAt\" <= {1} " +
                "FOR UPDATE SKIP LOCKED",
                LobbyStatus.Open.ToString(), orphanCutoff)
            .Include(l => l.Members)
            .Include(l => l.GameTemplate)
            .Include(l => l.Cafe)
            .AsSplitQuery()
            .ToListAsync(stoppingToken);

        // Case 3 (BR-LOBBY-READY-03): Lobby đã FULL + FullAt > 20 phút + chưa có ai Ready → timeout.
        // Dùng `LobbyReadyTimeoutReason` để phân biệt với timeout vì thiếu người.
        var readyCutoff = now - ReadyTimeoutWindow;
        var fullButNotReady = await db.Lobbies
            .FromSqlRaw(
                "SELECT * FROM \"Lobbies\" WHERE \"Status\" = {0} " +
                "AND \"FullAt\" IS NOT NULL " +
                "AND \"FullAt\" <= {1} " +
                "FOR UPDATE SKIP LOCKED",
                LobbyStatus.Full.ToString(), readyCutoff)
            .Include(l => l.Members)
            .Include(l => l.GameTemplate)
            .Include(l => l.Cafe)
            .AsSplitQuery()
            .ToListAsync(stoppingToken);

        // Lọc thêm ở C# (sau khi load Members) để chỉ timeout những lobby thật sự chưa có ai Ready.
        var fullButNotReadyFiltered = fullButNotReady
            .Where(l => !l.Members.Any(m => m.IsActive && m.Status == LobbyMemberStatus.Ready))
            .ToList();

        var timedOutLobbies = scheduledTimedOut
            .Concat(orphanTimedOut)
            .Concat(fullButNotReadyFiltered)
            .DistinctBy(l => l.Id)
            .ToList();

        if (timedOutLobbies.Count == 0)
            return;

        _logger.LogInformation("Found {Count} lobbies to check for timeout (scheduled={Scheduled}, orphan={Orphan}, fullNotReady={FullNotReady})",
            timedOutLobbies.Count, scheduledTimedOut.Count, orphanTimedOut.Count, fullButNotReadyFiltered.Count);

        // GAP #16 fix: SKIP LOCKED chỉ có hiệu lực trong transaction. Mở transaction 1 lần cho cả batch,
        // commit sau khi đã flip status. Foreach loop chỉ mutate entity đã lock trong tx này.
        await using var batchTx = await db.Database.BeginTransactionAsync(stoppingToken);

        var transitioned = new List<(Guid LobbyId, Guid? CafeId, string CafeName, DateTime? ScheduledTime, string Reason, List<Guid> MemberIds)>();

        foreach (var lobby in timedOutLobbies)
        {
            var minPlayers = lobby.GameTemplate?.MinPlayers ?? 2;

            var isOrphan = lobby.ScheduledStartTime == null;

            // BR-LOBBY-READY-04: Đếm readyCount thay vì memberCount cho scheduled-timeout.
            // Nếu ≥ minPlayers đã Ready → KHÔNG timeout (lobby vẫn còn khả thi).
            // Nếu readyCount < minPlayers → timeout vì nhóm chưa cam kết.
            var readyCount = lobby.Members.Count(m => m.IsActive && m.Status == LobbyMemberStatus.Ready);
            var memberShortage = readyCount < minPlayers;

            // Đánh dấu source case để dùng đúng reason khi timeout.
            var isFullNotReady = fullButNotReadyFiltered.Any(l => l.Id == lobby.Id);

            // Orphan luôn timeout (kể cả đủ người) vì không thể check-in.
            // Scheduled timeout chỉ apply khi thiếu ready.
            // FullNotReady timeout áp dụng riêng (khi không ai Ready sau 20p).
            var shouldTimeout = isOrphan || memberShortage || isFullNotReady;
            if (shouldTimeout)
            {
                lobby.Status = LobbyStatus.TimeoutFailed;

                string reason;
                if (isFullNotReady)
                {
                    reason = "LobbyReadyTimeout";
                }
                else if (isOrphan)
                {
                    reason = "OrphanLobbyExpired";
                }
                else
                {
                    reason = "NotEnoughReadyMembers";
                }

                transitioned.Add((
                    lobby.Id,
                    lobby.CafeId,
                    lobby.Cafe?.Name ?? "Unknown Cafe",
                    lobby.ScheduledStartTime,
                    reason,
                    lobby.Members.Where(m => m.IsActive).Select(m => m.UserId).ToList()));

                if (isFullNotReady)
                {
                    _logger.LogInformation(
                        "Lobby {LobbyId} timed out: FULL at {FullAt} but no member Ready after {TimeoutMinutes}m",
                        lobby.Id, lobby.FullAt, Lobby.ReadyTimeoutMinutes);
                }
                else if (isOrphan)
                {
                    _logger.LogInformation(
                        "Lobby {LobbyId} timed out as orphan (no ScheduledStartTime, age > {CutoffHours}h)",
                        lobby.Id, OrphanLobbyTimeout.TotalHours);
                }
                else
                {
                    _logger.LogInformation(
                        "Lobby {LobbyId} timed out with {ReadyCount}/{MinPlayers} ready members",
                        lobby.Id, readyCount, minPlayers);
                }
            }
        }

        if (transitioned.Count == 0)
            return;

        await db.SaveChangesAsync(stoppingToken);
        await batchTx.CommitAsync(stoppingToken);

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