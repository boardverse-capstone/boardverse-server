namespace BoardVerse.Core.DTOs.Game
{
    public class MasterGameResponseDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public string? Description { get; set; }
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public int PlayTime { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<GameComponentTemplateDto> Components { get; set; } = new();
        public List<CategoryDto> Categories { get; set; } = new();
        public bool? AlreadyInInventory { get; set; }
    }
}
