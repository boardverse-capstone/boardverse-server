using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Booking;

/// <summary>
/// Request cập nhật Booking (chỉ một số trường được phép sửa).
/// </summary>
public class UpdateBookingRequestDto
{
    /// <summary>
    /// Ngày đặt chỗ mới.
    /// </summary>
    public DateTime? BookingDate { get; set; }

    /// <summary>
    /// Giờ bắt đầu mới.
    /// </summary>
    public TimeSpan? StartTime { get; set; }

    /// <summary>
    /// Giờ kết thúc mới.
    /// </summary>
    public TimeSpan? EndTime { get; set; }

    /// <summary>
    /// Tổng số ghế mới.
    /// </summary>
    [Range(1, 50)]
    public int? TotalSlot { get; set; }

    /// <summary>
    /// Số bàn mới.
    /// </summary>
    public int? TableNumber { get; set; }

    /// <summary>
    /// Mã bàn mới.
    /// </summary>
    [StringLength(50)]
    public string? TableCode { get; set; }

    /// <summary>
    /// Ghi chú đặc biệt mới.
    /// </summary>
    [StringLength(1000)]
    public string? SpecialRequest { get; set; }
}
