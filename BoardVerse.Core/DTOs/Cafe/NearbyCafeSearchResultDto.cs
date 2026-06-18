using BoardVerse.Core.Common;

namespace BoardVerse.Core.DTOs.Cafe
{
    public class NearbyCafeSearchResultDto
    {
        public PaginatedResponse<NearbyCafeDto> Cafes { get; set; } = new();
        public string? EmptyResultMessage { get; set; }
        public IReadOnlyList<NearbyAlternativeGameSuggestionDto> AlternativeSuggestions { get; set; } = [];
    }
}
