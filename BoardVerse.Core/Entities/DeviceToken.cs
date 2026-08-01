using System;

namespace BoardVerse.Core.Entities;

/// <summary>
/// FCM device token cho mỗi thiết bị của User.
/// Mobile app gọi <c>POST /api/notifications/device-tokens</c> sau khi login để register,
/// backend lưu token này để gửi push notification cho các event (lobby auto-cancel,
/// cafe pricing changed, v.v.). Khi user logout hoặc gỡ app, mobile nên gọi DELETE.
/// </summary>
public class DeviceToken
{
    public Guid Id { get; set; }

    /// <summary>FK → User.Id (shared PK với UserProfile).</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// FCM registration token do Firebase SDK trả về phía mobile.
    /// Mobile phải refresh token định kỳ và gọi API update khi token đổi.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Platform: "android" / "ios" / "web".</summary>
    public string Platform { get; set; } = "android";

    /// <summary>App version code (để debug nếu notification fail trên 1 version cũ).</summary>
    public string? AppVersion { get; set; }

    /// <summary>Device model (vd: "Pixel 7", "iPhone 14") — debug only.</summary>
    public string? DeviceModel { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSeenAt { get; set; }

    /// <summary>True khi FCM trả về lỗi "registration-token-not-registered" → auto skip khi push.</summary>
    public bool IsInvalidated { get; set; }
}
