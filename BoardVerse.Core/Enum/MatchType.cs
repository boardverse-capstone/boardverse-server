namespace BoardVerse.Core.Enum;

/// <summary>
/// Loại bàn đấu trong tournament.
/// </summary>
public enum MatchType
{
    /// <summary>Vòng Swiss thông thường.</summary>
    Swiss = 0,

    /// <summary>Bàn chung kết (Final).</summary>
    Final = 1,

    /// <summary>Trận tranh hạng 3 (Third Place Match).</summary>
    ThirdPlaceMatch = 2
}
