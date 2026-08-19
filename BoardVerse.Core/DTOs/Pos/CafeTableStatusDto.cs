using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Pos
{
    public class CafeTableStatusDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public int SeatCount { get; set; }
        public CafeTableStatus Status { get; set; }
        /// <summary>Bàn còn đang hoạt động (chưa soft-delete). Mặc định query chỉ trả IsActive=true; set includeInactive=true để lấy cả bàn đã ẩn.</summary>
        public bool IsActive { get; set; }
    }
}
