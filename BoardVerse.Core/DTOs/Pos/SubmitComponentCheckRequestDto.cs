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

        public List<ComponentCheckResultDto> Results { get; set; } = [];
    }

    public class ComponentCheckResultDto
    {
        [Required]
        public Guid ComponentId { get; set; }

        [Required]
        [Range(0, int.MaxValue)]
        public int ActualQuantity { get; set; }
    }
}
