namespace BoardVerse.Core.DTOs.Payment;

public class CreatePaymentResponseDto
{
    public string PaymentUrl { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
}
