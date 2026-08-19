namespace BoardVerse.Core.Enum;

/// <summary>
/// Lý do kết thúc session chơi (docs/time-slot-fixed-end-design (1).md §9.1).
/// Lưu trên <c>Reservation.EndReason</c> sau khi POS đóng phiên hoặc hệ thống auto-release.
/// </summary>
public enum SessionEndReason
{
    /// <summary>Kết thúc đúng hoặc gần giờ (playedRatio ≥ 90%) — BR-END-03.</summary>
    OnTime = 0,

    /// <summary>Player về sớm (playedRatio &lt; 90%) — BR-END-04.</summary>
    EarlyLeave = 1,

    /// <summary>Đã gia hạn qua slot kế (qua ReservationExtensionService) — BR-EXT.</summary>
    Extended = 2,

    /// <summary>Không check-in sau grace 30 phút — BR-CHECKIN-02.</summary>
    NoShow = 3,

    /// <summary>Host hủy trước/sau khi check-in — BR-REFUND-02.</summary>
    Cancelled = 4,

    /// <summary>Staff bấm end session thay player — BR-REFUND-07.</summary>
    StaffEnded = 5,

    /// <summary>Auto-release sau grace 30 phút (BR-EC-08, AutoReleaseExpiredSessionsJob).</summary>
    AutoReleased = 6
}