namespace BoardVerse.Core.DTOs.Payment;

public class CreatePaymentResponseDto
{
    public string PaymentUrl { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string? QrImageUrl { get; set; }
    public string? Gateway { get; set; }
    public bool RequiresManualConfirmation { get; set; }
    public string? Message { get; set; }
}
