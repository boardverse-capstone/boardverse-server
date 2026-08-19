using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// §9.3: Khoảng thời gian trống có thể bán cho walk-in.
/// Tạo khi Reservation checkout sớm (early checkout) hoặc no-show.
///
/// BR-WALKIN-01: Chỉ walk-in booking khi Status ∈ {Available, Partial}.
/// BR-WALKIN-05: First-come-first-served — OCC trên Version.
///
/// `WindowEnd` được set bằng `Reservation.ScheduledEndTime` (BR-RESV-02).
/// </summary>
public class WalkInWindow
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK đến Reservation tạo ra window này (nullable — có thể tạo thủ công từ POS).</summary>
    public Guid? SourceReservationId { get; set; }

    public Guid CafeId { get; set; }

    /// <summary>Thời điểm bắt đầu window — thường là thời điểm early checkout / no-show.</summary>
    public DateTime WindowStart { get; set; }

    /// <summary>
    /// Thời điểm kết thúc window.
    /// BR-RESV-02: = Reservation.ScheduledEndTime (lưu DB, không derive runtime).
    /// </summary>
    public DateTime WindowEnd { get; set; }

    public int TotalSeats { get; set; }
    public int AvailableSeats { get; set; }
    public int HeldSeats { get; set; }
    public int InUseSeats { get; set; }

    /// <summary>OCC version — chống race condition khi nhiều POS cùng tạo walk-in (EC-06).</summary>
    public uint Version { get; set; }

    public WalkInWindowStatus Status { get; set; } = WalkInWindowStatus.Available;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Hết hạn nếu không ai đặt trong khoảng thời gian này.</summary>
    public DateTime ExpiresAt { get; set; }

    // === Navigation ===
    public virtual Reservation? SourceReservation { get; set; }
    public virtual Cafe? Cafe { get; set; }
    public virtual ICollection<WalkInBooking> WalkInBookings { get; set; } = [];
}
