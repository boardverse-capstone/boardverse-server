using BoardVerse.Core.DTOs.Game;

namespace BoardVerse.Core.DTOs.Cafe
{
    /// <summary>
    /// Thông tin board game đang hoạt động tại quán cafe cho player.
    /// Điều kiện "đang hoạt động":
    ///   • CafeGameInventory.IsActive = true (quán chưa xóa mềm khỏi kho).
    ///   • GameTemplate.IsActive = true (master game vẫn active).
    ///   • CafeGameInventory.Status ∈ {Available, InUse} (không Damaged/Maintenance/Retired).
    /// </summary>
    public class CafeActiveGameDto
    {
        /// <summary>Mã mục kho (CafeGameInventory.Id).</summary>
        public Guid InventoryId { get; set; }

        /// <summary>Mã master game (GameTemplates.Id).</summary>
        public Guid GameTemplateId { get; set; }

        /// <summary>Tên board game.</summary>
        public string GameName { get; set; } = string.Empty;

        /// <summary>URL ảnh thumbnail.</summary>
        public string? ThumbnailUrl { get; set; }

        /// <summary>Mô tả ngắn.</summary>
        public string? Description { get; set; }

        /// <summary>Số người chơi tối thiểu.</summary>
        public int MinPlayers { get; set; }

        /// <summary>Số người chơi tối đa.</summary>
        public int MaxPlayers { get; set; }

        /// <summary>Thời lượng chơi trung bình (phút).</summary>
        public int PlayTime { get; set; }

        /// <summary>Số hộp vật lý quán đang có (CafeGameInventory.BoxQuantity).</summary>
        public int BoxQuantity { get; set; }

        /// <summary>Số hộp đang trống (Available) để chơi ngay.</summary>
        public int AvailableBoxCount { get; set; }

        /// <summary>
        /// True nếu game có ít nhất 1 hộp <c>Available</c> để player đặt ngay.
        /// Field tiện ích cho UI — tránh phải kiểm tra <c>availableBoxCount &gt; 0</c> ở frontend.
        /// </summary>
        public bool IsAvailableNow => AvailableBoxCount > 0;

        /// <summary>
        /// True nếu game có thể host 1 nhóm có <paramref name="GroupSize"/> thành viên
        /// (nghĩa là <c>MinPlayers &lt;= GroupSize</c>). Null khi player không truyền groupSize.
        /// </summary>
        public bool? FitsGroupSize { get; set; }

        /// <summary>Trạng thái kho: Available hoặc InUse.</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Danh sách thể loại.</summary>
        public List<CategoryDto> Categories { get; set; } = [];
    }
}