namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái đặt chỗ (Booking).
/// Phân biệt với BookingDepositStatus (dùng cho đặt cọc).
/// </summary>
public enum BookingStatus
{
    /// <summary>Chờ cọc - đang chờ người dùng thanh toán tiền cọc.</summary>
    PendingDeposit = 0,

    /// <summary>Chờ thanh toán - đã cọc nhưng chưa thanh toán đầy đủ.</summary>
    PendingPayment = 1,

    /// <summary>Đã xác nhận - booking được xác nhận và chờ check-in.</summary>
    Confirmed = 2,

    /// <summary>Đã check-in - người dùng đã đến quán và bắt đầu chơi.</summary>
    CheckedIn = 3,

    /// <summary>Đã hoàn thành - phiên chơi kết thúc bình thường.</summary>
    Completed = 4,

    /// <summary>Đã hủy - booking bị hủy bởi người dùng hoặc quản lý.</summary>
    Cancelled = 5,

    /// <summary>Đã hết hạn - booking không được thực hiện trong thời gian cho phép.</summary>
    Expired = 6
}
