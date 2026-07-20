namespace BoardVerse.Core.Enum;

/// <summary>
/// Trạng thái đăng ký của một người chơi trong tournament.
/// </summary>
public enum TournamentParticipantStatus
{
    /// <summary>Đã đăng ký nhưng chưa check-in tại quán.</summary>
    Registered = 0,

    /// <summary>Đã check-in tại quán, sẵn sàng thi đấu.</summary>
    CheckedIn = 1,

    /// <summary>Đang trong vòng đấu. Có thể đã bị loại (Eliminated) hoặc còn thi đấu (Active).</summary>
    Active = 2,

    /// <summary>Đã bị loại (rớt khỏi tournament, áp dụng cho Final 4 elimination).</summary>
    Eliminated = 3,

    /// <summary>Không đến (no-show) sau khi tournament đã OnGoing.</summary>
    NoShow = 4,

    /// <summary>Hoàn thành tournament (đã xếp hạng cuối cùng).</summary>
    Finished = 5,

    /// <summary>Tự rút lui hoặc bị loại do vi phạm.</summary>
    Withdrawn = 6
}
