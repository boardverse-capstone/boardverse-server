namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái của BVC refund request (player gửi → admin duyệt).
/// Lifecycle:
///   Pending → Approved (admin duyệt, đã cộng BVC)
///   Pending → Rejected (admin từ chối, có note lý do)
///   Pending → Cancelled (player tự hủy trước khi admin xử lý)
/// </summary>
public enum RefundRequestStatus
{
    /// <summary>Mới tạo, chờ admin review.</summary>
    Pending = 0,

    /// <summary>Admin duyệt — BVC đã được cộng về ví player (ledger AdminCredit).</summary>
    Approved = 1,

    /// <summary>Admin từ chối — BVC không thay đổi.</summary>
    Rejected = 2,

    /// <summary>Player chủ động hủy yêu cầu trước khi admin xử lý.</summary>
    Cancelled = 3
}