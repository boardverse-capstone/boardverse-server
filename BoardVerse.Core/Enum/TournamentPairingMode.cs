namespace BoardVerse.Core.Enum;

/// <summary>
/// Cách chia bàn (pairing) cho các vòng đấu tournament.
/// </summary>
public enum TournamentPairingMode
{
    /// <summary>Hệ thống tự động chia bàn theo Swiss rule (mặc định).</summary>
    Auto = 0,

    /// <summary>Manager tự chọn ai ngồi bàn nào qua POS UI.</summary>
    Manual = 1
}