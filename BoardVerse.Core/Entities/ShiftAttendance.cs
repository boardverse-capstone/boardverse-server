namespace BoardVerse.Core.Entities;

/// <summary>
/// Bản ghi check-in/out của staff cho một ca làm việc cụ thể.
/// Tính toán LateMinutes / EarlyLeaveMinutes tự động dựa trên giờ bắt đầu/kết thúc lịch.
/// </summary>
public class ShiftAttendance
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Mã lịch làm việc (FK).</summary>
    public Guid ScheduleId { get; set; }

    /// <summary>UserId của nhân viên điểm danh.</summary>
    public Guid StaffUserId { get; set; }

    /// <summary>Ngày điểm danh (DateOnly).</summary>
    public DateOnly Date { get; set; }

    /// <summary>Giờ check-in thực tế (UTC).</summary>
    public DateTime? CheckInTime { get; set; }

    /// <summary>Giờ check-out thực tế (UTC).</summary>
    public DateTime? CheckOutTime { get; set; }

    /// <summary>Số phút đi muộn so với lịch (tính từ StartTime của StaffSchedule).</summary>
    public int LateMinutes { get; set; } = 0;

    /// <summary>Số phút về sớm so với lịch (tính từ EndTime của StaffSchedule).</summary>
    public int EarlyLeaveMinutes { get; set; } = 0;

    /// <summary>Trạng thái: Present | Late | Absent | OnLeave | EarlyLeave.</summary>
    public string? Status { get; set; }

    /// <summary>Ghi chú (vd: lý do đi muộn).</summary>
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual StaffSchedule Schedule { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}
