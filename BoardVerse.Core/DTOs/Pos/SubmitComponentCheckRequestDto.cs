using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Pos
{
    public class SubmitComponentCheckRequestDto
    {
        [Required]
        public Guid SessionGameId { get; set; }

        /// <summary>
        /// Nếu true = "Tất cả hợp lệ" → skip kiểm tra từng linh kiện, mark Verified ngay.
        /// Khi false = kiểm tra chi tiết từng linh kiện, tính penalty nếu thiếu.
        /// </summary>
        public bool MarkAllValid { get; set; }

        // Result items reuse ComponentCheckResultItemDto defined in ComponentChecklistDto.cs.
        public List<ComponentCheckResultItemDto> Results { get; set; } = [];

        /// <summary>
        /// BR-BGG-SYNC-01: Kết quả kiểm kê các orphaned penalties.
        /// Staff chọn "Mất linh kiện không có trong danh sách" → chọn từ dropdown
        /// OrphanedPenaltyItems trong GET checklist → gửi lên đây.
        /// Nếu không có linh kiện orphaned nào bị mất → gửi danh sách rỗng.
        /// </summary>
        public List<OrphanedPenaltyResultItemDto> OrphanedPenaltyResults { get; set; } = [];
    }
}