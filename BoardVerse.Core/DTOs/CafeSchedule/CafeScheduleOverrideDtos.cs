using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.CafeSchedule;

/// <summary>
/// Request tạo/sửa <c>CafeScheduleOverride</c>.
/// </summary>
public class UpsertCafeScheduleOverrideRequestDto
{
    public TimeSlot TimeSlot { get; set; }

    /// <summary>Giờ bắt đầu override. null = dùng default <c>CafeSchedule.GetStartTime(slot)</c>.</summary>
    public TimeOnly? StartTime { get; set; }

    /// <summary>Giờ kết thúc override. null = dùng default <c>CafeSchedule.GetEndTime(slot)</c>.</summary>
    public TimeOnly? EndTime { get; set; }

    /// <summary>true = cafe đóng slot này.</summary>
    public bool IsClosed { get; set; }

    /// <summary>Optional: override chỉ áp dụng từ ngày này (inclusive).</summary>
    public DateOnly? EffectiveFrom { get; set; }

    /// <summary>Optional: override chỉ áp dụng đến ngày này (inclusive).</summary>
    public DateOnly? EffectiveTo { get; set; }
}

/// <summary>
/// Response trả về thông tin override của cafe (kèm default từ <c>CafeSchedule</c> nếu không có override).
/// </summary>
public class CafeScheduleOverrideResponseDto
{
    public Guid Id { get; set; }
    public Guid CafeId { get; set; }
    public TimeSlot TimeSlot { get; set; }
    public string TimeSlotDisplay => TimeSlot.ToString();

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsClosed { get; set; }

    public bool HasOverride { get; set; }
    public bool IsDefault => !HasOverride;

    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Response trả về toàn bộ 4 slot của 1 cafe (kèm default nếu không có override).
/// </summary>
public class CafeScheduleResponseDto
{
    public Guid CafeId { get; set; }
    public List<CafeScheduleOverrideResponseDto> Slots { get; set; } = new();
}
