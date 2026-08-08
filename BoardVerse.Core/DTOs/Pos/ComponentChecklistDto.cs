using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Pos
{
    /// <summary>
    /// BR-12: Trả về cho GET /component-checklist.
    /// Mô tả các linh kiện cần kiểm — CHƯA CÓ dữ liệu thực tế.
    /// </summary>
    public class ComponentChecklistDto
    {
        public Guid SessionGameId { get; set; }
        public Guid GameTemplateId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public List<ComponentCheckItemDto> Components { get; set; } = [];
    }

    /// <summary>
    /// Một linh kiện trong checklist (GET).
    /// Chỉ chứa thông tin mô tả và số lượng kỳ vọng.
    /// Số lượng thực tế / phí phạt không có ở đây — xem <see cref="ComponentCheckResultItemDto"/>.
    /// </summary>
    public class ComponentCheckItemDto
    {
        public Guid ComponentId { get; set; }
        public string ComponentName { get; set; } = string.Empty;
        public BoardGameComponentKind? ComponentKind { get; set; }
        public int ExpectedQuantity { get; set; }
    }

    /// <summary>
    /// BR-12: Trả về cho POST /component-check (sau khi staff verify xong).
    /// Đây là response chứa kết quả verify + tổng phí phạt, có ý nghĩa persistent.
    /// </summary>
    public class ComponentCheckResultDto
    {
        public Guid SessionGameId { get; set; }
        public Guid GameTemplateId { get; set; }
        public string GameName { get; set; } = string.Empty;
        public ComponentCheckStatus CheckStatus { get; set; }
        public DateTime CheckedAt { get; set; }
        public decimal TotalPenaltyAmount { get; set; }
        public List<ComponentCheckResultItemDto> Components { get; set; } = [];
    }

    /// <summary>
    /// Một linh kiện trong kết quả verify (POST).
    /// <para>
    /// Lưu từ entity <see cref="BoardVerse.Core.Entities.ComponentCheckResult"/>.
    /// <see cref="ActualQuantity"/> = số thực tế nhân viên đếm được
    /// (hoặc = <see cref="ExpectedQuantity"/> nếu markAllValid=true).
    /// </para>
    /// </summary>
    public class ComponentCheckResultItemDto
    {
        public Guid ComponentId { get; set; }
        public string ComponentName { get; set; } = string.Empty;
        public BoardGameComponentKind? ComponentKind { get; set; }
        public int ExpectedQuantity { get; set; }
        public int ActualQuantity { get; set; }
        public decimal PenaltyFee { get; set; }

        /// <summary>
        /// Member chịu trách nhiệm penalty cho linh kiện này (optional).
        /// Null = phạt chung vào <c>session.PenaltyAmount</c>.
        /// </summary>
        public Guid? ResponsibleMemberId { get; set; }
    }
}