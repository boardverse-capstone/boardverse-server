using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Pos
{
    /// <summary>
    /// DTO cho PUT /api/cafes/{cafeId}/pos/tables.
    /// Hỗ trợ 2 shape — server sẽ chọn cái nào được gửi:
    /// 1. Cũ (backward compat): { "tableNames": ["Bàn 1", "Bàn 2"] }
    ///    → Áp dụng cho UI cũ chỉ quan tâm tên; SeatCount còn lại dùng default 4 / giữ nguyên.
    /// 2. Mới: { "tables": [{ "name": "Bàn 1", "seatCount": 8, "sortOrder": 0 }, ...] }
    ///    → Tạo/cập nhật cả Name + SeatCount + SortOrder trong một lần PUT.
    /// Không được gửi cả 2 cùng lúc — sẽ trả 400.
    /// </summary>
    public class SyncCafeTablesRequestDto
    {
        /// <summary>Shape cũ — danh sách tên bàn (string).</summary>
        [MinLength(1, ErrorMessage = "Cần ít nhất 1 tên bàn.")]
        public List<string>? TableNames { get; set; }

        /// <summary>Shape mới — danh sách bàn đầy đủ (name + seatCount + sortOrder).</summary>
        [MinLength(1, ErrorMessage = "Cần ít nhất 1 bàn.")]
        public List<CafeTableSyncItem>? Tables { get; set; }
    }
}
