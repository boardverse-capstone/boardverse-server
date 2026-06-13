namespace BoardVerse.Core.DTOs.Game
{
    public class BoardGameListItemDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public string? Description { get; set; }
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public int PlayTime { get; set; }
        public int ComponentCount { get; set; }
        public List<CategoryDto> Categories { get; set; } = [];
    }
}
