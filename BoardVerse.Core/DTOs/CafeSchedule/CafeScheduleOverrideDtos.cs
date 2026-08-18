namespace BoardVerse.Core.DTOs.CafeSchedule;

/// <summary>
/// Request tạo/sửa CafeScheduleOverride.
/// BR-NEW-15 (2026-08-18): BỎ TimeSlot - dùng ApplyDate/OpenTime/CloseTime.
/// </summary>
public class UpsertCafeScheduleOverrideRequestDto
{
    /// <summary>Ngày áp dụng override.</summary>
    public DateOnly ApplyDate { get; set; }

    /// <summary>Giờ mở cửa override. null = dùng default.</summary>
    public TimeOnly? OpenTime { get; set; }

    /// <summary>Giờ đóng cửa override. null = dùng default.</summary>
    public TimeOnly? CloseTime { get; set; }

    /// <summary>true = cafe đóng cửa ngày này.</summary>
    public bool IsClosed { get; set; }
}

/// <summary>
/// Response trả về thông tin override của cafe (kèm default nếu không có override).
/// </summary>
public class CafeScheduleOverrideResponseDto
{
    public Guid Id { get; set; }
    public Guid CafeId { get; set; }

    /// <summary>Ngày áp dụng.</summary>
    public DateOnly ApplyDate { get; set; }

    public TimeOnly OpenTime { get; set; }
    public TimeOnly CloseTime { get; set; }
    public bool IsClosed { get; set; }

    public bool HasOverride { get; set; }
    public bool IsDefault => !HasOverride;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Response trả về toàn bộ override của 1 cafe.
/// </summary>
public class CafeScheduleResponseDto
{
    public Guid CafeId { get; set; }
    public List<CafeScheduleOverrideResponseDto> Days { get; set; } = new();
}
