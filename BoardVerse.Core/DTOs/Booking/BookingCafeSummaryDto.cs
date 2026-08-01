namespace BoardVerse.Core.DTOs.Booking;

/// <summary>
/// Mobile task #14: GET /api/bookings/cafe/{cafeId} cho Player view (rút gọn).
/// Chỉ trả summary fields, KHÔNG trả verificationQRCode/paymentRef/memberIds để bảo mật.
/// </summary>
public class BookingCafeSummaryDto
{
    public Guid Id { get; set; }
    public DateTime ScheduledStartTime { get; set; }
    public DateTime ScheduleEndTime { get; set; }
    public int PlayerQuantity { get; set; }
    public string Status { get; set; } = string.Empty;
}
