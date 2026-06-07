namespace BoardVerse.Core.Entities
{
    public class GameComponentTemplate
    {
        public Guid Id { get; set; }
        public Guid GameTemplateId { get; set; }
        public string ComponentName { get; set; } = string.Empty;
        public int DefaultQuantity { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property to GameTemplate
        public virtual GameTemplate GameTemplate { get; set; } = null!;
    }
}
