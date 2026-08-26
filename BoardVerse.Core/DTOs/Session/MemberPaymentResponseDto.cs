using BoardVerse.Core.Enum;

namespace BoardVerse.Core.DTOs.Session
{
    /// <summary>
    /// Response trả về sau khi thanh toán cho một thành viên.
    /// </summary>
    public class MemberPaymentResponseDto
    {
        public Guid MemberId { get; set; }
        public string DisplayName { get; set; } = null!;
        public decimal AmountDue { get; set; }
        public decimal AmountPaid { get; set; }
        public string PaymentMethod { get; set; } = null!;
        public MemberPaymentStatus Status { get; set; }
        public DateTime? PaidAt { get; set; }

        /// <summary>Order ID của QR payment (nếu là QR).</summary>
        public string? OrderId { get; set; }

        /// <summary>URL hình ảnh QR code (nếu là QR).</summary>
        public string? QrImageUrl { get; set; }

        /// <summary>URL thanh toán gateway (nếu là QR).</summary>
        public string? PaymentUrl { get; set; }

        /// <summary>Số tiền cần chuyển (nội dung chuyển khoản).</summary>
        public string? TransferContent { get; set; }
    }
}
