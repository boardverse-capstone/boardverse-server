using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Booking;

/// <summary>
/// Request tạo Booking từ Lobby đã lock.
/// Host tạo booking sau khi lobby đã Full và đã khóa (lock).
/// </summary>
public class CreateBookingRequestDto
{
    /// <summary>
    /// Lobby đã lock — bắt buộc. Booking sẽ được link với lobby này.
    /// </summary>
    [Required]
    public Guid LobbyId { get; set; }

    /// <summary>
    /// Quán cafe đích — bắt buộc.
    /// </summary>
    [Required]
    public Guid CafeId { get; set; }

    /// <summary>
    /// Ngày đặt chỗ (date, không có giờ).
    /// </summary>
    [Required]
    public DateTime BookingDate { get; set; }

    /// <summary>
    /// Giờ bắt đầu dự kiến (VD: 14:00 = 14:00:00).
    /// </summary>
    [Required]
    public TimeSpan StartTime { get; set; }

    /// <summary>
    /// Giờ kết thúc dự kiến (VD: 18:00 = 18:00:00).
    /// </summary>
    [Required]
    public TimeSpan EndTime { get; set; }

    /// <summary>
    /// Tổng số ghế/người chơi. Mặc định = số members trong lobby.
    /// </summary>
    [Range(1, 50)]
    public int? TotalSlot { get; set; }

    /// <summary>
    /// Số bàn (optional, gán bởi quán).
    /// </summary>
    public int? TableNumber { get; set; }

    /// <summary>
    /// Mã bàn (optional).
    /// </summary>
    [StringLength(50)]
    public string? TableCode { get; set; }

    /// <summary>
    /// Ghi chú đặc biệt (VD: sinh nhật, team building).
    /// </summary>
    [StringLength(1000)]
    public string? SpecialRequest { get; set; }
}
