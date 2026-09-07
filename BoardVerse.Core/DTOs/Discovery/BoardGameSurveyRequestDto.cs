namespace BoardVerse.Core.DTOs.Discovery;

/// <summary>
/// Request DTO cho Survey — khảo sát để gợi ý board game phù hợp.
/// </summary>
public class BoardGameSurveyRequestDto
{
    /// <summary>
    /// Số người chơi muốn chơi. Giá trị 1-4 hoặc 5 (5+).
    /// </summary>
    public int PlayerCount { get; set; }

    /// <summary>
    /// Thể loại game yêu thích (multi-select GUID).
    /// </summary>
    public List<Guid>? CategoryIds { get; set; }

    /// <summary>
    /// Thời gian chơi ước lượng mong muốn (multi-select).
    /// </summary>
    public List<string>? PreferredDurations { get; set; }

    /// <summary>
    /// Kinh nghiệm chơi của user.
    /// </summary>
    public PlayerExperienceLevel? ExperienceLevel { get; set; }

    /// <summary>
    /// Từ khóa tìm kiếm thêm (optional).
    /// </summary>
    public string? SearchKeyword { get; set; }
}

/// <summary>
/// Mức kinh nghiệm chơi board game của user.
/// </summary>
public enum PlayerExperienceLevel
{
    /// <summary>Chưa biết gì / mới bắt đầu.</summary>
    Beginner = 1,

    /// <summary>Đã chơi vài lần.</summary>
    Casual = 2,

    /// <summary>Chơi thường xuyên.</summary>
    Regular = 3,

    /// <summary>Chơi rất nhiều / chuyên gia.</summary>
    Expert = 4
}
