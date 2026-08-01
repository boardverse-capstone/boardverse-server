using System.ComponentModel.DataAnnotations;
using BoardVerse.Core.Messages;

namespace BoardVerse.Core.DTOs.Pos
{
    /// <summary>
    /// Một bàn trong payload PUT /api/cafes/{cafeId}/pos/tables.
    /// Hỗ trợ tạo mới kèm SeatCount từ đầu (PUT), không cần gọi PATCH riêng.
    /// </summary>
    public class CafeTableSyncItem
    {
        [Required]
        [StringLength(100, MinimumLength = 1, ErrorMessage = ApiErrorMessages.Validation.TableNameLength)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Số ghế cho bàn (1–50). Optional — nếu null khi tạo mới sẽ dùng default 4.
        /// Khi match với bàn đã tồn tại (case-insensitive theo Name) → cập nhật SeatCount.
        /// </summary>
        [Range(1, 50, ErrorMessage = ApiErrorMessages.Validation.SeatsPerTableRange)]
        public int? SeatCount { get; set; }

        /// <summary>
        /// Thứ tự hiển thị. Optional — nếu null sẽ dùng index trong mảng.
        /// </summary>
        [Range(0, 9999, ErrorMessage = ApiErrorMessages.Validation.SortOrderRange)]
        public int? SortOrder { get; set; }
    }
}
