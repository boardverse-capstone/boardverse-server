namespace BoardVerse.Core.DTOs.Discovery;

/// <summary>
/// Response DTO cho Survey — trả về danh sách board game được đề xuất kèm thông tin điều hướng.
/// </summary>
public class BoardGameSurveyResponseDto
{
    /// <summary>
    /// Danh sách board game phù hợp với tiêu chí khảo sát.
    /// </summary>
    public List<DiscoveryBoardGameDto> Games { get; set; } = [];

    /// <summary>
    /// Tổng số kết quả tìm được.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Số người chơi đã khảo sát.
    /// </summary>
    public int SurveyedPlayerCount { get; set; }

    /// <summary>
    /// Thể loại đã lọc.
    /// </summary>
    public List<string> AppliedFilters { get; set; } = [];

    /// <summary>
    /// Gợi ý quán cafe gần nhất có game phù hợp (nếu user gửi location).
    /// </summary>
    public NearbyCafeForGameDto? NearestCafe { get; set; }

    /// <summary>
    /// Số lobby đang mở cho game được đề xuất.
    /// </summary>
    public List<OpenLobbySummaryDto> OpenLobbies { get; set; } = [];
}

/// <summary>
/// DTO cho board game trong discovery.
/// </summary>
public class DiscoveryBoardGameDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string? Description { get; set; }
    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public int PlayTimeMinutes { get; set; }
    public List<string> Categories { get; set; } = [];
    public double MatchScore { get; set; }
    public bool IsSaved { get; set; }
    public bool HasOpenLobby { get; set; }
}

/// <summary>
/// DTO cho quán cafe gần nhất có game.
/// </summary>
public class NearbyCafeForGameDto
{
    public Guid CafeId { get; set; }
    public string CafeName { get; set; } = string.Empty;
    public string? CafeAddress { get; set; }
    public double? DistanceKm { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int AvailableGamesCount { get; set; }
    public bool HasOpenLobby { get; set; }
}

/// <summary>
/// Tóm tắt lobby đang mở.
/// </summary>
public class OpenLobbySummaryDto
{
    public Guid LobbyId { get; set; }
    public Guid GameTemplateId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public int CurrentMembers { get; set; }
    public int MaxMembers { get; set; }
    public DateTime? PlayDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public Guid? CafeId { get; set; }
    public string? CafeName { get; set; }
    public bool IsOpen { get; set; }
}
