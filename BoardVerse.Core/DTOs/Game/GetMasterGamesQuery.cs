using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Game
{
    public class GetMasterGamesQuery
    {
        public string? SearchTerm { get; set; }
        public List<Guid>? CategoryIds { get; set; }
        public int? PlayerCount { get; set; }
        public List<PlayTimeRange>? PlayTimeRanges { get; set; }
        public Guid? CafeId { get; set; }
        public bool ExcludeInInventory { get; set; }
        private int _pageNumber = 1;
        private int _pageSize = 10;

        public int PageNumber
        {
            get => _pageNumber;
            set => _pageNumber = value < 1 ? 1 : value;
        }

        public int PageSize
        {
            get => _pageSize;
            set => _pageSize = value < 1 ? 10 : (value > 100 ? 100 : value);
        }
    }
}
