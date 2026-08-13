using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// §9.4: Đặt chỗ cho khách vãng lai (walk-in).
/// Link tới WalkInWindow qua WalkInWindowId.
///
/// BR-WALKIN-01: Tạo walk-in khi WalkInWindow.Status ∈ {Available, Partial}.
/// BR-WALKIN-04: Walk-in KHÔNG cọc — thanh toán 100% tiền giờ tại POS.
/// BR-WALKIN-05: OCC trên WalkInWindow.Version khi giữ ghế.
/// </summary>
public class WalkInBooking
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK đến WalkInWindow mà walk-in này đặt vào.</summary>
    public Guid WalkInWindowId { get; set; }

    public Guid CafeId { get; set; }

    /// <summary>Tên khách walk-in (bắt buộc).</summary>
    public string GuestName { get; set; } = string.Empty;

    /// <summary>Số điện thoại khách (optional).</summary>
    public string? GuestPhone { get; set; }

    /// <summary>Thời điểm bắt đầu của walk-in booking.</summary>
    public DateTime StartTime { get; set; }

    /// <summary>Thời điểm kết thúc của walk-in booking (≤ WalkInWindow.WindowEnd).</summary>
    public DateTime EndTime { get; set; }

    /// <summary>Số ghế khách yêu cầu.</summary>
    public int Seats { get; set; }

    /// <summary>Giá giờ được áp dụng (lấy từ CafeSchedule tại thời điểm tạo).</summary>
    public decimal HourlyRate { get; set; }

    /// <summary>Tổng số tiền phải trả = giờ chơi × giá.</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>Trạng thái thanh toán: UNPAID → PAID.</summary>
    public WalkInPaymentStatus PaymentStatus { get; set; } = WalkInPaymentStatus.Unpaid;

    /// <summary>Staff POS tạo walk-in này.</summary>
    public Guid? PosStaffId { get; set; }

    /// <summary>ActiveSession được tạo sau khi check-in (nullable cho đến khi check-in).</summary>
    public Guid? ActiveSessionId { get; set; }

    public WalkInBookingStatus Status { get; set; } = WalkInBookingStatus.Active;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // === Navigation ===
    public virtual WalkInWindow WalkInWindow { get; set; } = null!;
    public virtual Cafe? Cafe { get; set; }
    public virtual ActiveSession? ActiveSession { get; set; }
}
