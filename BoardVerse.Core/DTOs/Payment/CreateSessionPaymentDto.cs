namespace BoardVerse.Core.DTOs.Payment
{
    /// <summary>
    /// Request tạo thanh toán cho hóa đơn phiên chơi.
    /// </summary>
    public class CreateSessionPaymentRequestDto
    {
        public Guid SessionId { get; set; }
        public string? CustomerEmail { get; set; }
        public string? Notes { get; set; }
    }

    /// <summary>
    /// Response chứa payment URL để khách quét QR.
    /// </summary>
    public class CreateSessionPaymentResponseDto
    {
        public Guid SessionId { get; set; }
        public string PaymentUrl { get; set; } = string.Empty;
        public string OrderId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Status { get; set; } = "Pending";
    }
}
