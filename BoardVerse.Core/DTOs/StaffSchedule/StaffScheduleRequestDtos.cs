using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.StaffSchedule;

/// <summary>
/// Request tạo mới 1 lịch làm việc cho staff.
/// </summary>
public class CreateStaffScheduleRequestDto
{
    [Required]
    public Guid StaffUserId { get; set; }

    [Required]
    public DayOfWeek DayOfWeek { get; set; }

    [Required]
    public TimeOnly StartTime { get; set; }

    [Required]
    public TimeOnly EndTime { get; set; }

    public ShiftType ShiftType { get; set; } = ShiftType.Regular;

    /// <summary>Lặp hàng tuần — true = template cho mọi tuần.</summary>
    public bool IsRecurring { get; set; } = false;

    [MaxLength(500)]
    public string? Note { get; set; }
}

/// <summary>
/// Request cập nhật lịch làm việc.
/// </summary>
public class UpdateStaffScheduleRequestDto
{
    [Required]
    public TimeOnly StartTime { get; set; }

    [Required]
    public TimeOnly EndTime { get; set; }

    public ShiftType ShiftType { get; set; } = ShiftType.Regular;

    public bool IsRecurring { get; set; } = false;

    [MaxLength(500)]
    public string? Note { get; set; }

    public StaffScheduleStatus Status { get; set; } = StaffScheduleStatus.Active;
}

/// <summary>
/// Request tạo nhiều lịch cùng lúc (vd: copy toàn bộ lịch tuần).
/// </summary>
public class BulkCreateStaffScheduleRequestDto
{
    [Required]
    public List<CreateStaffScheduleRequestDto> Schedules { get; set; } = new();
}

/// <summary>
/// Request copy lịch từ tuần nguồn sang tuần đích.
/// </summary>
public class CopyWeekTemplateRequestDto
{
    /// <summary>Ngày bắt đầu tuần nguồn (DateOnly).</summary>
    [Required]
    public DateOnly FromWeekStart { get; set; }

    /// <summary>Ngày bắt đầu tuần đích (DateOnly).</summary>
    [Required]
    public DateOnly ToWeekStart { get; set; }

    /// <summary>Optional: chỉ copy cho staff này. Null = copy toàn bộ staff.</summary>
    public Guid? StaffUserIdFilter { get; set; }
}

/// <summary>
/// Request tạo yêu cầu nghỉ phép.
/// </summary>
public class CreateTimeOffRequestDto
{
    [Required]
    public DateOnly StartDate { get; set; }

    [Required]
    public DateOnly EndDate { get; set; }

    [MaxLength(1000)]
    public string? Reason { get; set; }
}

/// <summary>
/// Request duyệt / từ chối yêu cầu nghỉ phép.
/// </summary>
public class ReviewTimeOffRequestDto
{
    [Required]
    public TimeOffStatus Status { get; set; }

    [MaxLength(1000)]
    public string? ReviewNote { get; set; }
}

/// <summary>
/// Request tạo yêu cầu đổi ca.
/// </summary>
public class CreateShiftSwapRequestDto
{
    [Required]
    public Guid TargetStaffId { get; set; }

    [Required]
    public Guid RequesterScheduleId { get; set; }

    [Required]
    public Guid TargetScheduleId { get; set; }
}

/// <summary>
/// Request duyệt / từ chối đổi ca.
/// </summary>
public class ReviewShiftSwapRequestDto
{
    [Required]
    public ShiftSwapStatus Status { get; set; }

    [MaxLength(1000)]
    public string? ReviewNote { get; set; }
}

/// <summary>
/// Request thêm ngày nghỉ cố định cho staff.
/// </summary>
public class CreateUnavailableDateDto
{
    [Required]
    public DateOnly Date { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }
}

/// <summary>
/// Request check-in ca làm việc.
/// </summary>
public class CheckInAttendanceDto
{
    [Required]
    public DateTime CheckInTime { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}

/// <summary>
/// Request check-out ca làm việc.
/// </summary>
public class CheckOutAttendanceDto
{
    [Required]
    public DateTime CheckOutTime { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}
