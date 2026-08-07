using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Reservation;

/// <summary>
/// Player scan QR POS để check-in (BR §21A.7 — 2 chiều).
/// Body: { token: "ABC234XYZ..." }. Server lookup token → reservation → gọi check-in flow.
/// </summary>
public class PlayerScanTokenRequestDto
{
    /// <summary>16-char alphanumeric uppercase (PosToken).</summary>
    [Required]
    [StringLength(16, MinimumLength = 16)]
    public string Token { get; set; } = string.Empty;
}

/// <summary>
/// Response cho PlayerScanToken — trả ActiveSessionDto đã rút gọn (BR §21A.7).
/// </summary>
public class PlayerScanTokenResponseDto
{
    public Guid ActiveSessionId { get; set; }
    public Guid ReservationId { get; set; }
    public Guid CafeId { get; set; }
    public DateTime CheckedInAt { get; set; }
}