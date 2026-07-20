namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái giải đấu Splendor.
/// Theo state-machine Tournament - Swiss 3 rounds + Final 4.
/// </summary>
public enum TournamentStatus
{
    /// <summary>Bản nháp. Manager đang cấu hình, chưa mở đăng ký.</summary>
    Draft = 0,

    /// <summary>Đã mở đăng ký. Người chơi có thể đăng ký tham gia.</summary>
    RegistrationOpen = 1,

    /// <summary>Hết hạn đăng ký, đang ghép bàn Swiss round đầu tiên.</summary>
    RegistrationClosed = 2,

    /// <summary>Đang diễn ra (Swiss + Final).</summary>
    OnGoing = 3,

    /// <summary>Đã kết thúc. Đã trao giải và cập nhật FinalRank cho participants.</summary>
    Completed = 4,

    /// <summary>Đã hủy. Có thể do manager hủy hoặc không đủ người.</summary>
    Cancelled = 5
}
