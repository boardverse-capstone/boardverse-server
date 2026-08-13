namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái của một <c>RefundTransaction</c> (docs/time-slot-fixed-end-design (1).md §9.4).
/// </summary>
public enum RefundStatus
{
    /// <summary>Chưa áp dụng refund.</summary>
    None = 0,

    /// <summary>Refund đã được tính, chờ staff xử lý.</summary>
    Pending = 1,

    /// <summary>Đang xử lý ledger entry.</summary>
    Processing = 2,

    /// <summary>Refund hoàn tất (BVC về availableBalance của host).</summary>
    Completed = 3,

    /// <summary>Refund bị từ chối (staff override cancel).</summary>
    Rejected = 4,

    /// <summary>Refund thất bại (lỗi ledger).</summary>
    Failed = 5
}