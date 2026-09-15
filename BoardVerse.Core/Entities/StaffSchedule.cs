using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Lịch làm việc của staff theo ngày trong tuần.
/// Cho phép nhiều ca/ngày (vd: sáng 6-12h, chiều 14-22h) và ca qua đêm (StartTime > EndTime).
/// BR-NEW: Manager lên kế hoạch lịch cho staff của quán mình.
/// </summary>
public class StaffSchedule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Mã cafe áp dụng lịch.</summary>
    public Guid CafeId { get; set; }

    /// <summary>UserId của nhân viên áp dụng lịch.</summary>
    public Guid StaffUserId { get; set; }

    /// <summary>Thứ trong tuần (Sunday=0, Monday=1, ...).</summary>
    public DayOfWeek DayOfWeek { get; set; }

    /// <summary>Giờ bắt đầu ca (TimeOnly).</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>Giờ kết thúc ca (TimeOnly). Nếu nhỏ hơn StartTime → ca qua đêm.</summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>Loại ca (Regular/Overtime/OnCall/GameMaster).</summary>
    public ShiftType ShiftType { get; set; } = ShiftType.Regular;

    /// <summary>Lặp hàng tuần (true = template cho mọi tuần, false = chỉ 1 lần copy).</summary>
    public bool IsRecurring { get; set; } = false;

    /// <summary>Ghi chú (vd: "Ca sáng chính", "Hỗ trợ vệ sinh").</summary>
    public string? Note { get; set; }

    /// <summary>Trạng thái (Active/Inactive/Cancelled).</summary>
    public StaffScheduleStatus Status { get; set; } = StaffScheduleStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Cafe Cafe { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}
