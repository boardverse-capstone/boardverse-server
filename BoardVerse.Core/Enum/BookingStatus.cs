namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái đặt chỗ (Booking).
/// Phân biệt với BookingDepositStatus (dùng cho đặt cọc).
/// Theo ERD: PendingDeposit, Confirmed, CheckedIn, NoShow, Cancelled.
/// </summary>
public enum BookingStatus
{
    /// <summary>Chờ cọc - đang chờ người dùng thanh toán tiền cọc.</summary>
    PendingDeposit = 0,

    /// <summary>Đã xác nhận - booking được xác nhận (đã cọc) và chờ check-in.</summary>
    Confirmed = 1,

    /// <summary>Đã check-in - người dùng đã đến quán và bắt đầu chơi.</summary>
    CheckedIn = 2,

    /// <summary>Không đến - booking hết hạn mà khách không đến.</summary>
    NoShow = 3,

    /// <summary>Đã hủy - booking bị hủy bởi người dùng hoặc quản lý.</summary>
    Cancelled = 4
}
