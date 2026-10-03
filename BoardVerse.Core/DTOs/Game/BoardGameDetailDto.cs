namespace BoardVerse.Core.DTOs.Game
{
    public class BoardGameDetailDto
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
        public List<CategoryDto> Categories { get; set; } = [];
        public List<BoardGameComponentDto> Components { get; set; } = [];

        /// <summary>
        /// <c>true</c> nếu board game hiện tại đang nằm trong danh sách yêu thích (favorites) của player hiện tại.
        /// <c>false</c> nếu player chưa đăng nhập, chưa lưu game này, hoặc token không hợp lệ.
        /// Tương tự field <c>isSaved</c> trong <c>DiscoveryBoardGameDto</c> (endpoint <c>/api/v1/discovery/survey</c>)
        /// và <c>ActiveCafesByBoardGameResponseDto</c> (endpoint <c>/api/v1/board-games/{id}/active-cafes</c>).
        /// Dùng để hiển thị icon "đã lưu / chưa lưu" trên UI detail.
        /// </summary>
        public bool IsSaved { get; set; }
    }
}
