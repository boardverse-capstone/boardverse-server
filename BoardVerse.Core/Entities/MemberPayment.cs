namespace BoardVerse.Core.Entities
{
    /// <summary>
    /// Audit trail cho thanh toán per-member trong group session.
    /// Ghi nhận mỗi lần thanh toán được thực hiện cho một thành viên.
    /// </summary>
    public class MemberPayment
    {
        public Guid Id { get; set; }

        /// <summary>Session mà thanh toán này thuộc về.</summary>
        public Guid ActiveSessionId { get; set; }

        /// <summary>Thành viên đã thanh toán.</summary>
        public Guid MemberId { get; set; }

        /// <summary>Số tiền thanh toán (phải bằng TotalAmount của member tại thời điểm thanh toán).</summary>
        public decimal Amount { get; set; }

        /// <summary>Phương thức thanh toán: CASH, QR_CODE, BANK_TRANSFER.</summary>
        public string PaymentMethod { get; set; } = null!;

        /// <summary>Order ID của QR payment (nếu là QR).</summary>
        public string? OrderId { get; set; }

        /// <summary>Transaction ID của thanh toán thành công.</summary>
        public Guid? TransactionId { get; set; }

        /// <summary>Nhân viên thực hiện thanh toán.</summary>
        public Guid StaffId { get; set; }

        /// <summary>Ghi chú (optional).</summary>
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // === Navigation ===
        public virtual ActiveSession ActiveSession { get; set; } = null!;
        public virtual ActiveSessionMember Member { get; set; } = null!;
    }
}
