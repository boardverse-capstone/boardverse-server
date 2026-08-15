using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Cafe;

/// <summary>
/// Dành riêng cho Manager — kế thừa CafeDetailDto + thêm các field chỉ Manager thấy.
/// </summary>
public class ManagerCafeDto : CafeDetailDto
{
    // === Manager-only fields ===

    /// <summary>Manager ID sở hữu quán (chỉ manager thấy).</summary>
    public Guid ManagerId { get; set; }

    /// <summary>ID quản lý SePay (chỉ hiện cho manager).</summary>
    public string? SePayMerchantId { get; set; }

    /// <summary>Mã ngân hàng dùng cho VietQR fallback.</summary>
    public string? SePayBankCode { get; set; }

    /// <summary>Số tài khoản nhận tiền.</summary>
    public string? SePayAccountNumber { get; set; }

    /// <summary>Redirect URL sau khi thanh toán session.</summary>
    public string? SePayReturnUrl { get; set; }

    /// <summary>Phút giữ chỗ mặc định (BR-06: tối đa 30).</summary>
    public int DefaultHoldDurationMinutes { get; set; } = 30;

    // === Staff / Org ===

    /// <summary>T�ng số nhân viên đang active trong quán.</summary>
    public int StaffCount { get; set; }

    /// <summary>Số booking trong 7 ngày tới (kể cả walk-in pending).</summary>
    public int UpcomingBookingsCount { get; set; }

    /// <summary>Số lobby đang mở (open + viable + full) cho ngày hôm nay.</summary>
    public int ActiveLobbiesToday { get; set; }

    /// <summary>Pending cafe approval (lobby chờ duyệt cho BR-NEW-11).</summary>
    public int PendingCafeApprovalLobbiesCount { get; set; }

    // === Revenue snapshot (BR-REVENUE-01) ===

    /// <summary>Tổng cọc BVC đang giữ (heldBalance) cho player của quán này.</summary>
    public long HeldDepositTotal { get; set; }

    // === Schedule / Hours ===

    /// <summary>Giờ m� cửa ngày thường (HH:mm).</summary>
    public TimeOnly? WeekdayOpen { get; set; }

    /// <summary>Giờ đóng cửa ngày thường (HH:mm).</summary>
    public TimeOnly? WeekdayClose { get; set; }

    /// <summary>Giờ mở cửa cuối tuần (HH:mm).</summary>
    public TimeOnly? WeekendOpen { get; set; }

    /// <summary>Giờ đóng cửa cuối tuần (HH:mm).</summary>
    public TimeOnly? WeekendClose { get; set; }

    // === Audit timestamps ===

    /// <summary>Lần cuối cập nhật thông tin quán.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Lần cuối cập nhật pricing config.</summary>
    public DateTime? OperationalProfileUpdatedAt { get; set; }
}
