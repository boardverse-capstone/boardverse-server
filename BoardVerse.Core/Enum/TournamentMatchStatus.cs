namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái của một bàn đấu (match bracket) trong tournament.
/// Mỗi bàn đấu có tối đa 4 người chơi Splendor.
/// </summary>
public enum TournamentMatchStatus
{
    /// <summary>Đã lên lịch, người chơi chưa ngồi vào bàn.</summary>
    Scheduled = 0,

    /// <summary>Đang diễn ra. Người chơi đang thi đấu.</summary>
    OnGoing = 1,

    /// <summary>Đã kết thúc. Đã ghi nhận WinnerPlayerId.</summary>
    Completed = 2,

    /// <summary>Bị hủy (ví dụ: người chơi no-show, bàn thiếu người).</summary>
    Cancelled = 3
}
