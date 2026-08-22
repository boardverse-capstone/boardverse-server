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
    /// GAP-R6-RT-03 Fix: trước đây nhầm lobbyId thành sessionId khi gửi group.
    /// Frontend subscribe <c>session:{sessionId}</c> qua PosHub.JoinSession → nếu param
    /// truyền vào là lobbyId thì FE không bao giờ nhận được event này.
    /// Signature đổi: nhận sessionId (bắt buộc) thay vì lobbyId — caller truyền đúng.
    /// </summary>
    public async Task NotifySessionCompleted(Guid sessionId, Guid? lobbyId = null)
    {
        var notification = new
        {
            EventType = "SessionCompleted",
            SessionId = sessionId,
            LobbyId = lobbyId,
            Message = "Phiên chơi đã kết thúc. Cảm ơn bạn đã sử dụng BoardVerse!",
            Timestamp = DateTime.UtcNow
        };

        // GAP-R6-RT-03: gửi tới session:{sessionId} (không phải session:{lobbyId}).
        await _hubContext.Clients
            .Group($"session:{sessionId}")
            .SendAsync("SessionCompleted", notification);

        _logger.LogInformation(
            "Notified session {SessionId} about SessionCompleted (LobbyId={LobbyId})",
            sessionId, lobbyId);
    }

    /// <summary>
    /// GAP-R6-RT-02: Notify POS về yêu cầu gia hạn từ player.
    /// Staff POS subscribe group <c>cafe:{cafeId}</c> để nhận notification.
    /// Group name đã chuẩn hóa (cafe:{guid}) — match với helper dưới.
    /// </summary>
    public async Task NotifySessionExtensionRequestedAsync(
        Guid sessionId,
        Guid cafeId,
        Guid requestedByUserId,
        int requestedMinutes,
        decimal estimatedAdditionalCostVnd)
    {
        var notification = new
        {
            EventType = "SessionExtensionRequested",
            SessionId = sessionId,
            CafeId = cafeId,
            RequestedByUserId = requestedByUserId,
            RequestedMinutes = requestedMinutes,
            EstimatedAdditionalCostVnd = estimatedAdditionalCostVnd,
            Message = $"Yêu cầu gia hạn {requestedMinutes} phút (ước tính +{estimatedAdditionalCostVnd:N0} VND)",
            Timestamp = DateTime.UtcNow
        };

        // Notify cafe group — POS staff listening on cafe:{cafeId}
        // GAP-R6-RT-02: trước đây group name drift (cafe-{guid} vs cafe:{guid}) — đã chuẩn hóa.
        await _hubContext.Clients
            .Group($"cafe:{cafeId}")
            .SendAsync("SessionExtensionRequested", notification);

        _logger.LogInformation(
            "Notified cafe {CafeId} about extension request for session {SessionId} " +
            "({RequestedMinutes} min, {EstimatedCost:N0} VND) by user {UserId}",
            cafeId, sessionId, requestedMinutes, estimatedAdditionalCostVnd, requestedByUserId);
    }

    /// <summary>
    /// GAP-R6-RT-01: Notify tất cả members trong lobby về thay đổi.
    /// Lobby frontend join group <c>lobby:{lobbyId}</c> qua PosHub.JoinLobby để nhận event.
    /// Trước đây: PosHubService KHÔNG CÓ method broadcast về group lobby: → FE lobby không bao giờ
    /// nhận update realtime khi host cancel / member join / status change.
    /// </summary>
    public async Task NotifyLobbyUpdateAsync(
        Guid lobbyId,
        string eventType,
        object? data = null)
    {
        var notification = new
        {
            EventType = eventType,
            LobbyId = lobbyId,
            Data = data,
            Timestamp = DateTime.UtcNow
        };

        // Broadcast tới group lobby:{lobbyId} — match với PosHub.JoinLobby subscription.
        await _hubContext.Clients
            .Group($"lobby:{lobbyId}")
            .SendAsync(eventType, notification);

        _logger.LogInformation(
            "Notified lobby {LobbyId} about event {EventType}",
            lobbyId, eventType);
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

    /// <summary>
    /// GAP-NEW-1: Notify player that extension request was approved.
    /// </summary>
    public async Task NotifySessionExtensionApprovedAsync(
        Guid cafeId,
        Guid requestId,
        Guid playerId,
        int approvedMinutes)
    {
        var notification = new
        {
            EventType = "SessionExtensionApproved",
            RequestId = requestId,
            PlayerId = playerId,
            ApprovedMinutes = approvedMinutes,
            Message = $"Yeu cau gia han {approvedMinutes} phut da duoc duyet.",
            Timestamp = DateTime.UtcNow
        };

        // Notify user group — player mobile app listening on user:{userId}
        await _hubContext.Clients
            .Group($"user:{playerId}")
            .SendAsync("SessionExtensionApproved", notification);

        _logger.LogInformation(
            "Notified player {PlayerId} about extension approval (requestId={RequestId}, {ApprovedMinutes} min)",
            playerId, requestId, approvedMinutes);
    }

    /// <summary>
    /// GAP-NEW-1: Notify player that extension request was rejected.
    /// </summary>
    public async Task NotifySessionExtensionRejectedAsync(
        Guid cafeId,
        Guid requestId,
        Guid playerId,
        string? reason)
    {
        var notification = new
        {
            EventType = "SessionExtensionRejected",
            RequestId = requestId,
            PlayerId = playerId,
            Reason = reason,
            Message = reason != null
                ? $"Yeu cau gia han da bi tu choi. Ly do: {reason}"
                : "Yeu cau gia han da bi tu choi.",
            Timestamp = DateTime.UtcNow
        };

        // Notify user group — player mobile app listening on user:{userId}
        await _hubContext.Clients
            .Group($"user:{playerId}")
            .SendAsync("SessionExtensionRejected", notification);

        _logger.LogInformation(
            "Notified player {PlayerId} about extension rejection (requestId={RequestId})",
            playerId, requestId);
    }
}
