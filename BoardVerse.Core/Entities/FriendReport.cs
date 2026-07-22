namespace BoardVerse.Core.Entities;

/// <summary>
/// Loại báo cáo vi phạm đối với một user.
/// </summary>
public enum FriendReportCategory
{
    Spam = 0,
    Harassment = 1,
    FakeAccount = 2,
    InappropriateContent = 3,
    Other = 99
}

/// <summary>
/// Báo cáo vi phạm do user gửi về một người khác (chỉ trong context bạn bè).
/// BR-FRIEND-REPORT-01: Reporter phải từng có quan hệ Accepted với Target.
/// BR-FRIEND-REPORT-02: Một cặp (ReporterId, TargetId) chỉ có 1 Pending report.
/// BR-FRIEND-REPORT-03: Không báo cáo chính mình hoặc tài khoản Admin.
/// </summary>
public class FriendReport
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ReporterId { get; set; }

    public Guid TargetUserId { get; set; }

    public FriendReportCategory Category { get; set; } = FriendReportCategory.Other;

    /// <summary>Lý do chi tiết (5-1000 ký tự).</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Trạng thái xử lý: Pending / Reviewed / Dismissed.</summary>
    public string Status { get; set; } = "Pending";

    /// <summary>Admin xử lý.</summary>
    public Guid? ReviewedByAdminId { get; set; }

    public string? AdminNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }

    public virtual User Reporter { get; set; } = null!;
    public virtual User TargetUser { get; set; } = null!;
}
