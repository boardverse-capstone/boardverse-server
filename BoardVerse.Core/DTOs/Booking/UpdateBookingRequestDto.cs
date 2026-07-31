using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Booking;

/// <summary>
/// Request cập nhật Booking (chỉ một số trường được phép).
/// </summary>
public class UpdateBookingRequestDto
{
    /// <summary>Bàn mới (optional).</summary>
    public Guid? CafeTableId { get; set; }

    /// <summary>Thời gian bắt đầu mới.</summary>
    public DateTime? ScheduledStartTime { get; set; }

    /// <summary>Thời gian kết thúc mới.</summary>
    public DateTime? ScheduleEndTime { get; set; }

    /// <summary>Số người chơi mới.</summary>
    [Range(1, 50)]
    public int? PlayerQuantity { get; set; }
}
