using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Session
{
    /// <summary>
    /// Trạng thái thanh toán per-member của session.
    /// </summary>
    public class SessionPaymentStatusDto
    {
        public Guid SessionId { get; set; }
        public decimal TotalAmount { get; set; }
        public decimal TotalPaid { get; set; }
        public decimal TotalRemaining { get; set; }
        public List<MemberPaymentStatusDto> Members { get; set; } = [];
    }

    /// <summary>
    /// Trạng thái thanh toán của một thành viên.
    /// </summary>
    public class MemberPaymentStatusDto
    {
        public Guid MemberId { get; set; }
        public string DisplayName { get; set; } = null!;
        public decimal TotalAmount { get; set; }
        public decimal AmountPaid { get; set; }
        public MemberPaymentStatus Status { get; set; }
        public string? PaymentMethod { get; set; }
    }
}
