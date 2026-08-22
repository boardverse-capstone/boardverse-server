namespace BoardVerse.Services.IServices;

/// <summary>
/// Service để gửi real-time notifications qua SignalR PosHub.
/// AC 1.4: Phát tín hiệu đồng bộ thông báo cho các thiết bị di động.
/// </summary>
public interface IPosHubService
{
    /// <summary>
    /// Notify tất cả members trong session rằng phiên đã được kích hoạt.
    /// AC 1.4: Ứng dụng di động của các thành viên có mặt lập tức
    /// chuyển đổi sang trạng thái màn hình "Đang chơi tại quán" thời gian thực.
    /// </summary>
    Task NotifySessionActivatedAsync(Guid sessionId, Guid cafeId, string cafeName, Guid hostId, IReadOnlyList<Guid> memberUserIds);

    /// <summary>
    /// Notify một user cụ thể về thay đổi trạng thái session.
    /// </summary>
    Task NotifyUserSessionUpdateAsync(Guid userId, Guid sessionId, string status, string? message = null);

    /// <summary>
    /// Notify tất cả members trong session về thay đổi (game returned, checkout, etc).
    /// </summary>
    Task NotifySessionUpdateAsync(Guid sessionId, string eventType, object? data = null);

    /// <summary>BR-REQUIRED §17.5: POS đóng phiên → SessionCompleted.</summary>
    Task NotifySessionCompleted(Guid sessionId, Guid? lobbyId = null);

    /// <summary>
    /// GAP-XX: Push SignalR khi ActiveSession PAID (cả Manual lẫn SePay webhook).
    /// FE subscribe group <c>session:{sessionId}</c> nhận <c>SessionPaid</c> event → tắt UI Pay,
    /// hiển thị "Đã thanh toán". Walk-in session (LobbyId = null) vẫn nhận được event này.
    /// </summary>
    Task NotifySessionPaidAsync(Guid sessionId, Guid cafeId, Guid? lobbyId, decimal totalAmount, DateTime paidAt);

    Task NotifySessionExtensionRequestedAsync(Guid sessionId, Guid cafeId, Guid requestedByUserId, int requestedMinutes, decimal estimatedAdditionalCostVnd);

    Task NotifySessionExtensionApprovedAsync(Guid sessionId, Guid cafeId, Guid approvedByUserId, int approvedMinutes);

    Task NotifySessionExtensionRejectedAsync(Guid sessionId, Guid cafeId, Guid rejectedByUserId, string? reason);
}
