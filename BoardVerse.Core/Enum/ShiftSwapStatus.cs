namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái yêu cầu đổi ca giữa 2 staff.
/// </summary>
public enum ShiftSwapStatus
{
    /// <summary>Chờ staff kia đồng ý / manager duyệt.</summary>
    Pending = 0,

    /// <summary>Manager đã duyệt — ca đã được đổi.</summary>
    Approved = 1,

    /// <summary>Manager từ chối yêu cầu đổi ca.</summary>
    Rejected = 2,

    /// <summary>Staff tự hủy yêu cầu đổi ca.</summary>
    Cancelled = 3
}
