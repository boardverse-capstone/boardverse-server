using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Discovery;

/// <summary>
/// Request DTO for solo personalized discovery.
/// Returns game recommendations tailored to individual user's saved games and play history.
/// </summary>
public class SoloPersonalizedRequestDto
{
    /// <summary>
    /// Optional: filter by category IDs (UNION logic — game khớp ít nhất 1 category).
    /// </summary>
    public List<Guid>? CategoryIds { get; set; }

    /// <summary>
    /// Optional: filter by preferred durations (under30, 30to60, over60).
    /// Multiple values = UNION (game khớp ít nhất 1 range).
    /// </summary>
    public List<string>? PreferredDurations { get; set; }

    /// <summary>
    /// Optional: filter by weight ranges (1=Light, 2=MediumLight, 3=Medium, 4=MediumHeavy, 5=Heavy).
    /// Multiple values = UNION (game khớp ít nhất 1 range).
    /// </summary>
    public List<WeightRange>? WeightRanges { get; set; }

    /// <summary>
    /// Optional: số người chơi (1-5+). Nếu null → không filter theo player count.
    /// </summary>
    public int? PlayerCount { get; set; }

    /// <summary>
    /// Optional: search keyword (game name, description).
    /// </summary>
    public string? SearchKeyword { get; set; }

    /// <summary>
    /// Số lượng games trả về tối đa (default 20).
    /// </summary>
    public int PageSize { get; set; } = 20;

    /// <summary>
    /// Có loại trừ games đã lưu khỏi kết quả không (default false).
    /// True → chỉ gợi ý games mới chưa lưu.
    /// </summary>
    public bool ExcludeSavedGames { get; set; } = false;
}
