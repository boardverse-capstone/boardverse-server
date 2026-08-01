namespace BoardVerse.Core.DTOs.Booking;

/// <summary>
/// Mobile task #8: GET /api/bookings/{bookingId}/session-status
/// Trả về trạng thái ActiveSession liên kết với booking cho member lobby xem realtime.
/// BR-12: Member nào đã về sớm, bill bao nhiêu
/// BR-14: Phí phạt (nếu có)
/// </summary>
public class BookingSessionStatusResponseDto
{
    public Guid BookingId { get; set; }

    /// <summary>Null nếu booking chưa được check-in và chưa có ActiveSession.</summary>
    public Guid? ActiveSessionId { get; set; }

    public string SessionStatus { get; set; } = "NotStarted";

    public DateTime? StartedAt { get; set; }

    public int CurrentDurationMinutes { get; set; }

    public List<BookingSessionMemberStatusDto> Members { get; set; } = new();

    public BookingSessionEstimatedBillDto? EstimatedFinalBill { get; set; }
}

public class BookingSessionMemberStatusDto
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // "Active" | "LeftEarly" | "Suspended" | "Finished" | "GuestSlot"
    public DateTime? LeftAt { get; set; }
    public decimal PartialBillAmount { get; set; }
    public bool PartialBillPaid { get; set; }
    public Guid? MergedIntoSessionId { get; set; }
}

public class BookingSessionEstimatedBillDto
{
    public decimal Subtotal { get; set; }
    public decimal Penalty { get; set; }
    public decimal DepositApplied { get; set; }
    public decimal Total { get; set; }
}
