namespace BoardVerse.Core.DTOs.Discovery;

/// <summary>
/// DTO cho danh sách board game đã save của player.
/// </summary>
public class SavedBoardGameDto
{
    public Guid Id { get; set; }
    public Guid GameTemplateId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string? Description { get; set; }
    public int MinPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public int PlayTimeMinutes { get; set; }
    public List<string> Categories { get; set; } = [];
    public DateTime SavedAt { get; set; }
    public bool HasOpenLobby { get; set; }
}

/// <summary>
/// Request DTO để lưu board game.
/// </summary>
public class SaveBoardGameRequestDto
{
    public Guid GameTemplateId { get; set; }
}

/// <summary>
/// Response DTO khi save/unsave thành công.
/// </summary>
public class BoardGameSaveResultDto
{
    public Guid GameTemplateId { get; set; }
    public bool IsSaved { get; set; }
    public DateTime? SavedAt { get; set; }
}
