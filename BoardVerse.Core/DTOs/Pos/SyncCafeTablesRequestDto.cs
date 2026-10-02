using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Pos
{
    /// <summary>
    /// DTO cho PUT /api/cafes/{cafeId}/pos/tables.
    /// Hỗ trợ shape duy nhất: <c>tables</c> — mảng {Name, SeatCount, SortOrder}.
    /// Tạo/cập nhật cả Name + SeatCount + SortOrder trong một lần PUT.
    /// SeatCount và SortOrder là optional — null nghĩa là giữ nguyên giá trị DB (SeatCount)
    /// hoặc tự auto-append sau max SortOrder của cafe (SortOrder).
    /// </summary>
    public class SyncCafeTablesRequestDto
    {
        /// <summary>Danh sách bàn đồng bộ (name + seatCount + sortOrder).</summary>
        [MinLength(1, ErrorMessage = "Cần ít nhất 1 bàn.")]
        public List<CafeTableSyncItem>? Tables { get; set; }
    }
}