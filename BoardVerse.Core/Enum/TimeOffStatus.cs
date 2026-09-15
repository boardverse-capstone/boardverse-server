namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái yêu cầu nghỉ phép của staff.
/// </summary>
public enum TimeOffStatus
{
    /// <summary>Manager chưa xử lý yêu cầu.</summary>
    Pending = 0,

    /// <summary>Manager đã duyệt cho nghỉ.</summary>
    Approved = 1,

    /// <summary>Manager từ chối yêu cầu nghỉ.</summary>
    Rejected = 2,

    /// <summary>Staff tự hủy yêu cầu nghỉ.</summary>
    Cancelled = 3
}
