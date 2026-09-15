using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Yêu cầu nghỉ phép của staff — Manager sẽ duyệt hoặc từ chối.
/// Khi duyệt, hệ thống tự động tạo các bản ghi StaffUnavailableDate trong khoảng StartDate..EndDate.
/// </summary>
public class TimeOffRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CafeId { get; set; }
    public Guid StaffUserId { get; set; }

    /// <summary>Ngày bắt đầu nghỉ (inclusive).</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Ngày kết thúc nghỉ (inclusive).</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>Lý do nghỉ phép.</summary>
    public string? Reason { get; set; }

    /// <summary>Trạng thái (Pending/Approved/Rejected/Cancelled).</summary>
    public TimeOffStatus Status { get; set; } = TimeOffStatus.Pending;

    /// <summary>Manager đã xử lý yêu cầu (nullable nếu chưa xử lý).</summary>
    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAt { get; set; }

    /// <summary>Ghi chú của Manager khi duyệt/từ chối.</summary>
    public string? ReviewNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Cafe Cafe { get; set; } = null!;
    public virtual User Staff { get; set; } = null!;
    public virtual User? ReviewedByUser { get; set; } = null!;
}
