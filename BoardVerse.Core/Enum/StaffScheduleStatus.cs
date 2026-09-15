namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái của một lịch làm việc của staff.
/// </summary>
public enum StaffScheduleStatus
{
    /// <summary>Lịch đang hoạt động, staff có thể đi làm theo lịch này.</summary>
    Active = 0,

    /// <summary>Lịch tạm ngưng — manager đã tạm dừng áp dụng.</summary>
    Inactive = 1,

    /// <summary>Lịch đã hủy — không còn hiệu lực.</summary>
    Cancelled = 2
}
