using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Reservation;

/// <summary>
/// Request extend thời gian reservation (BR-EXT).
///
/// BR-EXT-01: Chỉ extend khi Status = Confirmed.
/// BR-EXT-02: Không extend qua midnight.
/// BR-EXT-03: Max 2 lần extend.
/// BR-EXT-05: Partial extension OK.
/// </summary>
public class ExtendReservationRequestDto
{
    /// <summary>Mã reservation cần extend.</summary>
    public Guid ReservationId { get; set; }

    /// <summary>
    /// Số phút muốn extend. Partial extension OK (BR-EXT-05).
    /// Must be positive. Recommended values: 30, 60, 90, 120.
    /// </summary>
    [Required]
    [Range(1, 120, ErrorMessage = "Số phút extend phải từ 1 đến 120.")]
    public int ExtensionMinutes { get; set; }
}

/// <summary>
/// Response sau khi extend thành công.
/// </summary>
public class ExtendReservationResponseDto
{
    public Guid ReservationId { get; set; }

    /// <summary>ScheduledEndTime mới (có thể là ExtendedEndTime nếu đã extend trước đó).</summary>
    public DateTime NewScheduledEndTime { get; set; }

    /// <summary>Thời điểm extend trước đó (null nếu lần đầu).</summary>
    public DateTime? PreviousEndTime { get; set; }

    /// <summary>Số lần extend đã thực hiện (bao gồm lần này).</summary>
    public int ExtensionCount { get; set; }

    /// <summary>Số phút đã extend trong lần này.</summary>
    public int ExtensionMinutes { get; set; }

    /// <summary>Remaining seats có thể extend (max 120 phút = 2 tiếng).</summary>
    public int RemainingExtensionMinutes { get; set; }
}

/// <summary>
/// Kết quả check availability trước khi extend.
/// </summary>
public class ExtendAvailabilityDto
{
    public Guid ReservationId { get; set; }

    /// <summary>ScheduledEndTime hiện tại.</summary>
    public DateTime CurrentEndTime { get; set; }

    /// <summary>ScheduledEndTime mới nếu extend.</summary>
    public DateTime ProposedEndTime { get; set; }

    /// <summary>Còn được phép extend không?</summary>
    public bool CanExtend { get; set; }

    /// <summary>Lý do không thể extend (nếu CanExtend = false).</summary>
    public string? Reason { get; set; }

    /// <summary>Số phút còn được phép extend.</summary>
    public int RemainingExtensionMinutes { get; set; }
}
