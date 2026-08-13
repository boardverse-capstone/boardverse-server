namespace BoardVerse.Core.Enum;

/// <summary>
/// Mức karma tổng quát của user (tính từ <c>UserProfile.KarmaPoints</c>).
/// BR-KARMA-01 §4.3 + §9.5.
/// </summary>
public enum KarmaLevel
{
    /// <summary>Karma >= 90.</summary>
    Excellent = 0,

    /// <summary>Karma 70-89.</summary>
    Good = 1,

    /// <summary>Karma 50-69.</summary>
    Average = 2,

    /// <summary>Karma 30-49.</summary>
    Low = 3,

    /// <summary>Karma 10-29.</summary>
    Poor = 4,

    /// <summary>Karma 0-9.</summary>
    Critical = 5
}

/// <summary>
/// Trạng thái xử lý của 1 violation (BR-KARMA-01 §4.3 + §9.5).
/// Dùng cho high-level <c>KarmaService</c> (alert, restrict, appeal).
/// </summary>
public enum KarmaStatus
{
    /// <summary>Violation mới, chưa xử lý.</summary>
    Active = 0,

    /// <summary>Đã gửi cảnh báo cho user.</summary>
    WarningSent = 1,

    /// <summary>Đã áp dụng restriction (chỉ cho đặt slot >= 4h).</summary>
    Restricted = 2,

    /// <summary>User đã gửi appeal đang chờ admin review.</summary>
    AppealPending = 3,

    /// <summary>Appeal được chấp thuận, violation bị xóa.</summary>
    ClearedByAppeal = 4,

    /// <summary>Violation đã hết hạn sau 30 ngày không vi phạm mới (BR-KARMA-04).</summary>
    Expired = 5
}
