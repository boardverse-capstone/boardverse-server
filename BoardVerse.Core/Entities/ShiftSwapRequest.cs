using BoardVerse.Core.Enum;

namespace BoardVerse.Core.Entities;

/// <summary>
/// Yêu cầu đổi ca giữa 2 staff.
/// Requester muốn đổi ca của mình (RequesterScheduleId) với ca của TargetStaff (TargetScheduleId).
/// Manager sẽ duyệt — sau khi duyệt, 2 lịch sẽ được hoán đổi StaffUserId.
/// </summary>
public class ShiftSwapRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CafeId { get; set; }

    /// <summary>Staff muốn đổi ca (sender).</summary>
    public Guid RequesterId { get; set; }

    /// <summary>Staff được yêu cầu đổi ca (target).</summary>
    public Guid TargetStaffId { get; set; }

    /// <summary>Ca của Requester muốn cho đi.</summary>
    public Guid RequesterScheduleId { get; set; }

    /// <summary>Ca của Target mà Requester muốn nhận.</summary>
    public Guid TargetScheduleId { get; set; }

    public ShiftSwapStatus Status { get; set; } = ShiftSwapStatus.Pending;

    public Guid? ReviewedByUserId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual Cafe Cafe { get; set; } = null!;
    public virtual User Requester { get; set; } = null!;
    public virtual User TargetStaff { get; set; } = null!;
}
