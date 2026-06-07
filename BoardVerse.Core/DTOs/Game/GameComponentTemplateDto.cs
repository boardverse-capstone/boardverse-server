namespace BoardVerse.Core.DTOs.Game
{
    public class GameComponentTemplateDto
    {
        public Guid Id { get; set; }
        public Guid GameTemplateId { get; set; }
        public string ComponentName { get; set; } = string.Empty;
        public int DefaultQuantity { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
