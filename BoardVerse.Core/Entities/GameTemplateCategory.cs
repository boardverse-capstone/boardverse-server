namespace BoardVerse.Core.Entities
{
    /// <summary>
    /// Bảng nối N-N giữa board game (GameTemplates) và thể loại (Categories).
    /// Bảng DB: GameTemplateCategories
    /// </summary>
    public class GameTemplateCategory
    {
        public Guid GameTemplateId { get; set; }
        public Guid CategoryId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public virtual GameTemplate GameTemplate { get; set; } = null!;
        public virtual Category Category { get; set; } = null!;
    }
}
