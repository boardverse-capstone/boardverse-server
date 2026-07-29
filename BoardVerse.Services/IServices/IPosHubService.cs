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
}
