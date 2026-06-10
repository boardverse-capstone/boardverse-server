using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Inventory
{
    public class GetCafeInventoryQuery
    {
        public string? SearchTerm { get; set; }
        public CafeGameInventoryStatus? Status { get; set; }
        public InventorySortField SortBy { get; set; } = InventorySortField.UpdatedAt;
        public bool SortDescending { get; set; } = true;

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
