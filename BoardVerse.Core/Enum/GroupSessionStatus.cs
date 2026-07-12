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
    Paid = 3
}
