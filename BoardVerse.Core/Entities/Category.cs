namespace BoardVerse.Core.Entities
{
    /// <summary>
    /// Thể loại board game (Ẩn vai, Chiến thuật, Giải trí...).
    /// Bảng DB: Categories
    /// </summary>
    public class Category
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<GameTemplateCategory> GameTemplates { get; set; } = [];
    }
}
