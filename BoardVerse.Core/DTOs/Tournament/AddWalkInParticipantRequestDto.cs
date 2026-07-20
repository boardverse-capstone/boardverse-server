using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Tournament;

/// <summary>
/// Manager tạo nhanh walk-in participant tại POS cho khách vãng lai
/// (không có tài khoản BoardVerse).
/// </summary>
public class AddWalkInParticipantRequestDto
{
    /// <summary>
    /// Tên hiển thị cho khách vãng lai (vd: "Lê A", "Khách vãng lai #1").
    /// </summary>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Số điện thoại liên lạc (optional — dùng để thông báo kết quả hoặc prize collection nếu walk-in thắng giải).
    /// </summary>
    [StringLength(20, MinimumLength = 9)]
    [RegularExpression(@"^[\d\s\-\+\(\)]+$", ErrorMessage = "Số điện thoại không hợp lệ.")]
    public string? PhoneNumber { get; set; }
}