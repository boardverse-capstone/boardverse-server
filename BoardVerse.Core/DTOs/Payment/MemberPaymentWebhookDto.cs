namespace BoardVerse.Core.DTOs.Payment
{
    /// <summary>
    /// Webhook payload cho thanh toán QR của một thành viên cụ thể.
    /// </summary>
    public class MemberPaymentWebhookDto
    {
        /// <summary>Order ID từ QR payment, format: BV-MEMBER-{shortMemberId}.</summary>
        public string OrderId { get; set; } = null!;

        /// <summary>ID của member (được encode trong orderId).</summary>
        public Guid MemberId { get; set; }

        /// <summary>Số tiền thanh toán.</summary>
        public decimal Amount { get; set; }

        /// <summary>Trạng thái: success, failed, cancelled.</summary>
        public string Status { get; set; } = null!;

        /// <summary>Gateway transaction ID.</summary>
        public string? GatewayTransactionId { get; set; }

        /// <summary>Mã tham chiếu từ gateway.</summary>
        public string? ReferenceCode { get; set; }

        /// <summary>Gateway: SePay, VietQR.</summary>
        public string? Gateway { get; set; }

        /// <summary>Thời điểm thanh toán thành công.</summary>
        public DateTime? PaidAt { get; set; }
    }
}
