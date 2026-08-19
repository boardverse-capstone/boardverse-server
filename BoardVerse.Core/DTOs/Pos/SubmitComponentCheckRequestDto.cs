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
    }
}