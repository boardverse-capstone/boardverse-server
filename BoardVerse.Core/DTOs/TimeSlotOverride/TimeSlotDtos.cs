using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.TimeSlotOverride;

/// <summary>
/// TimeSlot cố định trong hệ thống (BR-NEW-15 §7.1).
/// Enum là bất biến — manager không thể thêm slot mới, chỉ override StartTime/EndTime/IsClosed theo từng cafe qua <c>CafeScheduleOverride</c>.
/// </summary>
/// <remarks>
/// 4 slot mặc định cover 24/7:
/// <list type="bullet">
/// <item><description><c>Morning</c>:    06:00 – 12:00.</description></item>
/// <item><description><c>Afternoon</c>: 12:00 – 17:00.</description></item>
/// <item><description><c>Evening</c>:   17:00 – 23:00.</description></item>
/// <item><description><c>LateNight</c>: 23:00 – 06:00 (next day, overnight).</description></item>
/// </list>
/// </remarks>
public class DefaultTimeSlotDto
{
    /// <summary>Enum TimeSlot: Morning / Afternoon / Evening / LateNight.</summary>
    public string Slot { get; set; } = string.Empty;

    /// <summary>Tên hiển thị tiếng Việt (mapping từ <c>CafeSchedule</c>).</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Giờ bắt đầu mặc định.</summary>
    public TimeOnly DefaultStartTime { get; set; }

    /// <summary>Giờ kết thúc mặc định.</summary>
    public TimeOnly DefaultEndTime { get; set; }

    /// <summary>Số phút duration (LateNight overnight = 420 phút).</summary>
    public int DurationMinutes { get; set; }

    /// <summary>Mô tả ngắn cho UI manager.</summary>
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Response trả về TimeSlot cho 1 cafe — gộp default + override (nếu có).
/// Manager dùng để hiển thị form cấu hình giờ mở cửa.
/// </summary>
public class ManagerTimeSlotResponseDto
{
    /// <summary>Id của override row. <c>Guid.Empty</c> nếu cafe chưa có override (đang dùng default).</summary>
    public Guid Id { get; set; }

    public Guid CafeId { get; set; }

    /// <summary>Enum TimeSlot: Morning / Afternoon / Evening / LateNight.</summary>
    public string TimeSlot { get; set; } = string.Empty;

    /// <summary>Giờ bắt đầu hiệu lực (sau khi áp override nếu có).</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>Giờ kết thúc hiệu lực (sau khi áp override nếu có).</summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>Giờ bắt đầu default (từ <c>CafeSchedule</c>) — để FE hiển thị "đã đổi từ default".</summary>
    public TimeOnly DefaultStartTime { get; set; }

    /// <summary>Giờ kết thúc default (từ <c>CafeSchedule</c>).</summary>
    public TimeOnly DefaultEndTime { get; set; }

    /// <summary>true = cafe đóng slot này.</summary>
    public bool IsClosed { get; set; }

    /// <summary>true = có override row trong DB.</summary>
    public bool HasOverride { get; set; }

    /// <summary>true = StartTime hoặc EndTime đã khác default.</summary>
    public bool IsCustomized => HasOverride && (StartTime != DefaultStartTime || EndTime != DefaultEndTime || IsClosed);

    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    /// <summary>Thời điểm tạo override. <c>null</c> nếu chưa có override.</summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>Lần cập nhật cuối. <c>null</c> nếu chưa có override.</summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Request tạo override cho 1 TimeSlot (manager).
/// </summary>
public class CreateTimeSlotOverrideRequestDto
{
    [Required(ErrorMessage = ApiErrorMessages.Validation.TimeSlotRequired)]
    public string TimeSlot { get; set; } = string.Empty;

    /// <summary>Giờ bắt đầu override. null = giữ default.</summary>
    public TimeOnly? StartTime { get; set; }

    /// <summary>Giờ kết thúc override. null = giữ default.</summary>
    public TimeOnly? EndTime { get; set; }

    /// <summary>true = cafe đóng slot này.</summary>
    public bool IsClosed { get; set; }

    /// <summary>Optional: override chỉ áp dụng từ ngày này (inclusive).</summary>
    public DateOnly? EffectiveFrom { get; set; }

    /// <summary>Optional: override chỉ áp dụng đến ngày này (inclusive).</summary>
    public DateOnly? EffectiveTo { get; set; }
}

/// <summary>
/// Request cập nhật override cho 1 TimeSlot (manager — PUT partial update).
/// Field nào null = giữ nguyên giá trị hiện tại. Field nào có giá trị = cập nhật.
/// Để reset StartTime/EndTime về default, dùng DELETE endpoint (xóa override row).
/// </summary>
public class UpdateTimeSlotOverrideRequestDto
{
    /// <summary>Giờ bắt đầu override. null = giữ nguyên.</summary>
    public TimeOnly? StartTime { get; set; }

    /// <summary>Giờ kết thúc override. null = giữ nguyên.</summary>
    public TimeOnly? EndTime { get; set; }

    /// <summary>true = cafe đóng slot này. null = giữ nguyên.</summary>
    public bool? IsClosed { get; set; }

    /// <summary>Optional: bắt đầu áp dụng. null = giữ nguyên.</summary>
    public DateOnly? EffectiveFrom { get; set; }

    /// <summary>Optional: kết thúc áp dụng. null = giữ nguyên.</summary>
    public DateOnly? EffectiveTo { get; set; }
}
