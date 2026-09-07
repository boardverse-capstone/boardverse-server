namespace BoardVerse.Core.Entities;

/// <summary>
/// Lưu trữ game mà player đã save/lưu lại để xem lại sau.
/// Khi user thấy game hay trong Discovery Survey, có thể save để tham khảo.
/// </summary>
public class PlayerBoardGameSave
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid GameTemplateId { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;

    public virtual User User { get; set; } = null!;
    public virtual GameTemplate GameTemplate { get; set; } = null!;
}
