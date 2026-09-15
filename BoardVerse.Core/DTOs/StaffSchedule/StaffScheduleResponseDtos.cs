using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.StaffSchedule;

/// <summary>
/// Response lịch làm việc của staff — bao gồm các field phái sinh (IsOvernight, DayOfWeekName, StaffName).
/// </summary>
public class StaffScheduleResponseDto
{
    public Guid Id { get; set; }
    public Guid CafeId { get; set; }
    public Guid StaffUserId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public DayOfWeek DayOfWeek { get; set; }

    /// <summary>Tên ngày trong tuần bằng tiếng Việt (vd: "Thứ 2", "Chủ nhật").</summary>
    public string DayOfWeekName { get; set; } = string.Empty;

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    /// <summary>True nếu EndTime &lt; StartTime (ca qua đêm).</summary>
    public bool IsOvernight { get; set; }

    /// <summary>Số giờ của ca (tính theo phút).</summary>
    public int DurationMinutes { get; set; }

    public ShiftType ShiftType { get; set; }
    public bool IsRecurring { get; set; }
    public string? Note { get; set; }
    public StaffScheduleStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Response danh sách lịch làm việc kèm filter info.
/// </summary>
public class StaffScheduleListResponseDto
{
    public Guid CafeId { get; set; }
    public Guid? FilterStaffUserId { get; set; }
    public DayOfWeek? FilterDayOfWeek { get; set; }
    public int TotalCount { get; set; }
    public List<StaffScheduleResponseDto> Schedules { get; set; } = new();
}

/// <summary>
/// Response sau khi copy lịch tuần.
/// </summary>
public class CopyTemplateResultDto
{
    public int CopiedCount { get; set; }
    public DateOnly FromWeekStart { get; set; }
    public DateOnly ToWeekStart { get; set; }
    public List<StaffScheduleResponseDto> NewSchedules { get; set; } = new();
}

/// <summary>
/// Response tổng hợp số giờ làm của staff trong khoảng thời gian.
/// </summary>
public class WorkHoursSummaryDto
{
    public Guid StaffUserId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    /// <summary>Tổng giờ lịch dự kiến (từ StaffSchedule).</summary>
    public int TotalScheduledMinutes { get; set; }

    /// <summary>Tổng giờ thực tế đã làm (từ ShiftAttendance).</summary>
    public int TotalWorkedMinutes { get; set; }

    /// <summary>Tổng số phút đi muộn.</summary>
    public int TotalLateMinutes { get; set; }

    /// <summary>Tổng số phút về sớm.</summary>
    public int TotalEarlyLeaveMinutes { get; set; }

    /// <summary>Số ngày có mặt (Present + Late).</summary>
    public int PresentDays { get; set; }

    /// <summary>Số ngày vắng (Absent).</summary>
    public int AbsentDays { get; set; }

    /// <summary>Số ca đang chờ check-out (đã check-in nhưng chưa check-out).</summary>
    public int InProgressDays { get; set; }
}

/// <summary>
/// Response yêu cầu nghỉ phép.
/// </summary>
public class TimeOffRequestResponseDto
{
    public Guid Id { get; set; }
    public Guid CafeId { get; set; }
    public Guid StaffUserId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string? Reason { get; set; }
    public TimeOffStatus Status { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewerName { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Response yêu cầu đổi ca.
/// </summary>
public class ShiftSwapRequestResponseDto
{
    public Guid Id { get; set; }
    public Guid CafeId { get; set; }
    public Guid RequesterId { get; set; }
    public string RequesterName { get; set; } = string.Empty;
    public Guid TargetStaffId { get; set; }
    public string TargetStaffName { get; set; } = string.Empty;
    public Guid RequesterScheduleId { get; set; }
    public StaffScheduleResponseDto? RequesterSchedule { get; set; }
    public Guid TargetScheduleId { get; set; }
    public StaffScheduleResponseDto? TargetSchedule { get; set; }
    public ShiftSwapStatus Status { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public string? ReviewerName { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Response ngày nghỉ cố định của staff.
/// </summary>
public class UnavailableDateResponseDto
{
    public Guid Id { get; set; }
    public Guid StaffUserId { get; set; }
    public Guid CafeId { get; set; }
    public DateOnly Date { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Response check-in/out ca làm việc.
/// </summary>
public class ShiftAttendanceResponseDto
{
    public Guid Id { get; set; }
    public Guid ScheduleId { get; set; }
    public Guid StaffUserId { get; set; }
    public string StaffName { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public DateTime? CheckInTime { get; set; }
    public DateTime? CheckOutTime { get; set; }
    public int LateMinutes { get; set; }
    public int EarlyLeaveMinutes { get; set; }
    public string? Status { get; set; }
    public string? Note { get; set; }
}
