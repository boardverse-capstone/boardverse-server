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

        /// <summary>
        /// BR-BGG-SYNC-01: Linh kiện đã bị xóa khỏi BGG catalog nhưng vẫn còn penalty config.
        /// Staff có thể gán penalty cho các linh kiện này khi khách làm mất.
        /// </summary>
        public List<OrphanedPenaltyItemDto> OrphanedPenaltyItems { get; set; } = [];
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
    /// BR-BGG-SYNC-01: Linh kiện đã bị xóa khỏi BGG catalog nhưng vẫn còn penalty.
    /// Hiển thị trong POS checklist để staff có thể gán penalty khi khách làm mất
    /// component không còn trong catalog BGG.
    /// </summary>
    public class OrphanedPenaltyItemDto
    {
        /// <summary>Mã penalty trong DB (dùng để submit check với component giả).</summary>
        public Guid PenaltyId { get; set; }
        public string ComponentName { get; set; } = string.Empty;
        public decimal PenaltyFee { get; set; }
    }

    /// <summary>
    /// BR-BGG-SYNC-01: Kết quả kiểm kê một orphaned penalty.
    /// Dùng cho cả request (submit) và response (result).
    /// </summary>
    public class OrphanedPenaltyResultItemDto
    {
        /// <summary>Mã penalty config trong DB (CafeGameComponentPenalty.Id).</summary>
        public Guid PenaltyId { get; set; }

        /// <summary>Tên linh kiện (trả trong response, bỏ trống trong request).</summary>
        public string ComponentName { get; set; } = string.Empty;

        /// <summary>
        /// Số lượng bị mất (thường = 1). Staff báo 0 nếu linh kiện đã được hoàn trả
        /// sau khi từng bị phạt trước đó.
        /// </summary>
        public int MissingQuantity { get; set; } = 1;

        /// <summary>Phí đền bù (trả trong response, bỏ trống trong request).</summary>
        public decimal PenaltyFee { get; set; }

        /// <summary>
        /// Member chịu trách nhiệm penalty (optional).
        /// Null = phạt chung vào session.PenaltyAmount.
        /// BR-14: không được là Guest_Slot.
        /// </summary>
        public Guid? ResponsibleMemberId { get; set; }
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

        /// <summary>
        /// BR-BGG-SYNC-01: Kết quả kiểm kê các orphaned penalties (component đã bị xóa khỏi BGG).
        /// </summary>
        public List<OrphanedPenaltyResultItemDto> OrphanedPenaltyResults { get; set; } = [];
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