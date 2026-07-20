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

        /// <summary>
        /// Khi <c>true</c>, repository sẽ bỏ qua <c>Skip/Take</c> và trả về toàn bộ kết quả
        /// (áp dụng cho mobile Player khi gọi "Get All" mà không truyền filter).
        /// Mặc định <c>true</c> khi tất cả filter đều rỗng; <c>false</c> khi có bất kỳ filter.
        /// </summary>
        public bool SkipPagination => !HasFilter;

        /// <summary>
        /// True nếu request có ít nhất 1 filter (search, category, player count, play time, cafe, exclude).
        /// </summary>
        public bool HasFilter =>
            !string.IsNullOrWhiteSpace(SearchTerm)
            || (CategoryIds is { Count: > 0 })
            || PlayerCount.HasValue
            || (PlayTimeRanges is { Count: > 0 })
            || CafeId.HasValue
            || ExcludeInInventory;

        public int PageSize
        {
            get
            {
                if (SkipPagination) return int.MaxValue;
                return _pageSize;
            }
            set => _pageSize = value < 1 ? 10 : (value > 100 ? 100 : value);
        }

        public int PageNumber
        {
            get => _pageNumber;
            set => _pageNumber = value < 1 ? 1 : value;
        }
    }
}
