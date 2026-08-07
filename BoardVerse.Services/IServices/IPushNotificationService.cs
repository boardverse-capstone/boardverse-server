using BoardVerse.Core.Entities;

namespace BoardVerse.Services.IServices;

/// <summary>
/// Service push notification (FCM). Implement này được wire vào các trigger:
/// <list type="bullet">
///   <item><description>Lobby auto-cancel (gap #9) — sau khi <c>LobbyTimeoutJob</c> gọi <c>NotifyLobbyAutoCancelled</c>.</description></item>
///   <item><description>Cafe pricing changed (gap #13) — sau khi <c>CafeController.UpdatePricingConfig</c> broadcast SignalR.</description></item>
/// </list>
/// Cho phép <c>Enabled=false</c> (dev/test) — service log payload thay vì gửi thật.
/// </summary>
public interface IPushNotificationService
{
    /// <summary>
    /// Gửi cùng 1 payload tới tất cả active device tokens của danh sách userIds.
    /// Auto-skip tokens bị FCM trả lỗi "registration-token-not-registered"
    /// bằng cách set <see cref="DeviceToken.IsInvalidated"/> = true.
    /// </summary>
    /// <param name="userIds">Danh sách UserId nhận notification.</param>
    /// <param name="payload">FCM message payload (title/body/data).</param>
    /// <returns>Số notification gửi thành công.</returns>
    Task<int> SendToUsersAsync(IReadOnlyCollection<Guid> userIds, PushNotificationPayload payload);

    /// <summary>
    /// Gửi notification tới 1 user (single overload).
    /// </summary>
    Task<int> SendAsync(Guid userId, string title, string body, Dictionary<string, string>? data = null);

    /// <summary>
    /// Invalidate token khi mobile báo token hết hạn (vd: gọi từ cleanup job
    /// sau khi FCM trả lỗi). Token sẽ không được push nữa.
    /// </summary>
    Task InvalidateTokenAsync(string token);
}

/// <summary>
/// FCM message payload. <c>Data</c> được map sang <c>notification.data</c>
/// (key-value strings) để mobile app xử lý routing/deeplink.
/// </summary>
public class PushNotificationPayload
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Event type discriminator — mobile dùng để route/deeplink.
    /// VD: "LobbyAutoCancelled", "CafePricingChanged", "BookingConfirmed".
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Custom key-value data fields (vd: lobbyId, cafeId).</summary>
    public Dictionary<string, string> Data { get; set; } = new();
}
