using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Booking;

/// <summary>
/// Khảo sát capacity của quán trong một khung giờ cụ thể.
/// Trả về cho mobile BoardGameDetailPage TRƯỚC khi vào luồng BookingSummary
/// (xem booking-payment-gaps.md #2).
/// </summary>
public class CafeAvailabilityDto
{
    public Guid CafeId { get; set; }
    public string CafeName { get; set; } = string.Empty;
    public DateTime RequestedStartTime { get; set; }
    public DateTime RequestedEndTime { get; set; }
    /// <summary>True khi còn đủ ghế cho seatCount yêu cầu.</summary>
    public bool HasCapacity { get; set; }
    public int AvailableSeats { get; set; }
    public int TotalSeats { get; set; }
    /// <summary>Số hộp game Available của <c>gameTemplateId</c> trong quán (nếu có filter).</summary>
    public int AvailableGameBoxCount { get; set; }
    public NearbyCafeGameAvailabilityStatus? SelectedGameAvailabilityStatus { get; set; }
    /// <summary>Các khung giờ thay thế gần nhất (mỗi slot cách 30 phút).</summary>
    public List<CafeAvailabilitySlotDto> AlternativeSlots { get; set; } = new();
}

public class CafeAvailabilitySlotDto
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int AvailableSeats { get; set; }
}