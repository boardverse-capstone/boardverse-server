namespace BoardVerse.Core.Entities;

/// <summary>
/// Ngày staff đánh dấu không thể làm việc (vd: nghỉ phép cá nhân, khám bệnh).
/// Hệ thống sẽ cảnh báo Manager khi lên lịch ca trùng ngày này.
/// </summary>
public class StaffUnavailableDate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CafeId { get; set; }
    public Guid StaffUserId { get; set; }

    /// <summary>Ngày cụ thể staff không thể làm việc.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Lý do (tùy chọn, staff có thể ghi chú).</summary>
    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Cafe Cafe { get; set; } = null!;
    public virtual User User { get; set; } = null!;
}
