namespace BoardVerse.Core.Enum;

/// <summary>
/// §9.3 (docs/time-slot-fixed-end-design (1).md): Trạng thái của một WalkInBooking (đặt chỗ walk-in).
/// </summary>
public enum WalkInBookingStatus
{
    /// <summary>POS đã tạo booking, chờ khách đến.</summary>
    Pending = 0,

    /// <summary>Walk-in đã confirm, đã sẵn sàng cho POS.</summary>
    Confirmed = 1,

    /// <summary>Walk-in đang hoạt động (đã check-in, đang chơi).</summary>
    InProgress = 2,

    /// <summary>Walk-in đã hoàn thành (thanh toán xong).</summary>
    Completed = 3,

    /// <summary>Walk-in đã bị hủy (khách không đến hoặc staff hủy).</summary>
    Cancelled = 4,

    /// <summary>Walk-in không đến sau grace 30 phút — BR-WALKIN-06.</summary>
    NoShow = 5,

    /// <summary>Legacy alias — <c>Pending</c> đã thay thế.</summary>
    [Obsolete("Dùng InProgress thay vì Active. Active chỉ còn cho backward-compat.")]
    Active = Pending
}