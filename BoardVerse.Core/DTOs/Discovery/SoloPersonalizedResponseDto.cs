namespace BoardVerse.Core.DTOs.Discovery;

/// <summary>
/// Response DTO for solo personalized discovery.
/// Contains personalized game recommendations with scoring breakdown.
/// </summary>
public class SoloPersonalizedResponseDto
{
    /// <summary>
    /// Danh sách games được gợi ý, sắp xếp theo PersonalizedScore giảm dần.
    /// </summary>
    public List<PersonalizedBoardGameDto> Games { get; set; } = [];

    /// <summary>
    /// User preference profile extracted from saved games.
    /// Null nếu user chưa có đủ 3 saved games.
    /// </summary>
    public UserGamePreference? UserProfile { get; set; }

    /// <summary>
    /// Tổng số games tìm thấy trước khi phân trang.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Nearest cafe có game top 1 (nếu có location).
    /// </summary>
    public NearbyCafeForGameDto? NearestCafe { get; set; }

    /// <summary>
    /// Lobbies đang mở cho game top 1.
    /// </summary>
    public List<OpenLobbySummaryDto> OpenLobbies { get; set; } = [];
}

/// <summary>
/// Personalized board game recommendation with scoring breakdown.
/// </summary>
public class PersonalizedBoardGameDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string? Description { get; set; }
    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public int PlayTimeMinutes { get; set; }
    public double? Weight { get; set; }
    public List<string> Categories { get; set; } = [];

    /// <summary>
    /// Base match score (0-100) dựa trên filters.
    /// </summary>
    public double BaseScore { get; set; }

    /// <summary>
    /// Personalization boost (0-15) dựa trên saved games.
    /// </summary>
    public double PersonalizationBoost { get; set; }

    /// <summary>
    /// Play history penalty (-10 to 0) dựa trên frequency/recency.
    /// </summary>
    public double PlayHistoryPenalty { get; set; }

    /// <summary>
    /// Final personalized score (BaseScore + Boost + Penalty).
    /// </summary>
    public double PersonalizedScore { get; set; }

    /// <summary>
    /// Lý do match chính (ngắn gọn, 1 câu).
    /// </summary>
    public string? MatchReason { get; set; }

    /// <summary>
    /// User đã lưu game này chưa.
    /// </summary>
    public bool IsSaved { get; set; }

    /// <summary>
    /// Có lobby đang mở cho game này không.
    /// </summary>
    public bool HasOpenLobby { get; set; }
}
