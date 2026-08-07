namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái của một waitlist entry.
/// </summary>
public enum TournamentWaitlistStatus
{
    /// <summary>Đang chờ slot.</summary>
    Pending = 0,

    /// <summary>Đã có slot mở, đang chờ user xác nhận.</summary>
    Offered = 1,

    /// <summary>User đã xác nhận và tham gia tournament.</summary>
    Joined = 2,

    /// <summary>Hết hạn offer mà không xác nhận.</summary>
    Expired = 3,

    /// <summary>User chủ động rời waitlist.</summary>
    Cancelled = 4
}
