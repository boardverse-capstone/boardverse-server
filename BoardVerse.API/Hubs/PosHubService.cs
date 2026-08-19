using BoardVerse.Services.IServices;
using Microsoft.AspNetCore.SignalR;

namespace BoardVerse.API.Hubs;

/// <summary>
/// Implementation của IPosHubService - gửi real-time notifications qua SignalR.
/// AC 1.4: Phát tín hiệu đồng bộ thông báo cho các thiết bị di động.
/// </summary>
public class PosHubService : IPosHubService
{
    private readonly IHubContext<PosHub> _hubContext;
    private readonly ILogger<PosHubService> _logger;

    public PosHubService(IHubContext<PosHub> hubContext, ILogger<PosHubService> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Notify tất cả members trong session rằng phiên đã được kích hoạt.
    /// AC 1.4: Mobile app nhận notification → hiển thị "Đang chơi tại quán".
    /// </summary>
    public async Task NotifySessionActivatedAsync(
        Guid sessionId,
        Guid cafeId,
        string cafeName,
        Guid hostId,
        IReadOnlyList<Guid> memberUserIds)
    {
        var notification = new
        {
            EventType = "SessionActivated",
            SessionId = sessionId,
            CafeId = cafeId,
            CafeName = cafeName,
            HostId = hostId,
            Timestamp = DateTime.UtcNow
        };

        // Notify session group
        await _hubContext.Clients
            .Group($"session:{sessionId}")
            .SendAsync("SessionActivated", notification);

        // Notify each member's personal channel
        foreach (var userId in memberUserIds)
        {
            await _hubContext.Clients
                .Group($"user:{userId}")
                .SendAsync("SessionActivated", notification);
        }

        _logger.LogInformation(
            "Notified {MemberCount} members about session {SessionId} activation",
            memberUserIds.Count,
            sessionId);
    }

    /// <summary>
    /// Notify một user cụ thể về thay đổi trạng thái session.
    /// </summary>
    public async Task NotifyUserSessionUpdateAsync(
        Guid userId,
        Guid sessionId,
        string status,
        string? message = null)
    {
        var notification = new
        {
            EventType = "SessionStatusChanged",
            SessionId = sessionId,
            Status = status,
            Message = message,
            Timestamp = DateTime.UtcNow
        };

        await _hubContext.Clients
            .Group($"user:{userId}")
            .SendAsync("SessionStatusChanged", notification);

        _logger.LogInformation(
            "Notified user {UserId} about session {SessionId} status change to {Status}",
            userId,
            sessionId,
            status);
    }

    /// <summary>
    /// Notify tất cả members trong session về thay đổi.
    /// </summary>
    public async Task NotifySessionUpdateAsync(
        Guid sessionId,
        string eventType,
        object? data = null)
    {
        var notification = new
        {
            EventType = eventType,
            SessionId = sessionId,
            Data = data,
            Timestamp = DateTime.UtcNow
        };

        await _hubContext.Clients
            .Group($"session:{sessionId}")
            .SendAsync(eventType, notification);

        _logger.LogInformation(
            "Notified session {SessionId} about event {EventType}",
            sessionId,
            eventType);
    }

    /// <summary>
    /// BR-REQUIRED §17.5: POS đóng phiên → SessionCompleted.
    /// </summary>
    public async Task NotifySessionCompleted(Guid lobbyId)
    {
        var notification = new
        {
            EventType = "SessionCompleted",
            LobbyId = lobbyId,
            Message = "Phiên chơi đã kết thúc. Cảm ơn bạn đã sử dụng BoardVerse!",
            Timestamp = DateTime.UtcNow
        };

        await _hubContext.Clients
            .Group($"session:{lobbyId}")
            .SendAsync("SessionCompleted", notification);

        _logger.LogInformation(
            "Notified session {LobbyId} about SessionCompleted",
            lobbyId);
    }

    /// <summary>
    /// GAP-XX: Notify tất cả clients trong group <c>session:{sessionId}</c> rằng session đã PAID.
    /// Được gọi từ <c>RealOutboxPublisher.PublishSessionCompletedAsync</c> khi đọc OutboxEvents.
    /// FE subscribe group này (PosHub.JoinSession) nhận event <c>SessionPaid</c> → tắt UI thanh toán.
    /// Walk-in session (LobbyId = null) vẫn được notify qua group session:{sessionId} —
    /// lobby-only fallback (NotifySessionCompleted theo lobbyId) sẽ miss trường hợp này.
    /// </summary>
    public async Task NotifySessionPaidAsync(
        Guid sessionId,
        Guid cafeId,
        Guid? lobbyId,
        decimal totalAmount,
        DateTime paidAt)
    {
        var notification = new
        {
            EventType = "SessionPaid",
            SessionId = sessionId,
            CafeId = cafeId,
            LobbyId = lobbyId,
            TotalAmount = totalAmount,
            PaidAt = paidAt,
            Timestamp = DateTime.UtcNow
        };

        // Push tới group session:{sessionId} — đây là group PosHub.JoinSession tạo ra.
        // Cả POS lẫn member mobile app đã subscribe group này đều nhận được.
        await _hubContext.Clients
            .Group($"session:{sessionId}")
            .SendAsync("SessionPaid", notification);

        _logger.LogInformation(
            "Notified session {SessionId} about SessionPaid (TotalAmount={Total}, LobbyId={LobbyId})",
            sessionId, totalAmount, lobbyId);
    }
}
