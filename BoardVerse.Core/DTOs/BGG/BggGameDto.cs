namespace BoardVerse.Core.DTOs.BGG
{
    public class BggGameDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? ImageUrl { get; set; }
        public int YearPublished { get; set; }
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public int MinPlayTime { get; set; }
        public int MaxPlayTime { get; set; }
        public int PlayingTime { get; set; }
        public List<string> Categories { get; set; } = new();
        public List<string> Mechanics { get; set; } = new();
        public List<BggComponentDto> Components { get; set; } = new();
    }

    public class BggComponentDto
    {
        public string Name { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }
}
