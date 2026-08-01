using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Notification;

/// <summary>
/// Mobile app gọi POST /api/notifications/device-tokens sau khi Firebase SDK
/// trả FCM token. Backend lưu token → dùng cho push notification pipeline
/// (lobby auto-cancel, cafe pricing changed, v.v.).
/// </summary>
public class RegisterDeviceTokenRequestDto
{
    /// <summary>FCM registration token từ Firebase SDK.</summary>
    [Required]
    [StringLength(512, MinimumLength = 10)]
    public string Token { get; set; } = string.Empty;

    /// <summary>Platform: android / ios / web.</summary>
    [Required]
    [StringLength(16)]
    public string Platform { get; set; } = "android";

    /// <summary>App version code (debug only).</summary>
    [StringLength(32)]
    public string? AppVersion { get; set; }

    /// <summary>Device model (debug only).</summary>
    [StringLength(128)]
    public string? DeviceModel { get; set; }
}

public class DeviceTokenResponseDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
    public string? DeviceModel { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }

    public static DeviceTokenResponseDto FromEntity(Entities.DeviceToken entity) => new()
    {
        Id = entity.Id,
        UserId = entity.UserId,
        Platform = entity.Platform,
        AppVersion = entity.AppVersion,
        DeviceModel = entity.DeviceModel,
        CreatedAt = entity.CreatedAt,
        LastSeenAt = entity.LastSeenAt
    };
}
