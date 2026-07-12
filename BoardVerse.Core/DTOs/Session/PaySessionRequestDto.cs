using System.ComponentModel.DataAnnotations;

namespace BoardVerse.Core.DTOs.Session
{
    /// <summary>
    /// Request thanh toán hóa đơn tổng của phiên chơi.
    /// BR-15: TotalAmount = Subtotal + Penalty - DepositAppliedAmount
    /// </summary>
    public class PaySessionRequestDto
    {
        /// <summary>Danh sách linh kiện bị mất/hỏng và mức phạt.</summary>
        public List<ComponentPenaltyItemDto>? PenaltyItems { get; set; }

        /// <summary>Ghi chú thanh toán (optional).</summary>
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Item phạt cho linh kiện bị mất/hỏng.
    /// BR-14: Không gán phí phạt cho Guest_Slot.
    /// </summary>
    public class ComponentPenaltyItemDto
    {
        [Required]
        public Guid ComponentId { get; set; }

        [Required]
        public string ComponentName { get; set; } = string.Empty;

        [Required]
        public decimal PenaltyAmount { get; set; }

        /// <summary>Mã thành viên chịu trách nhiệm (nếu có). Không áp dụng cho Guest_Slot (BR-14).</summary>
        public Guid? ResponsibleMemberId { get; set; }
    }

    /// <summary>
    /// Response sau khi thanh toán hóa đơn tổng.
    /// </summary>
    public class PaySessionResponseDto
    {
        public Guid SessionId { get; set; }
        public decimal Subtotal { get; set; }
        public decimal PenaltyAmount { get; set; }
        public decimal DepositAppliedAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public DateTime PaidAt { get; set; }
        public string? SettlementStatus { get; set; }
        public ActiveSessionResponseDto Session { get; set; } = null!;
    }
}
