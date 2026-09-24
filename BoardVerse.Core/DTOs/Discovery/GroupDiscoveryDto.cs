using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Discovery;

/// <summary>
/// Request DTO cho Group Discovery — gợi ý game cho nhóm nhiều người với sở thích khác nhau.
/// Sử dụng AWM (Average of Weighted Member match scores) để tìm game phù hợp nhất cho cả nhóm.
/// </summary>
public class GroupDiscoveryRequestDto
{
    /// <summary>
    /// Danh sách thành viên trong nhóm, mỗi entry là một "nhóm con" với sở thích riêng.
    /// Tối thiểu 1, tối đa 4 nhóm con (mỗi nhóm tối thiểu 1 người).
    /// </summary>
    public List<MemberPreferenceDto> Members { get; set; } = [];

    /// <summary>
    /// Số người chơi tổng cộng trong nhóm (sum của tất cả Members.PlayerCount).
    /// Server tính tự động nhưng có thể gửi để xác thực.
    /// </summary>
    public int? TotalPlayerCount { get; set; }

    /// <summary>
    /// Kinh độ player (WGS84, optional — để tìm quán cafe gần nhất).
    /// </summary>
    public double? Latitude { get; set; }

    /// <summary>
    /// Vĩ độ player (WGS84, optional).
    /// </summary>
    public double? Longitude { get; set; }

    /// <summary>
    /// Bán kính tìm quán cafe (km). Mặc định 15km.
    /// </summary>
    public double? RadiusKm { get; set; }
}

/// <summary>
/// Sở thích của một thành viên (hoặc nhóm con) trong group discovery.
/// </summary>
public class MemberPreferenceDto
{
    /// <summary>
    /// User ID của thành viên (optional — để personalize từ saved games).
    /// </summary>
    public Guid? UserId { get; set; }

    /// <summary>
    /// Số người trong nhóm con này (sub-group).
    /// </summary>
    public int PlayerCount { get; set; }

    /// <summary>
    /// Kinh nghiệm chơi của nhóm con.
    /// </summary>
    public PlayerExperienceLevel? ExperienceLevel { get; set; }

    /// <summary>
    /// Thể loại game ưa thích (multi-select GUID).
    /// </summary>
    public List<Guid>? CategoryIds { get; set; }

    /// <summary>
    /// Thời gian chơi mong muốn (multi-select).
    /// </summary>
    public List<string>? PreferredDurations { get; set; }

    /// <summary>
    /// BGG complexity weight ranges (multi-select: Light, MediumLight, Medium, MediumHeavy, Heavy).
    /// </summary>
    public List<WeightRange>? WeightRanges { get; set; }

    /// <summary>
    /// Ghi chú thêm (optional, ví dụ: "muốn chơi party game", "thích chiến thuật").
    /// </summary>
    public string? Note { get; set; }
}

/// <summary>
/// Response DTO cho Group Discovery.
/// </summary>
public class GroupDiscoveryResponseDto
{
    /// <summary>
    /// Danh sách game được đề xuất, sắp xếp theo AggregateScore giảm dần.
    /// </summary>
    public List<GroupDiscoveryBoardGameDto> Games { get; set; } = [];

    /// <summary>
    /// Tổng số người chơi trong nhóm.
    /// </summary>
    public int TotalPlayerCount { get; set; }

    /// <summary>
    /// Số lượng sub-group trong nhóm.
    /// </summary>
    public int SubGroupCount { get; set; }

    /// <summary>
    /// Quán cafe gần nhất có game đứng đầu (nếu có location).
    /// </summary>
    public NearbyCafeForGameDto? NearestCafe { get; set; }
}

/// <summary>
/// Board game trong group discovery response, kèm breakdown điểm theo sub-group.
/// </summary>
public class GroupDiscoveryBoardGameDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string? Description { get; set; }
    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public int PlayTimeMinutes { get; set; }

    /// <summary>
    /// BGG Complexity Weight (1.0 – 5.0).
    /// </summary>
    public double? Weight { get; set; }

    public List<string> Categories { get; set; } = [];

    /// <summary>
    /// Aggregate Weighted Match Score — điểm tổng hợp cho cả nhóm (AWMS).
    /// Thang 0-100. Càng cao = càng phù hợp với nhóm.
    /// </summary>
    public double AggregateScore { get; set; }

    /// <summary>
    /// Số lượng sub-group hài lòng với game này (individual score > 50).
    /// </summary>
    public int SatisfiedSubGroups { get; set; }

    /// <summary>
    /// Tổng số sub-group trong nhóm.
    /// </summary>
    public int TotalSubGroups { get; set; }

    /// <summary>
    /// Tỷ lệ phần trăm sub-group hài lòng với game này.
    /// </summary>
    public double SatisfactionRate => TotalSubGroups > 0
        ? (double)SatisfiedSubGroups / TotalSubGroups * 100.0
        : 0.0;

    /// <summary>
    /// Chi tiết điểm match cho từng sub-group.
    /// </summary>
    public List<SubGroupScoreDto> SubGroupBreakdown { get; set; } = [];

    /// <summary>
    /// Lý do game này được đề xuất (dựa trên lý do có điểm cao nhất).
    /// </summary>
    public string? TopMatchReason { get; set; }

    public bool HasOpenLobby { get; set; }
}

/// <summary>
/// Điểm match của một sub-group với game.
/// </summary>
public class SubGroupScoreDto
{
    /// <summary>
    /// Index của sub-group (0-based).
    /// </summary>
    public int SubGroupIndex { get; set; }

    /// <summary>
    /// Số người trong sub-group này.
    /// </summary>
    public int PlayerCount { get; set; }

    /// <summary>
    /// Kinh nghiệm của sub-group.
    /// </summary>
    public PlayerExperienceLevel? ExperienceLevel { get; set; }

    /// <summary>
    /// Individual match score (0-100).
    /// </summary>
    public double Score { get; set; }

    /// <summary>
    /// Lý do chính khiến game phù hợp/không phù hợp với sub-group này.
    /// </summary>
    public string? Reason { get; set; }
}
