namespace BoardVerse.Core.DTOs.Game
{
    public class GameCatalogSeedDto
    {
        public string Slug { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public string? Description { get; set; }
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public int PlayTime { get; set; }
        public int? BggId { get; set; }
        public List<GameCatalogComponentDto> Components { get; set; } = [];
    }

    public class GameCatalogComponentDto
    {
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }
}
