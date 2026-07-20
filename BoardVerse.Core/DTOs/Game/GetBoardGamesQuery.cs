using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Game
{
    public class GetBoardGamesQuery
    {
        public string? Search { get; set; }
        public List<Guid>? CategoryIds { get; set; }
        public int? PlayerCount { get; set; }
        public List<PlayTimeRange>? DurationRange { get; set; }

        /// <summary>
        /// True nếu có ít nhất 1 filter — dùng để mobile gọi Get All không paginate.
        /// </summary>
        public bool HasFilter =>
            !string.IsNullOrWhiteSpace(Search)
            || (CategoryIds is { Count: > 0 })
            || PlayerCount.HasValue
            || (DurationRange is { Count: > 0 });

        private int _pageNumber = 1;
        private int _pageSize = 10;

        public int PageNumber
        {
            get => _pageNumber;
            set => _pageNumber = value < 1 ? 1 : value;
        }

        public int PageSize
        {
            get
            {
                if (!HasFilter) return int.MaxValue;
                return _pageSize;
            }
            set => _pageSize = value < 1 ? 10 : (value > 100 ? 100 : value);
        }

        public GetMasterGamesQuery ToMasterGamesQuery() =>
            new()
            {
                SearchTerm = Search,
                CategoryIds = CategoryIds,
                PlayerCount = PlayerCount is > 0 ? PlayerCount : null,
                PlayTimeRanges = DurationRange,
                PageNumber = PageNumber,
                PageSize = PageSize
            };
    }
}
