namespace BoardVerse.Core.DTOs.Booking;

/// <summary>
/// Bàn trống phù hợp với yêu cầu của Player trong một khung giờ cụ thể.
/// Trả về cho mobile BookingSummaryPage để user chọn <c>cafeTableId</c> gửi lên POST /api/bookings.
/// Theo booking-payment-gaps.md #1.
/// </summary>
public class AvailableCafeTableDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SeatCount { get; set; }
    /// <summary>True khi bàn không bị conflict giờ và đang Available.</summary>
    public bool IsAvailable { get; set; }
    /// <summary>Giá giờ đầu của quán (BasePrice). Mobile hiển thị để user biết trước.</summary>
    public decimal PricePerHour { get; set; }
}