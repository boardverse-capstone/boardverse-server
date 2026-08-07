using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Data;
using BoardVerse.Services.IServices;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.API.BackgroundServices;

/// <summary>
/// N-02: Cảnh báo lobby có nguy cơ fail (BR-NEW-14).
/// 
/// Sau 50% thời gian tuyển mà <c>currentPlayers &lt; 50% × minPlayers</c>
/// → gửi notification cho host kèm 4 đề xuất:
/// (a) Chia sẻ link mời bạn
/// (b) Đổi timeSlot khác
/// (c) Hủy lobby (hoàn cọc)
/// (d) Boost lobby (tương lai)
///
/// Chạy mỗi 5 phút.
/// </summary>
public class LobbyAtRiskWarningJob : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LobbyAtRiskWarningJob> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5);

    public LobbyAtRiskWarningJob(IServiceProvider serviceProvider, ILogger<LobbyAtRiskWarningJob> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LobbyAtRiskWarningJob started (interval=5m).");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAtRiskLobbiesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in LobbyAtRiskWarningJob");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task CheckAtRiskLobbiesAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BoardVerseDbContext>();
        var pushService = scope.ServiceProvider.GetRequiredService<IPushNotificationService>();

        var now = DateTime.UtcNow;

        // Chỉ xét lobby đang tuyển người và có recruitmentDeadline trong tương lai
        var recruitingStatuses = new[]
        {
            LobbyStatus.Open,
            LobbyStatus.Viable,
            LobbyStatus.PendingCafeApproval,
            LobbyStatus.PendingActivation
        };

        // Lấy lobbies còn nhận member (chưa Full)
        var lobbies = await db.Lobbies
            .Where(l => recruitingStatuses.Contains(l.Status)
                        && l.Status != LobbyStatus.Full
                        && l.Status != LobbyStatus.Closed
                        && l.Status != LobbyStatus.InProgress
                        && l.RecruitmentDeadline.HasValue
                        && l.RecruitmentDeadline > now
                        && l.CreatedAt < now)
            .Include(l => l.Members.Where(m => m.IsActive))
            .Include(l => l.Cafe)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync(ct);

        if (lobbies.Count == 0)
            return;

        // Lọc bỏ những lobby đã được cảnh báo at-risk
        var atRiskSent = await db.LobbyAtRiskWarnings
            .Where(w => lobbies.Select(l => l.Id).Contains(w.LobbyId))
            .Select(w => w.LobbyId)
            .ToListAsync(ct);

        var newWarnings = new List<LobbyAtRiskWarning>();

        foreach (var lobby in lobbies)
        {
            if (atRiskSent.Contains(lobby.Id))
                continue;

            var currentPlayers = lobby.Members.Count(m => m.IsActive);
            var minPlayers = lobby.MinPlayers;
            var halfMinPlayers = minPlayers / 2.0;

            // currentPlayers >= 50% × minPlayers → không có nguy cơ
            if (currentPlayers >= halfMinPlayers)
                continue;

            // Tính thời gian đã tuyển và tổng thời gian tuyển
            var createdAt = lobby.CreatedAt;
            var deadline = lobby.RecruitmentDeadline!.Value;
            var totalDuration = (deadline - createdAt).TotalMinutes;
            var elapsed = (now - createdAt).TotalMinutes;

            if (totalDuration <= 0)
                continue;

            // Nếu chưa đạt 50% thời gian → bỏ qua
            if (elapsed < totalDuration * 0.5)
                continue;

            // Trigger: đã qua 50% thời gian tuyển VÀ currentPlayers < 50% × minPlayers
            var hostUserId = lobby.Members.FirstOrDefault(m => m.IsHost)?.UserId ?? lobby.HostUserId;
            var lobbyUrl = $"boardverse://lobby/{lobby.Id}";
            var missingPlayers = Math.Max(0, minPlayers - currentPlayers);

            var body = missingPlayers == 1
                ? $"Phòng '{lobby.Description ?? "Không tên"}' còn thiếu 1 người để đủ điều kiện xác nhận. Hãy hành động ngay!"
                : $"Phòng '{lobby.Description ?? "Không tên"}' còn thiếu {missingPlayers} người để đủ điều kiện xác nhận. Hãy hành động ngay!";

            try
            {
                await pushService.SendToUsersAsync(new[] { hostUserId }, new PushNotificationPayload
                {
                    Type = "LobbyAtRisk",
                    Title = "Phòng chờ có nguy cơ không đủ người!",
                    Body = body,
                    Data = new Dictionary<string, string>
                    {
                        { "lobbyId", lobby.Id.ToString() },
                        { "cafeId", lobby.CafeId?.ToString() ?? "" },
                        { "cafeName", lobby.Cafe?.Name ?? "" },
                        { "currentPlayers", currentPlayers.ToString() },
                        { "minPlayers", minPlayers.ToString() },
                        { "missingPlayers", missingPlayers.ToString() },
                        { "deadline", deadline.ToString("o") },
                        { "actionShareLink", lobbyUrl },
                        // Các action options cho client xử lý deeplink/callback
                        { "actionChangeTimeSlot", $"boardverse://lobby/{lobby.Id}/change-timeslot" },
                        { "actionCancel", $"boardverse://lobby/{lobby.Id}/cancel" },
                        { "actionBoost", $"boardverse://lobby/{lobby.Id}/boost" }
                    }
                });

                newWarnings.Add(new LobbyAtRiskWarning
                {
                    Id = Guid.NewGuid(),
                    LobbyId = lobby.Id,
                    WarnedAt = now,
                    CurrentPlayers = currentPlayers,
                    MinPlayers = minPlayers
                });

                _logger.LogInformation(
                    "LobbyAtRiskWarningJob: sent at-risk warning for lobby {LobbyId} ({CurrentPlayers}/{MinPlayers})",
                    lobby.Id, currentPlayers, minPlayers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send at-risk warning for lobby {LobbyId}", lobby.Id);
            }
        }

        if (newWarnings.Count > 0)
        {
            db.LobbyAtRiskWarnings.AddRange(newWarnings);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("LobbyAtRiskWarningJob: warned {Count} at-risk lobbies.", newWarnings.Count);
        }
    }
}
