using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Pos;

/// <summary>
/// POS QR token trả về cho staff để hiển thị lên màn hình POS.
/// </summary>
public class PosCheckInTokenDto
{
    public Guid Id { get; set; }
    public Guid CafeId { get; set; }
    public Guid? ReservationId { get; set; }

    /// <summary>16-char alphanumeric uppercase — payload QR.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Deep-link payload để client render QR.</summary>
    public string QrPayload { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Request tạo POS QR token (optional gắn reservation cụ thể).
/// </summary>
public class CreatePosCheckInTokenRequestDto
{
    /// <summary>Optional — gắn token với 1 reservation. Để trống → token dùng cho walk-in/general.</summary>
    public Guid? ReservationId { get; set; }

    /// <summary>Optional — TTL tùy chỉnh (phút). Mặc định 30 phút nếu không gửi.</summary>
    [Range(1, 240)]
    public int? TtlMinutes { get; set; }
}