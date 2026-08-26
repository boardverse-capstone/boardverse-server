using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Override lịch mặc định của Cafe cho từng ngày cụ thể.
/// BR-NEW-15 (2026-08-18): BỎ TimeSlot enum - dùng ApplyDate/OpenTime/CloseTime.
/// </summary>
public class CafeScheduleOverride
{
    public Guid Id { get; set; }

    /// <summary>FK Cafe.</summary>
    public Guid CafeId { get; set; }

    /// <summary>Ngày áp dụng override.</summary>
    public DateOnly ApplyDate { get; set; }

    /// <summary>Giờ mở cửa override. null = dùng default CafeSchedule.DefaultOpenTime.</summary>
    public TimeOnly? OpenTime { get; set; }

    /// <summary>Giờ đóng cửa override. null = dùng default CafeSchedule.DefaultCloseTime.</summary>
    public TimeOnly? CloseTime { get; set; }

    /// <summary>true = cafe đóng cửa vào ngày này, không nhận reservation.</summary>
    public bool IsClosed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Cafe? Cafe { get; set; }
}
