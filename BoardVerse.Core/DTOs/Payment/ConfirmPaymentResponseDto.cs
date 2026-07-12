namespace BoardVerse.Core.DTOs.Payment;

public class ConfirmPaymentResponseDto
{
    public Guid DepositId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? PaidAt { get; set; }
}
