using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Game
{
    public class GameComponentTemplateDto
    {
        public Guid Id { get; set; }
        public Guid GameTemplateId { get; set; }
        public string ComponentName { get; set; } = string.Empty;
        public BoardGameComponentKind? ComponentKind { get; set; }
        public int DefaultQuantity { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
