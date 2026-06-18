using BoardVerse.Core.DTOs.Game;

namespace BoardVerse.Core.DTOs.Cafe
{
    public class NearbyAlternativeGameSuggestionDto
    {
        public Guid GameTemplateId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public string? ThumbnailUrl { get; set; }
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public int NearbyCafeCount { get; set; }
        public double NearestCafeDistanceMeters { get; set; }
        public int AvailableBoxCount { get; set; }
        public List<CategoryDto> SharedCategories { get; set; } = [];
    }
}
