namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái phiên chơi tổng (Group Session - ActiveSession).
/// Theo boardverse-state-machine.mdc - Section 4.1.
/// </summary>
public enum GroupSessionStatus
{
    /// <summary>Đếm giờ chơi thực, gán với barcode game đang mượn.</summary>
    Active = 0,

    /// <summary>Kiểm kê trung gian. Khóa tính năng in hóa đơn. Digital Component Checklist. (BR-12)</summary>
    Checking = 1,

    /// <summary>Đối chiếu xong. Chốt phút, áp biểu phí, xuất hóa đơn. Áp dụng BR-09 (cấn trừ cọc).</summary>
    Unpaid = 2,

    /// <summary>Thanh toán xong. Giải phóng ghế, trigger Karma rating.</summary>
    Paid = 3,

    /// <summary>
    /// BR-END-05: Auto-release khi staff quên end session.
    /// Grace 30 phút sau ScheduledEndTime → auto-release.
    /// </summary>
    Closed = 4,

    /// <summary>
    /// M2/C2.16: Force-close khi session vẫn còn unpaid members.
    /// Set bởi <c>ForceCloseService</c> khi Manager force-close và
    /// không cho phép late payment — bill đã chốt nhưng 1 số members chưa pay.
    /// Khác <see cref="Unpaid"/>: UnpaidForced = terminal, bill đã settle;
    /// Unpaid = vẫn chờ members pay.
    /// (2026-10-01)
    /// </summary>
    UnpaidForced = 5
}
