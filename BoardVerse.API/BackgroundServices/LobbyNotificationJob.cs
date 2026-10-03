using BoardVerse.Core.Constants;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// N-01: Gửi notification ở 4 milestone (BR-NEW-13).
/// Chạy mỗi 5 phút.
/// 
/// Milestones:
/// - 48h trước recruitmentDeadline → host
/// - 24h trước recruitmentDeadline → host + members
/// - 2h trước preferredStartTime → host + members (nếu có preferredStartTime)
/// - 30p trước preferredStartTime → host + members (nếu có preferredStartTime)
/// 
/// Mỗi milestone chỉ gửi 1 lần (tracked qua LobbyNotificationSent).
/// </summary>
public class LobbyNotificationJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LobbyNotificationJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    /// <summary>Biên độ trước milestone để trigger (ví dụ: 48h ± 5 phút).</summary>
    private static readonly TimeSpan TriggerWindow = TimeSpan.FromMinutes(5);

    /// <summary>Chỉ query lobbies có recruitmentDeadline trong khoảng [now, now + MaxNotificationHorizon].</summary>
    private static readonly TimeSpan MaxNotificationHorizon = TimeSpan.FromHours(52); // 48h + 2×TriggerWindow

    /// <summary>Batch size để tránh query quá nhiều rows mỗi lần chạy.</summary>
    private const int BatchSize = 200;

    public LobbyNotificationJob(IServiceProvider serviceProvider, ILogger<LobbyNotificationJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LobbyNotificationJob started (interval=5m, window=5m, maxHorizon=52h, batchSize={BatchSize}).", BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessNotificationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("LobbyNotificationJob stopped (host shutdown).");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in LobbyNotificationJob");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ProcessNotificationsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
        var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();

        var now = DateTime.UtcNow;

        // Query lobbies cần notification (chỉ OPEN, Viable, Full, PendingCafeApproval, PendingActivation)
        var activeStatuses = new[]
        {
            LobbyStatus.Open,
            LobbyStatus.Viable,
            LobbyStatus.Full,
            LobbyStatus.PendingCafeApproval,
            LobbyStatus.PendingActivation
        };

        var lobbies = await db.Lobbies
            .Where(l => activeStatuses.Contains(l.Status))
            .Where(l => l.RecruitmentDeadline >= now && l.RecruitmentDeadline <= now.Add(MaxNotificationHorizon))
            .OrderBy(l => l.RecruitmentDeadline)
            .Take(BatchSize)
            .Include(l => l.Members.Where(m => m.IsActive))
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync(ct);

        if (lobbies.Count == 0)
            return;

        // Lookup Cafe.Name riêng (chỉ cần 2 cột) tránh EF generate SELECT c.*
        // bao gồm các cột đã gỡ khỏi DB (vd TableLayoutJson).
        var cafeIds = lobbies
            .Where(l => l.CafeId.HasValue)
            .Select(l => l.CafeId!.Value)
            .Distinct()
            .ToList();

        var cafeNameById = cafeIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Cafes
                .Where(c => cafeIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Name })
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var sentRecords = await db.LobbyNotificationSents
            .Where(s => lobbies.Select(l => l.Id).Contains(s.LobbyId))
            .ToListAsync(ct);

        var grouped = sentRecords.GroupBy(s => new { s.LobbyId, s.Milestone }).ToDictionary(g => g.Key, g => g.First());

        var newRecords = new List<LobbyNotificationSent>();

        // Local helper: lookup CafeName từ dictionary (tránh load navigation Cafe vốn
        // chứa các cột đã gỡ khỏi DB như TableLayoutJson).
        string GetCafeName(Lobby l) =>
            l.CafeId.HasValue && cafeNameById.TryGetValue(l.CafeId.Value, out var n) ? n : "";

        // FIX TZ-LOBBY-NOTIF-01 (2026-10-03): trả về DateTime Kind=Utc đại diện cho
        // scheduledStart của lobby, dùng được trong phép so sánh với `DateTime.UtcNow`.
        //
        // Thứ tự ưu tiên:
        //  1. `lobby.ScheduledStartTime` (MIRROR từ Reservation.ScheduledStartTime, đã là Kind=Utc
        //     sau TZ-DT-UTC-02 2026-10-02). Áp dụng cho lobby tạo sau fix.
        //  2. `BuildScheduledStartEndFromPreferred(playDate, preferredStart, preferredEnd)` —
        //     build lại Kind=Utc từ raw VN local inputs. Áp dụng cho lobby cũ chưa có
        //     ScheduledStartTime (lobby legacy hoặc pre-fix data).
        //
        // Sau khi có Kind=Utc, mọi phép so sánh với `DateTime.UtcNow` (Kind=Utc) trong
        // ProcessMilestoneAsync đều cho kết quả chính xác — không còn lệch 7 giờ.
        DateTime ResolveScheduledStartUtc(Lobby l)
        {
            if (l.ScheduledStartTime.HasValue)
            {
                // Nếu data cũ vẫn còn Kind=Unspecified (raw VN local), ToUtcAssumingVietnamLocal sẽ
                // convert đúng theo convention của project. Nếu Kind=Utc thì trả nguyên giá trị.
                return CafeSchedule.ToUtcAssumingVietnamLocal(l.ScheduledStartTime.Value);
            }

            if (l.PlayDate.HasValue && l.PreferredStartTime.HasValue)
            {
                var endTime = l.PreferredEndTime ?? l.PreferredStartTime.Value;
                var (start, _) = CafeSchedule.BuildScheduledStartEndFromPreferred(
                    l.PlayDate.Value, l.PreferredStartTime.Value, endTime);
                return start;
            }

            // Fallback: trả DateTime.MinValue (Kind=Utc) để ProcessMilestoneAsync check `referenceTime.HasValue`
            // và skip — an toàn hơn throw exception trong hot path.
            return DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
        }

        foreach (var lobby in lobbies)
        {
            var currentPlayers = lobby.Members.Count(m => m.IsActive);
            var hostUserId = lobby.Members.FirstOrDefault(m => m.IsHost)?.UserId ?? lobby.HostUserId;

            // ── Milestone 1: 48h trước recruitmentDeadline → chỉ host ──
            await ProcessMilestoneAsync(
                lobby, hostUserId, currentPlayers,
                LobbyNotificationMilestone.At48hRecruitmentDeadline,
                lobby.RecruitmentDeadline,
                TimeSpan.FromHours(48),
                $"Phòng '{lobby.Description ?? "Không tên"}' còn 48 giờ để tuyển đủ người. Hiện có {currentPlayers}/{lobby.MinPlayers} thành viên.",
                "Lobby48hReminder",
                ct);

            // ── Milestone 2: 24h trước recruitmentDeadline → host + members ──
            await ProcessMilestoneAsync(
                lobby, hostUserId, currentPlayers,
                LobbyNotificationMilestone.At24hRecruitmentDeadline,
                lobby.RecruitmentDeadline,
                TimeSpan.FromHours(24),
                $"Phòng '{lobby.Description ?? "Không tên"}' sắp đến deadline. Còn thiếu {Math.Max(0, lobby.MinPlayers - currentPlayers)} người.",
                "Lobby24hReminder",
                ct);

            // ── Milestone 3: 2h trước preferredStartTime → host + members ──
            // FIX TZ-LOBBY-NOTIF-01 (2026-10-03): trước đây dùng
            // `lobby.PlayDate.Value.ToDateTime(lobby.PreferredStartTime.Value)` trả DateTime
            // Kind=Unspecified, raw ticks = VN local wall clock. Khi so sánh với `DateTime.UtcNow`
            // (Kind=Utc) trong ProcessMilestoneAsync, C# KHÔNG tự convert Kind → raw tick subtraction
            // cho kết quả sai lệch 7 giờ (lobby tối 20:45 VN sẽ trigger sớm 7 tiếng).
            //
            // Sau fix: ưu tiên dùng `lobby.ScheduledStartTime` (MIRROR từ Reservation.ScheduledStartTime,
            // đã là Kind=Utc sau TZ-DT-UTC-02 2026-10-02). Nếu lobby cũ chưa có, fallback về
            // `BuildScheduledStartEndFromPreferred` để build Kind=Utc đúng quy ước.
            if (lobby.PreferredStartTime.HasValue && lobby.PlayDate.HasValue)
            {
                var scheduledTime = ResolveScheduledStartUtc(lobby);
                await ProcessMilestoneAsync(
                    lobby, hostUserId, currentPlayers,
                    LobbyNotificationMilestone.At2hPreferredStart,
                    scheduledTime,
                    TimeSpan.FromHours(2),
                    $"Phòng '{lobby.Description ?? "Không tên"}' bắt đầu sau 2 giờ tại {GetCafeName(lobby) ?? "quán"}.",
                    "Lobby2hReminder",
                    ct);
            }

            // ── Milestone 4: 30p trước preferredStartTime → host + members ──
            if (lobby.PreferredStartTime.HasValue && lobby.PlayDate.HasValue)
            {
                var scheduledTime = ResolveScheduledStartUtc(lobby);
                await ProcessMilestoneAsync(
                    lobby, hostUserId, currentPlayers,
                    LobbyNotificationMilestone.At30mPreferredStart,
                    scheduledTime,
                    TimeSpan.FromMinutes(30),
                    $"Phòng '{lobby.Description ?? "Không tên"}' bắt đầu sau 30 phút. Hãy chuẩn bị đến {GetCafeName(lobby) ?? "quán"} nhé!",
                    "Lobby30mReminder",
                    ct);
            }
        }

        if (newRecords.Count > 0)
        {
            db.LobbyNotificationSents.AddRange(newRecords);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("LobbyNotificationJob: sent {Count} notifications across {LobbyCount} lobbies (batch {BatchSize}).",
                newRecords.Count, lobbies.Count, BatchSize);
        }

        async Task ProcessMilestoneAsync(
            Lobby lobby,
            Guid hostUserId,
            int currentPlayers,
            LobbyNotificationMilestone milestone,
            DateTime? referenceTime,
            TimeSpan targetOffset,
            string body,
            string notificationType,
            CancellationToken ct)
        {
            if (!referenceTime.HasValue)
                return;

            var key = new { LobbyId = lobby.Id, Milestone = milestone };
            if (grouped.ContainsKey(key) || newRecords.Any(r => r.LobbyId == lobby.Id && r.Milestone == milestone))
                return;

            var targetTime = referenceTime.Value - targetOffset;
            var diff = (targetTime - now).TotalMinutes;

            // Trigger nếu đang trong window: [targetTime - TriggerWindow, targetTime + TriggerWindow]
            if (diff < -TriggerWindow.TotalMinutes || diff > TriggerWindow.TotalMinutes)
                return;

            var memberIds = lobby.Members
                .Where(m => m.IsActive)
                .Select(m => m.UserId)
                .ToList();

            var recipients = milestone == LobbyNotificationMilestone.At48hRecruitmentDeadline
                ? new List<Guid> { hostUserId }
                : memberIds;

            if (recipients.Count == 0)
                return;

            try
            {
                await pushService.SendToUsersAsync(recipients, new PushNotificationPayload
                {
                    Type = notificationType,
                    Title = milestone switch
                    {
                        LobbyNotificationMilestone.At48hRecruitmentDeadline => "Còn 48 giờ tuyển người",
                        LobbyNotificationMilestone.At24hRecruitmentDeadline => "Còn 24 giờ — gần đến deadline",
                        LobbyNotificationMilestone.At2hPreferredStart => "Còn 2 giờ — chuẩn bị đến quán",
                        LobbyNotificationMilestone.At30mPreferredStart => "Còn 30 phút — bắt đầu sớm thôi!",
                        _ => "Nhắc nhở phòng chờ"
                    },
                    Body = body,
                    Data = new Dictionary<string, string>
                    {
                        { "lobbyId", lobby.Id.ToString() },
                        { "cafeId", lobby.CafeId?.ToString() ?? "" },
                        { "cafeName", GetCafeName(lobby) },
                        { "currentPlayers", currentPlayers.ToString() },
                        { "minPlayers", lobby.MinPlayers.ToString() },
                        { "milestone", milestone.ToString() }
                    }
                });

                newRecords.Add(new LobbyNotificationSent
                {
                    Id = Guid.NewGuid(),
                    LobbyId = lobby.Id,
                    Milestone = milestone,
                    SentAt = now,
                    RecipientUserId = null // broadcasted to multiple
                });

                _logger.LogInformation(
                    "LobbyNotificationJob: sent {Milestone} for lobby {LobbyId} to {Count} users",
                    milestone, lobby.Id, recipients.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send {Milestone} notification for lobby {LobbyId}",
                    milestone, lobby.Id);
            }
        }
    }
}
